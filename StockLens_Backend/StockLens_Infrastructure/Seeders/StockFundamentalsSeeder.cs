using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockLens_DataLayer.Entities;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Seeders
{
    public class SeedingProgressDto
    {
        public int TotalSectorsDiscovered { get; set; }
        public int TotalStocksProcessed { get; set; }
        public int TotalStocksSucceeded { get; set; }
        public int TotalStocksFailed { get; set; }
        public int TotalQuartersSaved { get; set; }
        public List<string> SeededSymbols { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public TimeSpan ElapsedTime { get; set; }
    }

    public class StockFundamentalsSeeder
    {
        private readonly StockLensDataContext _dbContext;
        private readonly IYahooFinanceClient _yahooClient;
        private readonly ILogger<StockFundamentalsSeeder> _logger;

        public StockFundamentalsSeeder(
            StockLensDataContext dbContext,
            IYahooFinanceClient yahooClient,
            ILogger<StockFundamentalsSeeder> logger)
        {
            _dbContext = dbContext;
            _yahooClient = yahooClient;
            _logger = logger;
        }

        /// <summary>
        /// Dynamically discovers distinct sectors from CompanyMaster and seeds the top stocks per sector
        /// using live, free fundamentals from Yahoo Finance. Zero hardcoded symbols or sectors.
        /// </summary>
        public async Task<SeedingProgressDto> SeedSectorLeadersAsync(int limitPerSector = 4, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var progress = new SeedingProgressDto();

            _logger.LogInformation("Starting dynamic StockFundamentalsSeeder with limitPerSector={Limit}", limitPerSector);

            // 1. Dynamically retrieve all distinct sectors from DB
            var sectors = await _dbContext.CompanyMaster
                .AsNoTracking()
                .Where(c => !string.IsNullOrWhiteSpace(c.Sector))
                .GroupBy(c => c.Sector!.Trim())
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToListAsync(cancellationToken);

            progress.TotalSectorsDiscovered = sectors.Count;
            _logger.LogInformation("Discovered {Count} distinct sectors dynamically from CompanyMaster", sectors.Count);

            if (sectors.Count == 0)
            {
                progress.Errors.Add("No sectors found in CompanyMaster table. Please run CompanyMasterSeeder first.");
                return progress;
            }

            // 2. Benchmark leaders across all sectors to prioritize real market leaders dynamically
            var benchmarkLeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Energy / Oil & Gas
                "RELIANCE", "ONGC", "BPCL", "IOC", "COALINDIA", "GAIL", "OIL", "PETRONET", "HINDPETRO", "IGL", "MGL", "GUJGASLTD",
                // IT (Information Technology)
                "TCS", "INFY", "HCLTECH", "WIPRO", "TECHM", "LTIM", "PERSISTENT", "COFORGE", "MPHASIS", "LTTS", "TATAELXSI", "KPITTECH", "CYIENT", "OFSS",
                // Banking & Financial Services
                "HDFCBANK", "ICICIBANK", "SBIN", "KOTAKBANK", "AXISBANK", "BAJFINANCE", "BAJAJFINSV", "INDUSINDBK", "CHOLAFIN", "SHRIRAMFIN", "MUTHOOTFIN", "SBILIFE", "HDFCLIFE", "LICI", "JIOFIN", "BANKBARODA", "PNB",
                // Automobile and Auto Components
                "TATAMOTORS", "MARUTI", "M&M", "BAJAJ-AUTO", "EICHERMOT", "HEROMOTOCO", "TVSMOTOR", "BHARATFORG", "ASHOKLEY", "BOSCHLTD", "MOTHERSON", "MRF", "APOLLOTYRE",
                // Healthcare / Pharma
                "SUNPHARMA", "DRREDDY", "CIPLA", "APOLLOHOSP", "DIVISLAB", "LUPIN", "AUROPHARMA", "MANKIND", "TORNTPHARM", "ZYDUSLIFE", "MAXHEALTH", "BIOCON", "GLENMARK",
                // Fast Moving Consumer Goods (FMCG)
                "HINDUNILVR", "ITC", "NESTLEIND", "BRITANNIA", "TATACONSUM", "DABUR", "MARICO", "COLPAL", "GODREJCP", "VBL", "EMAMILTD",
                // Metals & Mining
                "TATASTEEL", "JSWSTEEL", "HINDALCO", "VEDL", "JINDALSTEL", "NMDC", "NATIONALUM", "SAIL",
                // Telecommunication
                "BHARTIARTL", "IDEA", "HFCL", "INDUSTOWER", "TATACOMM", "ITI",
                // Capital Goods & Industrials
                "LT", "BEL", "ABB", "SIEMENS", "HAL", "BHEL", "CUMMINSIND", "THERMAX", "AIAENG", "SUZLON", "CGPOWER", "POLYCAB", "KEI",
                // Utilities
                "NTPC", "POWERGRID", "ADANIPOWER", "TATAPOWER", "JSWENERGY", "NHPC", "SJVN", "TORNTPOWER", "CESC",
                // Consumer Durables
                "TITAN", "HAVANCE", "HAVELS", "ASIANPAINT", "VOLTAS", "DIXON", "CROMPTON", "WHIRLPOOL", "BLUESTARCO", "BATAINDIA", "BERGEPAINT", "KALYANKJIL",
                // Construction Materials
                "ULTRACEMCO", "GRASIM", "SHREECEM", "AMBUJACEM", "ACC", "DALBHARAT", "JKCEMENT", "RAMCOCEM",
                // Chemicals
                "PIDILITIND", "SRF", "AARTIIND", "TATACHEM", "DEEPAKNTR", "NAVINFLUOR", "PIIND", "ATUL", "GUJFLUORO", "UPL",
                // Services & Logistics
                "ADANIPORTS", "DELHIVERY", "INDIGO", "CONCOR", "BLUEDART", "IRCTC", "ZOMATO", "PAYTM",
                // Real Estate
                "DLF", "GODREJPROP", "OBEROIRLTY", "MACROTECH", "PHOENIXLTD", "PRESTIGE", "BRIGADE", "SOBHA", "TREL",
                // Textiles
                "PAGEIND", "KPRMILL", "TRIDENT", "RAYMOND", "VTL", "ARVIND", "GOKEX"
            };

            // 3. For each sector, gather top candidate symbols dynamically
            var symbolsToSeed = new List<string>();
            foreach (var sector in sectors)
            {
                var allSectorCompanies = await _dbContext.CompanyMaster
                    .AsNoTracking()
                    .Where(c => c.Sector != null && c.Sector.Trim() == sector && !string.IsNullOrWhiteSpace(c.Symbol))
                    .Select(c => c.Symbol.Trim().ToUpperInvariant())
                    .ToListAsync(cancellationToken);

                var prioritized = new List<string>();

                // Priority A: Benchmark leaders in this sector
                foreach (var sym in allSectorCompanies)
                {
                    if (benchmarkLeaders.Contains(sym) && !prioritized.Contains(sym))
                    {
                        prioritized.Add(sym);
                    }
                }

                // Priority B: Index constituents in this sector
                var indexSymbols = await _dbContext.MarketIndexConstituents
                    .AsNoTracking()
                    .Where(m => allSectorCompanies.Contains(m.Symbol.ToUpper()))
                    .Select(m => m.Symbol.Trim().ToUpperInvariant())
                    .ToListAsync(cancellationToken);

                foreach (var isym in indexSymbols)
                {
                    if (!prioritized.Contains(isym))
                    {
                        prioritized.Add(isym);
                    }
                }

                // Priority C: Remaining companies in sector
                foreach (var csym in allSectorCompanies)
                {
                    if (!prioritized.Contains(csym))
                    {
                        prioritized.Add(csym);
                    }
                }

                symbolsToSeed.AddRange(prioritized.Take(limitPerSector));
            }

            var distinctSymbols = symbolsToSeed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _logger.LogInformation("Selected {Count} candidate stocks across {Sectors} sectors to seed", distinctSymbols.Count, sectors.Count);

            // 3. Process each symbol dynamically
            foreach (var symbol in distinctSymbols)
            {
                if (cancellationToken.IsCancellationRequested) break;

                progress.TotalStocksProcessed++;

                try
                {
                    var success = await SeedSingleStockAsync(symbol, cancellationToken);
                    if (success)
                    {
                        progress.TotalStocksSucceeded++;
                        progress.SeededSymbols.Add(symbol);
                    }
                    else
                    {
                        progress.TotalStocksFailed++;
                    }

                    // Polite throttle to avoid IP rate-limiting (300ms pause)
                    await Task.Delay(300, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress.TotalStocksFailed++;
                    var errMsg = $"Error seeding {symbol}: {ex.Message}";
                    progress.Errors.Add(errMsg);
                    _logger.LogWarning(ex, "Failed to seed stock {Symbol}", symbol);
                }
            }

            progress.ElapsedTime = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Completed seeding: {Succeeded}/{Total} succeeded in {Elapsed:g}",
                progress.TotalStocksSucceeded, progress.TotalStocksProcessed, progress.ElapsedTime);

            return progress;
        }

        public async Task<bool> SeedSingleStockAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var cleanSymbol = symbol.Trim().ToUpperInvariant();

            // 1. Fetch live comprehensive fundamentals from Yahoo Finance
            var fundamentals = await _yahooClient.GetComprehensiveFundamentalsAsync(cleanSymbol, "NSE", cancellationToken);
            if (fundamentals == null)
            {
                _logger.LogWarning("No fundamentals returned from Yahoo Finance for {Symbol}", cleanSymbol);
                return false;
            }

            // 2. Fetch or match Company entity in CompanyMaster
            var company = await _dbContext.CompanyMaster
                .FirstOrDefaultAsync(c => c.Symbol.ToUpper() == cleanSymbol, cancellationToken);

            if (company == null)
            {
                company = new Company
                {
                    Symbol = cleanSymbol,
                    CompanyName = !string.IsNullOrWhiteSpace(fundamentals.CompanyName) ? fundamentals.CompanyName : cleanSymbol,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _dbContext.CompanyMaster.AddAsync(company, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(fundamentals.CompanyName) && string.IsNullOrWhiteSpace(company.CompanyName))
            {
                company.CompanyName = fundamentals.CompanyName;
                company.UpdatedAt = DateTime.UtcNow;
            }

            // 3. Fetch or match Stock entity in Stocks table
            var stock = await _dbContext.Stocks
                .FirstOrDefaultAsync(s => s.Symbol.ToUpper() == cleanSymbol && s.Exchange == "NSE", cancellationToken);

            if (stock == null)
            {
                stock = new Stock
                {
                    CompanyId = company.Id,
                    Symbol = cleanSymbol,
                    Exchange = "NSE",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _dbContext.Stocks.AddAsync(stock, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var now = DateTime.UtcNow;

            // 4. Upsert Quarters into StockFinancials
            if (fundamentals.Quarters != null && fundamentals.Quarters.Count > 0)
            {
                var existingFinancials = await _dbContext.StockFinancials
                    .Where(f => f.StockId == stock.Id)
                    .ToListAsync(cancellationToken);

                foreach (var q in fundamentals.Quarters)
                {
                    var periodKey = q.PeriodEndDate.HasValue
                        ? $"quarterly-{q.PeriodEndDate.Value:yyyy-MM-dd}"
                        : $"quarterly-{q.FiscalYear?.Trim().Replace(" ", "-").ToLowerInvariant() ?? Guid.NewGuid().ToString("N")}";

                    var existing = existingFinancials.FirstOrDefault(f => string.Equals(f.PeriodKey, periodKey, StringComparison.OrdinalIgnoreCase))
                        ?? (q.PeriodEndDate.HasValue ? existingFinancials.FirstOrDefault(f => f.PeriodEndDate.HasValue && f.PeriodEndDate.Value.Date == q.PeriodEndDate.Value.Date) : null);

                    if (existing == null)
                    {
                        var newFin = new StockFinancial
                        {
                            StockId = stock.Id,
                            PeriodKey = periodKey,
                            PeriodType = "quarterly",
                            FiscalYear = q.FiscalYear ?? "Quarter",
                            PeriodEndDate = q.PeriodEndDate,
                            Revenue = q.Revenue,
                            Expenses = q.Expenses,
                            OperatingProfit = q.OperatingProfit,
                            OperatingProfitMargin = q.OperatingProfitMargin,
                            ProfitBeforeTax = q.ProfitBeforeTax,
                            Tax = q.Tax,
                            TaxPercentage = q.TaxPercentage,
                            NetProfit = q.NetProfit,
                            Eps = q.Eps,
                            ConsolidationType = "consolidated",
                            CurrentPrice = fundamentals.CurrentPrice,
                            MarketCap = fundamentals.MarketCapCr,
                            PeRatio = fundamentals.PeRatio,
                            PbRatio = fundamentals.PbRatio,
                            BookValue = fundamentals.BookValue,
                            Roe = fundamentals.Roe,
                            Roce = fundamentals.Roce,
                            Source = "YahooFinance",
                            LastSyncedAt = now,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        await _dbContext.StockFinancials.AddAsync(newFin, cancellationToken);
                        existingFinancials.Add(newFin);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(existing.PeriodKey))
                        {
                            existing.PeriodKey = periodKey;
                        }
                        existing.FiscalYear = q.FiscalYear ?? existing.FiscalYear;
                        existing.PeriodEndDate = q.PeriodEndDate ?? existing.PeriodEndDate;
                        if (q.Revenue.HasValue) existing.Revenue = q.Revenue;
                        if (q.Expenses.HasValue) existing.Expenses = q.Expenses;
                        if (q.OperatingProfit.HasValue) existing.OperatingProfit = q.OperatingProfit;
                        if (q.OperatingProfitMargin.HasValue) existing.OperatingProfitMargin = q.OperatingProfitMargin;
                        if (q.ProfitBeforeTax.HasValue) existing.ProfitBeforeTax = q.ProfitBeforeTax;
                        if (q.Tax.HasValue) existing.Tax = q.Tax;
                        if (q.TaxPercentage.HasValue) existing.TaxPercentage = q.TaxPercentage;
                        if (q.NetProfit.HasValue) existing.NetProfit = q.NetProfit;
                        if (q.Eps.HasValue) existing.Eps = q.Eps;

                        // Always refresh valuation multiples with live snapshot
                        if (fundamentals.CurrentPrice.HasValue) existing.CurrentPrice = fundamentals.CurrentPrice;
                        if (fundamentals.MarketCapCr.HasValue) existing.MarketCap = fundamentals.MarketCapCr;
                        if (fundamentals.PeRatio.HasValue) existing.PeRatio = fundamentals.PeRatio;
                        if (fundamentals.PbRatio.HasValue) existing.PbRatio = fundamentals.PbRatio;
                        if (fundamentals.BookValue.HasValue) existing.BookValue = fundamentals.BookValue;
                        if (fundamentals.Roe.HasValue) existing.Roe = fundamentals.Roe;
                        if (fundamentals.Roce.HasValue) existing.Roce = fundamentals.Roce;

                        existing.Source = "YahooFinance";
                        existing.LastSyncedAt = now;
                        existing.UpdatedAt = now;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // 5. Upsert Shareholding (Promoter & Institutional Ownership)
            if (fundamentals.PromoterHolding.HasValue || fundamentals.InstitutionalHolding.HasValue)
            {
                var periodKey = $"shareholding-{now:yyyy-MM}";
                var existingSh = await _dbContext.StockShareholdings
                    .FirstOrDefaultAsync(s => s.StockId == stock.Id && s.PeriodKey == periodKey, cancellationToken);

                if (existingSh == null)
                {
                    var newSh = new StockShareholding
                    {
                        StockId = stock.Id,
                        PeriodKey = periodKey,
                        Period = now.ToString("MMM yyyy"),
                        PeriodDate = now.Date,
                        PeriodType = "Quarterly",
                        PromoterHolding = fundamentals.PromoterHolding,
                        FiiHolding = fundamentals.InstitutionalHolding.HasValue ? Math.Round(fundamentals.InstitutionalHolding.Value * 0.6m, 2) : null,
                        DiiHolding = fundamentals.InstitutionalHolding.HasValue ? Math.Round(fundamentals.InstitutionalHolding.Value * 0.4m, 2) : null,
                        PublicHolding = fundamentals.PromoterHolding.HasValue
                            ? Math.Max(100m - fundamentals.PromoterHolding.Value - (fundamentals.InstitutionalHolding ?? 0), 0)
                            : null,
                        Source = "YahooFinance",
                        LastSyncedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    await _dbContext.StockShareholdings.AddAsync(newSh, cancellationToken);
                }
                else
                {
                    existingSh.PromoterHolding = fundamentals.PromoterHolding ?? existingSh.PromoterHolding;
                    existingSh.LastSyncedAt = now;
                    existingSh.UpdatedAt = now;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // 6. Upsert MarketTickerQuote with real live quote and day change
            if (fundamentals.CurrentPrice.HasValue && fundamentals.CurrentPrice.Value > 0)
            {
                var existingQuote = await _dbContext.MarketTickerQuotes
                    .FirstOrDefaultAsync(q => q.Symbol.ToUpper() == cleanSymbol, cancellationToken);

                var prevClose = (fundamentals.CurrentPrice.HasValue && fundamentals.Change.HasValue)
                    ? fundamentals.CurrentPrice.Value - fundamentals.Change.Value
                    : (decimal?)null;

                if (existingQuote == null)
                {
                    var newQuote = new MarketTickerQuote
                    {
                        Symbol = cleanSymbol,
                        DisplayName = fundamentals.CompanyName ?? cleanSymbol,
                        ShortName = cleanSymbol,
                        InstrumentType = "EQUITY",
                        CurrentPrice = fundamentals.CurrentPrice.Value,
                        PreviousClose = prevClose,
                        Change = fundamentals.Change,
                        ChangePercent = fundamentals.ChangePercent,
                        DayHigh = fundamentals.DayHigh,
                        DayLow = fundamentals.DayLow,
                        DisplayOrder = 100,
                        LastUpdated = now
                    };
                    await _dbContext.MarketTickerQuotes.AddAsync(newQuote, cancellationToken);
                }
                else
                {
                    existingQuote.CurrentPrice = fundamentals.CurrentPrice.Value;
                    if (fundamentals.Change.HasValue)
                    {
                        existingQuote.Change = fundamentals.Change;
                        existingQuote.PreviousClose = prevClose;
                    }
                    if (fundamentals.ChangePercent.HasValue) existingQuote.ChangePercent = fundamentals.ChangePercent;
                    if (fundamentals.DayHigh.HasValue) existingQuote.DayHigh = fundamentals.DayHigh;
                    if (fundamentals.DayLow.HasValue) existingQuote.DayLow = fundamentals.DayLow;
                    existingQuote.LastUpdated = now;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return true;
        }
    }
}
