using AutoMapper;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockQuarterlyResultsService : IStockQuarterlyResultsService
    {
        private readonly IStockRepository _stockRepository;
        private readonly IStockFinancialRepository _financialRepository;
        private readonly ICompanyRepository _companyRepository;
        private readonly IIndianApiBalanceSheetClient? _indianApiClient;
        private readonly IYahooFinanceClient? _yahooFinanceClient;
        private readonly IMapper _mapper;
        private readonly ILogger<StockQuarterlyResultsService> _logger;

        private static readonly ConcurrentDictionary<int, SemaphoreSlim> StockLocks = new();

        public StockQuarterlyResultsService(
            IStockRepository stockRepository,
            IStockFinancialRepository financialRepository,
            ICompanyRepository companyRepository,
            IMapper mapper,
            ILogger<StockQuarterlyResultsService> logger,
            IIndianApiBalanceSheetClient? indianApiClient = null,
            IYahooFinanceClient? yahooFinanceClient = null)
        {
            _stockRepository = stockRepository;
            _financialRepository = financialRepository;
            _companyRepository = companyRepository;
            _mapper = mapper;
            _logger = logger;
            _indianApiClient = indianApiClient;
            _yahooFinanceClient = yahooFinanceClient;
        }

        public async Task<StockQuarterlyResultsResponseDto> GetQuarterlyResultsBySymbolAsync(
            string symbol,
            string? exchange = "NSE",
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();

            var stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, cleanExchange, cancellationToken: cancellationToken);

            return await ProcessQuarterlyResultsAsync(stock, forceRefresh, cancellationToken);
        }

        private async Task<StockQuarterlyResultsResponseDto> ProcessQuarterlyResultsAsync(
            Stock stock,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var semaphore = StockLocks.GetOrAdd(stock.Id, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                var existingQuarters = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "quarterly", limit: 12) ?? new List<StockFinancial>();

                var needsSync = IsSyncNeeded(existingQuarters, forceRefresh, out var syncReason);
                if (needsSync)
                {
                    _logger.LogInformation("Quarterly sync triggered for {Symbol}. Reason: {Reason}", stock.Symbol, syncReason);
                    await SyncQuarterlyResultsAsync(stock, existingQuarters, cancellationToken);
                    existingQuarters = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "quarterly", limit: 12) ?? new List<StockFinancial>();
                }
                else
                {
                    _logger.LogDebug("Quarterly sync skipped for {Symbol}. Reason: {Reason}", stock.Symbol, syncReason);
                }

                // If any existing records in DB still have Tax == null, calculate and persist it to DB
                var needsDbSave = false;
                foreach (var q in existingQuarters)
                {
                    if (!q.Tax.HasValue && q.ProfitBeforeTax.HasValue && q.NetProfit.HasValue)
                    {
                        var comp = q.ProfitBeforeTax.Value - q.NetProfit.Value;
                        if (comp >= 0)
                        {
                            q.Tax = Math.Round(comp, 2);
                            needsDbSave = true;
                        }
                    }
                    if (!q.TaxPercentage.HasValue && q.Tax.HasValue && q.ProfitBeforeTax.HasValue && q.ProfitBeforeTax.Value > 0)
                    {
                        q.TaxPercentage = Math.Round((q.Tax.Value / q.ProfitBeforeTax.Value) * 100m, 2);
                        needsDbSave = true;
                    }
                    if (needsDbSave)
                    {
                        q.UpdatedAt = DateTime.UtcNow;
                        await _financialRepository.UpdateAsync(q);
                    }
                }
                if (needsDbSave)
                {
                    await _financialRepository.SaveChangesAsync();
                }

                return BuildResponseDto(stock, existingQuarters);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public static bool IsSyncNeeded(List<StockFinancial> existingQuarters, bool forceRefresh, out string reason)
        {
            if (forceRefresh)
            {
                reason = "Manual force refresh requested";
                return true;
            }

            if (existingQuarters == null || existingQuarters.Count == 0)
            {
                reason = "No quarterly records present in database";
                return true;
            }

            var latestDbQuarter = existingQuarters
                .OrderByDescending(q => q.PeriodEndDate ?? DateTime.MinValue)
                .ThenByDescending(q => q.FiscalYear)
                .First();

            // Self-healing check: If the latest record came from YahooFinance fallback OR has zero tax bug OR is missing core metrics
            // Note: Banking/NBFC stocks (e.g. HDFCBANK) do not have Operating Profit by accounting standard, so only check core fields
            var isYahooFallback = string.Equals(latestDbQuarter.Source, "YahooFinance", StringComparison.OrdinalIgnoreCase);
            var isMissingCoreFields = !latestDbQuarter.Revenue.HasValue || !latestDbQuarter.NetProfit.HasValue || !latestDbQuarter.Eps.HasValue;
            var hasZeroTaxBug = latestDbQuarter.Tax == 0 && latestDbQuarter.ProfitBeforeTax.HasValue && latestDbQuarter.ProfitBeforeTax > 0;
            var hasIncompleteData = isYahooFallback || isMissingCoreFields || hasZeroTaxBug;

            if (hasIncompleteData)
            {
                // To prevent tight retry loops if upstream is persistently down, throttle self-healing attempts to once per hour
                if (latestDbQuarter.LastSyncedAt < DateTime.UtcNow.AddHours(-1))
                {
                    reason = "Latest quarterly record has incomplete fallback data, syncing authoritative data from IndianAPI";
                    return true;
                }
            }

            var now = DateTime.UtcNow;
            var latestCompletedQuarterEnd = GetLatestCompletedQuarterEndDate(now);

            // Gate 1: If DB already has the latest completed quarter with complete data, LOCK IT!
            if (latestDbQuarter.PeriodEndDate.HasValue &&
                latestDbQuarter.PeriodEndDate.Value.Date >= latestCompletedQuarterEnd.Date &&
                !hasIncompleteData)
            {
                reason = $"Locked: Database already holds latest completed quarter ({latestDbQuarter.PeriodEndDate.Value:MMM yyyy})";
                return false;
            }

            // Gate 2: If the latest completed quarter is not yet in DB, check if we are in the 55-day declaration window
            var daysSinceQuarterEnd = (now - latestCompletedQuarterEnd).TotalDays;
            if (daysSinceQuarterEnd >= 0 && daysSinceQuarterEnd <= 55)
            {
                var lastSynced = existingQuarters.Max(q => q.LastSyncedAt);
                // Within declaration window: check once every 3 days
                if (lastSynced < now.AddDays(-3))
                {
                    reason = $"Quarter declaration window active ({daysSinceQuarterEnd:F0} days since {latestCompletedQuarterEnd:MMM yyyy}), checking for new results (3-day cycle)";
                    return true;
                }

                reason = $"Quarter declaration window active, but checked recently on {lastSynced:yyyy-MM-dd} (< 3 days ago). Waiting.";
                return false;
            }

            // Gate 3: Outside standard window (unusually late filing or historical gap)
            var lastSyncGeneral = existingQuarters.Max(q => q.LastSyncedAt);
            if (lastSyncGeneral < now.AddDays(-7))
            {
                reason = "Quarter is overdue and outside standard declaration window, running weekly check";
                return true;
            }

            reason = "Data is fresh and locked";
            return false;
        }

        public static DateTime GetLatestCompletedQuarterEndDate(DateTime date)
        {
            var year = date.Year;
            // Calendar financial quarter ends: March 31, June 30, September 30, December 31
            if (date.Date < new DateTime(year, 3, 31))
            {
                return new DateTime(year - 1, 12, 31);
            }
            if (date.Date < new DateTime(year, 6, 30))
            {
                return new DateTime(year, 3, 31);
            }
            if (date.Date < new DateTime(year, 9, 30))
            {
                return new DateTime(year, 6, 30);
            }
            if (date.Date < new DateTime(year, 12, 31))
            {
                return new DateTime(year, 9, 30);
            }
            return new DateTime(year, 12, 31);
        }

        private async Task SyncQuarterlyResultsAsync(Stock stock, List<StockFinancial> existingQuarters, CancellationToken cancellationToken)
        {
            var syncedFromIndianApi = false;

            // Tier 1: Try IndianAPI
            if (_indianApiClient != null)
            {
                try
                {
                    _logger.LogInformation("Tier 1: Fetching quarterly financials from IndianAPI for {Symbol}", stock.Symbol);
                    var overview = await _indianApiClient.GetStockFinancialsAndOverviewAsync(stock.Symbol, stock.Exchange, cancellationToken);

                    if (overview != null && overview.Financials != null && overview.Financials.Count > 0)
                    {
                        var quarterlyPeriods = overview.Financials
                            .Where(f => f.PeriodType.Equals("quarterly", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (quarterlyPeriods.Count > 0)
                        {
                            foreach (var q in quarterlyPeriods)
                            {
                                await UpsertQuarterlyRecordAsync(stock.Id, q, "IndianAPI", existingQuarters);
                            }
                            await _financialRepository.SaveChangesAsync();
                            syncedFromIndianApi = true;
                            _logger.LogInformation("Successfully synced {Count} quarterly records from IndianAPI for {Symbol}", quarterlyPeriods.Count, stock.Symbol);
                        }
                    }
                    else if (existingQuarters.Count > 0)
                    {
                        // Even if no new quarter returned yet, touch LastSyncedAt on latest record so 3-day window is respected
                        existingQuarters[0].LastSyncedAt = DateTime.UtcNow;
                        existingQuarters[0].UpdatedAt = DateTime.UtcNow;
                        await _financialRepository.UpdateAsync(existingQuarters[0]);
                        await _financialRepository.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tier 1 IndianAPI quarterly fetch failed for {Symbol}, evaluating fallback.", stock.Symbol);
                }
            }

            if (syncedFromIndianApi) return;

            // Tier 2: Try Yahoo Finance (Strictly Guarded)
            if (_yahooFinanceClient != null)
            {
                try
                {
                    // Check if DB already has authoritative IndianAPI data
                    var hasAuthoritativeData = existingQuarters.Any(q => string.Equals(q.Source, "IndianAPI", StringComparison.OrdinalIgnoreCase) && q.OperatingProfit.HasValue);

                    // If DB already has complete IndianAPI data, DO NOT pollute it with Yahoo Finance fallback!
                    if (hasAuthoritativeData)
                    {
                        _logger.LogInformation("Skipping Yahoo Finance fallback for {Symbol}: Database already holds authoritative IndianAPI records.", stock.Symbol);
                        if (existingQuarters.Count > 0)
                        {
                            existingQuarters[0].LastSyncedAt = DateTime.UtcNow;
                            await _financialRepository.UpdateAsync(existingQuarters[0]);
                            await _financialRepository.SaveChangesAsync();
                        }
                        return;
                    }

                    _logger.LogInformation("Tier 2: Fetching quarterly income statements from Yahoo Finance for {Symbol}", stock.Symbol);
                    var yfQuarters = await _yahooFinanceClient.GetQuarterlyIncomeStatementsAsync(stock.Symbol, stock.Exchange, cancellationToken);

                    if (yfQuarters != null && yfQuarters.Count > 0)
                    {
                        foreach (var q in yfQuarters)
                        {
                            await UpsertQuarterlyRecordAsync(stock.Id, q, "YahooFinance", existingQuarters);
                        }
                        await _financialRepository.SaveChangesAsync();
                        _logger.LogInformation("Successfully synced {Count} quarterly records from Yahoo Finance for {Symbol}", yfQuarters.Count, stock.Symbol);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tier 2 Yahoo Finance quarterly fetch failed for {Symbol}.", stock.Symbol);
                }
            }
        }

        private async Task UpsertQuarterlyRecordAsync(int stockId, IndianApiFinancialPeriodDto dto, string source, List<StockFinancial>? existingQuarters = null)
        {
            // Ensure Tax and TaxPercentage are computed on dto if missing
            if (!dto.Tax.HasValue && dto.ProfitBeforeTax.HasValue && dto.NetProfit.HasValue)
            {
                var computedTax = dto.ProfitBeforeTax.Value - dto.NetProfit.Value;
                if (computedTax >= 0)
                {
                    dto.Tax = Math.Round(computedTax, 2);
                }
            }
            if (!dto.TaxPercentage.HasValue && dto.Tax.HasValue && dto.ProfitBeforeTax.HasValue && dto.ProfitBeforeTax.Value > 0)
            {
                dto.TaxPercentage = Math.Round((dto.Tax.Value / dto.ProfitBeforeTax.Value) * 100m, 2);
            }

            var periodKey = ResolveQuarterlyPeriodKey(dto);
            var normalizedPeriod = dto.PeriodEndDate.HasValue
                ? dto.PeriodEndDate.Value.ToString("MMM yyyy")
                : (!string.IsNullOrWhiteSpace(dto.FiscalYear) ? dto.FiscalYear : "Quarter");

            var existing = await _financialRepository.GetByStockIdAndPeriodKeyAsync(stockId, periodKey);
            if (existing == null && !string.IsNullOrWhiteSpace(dto.FiscalYear))
            {
                var altKey = $"quarterly-{dto.FiscalYear.Trim().Replace(" ", "-").ToLowerInvariant()}";
                if (!string.Equals(altKey, periodKey, StringComparison.OrdinalIgnoreCase))
                {
                    existing = await _financialRepository.GetByStockIdAndPeriodKeyAsync(stockId, altKey);
                }
            }

            // Secondary lookup by PeriodEndDate to prevent duplicate keys for the same calendar quarter
            if (existing == null && dto.PeriodEndDate.HasValue && existingQuarters != null && existingQuarters.Count > 0)
            {
                existing = existingQuarters.FirstOrDefault(q => q.PeriodEndDate.HasValue && q.PeriodEndDate.Value.Date == dto.PeriodEndDate.Value.Date);
            }

            if (existing == null)
            {
                var newEntity = new StockFinancial
                {
                    StockId = stockId,
                    PeriodKey = periodKey,
                    PeriodType = "quarterly",
                    FiscalYear = normalizedPeriod,
                    PeriodEndDate = dto.PeriodEndDate,
                    Revenue = dto.Revenue,
                    Expenses = dto.Expenses,
                    OperatingProfit = dto.OperatingProfit,
                    OperatingProfitMargin = dto.OperatingProfitMargin,
                    OtherIncome = dto.OtherIncome,
                    Interest = dto.Interest,
                    Depreciation = dto.Depreciation,
                    ProfitBeforeTax = dto.ProfitBeforeTax,
                    Tax = dto.Tax,
                    TaxPercentage = dto.TaxPercentage,
                    NetProfit = dto.NetProfit,
                    Eps = dto.Eps,
                    ConsolidationType = dto.ConsolidationType ?? "consolidated",
                    Source = source,
                    LastSyncedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _financialRepository.AddAsync(newEntity);
            }
            else
            {
                // Quality guard: IndianAPI is authoritative. Only update fields if source is IndianAPI OR existing is not IndianAPI
                var isAuthoritative = string.Equals(source, "IndianAPI", StringComparison.OrdinalIgnoreCase);
                var existingIsAuthoritative = string.Equals(existing.Source, "IndianAPI", StringComparison.OrdinalIgnoreCase);

                if (isAuthoritative || !existingIsAuthoritative)
                {
                    existing.PeriodKey = periodKey;
                    existing.FiscalYear = normalizedPeriod;
                    existing.PeriodEndDate = dto.PeriodEndDate ?? existing.PeriodEndDate;
                    existing.Revenue = dto.Revenue ?? existing.Revenue;
                    existing.Expenses = dto.Expenses ?? existing.Expenses;
                    existing.OperatingProfit = dto.OperatingProfit ?? existing.OperatingProfit;
                    existing.OperatingProfitMargin = dto.OperatingProfitMargin ?? existing.OperatingProfitMargin;
                    existing.OtherIncome = dto.OtherIncome ?? existing.OtherIncome;
                    existing.Interest = dto.Interest ?? existing.Interest;
                    existing.Depreciation = dto.Depreciation ?? existing.Depreciation;
                    existing.ProfitBeforeTax = dto.ProfitBeforeTax ?? existing.ProfitBeforeTax;

                    // Only update tax if incoming has a valid non-zero tax, or existing has no tax
                    if (dto.Tax.HasValue && (dto.Tax.Value > 0 || !existing.Tax.HasValue))
                    {
                        existing.Tax = dto.Tax.Value;
                    }
                    if (dto.TaxPercentage.HasValue && (dto.TaxPercentage.Value > 0 || !existing.TaxPercentage.HasValue))
                    {
                        existing.TaxPercentage = dto.TaxPercentage.Value;
                    }

                    if (!existing.Tax.HasValue && existing.ProfitBeforeTax.HasValue && existing.NetProfit.HasValue)
                    {
                        var comp = existing.ProfitBeforeTax.Value - existing.NetProfit.Value;
                        if (comp >= 0)
                        {
                            existing.Tax = Math.Round(comp, 2);
                        }
                    }
                    if (!existing.TaxPercentage.HasValue && existing.Tax.HasValue && existing.ProfitBeforeTax.HasValue && existing.ProfitBeforeTax.Value > 0)
                    {
                        existing.TaxPercentage = Math.Round((existing.Tax.Value / existing.ProfitBeforeTax.Value) * 100m, 2);
                    }

                    existing.NetProfit = dto.NetProfit ?? existing.NetProfit;
                    existing.Eps = dto.Eps ?? existing.Eps;
                    existing.ConsolidationType = dto.ConsolidationType ?? existing.ConsolidationType;
                    existing.Source = source;
                    existing.LastSyncedAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _financialRepository.UpdateAsync(existing);
                }
            }
        }

        private static string ResolveQuarterlyPeriodKey(IndianApiFinancialPeriodDto dto)
        {
            if (dto.PeriodEndDate.HasValue)
            {
                return $"quarterly-{dto.PeriodEndDate.Value:yyyy-MM-dd}";
            }
            if (!string.IsNullOrWhiteSpace(dto.FiscalYear))
            {
                if (DateTime.TryParse(dto.FiscalYear, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    return $"quarterly-{parsedDate:yyyy-MM-dd}";
                }
                var clean = dto.FiscalYear.Trim().Replace(" ", "-").ToLowerInvariant();
                return $"quarterly-{clean}";
            }
            return $"quarterly-{Guid.NewGuid():N}";
        }

        private StockQuarterlyResultsResponseDto BuildResponseDto(Stock stock, List<StockFinancial> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return new StockQuarterlyResultsResponseDto
                {
                    StockId = stock.Id,
                    Symbol = stock.Symbol,
                    Exchange = stock.Exchange,
                    CompanyName = stock.Company?.CompanyName ?? stock.Symbol,
                    LatestQuarter = "—",
                    Source = "—",
                    LastSyncedAt = DateTime.UtcNow,
                    Summary = new QuarterlyRecordDto
                    {
                        Period = "—"
                    },
                    History = new List<QuarterlyRecordDto>()
                };
            }

            // Deduplicate and merge any duplicate entries for the same quarter period
            var historyDtos = entities
                .GroupBy(e => e.PeriodEndDate.HasValue
                    ? e.PeriodEndDate.Value.ToString("yyyy-MM")
                    : (!string.IsNullOrWhiteSpace(e.FiscalYear) ? e.FiscalYear.Trim().ToUpperInvariant() : e.PeriodKey))
                .Select(g =>
                {
                    // Prioritize IndianAPI over YahooFinance as primary record
                    var primary = g.OrderByDescending(x => string.Equals(x.Source, "IndianAPI", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                                   .ThenByDescending(x => x.LastSyncedAt)
                                   .First();
                    var mergedDto = _mapper.Map<QuarterlyRecordDto>(primary);

                    foreach (var other in g)
                    {
                        mergedDto.Sales ??= other.Revenue;
                        mergedDto.Expenses ??= other.Expenses;
                        mergedDto.OperatingProfit ??= other.OperatingProfit;
                        mergedDto.OpmPercentage ??= other.OperatingProfitMargin;
                        mergedDto.OtherIncome ??= other.OtherIncome;
                        mergedDto.Interest ??= other.Interest;
                        mergedDto.Depreciation ??= other.Depreciation;
                        mergedDto.ProfitBeforeTax ??= other.ProfitBeforeTax;
                        if (!mergedDto.Tax.HasValue || mergedDto.Tax == 0)
                        {
                            if (other.Tax.HasValue && other.Tax.Value > 0)
                            {
                                mergedDto.Tax = other.Tax;
                                mergedDto.TaxPercentage = other.TaxPercentage;
                            }
                        }
                        mergedDto.NetProfit ??= other.NetProfit;
                        mergedDto.Eps ??= other.Eps;
                    }

                    if (!mergedDto.Tax.HasValue && mergedDto.ProfitBeforeTax.HasValue && mergedDto.NetProfit.HasValue)
                    {
                        var comp = mergedDto.ProfitBeforeTax.Value - mergedDto.NetProfit.Value;
                        if (comp >= 0)
                        {
                            mergedDto.Tax = Math.Round(comp, 2);
                        }
                    }
                    if (!mergedDto.TaxPercentage.HasValue && mergedDto.Tax.HasValue && mergedDto.ProfitBeforeTax.HasValue && mergedDto.ProfitBeforeTax.Value > 0)
                    {
                        mergedDto.TaxPercentage = Math.Round((mergedDto.Tax.Value / mergedDto.ProfitBeforeTax.Value) * 100m, 2);
                    }

                    if (primary.PeriodEndDate.HasValue)
                    {
                        mergedDto.Period = primary.PeriodEndDate.Value.ToString("MMM yyyy");
                    }
                    else if (string.IsNullOrWhiteSpace(mergedDto.Period))
                    {
                        mergedDto.Period = primary.FiscalYear ?? "Quarter";
                    }

                    return new
                    {
                        Date = primary.PeriodEndDate ?? DateTime.MinValue,
                        FiscalYear = primary.FiscalYear ?? "",
                        Merged = mergedDto
                    };
                })
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.FiscalYear)
                .Select(x => x.Merged)
                .ToList();

            var latest = historyDtos.FirstOrDefault() ?? new QuarterlyRecordDto { Period = "—" };

            var response = new StockQuarterlyResultsResponseDto
            {
                StockId = stock.Id,
                Symbol = stock.Symbol,
                Exchange = stock.Exchange,
                CompanyName = stock.Company?.CompanyName ?? stock.Symbol,
                LatestQuarter = latest.Period,
                Source = entities.FirstOrDefault()?.Source ?? "—",
                LastSyncedAt = entities.FirstOrDefault()?.LastSyncedAt ?? DateTime.UtcNow,
                Summary = latest,
                History = historyDtos
            };

            // Calculate QoQ Growth (Latest vs Previous Quarter)
            if (historyDtos.Count >= 2)
            {
                var prev = historyDtos[1];
                response.QoQGrowth = new QuarterlyGrowthDto
                {
                    SalesGrowthPercent = CalculateGrowth(latest.Sales, prev.Sales),
                    OperatingProfitGrowthPercent = CalculateGrowth(latest.OperatingProfit, prev.OperatingProfit),
                    NetProfitGrowthPercent = CalculateGrowth(latest.NetProfit, prev.NetProfit),
                    EpsGrowthPercent = CalculateGrowth(latest.Eps, prev.Eps),
                    DepreciationGrowthPercent = CalculateGrowth(latest.Depreciation, prev.Depreciation),
                    InterestGrowthPercent = CalculateGrowth(latest.Interest, prev.Interest),
                    TaxGrowthPercent = CalculateGrowth(latest.Tax, prev.Tax)
                };
            }

            // Calculate YoY Growth (Latest vs 4 Quarters Ago)
            if (historyDtos.Count >= 5)
            {
                var yoy = historyDtos[4];
                response.YoYGrowth = new QuarterlyGrowthDto
                {
                    SalesGrowthPercent = CalculateGrowth(latest.Sales, yoy.Sales),
                    OperatingProfitGrowthPercent = CalculateGrowth(latest.OperatingProfit, yoy.OperatingProfit),
                    NetProfitGrowthPercent = CalculateGrowth(latest.NetProfit, yoy.NetProfit),
                    EpsGrowthPercent = CalculateGrowth(latest.Eps, yoy.Eps),
                    DepreciationGrowthPercent = CalculateGrowth(latest.Depreciation, yoy.Depreciation),
                    InterestGrowthPercent = CalculateGrowth(latest.Interest, yoy.Interest),
                    TaxGrowthPercent = CalculateGrowth(latest.Tax, yoy.Tax)
                };
            }

            return response;
        }

        private static decimal? CalculateGrowth(decimal? current, decimal? previous)
        {
            if (!current.HasValue || !previous.HasValue || previous.Value == 0)
            {
                return null;
            }

            var growth = ((current.Value - previous.Value) / Math.Abs(previous.Value)) * 100m;
            return Math.Round(growth, 2);
        }
    }
}
