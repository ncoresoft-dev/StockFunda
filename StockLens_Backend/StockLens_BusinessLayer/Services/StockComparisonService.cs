using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockComparisonService : IStockComparisonService
    {
        private readonly ICompanyService _companyService;
        private readonly IStockEvaluationService _evaluationService;
        private readonly IPeerService _peerService;
        private readonly IStockPriceHistoryService _priceHistoryService;
        private readonly IStockQuarterlyResultsService _quarterlyResultsService;
        private readonly IStockShareholdingService _shareholdingService;
        private readonly IStockCashflowService _cashflowService;
        private readonly IStockRepository _stockRepository;
        private readonly ICompanyRepository _companyRepository;
        private readonly IStockFinancialRepository _financialRepository;
        private readonly IStockShareholdingRepository _shareholdingRepository;
        private readonly IStockPeerRepository _stockPeerRepository;
        private readonly IStockPriceHistoryRepository _priceHistoryRepository;
        private readonly IMemoryCache _cache;
        private readonly ILogger<StockComparisonService> _logger;

        private static readonly List<ComparisonPresetDto> Presets = new()
        {
            new ComparisonPresetDto { Stock1 = "TCS", Stock2 = "INFY", Title = "TCS vs Infosys", Category = "IT & Tech" },
            new ComparisonPresetDto { Stock1 = "HDFCBANK", Stock2 = "ICICIBANK", Title = "HDFC Bank vs ICICI Bank", Category = "Banking" },
            new ComparisonPresetDto { Stock1 = "TATAMOTORS", Stock2 = "MARUTI", Title = "Tata Motors vs Maruti", Category = "Automobile" },
            new ComparisonPresetDto { Stock1 = "RELIANCE", Stock2 = "ADANIENT", Title = "Reliance vs Adani Enterprises", Category = "Conglomerates" },
            new ComparisonPresetDto { Stock1 = "SUNPHARMA", Stock2 = "CIPLA", Title = "Sun Pharma vs Cipla", Category = "Pharma" },
            new ComparisonPresetDto { Stock1 = "TATASTEEL", Stock2 = "JSWSTEEL", Title = "Tata Steel vs JSW Steel", Category = "Metals & Mining" }
        };

        public StockComparisonService(
            ICompanyService companyService,
            IStockEvaluationService evaluationService,
            IPeerService peerService,
            IStockPriceHistoryService priceHistoryService,
            IStockQuarterlyResultsService quarterlyResultsService,
            IStockShareholdingService shareholdingService,
            IStockCashflowService cashflowService,
            IStockRepository stockRepository,
            ICompanyRepository companyRepository,
            IStockFinancialRepository financialRepository,
            IStockShareholdingRepository shareholdingRepository,
            IStockPeerRepository stockPeerRepository,
            IStockPriceHistoryRepository priceHistoryRepository,
            IMemoryCache cache,
            ILogger<StockComparisonService> logger)
        {
            _companyService = companyService;
            _evaluationService = evaluationService;
            _peerService = peerService;
            _priceHistoryService = priceHistoryService;
            _quarterlyResultsService = quarterlyResultsService;
            _shareholdingService = shareholdingService;
            _cashflowService = cashflowService;
            _stockRepository = stockRepository;
            _companyRepository = companyRepository;
            _financialRepository = financialRepository;
            _shareholdingRepository = shareholdingRepository;
            _stockPeerRepository = stockPeerRepository;
            _priceHistoryRepository = priceHistoryRepository;
            _cache = cache;
            _logger = logger;
        }

        public IReadOnlyList<ComparisonPresetDto> GetComparisonPresets() => Presets;

        public async Task<StockComparisonDto> CompareStocksAsync(
            string symbolA,
            string symbolB,
            string? exchange = "NSE",
            string period = "1yr",
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            var cleanSymbolA = symbolA?.Trim().ToUpperInvariant() ?? string.Empty;
            var cleanSymbolB = symbolB?.Trim().ToUpperInvariant() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(cleanSymbolA) || string.IsNullOrWhiteSpace(cleanSymbolB))
            {
                throw new ArgumentException("Both stock symbols are required for comparison.");
            }

            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
            var cleanPeriod = string.IsNullOrWhiteSpace(period) ? "1yr" : period.Trim().ToLowerInvariant();

            // Cache-first profile retrieval ensures identical values across tabs & orders
            var profileA = await GetOrBuildProfileAsync(cleanSymbolA, cleanExchange, refresh, cancellationToken);
            var profileB = await GetOrBuildProfileAsync(cleanSymbolB, cleanExchange, refresh, cancellationToken);
            var historyA = await GetOrFetchPriceHistoryAsync(cleanSymbolA, cleanExchange, cleanPeriod, refresh, cancellationToken);
            var historyB = await GetOrFetchPriceHistoryAsync(cleanSymbolB, cleanExchange, cleanPeriod, refresh, cancellationToken);

            var normalizedHistory = BuildNormalizedHistory(historyA, historyB);
            var headToHead = ComputeHeadToHead(profileA, profileB);

            return new StockComparisonDto
            {
                StockA = profileA,
                StockB = profileB,
                HeadToHead = headToHead,
                NormalizedHistory = normalizedHistory,
                Period = cleanPeriod,
                GeneratedAt = DateTime.UtcNow
            };
        }

        private async Task<StockComparisonProfileDto> GetOrBuildProfileAsync(
            string symbol,
            string exchange,
            bool refresh,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"StockComparison_Profile_{symbol}_{exchange}";

            if (!refresh && _cache.TryGetValue(cacheKey, out StockComparisonProfileDto? cached) && cached != null)
            {
                return cached;
            }

            var profile = await BuildProfileAsync(symbol, exchange, refresh, cancellationToken);

            if (profile != null)
            {
                _cache.Set(cacheKey, profile, TimeSpan.FromMinutes(15));
            }

            return profile;
        }

        private async Task<PriceHistoryResponseDto?> GetOrFetchPriceHistoryAsync(
            string symbol,
            string exchange,
            string period,
            bool refresh,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"StockComparison_History_{symbol}_{exchange}_{period}";

            if (!refresh && _cache.TryGetValue(cacheKey, out PriceHistoryResponseDto? cached) && cached != null)
            {
                return cached;
            }

            var history = await SafeGetPriceHistoryAsync(symbol, exchange, period, refresh, cancellationToken);

            if (history != null && history.Dates != null && history.Dates.Count > 0)
            {
                _cache.Set(cacheKey, history, TimeSpan.FromMinutes(15));
            }

            return history;
        }

        private async Task<StockComparisonProfileDto> BuildProfileAsync(
            string symbol,
            string? exchange,
            bool refresh,
            CancellationToken cancellationToken)
        {
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
            var profile = new StockComparisonProfileDto
            {
                Symbol = symbol,
                CompanyName = symbol
            };

            // 1. Resolve stock entity from DB (or create placeholder if totally new)
            var stock = await _stockRepository.GetBySymbolAsync(symbol, cleanExchange);
            if (stock == null)
            {
                stock = await _stockRepository.GetOrCreateStockAsync(symbol, cleanExchange, cancellationToken: cancellationToken);
            }

            // 2. Company Overview (Cache-First from DB CompanyMaster)
            var company = await _companyRepository.GetCompanyBySymbolAsync(symbol);
            if (company != null && !string.IsNullOrWhiteSpace(company.CompanyName))
            {
                profile.CompanyName = company.CompanyName;
                profile.Sector = company.Sector;
                profile.Industry = company.Industry;
                profile.LogoUrl = company.LogoUrl;
                profile.WebsiteUrl = company.WebsiteUrl;
                profile.KeyPoints = ParseKeyPoints(company.KeyPointsJson, company.About);
            }
            else
            {
                // Fallback to overview service with safe forceRefresh=false
                var overview = await SafeExecuteAsync(() => _companyService.GetCompanyOverviewAsync(symbol, cleanExchange, false, cancellationToken));
                if (overview != null)
                {
                    profile.CompanyName = !string.IsNullOrWhiteSpace(overview.CompanyName) ? overview.CompanyName : symbol;
                    profile.Sector = overview.Sector;
                    profile.Industry = overview.Industry;
                    profile.LogoUrl = overview.LogoUrl;
                    profile.WebsiteUrl = overview.WebsiteUrl;
                    profile.KeyPoints = overview.KeyPoints ?? new List<string>();
                }
            }

            // 3. Valuation & Financial Ratios (MarketCap, P/E, P/B, ROE, ROCE, DividendYield)
            // Primary Source of Truth: Annual StockFinancials (Identical to Stock Detail / Cashflow Page)
            bool financialsLoaded = false;
            if (stock != null)
            {
                var annualFinancials = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "annual", 5);
                if (annualFinancials != null && annualFinancials.Count > 0)
                {
                    var cur = annualFinancials[0];
                    profile.CurrentPrice = cur.CurrentPrice;
                    profile.MarketCap = cur.MarketCap;
                    profile.PeRatio = cur.PeRatio;
                    profile.PbRatio = cur.PbRatio;
                    profile.Roe = cur.Roe;
                    profile.Roce = cur.Roce;
                    profile.DividendYield = cur.DividendYield;
                    financialsLoaded = profile.MarketCap.HasValue && profile.PeRatio.HasValue;
                }
            }

            // Fallback: If not found in StockFinancials, try CashflowService
            if (!financialsLoaded)
            {
                var cashflow = await SafeExecuteAsync(() => _cashflowService.GetCashflowBySymbolAsync(symbol, cleanExchange, false, cancellationToken));
                if (cashflow?.Ratios != null)
                {
                    profile.CurrentPrice ??= cashflow.Ratios.CurrentPrice;
                    profile.MarketCap ??= cashflow.Ratios.MarketCap;
                    profile.PeRatio ??= cashflow.Ratios.PeRatio;
                    profile.PbRatio ??= cashflow.Ratios.PbRatio;
                    profile.Roe ??= cashflow.Ratios.Roe;
                    profile.Roce ??= cashflow.Ratios.Roce;
                    profile.DividendYield ??= cashflow.Ratios.DividendYield;
                    financialsLoaded = profile.MarketCap.HasValue && profile.PeRatio.HasValue;
                }
            }

            // Secondary Fallback: StockPeers (if fundamentals not yet synced)
            if (!financialsLoaded && stock != null)
            {
                var dbPeers = await _stockPeerRepository.GetPeersByStockIdAsync(stock.Id);
                if (dbPeers != null && dbPeers.Count > 0)
                {
                    var match = dbPeers.FirstOrDefault(p =>
                        p.CompanyName.Equals(profile.CompanyName, StringComparison.OrdinalIgnoreCase) ||
                        p.CompanyName.Contains(symbol, StringComparison.OrdinalIgnoreCase)) ?? dbPeers.FirstOrDefault();

                    if (match != null)
                    {
                        profile.CurrentPrice ??= match.Price;
                        profile.MarketCap ??= match.MarketCap;
                        profile.PeRatio ??= match.PeRatio;
                        profile.PbRatio ??= match.PbRatio;
                        profile.Roe ??= match.Roe;
                        profile.Roce ??= match.Roce;
                        profile.DividendYield ??= match.DividendYield;
                    }
                }
            }

            if (!financialsLoaded && profile.PeRatio == null)
            {
                var peers = await SafeExecuteAsync(() => _peerService.GetStockPeersAsync(symbol, cleanExchange, false, cancellationToken));
                if (peers != null && peers.Count > 0)
                {
                    var match = peers.FirstOrDefault(p =>
                        p.CompanyName.Equals(profile.CompanyName, StringComparison.OrdinalIgnoreCase) ||
                        p.CompanyName.Contains(symbol, StringComparison.OrdinalIgnoreCase)) ?? peers.FirstOrDefault();

                    if (match != null)
                    {
                        profile.CurrentPrice ??= match.Price;
                        profile.MarketCap ??= match.MarketCap;
                        profile.PeRatio ??= match.PeRatio;
                        profile.PbRatio ??= match.PbRatio;
                        profile.Roe ??= match.Roe;
                        profile.Roce ??= match.Roce;
                        profile.DividendYield ??= match.DividendYield;
                    }
                }
            }

            // 4. Quarterly Results & YoY/QoQ Growth (Primary: IStockQuarterlyResultsService - Exact same source as Stock Detail Page)
            var qtr = await SafeExecuteAsync(() => _quarterlyResultsService.GetQuarterlyResultsBySymbolAsync(symbol, cleanExchange, false, cancellationToken));
            if (qtr != null)
            {
                profile.SalesQuarterlyGrowthYoY = qtr.YoYGrowth?.SalesGrowthPercent;
                profile.SalesQuarterlyGrowthQoQ = qtr.QoQGrowth?.SalesGrowthPercent;
                profile.ProfitQuarterlyGrowthYoY = qtr.YoYGrowth?.NetProfitGrowthPercent;
                profile.ProfitQuarterlyGrowthQoQ = qtr.QoQGrowth?.NetProfitGrowthPercent;
                profile.OperatingMargin = qtr.Summary?.OpmPercentage;

                // If OPM is not in summary, look for it in recent quarterly history
                if (profile.OperatingMargin == null && qtr.History != null)
                {
                    var qWithMargin = qtr.History.FirstOrDefault(h => h.OpmPercentage.HasValue && h.OpmPercentage > 0);
                    if (qWithMargin != null)
                    {
                        profile.OperatingMargin = qWithMargin.OpmPercentage;
                    }
                }
            }

            // Fallback: If DB query needed or quarterly service returned null YoY/QoQ/Margin
            if (stock != null && (profile.SalesQuarterlyGrowthQoQ == null || profile.SalesQuarterlyGrowthYoY == null || profile.OperatingMargin == null))
            {
                var dbQuarters = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "quarterly", limit: 12);
                if (dbQuarters != null && dbQuarters.Count > 0)
                {
                    // Deduplicate by Year-Month to eliminate duplicate rows synced from different sources
                    var uniqueQuarters = dbQuarters
                        .GroupBy(e => e.PeriodEndDate.HasValue
                            ? e.PeriodEndDate.Value.ToString("yyyy-MM")
                            : (!string.IsNullOrWhiteSpace(e.FiscalYear) ? e.FiscalYear.Trim().ToUpperInvariant() : e.PeriodKey))
                        .Select(g => g.OrderByDescending(x => x.LastSyncedAt).First())
                        .OrderByDescending(q => q.PeriodEndDate ?? DateTime.MinValue)
                        .ToList();

                    var latest = uniqueQuarters.FirstOrDefault();
                    if (latest != null)
                    {
                        profile.OperatingMargin ??= latest.OperatingProfitMargin;

                        if (profile.OperatingMargin == null && latest.OperatingProfit.HasValue && latest.Revenue.HasValue && latest.Revenue.Value > 0)
                        {
                            profile.OperatingMargin = Math.Round((latest.OperatingProfit.Value / latest.Revenue.Value) * 100m, 2);
                        }

                        if (profile.OperatingMargin == null)
                        {
                            var qWithMargin = uniqueQuarters.FirstOrDefault(q => q.OperatingProfitMargin.HasValue || (q.OperatingProfit.HasValue && q.Revenue.HasValue && q.Revenue.Value > 0));
                            if (qWithMargin != null)
                            {
                                profile.OperatingMargin = qWithMargin.OperatingProfitMargin ?? 
                                    Math.Round((qWithMargin.OperatingProfit!.Value / qWithMargin.Revenue!.Value) * 100m, 2);
                            }
                        }

                        // QoQ: Exactly 1 quarter prior (index 1 in unique sorted list)
                        if (uniqueQuarters.Count >= 2)
                        {
                            var prev = uniqueQuarters[1];
                            if (profile.SalesQuarterlyGrowthQoQ == null && latest.Revenue.HasValue && prev.Revenue.HasValue && prev.Revenue.Value > 0)
                            {
                                profile.SalesQuarterlyGrowthQoQ = Math.Round(((latest.Revenue.Value - prev.Revenue.Value) / prev.Revenue.Value) * 100m, 2);
                            }
                            if (profile.ProfitQuarterlyGrowthQoQ == null && latest.NetProfit.HasValue && prev.NetProfit.HasValue && prev.NetProfit.Value != 0)
                            {
                                profile.ProfitQuarterlyGrowthQoQ = Math.Round(((latest.NetProfit.Value - prev.NetProfit.Value) / Math.Abs(prev.NetProfit.Value)) * 100m, 2);
                            }
                        }

                        // YoY: Exactly 4 quarters prior (index 4 in a unique sorted list)
                        if (profile.SalesQuarterlyGrowthYoY == null && uniqueQuarters.Count >= 5)
                        {
                            var yoy = uniqueQuarters[4];
                            if (latest.Revenue.HasValue && yoy.Revenue.HasValue && yoy.Revenue.Value > 0)
                            {
                                profile.SalesQuarterlyGrowthYoY = Math.Round(((latest.Revenue.Value - yoy.Revenue.Value) / yoy.Revenue.Value) * 100m, 2);
                            }
                            if (latest.NetProfit.HasValue && yoy.NetProfit.HasValue && yoy.NetProfit.Value != 0)
                            {
                                profile.ProfitQuarterlyGrowthYoY = Math.Round(((latest.NetProfit.Value - yoy.NetProfit.Value) / Math.Abs(yoy.NetProfit.Value)) * 100m, 2);
                            }
                        }
                    }
                }
            }

            // Fallback to Annual Financials for OperatingMargin if still null
            if (stock != null && profile.OperatingMargin == null)
            {
                var annuals = await _financialRepository.GetFinancialsByStockIdAsync(stock.Id, "annual", 1);
                var annCur = annuals?.FirstOrDefault();
                if (annCur != null)
                {
                    profile.OperatingMargin = annCur.OperatingProfitMargin;
                    if (profile.OperatingMargin == null && annCur.OperatingProfit.HasValue && annCur.Revenue.HasValue && annCur.Revenue.Value > 0)
                    {
                        profile.OperatingMargin = Math.Round((annCur.OperatingProfit.Value / annCur.Revenue.Value) * 100m, 2);
                    }
                }
            }

            // 5. Shareholding Pattern (Reuse existing DB shareholding, changes once every 90 days)
            bool shareholdingLoaded = false;
            if (stock != null)
            {
                var dbSh = await _shareholdingRepository.GetShareholdingsByStockIdAsync(stock.Id, 4);
                if (dbSh != null && dbSh.Count > 0)
                {
                    var latestSh = dbSh.OrderByDescending(s => s.PeriodDate ?? DateTime.MinValue).FirstOrDefault();
                    if (latestSh != null)
                    {
                        profile.PromoterHolding = latestSh.PromoterHolding;
                        profile.FiiHolding = latestSh.FiiHolding;
                        profile.DiiHolding = latestSh.DiiHolding;
                        profile.PublicHolding = latestSh.PublicHolding;
                        shareholdingLoaded = true;
                    }
                }
            }

            if (!shareholdingLoaded)
            {
                var sh = await SafeExecuteAsync(() => _shareholdingService.GetShareholdingBySymbolAsync(symbol, cleanExchange, false, cancellationToken));
                if (sh?.CurrentPeriod != null)
                {
                    profile.PromoterHolding = sh.CurrentPeriod.Promoter;
                    profile.FiiHolding = sh.CurrentPeriod.Fii;
                    profile.DiiHolding = sh.CurrentPeriod.Dii;
                    profile.PublicHolding = sh.CurrentPeriod.Public;
                }
            }

            // 6. Fundamental Health Score & Evaluation
            var eval = await SafeExecuteAsync(() => _evaluationService.EvaluateStockAsync(symbol, cleanExchange, refresh, cancellationToken));
            if (eval != null)
            {
                profile.HealthScore = eval.TotalScore;
                profile.Signal = eval.Signal;
                profile.SignalClass = eval.SignalClass;
                profile.Pros = eval.Pros ?? new List<string>();
                profile.Cons = eval.Cons ?? new List<string>();
                if (!string.IsNullOrWhiteSpace(eval.CompanyName) && profile.CompanyName == symbol)
                {
                    profile.CompanyName = eval.CompanyName;
                }
            }

            return profile;
        }

        private async Task<PriceHistoryResponseDto?> SafeGetPriceHistoryAsync(
            string symbol,
            string? exchange,
            string period,
            bool refresh,
            CancellationToken cancellationToken)
        {
            try
            {
                var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
                var stock = await _stockRepository.GetBySymbolAsync(symbol, cleanExchange);

                // 1. Try reading directly from local DB PriceHistory records first (Zero external API call)
                if (stock != null)
                {
                    var fromDate = GetPeriodStartDate(period);
                    var pricePoints = await _priceHistoryRepository.GetPricePointsSinceDateAsync(stock.Id, fromDate, cancellationToken);
                    if (pricePoints != null && pricePoints.Count >= 5)
                    {
                        return new PriceHistoryResponseDto
                        {
                            Symbol = symbol,
                            Dates = pricePoints.Select(p => p.Date.ToString("yyyy-MM-dd")).ToList(),
                            ClosePrices = pricePoints.Select(p => p.Close).ToList(),
                            Source = "DatabaseCache"
                        };
                    }
                }

                // 2. Only if DB doesn't have sufficient historical prices, call PriceHistoryService with safe refresh=false
                return await _priceHistoryService.GetPriceHistoryBySymbolAsync(symbol, exchange, period, false, "price", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch price history for {Symbol} during stock comparison.", symbol);
                return null;
            }
        }

        private static DateTime GetPeriodStartDate(string period)
        {
            var now = DateTime.UtcNow;
            return (period ?? "1yr").ToLowerInvariant().Trim() switch
            {
                "1m" or "1mo" => now.AddMonths(-1),
                "3m" or "3mo" => now.AddMonths(-3),
                "6m" or "6mo" => now.AddMonths(-6),
                "1y" or "1yr" => now.AddYears(-1),
                "3y" or "3yr" => now.AddYears(-3),
                "5y" or "5yr" => now.AddYears(-5),
                _ => now.AddYears(-1)
            };
        }

        private static List<string> ParseKeyPoints(string? keyPointsJson, string? about)
        {
            if (!string.IsNullOrWhiteSpace(keyPointsJson))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(keyPointsJson);
                    if (list != null && list.Count > 0)
                    {
                        return list;
                    }
                }
                catch
                {
                    // Ignore JSON parsing failure
                }
            }

            if (!string.IsNullOrWhiteSpace(about))
            {
                return about
                    .Split(new[] { '\n', '.', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 20)
                    .Take(4)
                    .ToList();
            }

            return new List<string>();
        }

        private async Task<T?> SafeExecuteAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Safe sub-task failed during comparison aggregation.");
                return default;
            }
        }

        private List<StockComparisonPricePointDto> BuildNormalizedHistory(
            PriceHistoryResponseDto? historyA,
            PriceHistoryResponseDto? historyB)
        {
            var result = new List<StockComparisonPricePointDto>();

            var datesA = historyA?.Dates ?? new List<string>();
            var pricesA = historyA?.ClosePrices ?? new List<decimal>();

            var datesB = historyB?.Dates ?? new List<string>();
            var pricesB = historyB?.ClosePrices ?? new List<decimal>();

            if (datesA.Count == 0 && datesB.Count == 0)
            {
                return result;
            }

            var mapA = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Min(datesA.Count, pricesA.Count); i++)
            {
                mapA[datesA[i]] = pricesA[i];
            }

            var mapB = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Min(datesB.Count, pricesB.Count); i++)
            {
                mapB[datesB[i]] = pricesB[i];
            }

            // Collect all unique sorted dates
            var allDates = mapA.Keys.Union(mapB.Keys)
                .OrderBy(d => d)
                .ToList();

            if (allDates.Count == 0) return result;

            // Find base prices (first available close price for each)
            decimal? basePriceA = pricesA.FirstOrDefault(p => p > 0);
            decimal? basePriceB = pricesB.FirstOrDefault(p => p > 0);

            foreach (var date in allDates)
            {
                decimal? priceA = mapA.TryGetValue(date, out var pA) ? pA : null;
                decimal? priceB = mapB.TryGetValue(date, out var pB) ? pB : null;

                decimal? returnA = (priceA.HasValue && basePriceA.HasValue && basePriceA.Value > 0)
                    ? Math.Round(((priceA.Value - basePriceA.Value) / basePriceA.Value) * 100, 2)
                    : null;

                decimal? returnB = (priceB.HasValue && basePriceB.HasValue && basePriceB.Value > 0)
                    ? Math.Round(((priceB.Value - basePriceB.Value) / basePriceB.Value) * 100, 2)
                    : null;

                result.Add(new StockComparisonPricePointDto
                {
                    Date = date,
                    StockAPrice = priceA,
                    StockBPrice = priceB,
                    StockAPercentReturn = returnA,
                    StockBPercentReturn = returnB
                });
            }

            return result;
        }

        private StockComparisonHeadToHeadDto ComputeHeadToHead(
            StockComparisonProfileDto a,
            StockComparisonProfileDto b)
        {
            var h2h = new StockComparisonHeadToHeadDto();

            // 1. Overall Score
            if (a.HealthScore > b.HealthScore) h2h.OverallScoreWinner = "StockA";
            else if (b.HealthScore > a.HealthScore) h2h.OverallScoreWinner = "StockB";
            else h2h.OverallScoreWinner = "Tie";

            // 2. Valuation (Lower P/E is better, assuming positive P/E)
            if (a.PeRatio.HasValue && a.PeRatio > 0 && b.PeRatio.HasValue && b.PeRatio > 0)
            {
                if (a.PeRatio < b.PeRatio) h2h.ValuationAdvantage = "StockA";
                else if (b.PeRatio < a.PeRatio) h2h.ValuationAdvantage = "StockB";
                else h2h.ValuationAdvantage = "Tie";
            }
            else if (a.PbRatio.HasValue && b.PbRatio.HasValue)
            {
                h2h.ValuationAdvantage = a.PbRatio < b.PbRatio ? "StockA" : (b.PbRatio < a.PbRatio ? "StockB" : "Tie");
            }

            // 3. Profitability (Higher ROE is better)
            if (a.Roe.HasValue && b.Roe.HasValue)
            {
                h2h.ProfitabilityAdvantage = a.Roe > b.Roe ? "StockA" : (b.Roe > a.Roe ? "StockB" : "Tie");
            }
            else if (a.Roce.HasValue && b.Roce.HasValue)
            {
                h2h.ProfitabilityAdvantage = a.Roce > b.Roce ? "StockA" : (b.Roce > a.Roce ? "StockB" : "Tie");
            }

            // 4. Growth (Higher Sales YoY Growth is better)
            if (a.SalesQuarterlyGrowthYoY.HasValue && b.SalesQuarterlyGrowthYoY.HasValue)
            {
                h2h.GrowthAdvantage = a.SalesQuarterlyGrowthYoY > b.SalesQuarterlyGrowthYoY ? "StockA" :
                    (b.SalesQuarterlyGrowthYoY > a.SalesQuarterlyGrowthYoY ? "StockB" : "Tie");
            }
            else if (a.ProfitQuarterlyGrowthYoY.HasValue && b.ProfitQuarterlyGrowthYoY.HasValue)
            {
                h2h.GrowthAdvantage = a.ProfitQuarterlyGrowthYoY > b.ProfitQuarterlyGrowthYoY ? "StockA" :
                    (b.ProfitQuarterlyGrowthYoY > a.ProfitQuarterlyGrowthYoY ? "StockB" : "Tie");
            }

            // Construct meaningful summary verdict
            var higherScoreStock = h2h.OverallScoreWinner == "StockA" ? a.Symbol : (h2h.OverallScoreWinner == "StockB" ? b.Symbol : null);
            var lowerValuationStock = h2h.ValuationAdvantage == "StockA" ? a.Symbol : (h2h.ValuationAdvantage == "StockB" ? b.Symbol : null);
            var higherRoeStock = h2h.ProfitabilityAdvantage == "StockA" ? a.Symbol : (h2h.ProfitabilityAdvantage == "StockB" ? b.Symbol : null);

            var verdictParts = new List<string>();
            if (!string.IsNullOrEmpty(higherScoreStock))
            {
                verdictParts.Add($"{higherScoreStock} leads in overall fundamental health score ({Math.Max(a.HealthScore, b.HealthScore)}/100).");
            }
            if (!string.IsNullOrEmpty(lowerValuationStock))
            {
                verdictParts.Add($"{lowerValuationStock} trades at an attractive valuation multiple.");
            }
            if (!string.IsNullOrEmpty(higherRoeStock) && higherRoeStock != higherScoreStock)
            {
                verdictParts.Add($"{higherRoeStock} shows stronger return on equity (ROE).");
            }

            h2h.SummaryVerdict = verdictParts.Count > 0
                ? string.Join(" ", verdictParts)
                : $"{a.Symbol} and {b.Symbol} exhibit balanced comparative metrics.";

            return h2h;
        }
    }
}
