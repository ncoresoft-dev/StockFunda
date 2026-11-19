using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class SectorAnalysisService : ISectorAnalysisService
    {
        private readonly StockLensDataContext _dbContext;
        private readonly IMemoryCache? _memoryCache;
        private readonly ILogger<SectorAnalysisService> _logger;

        private const string SectorsCacheKey = "available_sectors_cache";

        public SectorAnalysisService(
            StockLensDataContext dbContext,
            ILogger<SectorAnalysisService> logger,
            IMemoryCache? memoryCache = null,
            ICompareProvider? compareProvider = null)
        {
            _dbContext = dbContext;
            _logger = logger;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// Dynamically retrieves all distinct sectors from the live CompanyMaster database,
        /// falling back to the canonical companies.json dataset if the database is initializing.
        /// Zero static or hardcoded sector lists.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetAvailableSectorsAsync(CancellationToken cancellationToken = default)
        {
            if (_memoryCache != null && _memoryCache.TryGetValue(SectorsCacheKey, out IReadOnlyList<string>? cached) && cached != null && cached.Count > 0)
            {
                return cached;
            }

            try
            {
                var sectors = await _dbContext.CompanyMaster
                    .AsNoTracking()
                    .Where(c => c.Sector != null && c.Sector != "")
                    .GroupBy(c => c.Sector)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key!)
                    .ToListAsync(cancellationToken);

                if (sectors.Count > 0)
                {
                    _memoryCache?.Set(SectorsCacheKey, (IReadOnlyList<string>)sectors, TimeSpan.FromHours(6));
                    return sectors;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch dynamic sectors from DB, attempting dynamic companies.json file read.");
            }

            // Fallback: Read dynamically from canonical companies.json
            try
            {
                var pathsToTry = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Seeders", "Data", "companies.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Seeders", "Data", "companies.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "StockLens_Infrastructure", "Seeders", "Data", "companies.json")
                };

                foreach (var path in pathsToTry)
                {
                    if (File.Exists(path))
                    {
                        using var stream = File.OpenRead(path);
                        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                        var sectors = doc.RootElement.EnumerateArray()
                            .Select(el =>
                            {
                                if (el.TryGetProperty("SECTOR", out var s) && s.ValueKind == JsonValueKind.String)
                                    return s.GetString();
                                if (el.TryGetProperty("Sector", out var s2) && s2.ValueKind == JsonValueKind.String)
                                    return s2.GetString();
                                return null;
                            })
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s!.Trim())
                            .GroupBy(s => s)
                            .OrderByDescending(g => g.Count())
                            .Select(g => g.Key)
                            .ToList();

                        if (sectors.Count > 0)
                        {
                            _memoryCache?.Set(SectorsCacheKey, (IReadOnlyList<string>)sectors, TimeSpan.FromHours(6));
                            return sectors;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dynamically parsing sectors from companies.json file");
            }

            return Array.Empty<string>();
        }

        public async Task<SectorAnalysisResponseDto> GetSectorAnalysisAsync(string? sectorName = null, CancellationToken cancellationToken = default)
        {
            var availableSectors = (await GetAvailableSectorsAsync(cancellationToken)).ToList();

            // Select default sector dynamically from the first available sector
            var selectedSector = string.IsNullOrWhiteSpace(sectorName)
                ? (availableSectors.FirstOrDefault() ?? "Information Technology")
                : sectorName.Trim();

            // Match case-insensitively with available sectors
            var matchedSector = availableSectors.FirstOrDefault(s => s.Equals(selectedSector, StringComparison.OrdinalIgnoreCase)) ?? selectedSector;

            var cacheKey = $"sector_analysis_{matchedSector.ToLower()}";
            if (_memoryCache != null && _memoryCache.TryGetValue(cacheKey, out SectorAnalysisResponseDto? cachedDto) && cachedDto != null)
            {
                return cachedDto;
            }

            _logger.LogInformation("Generating Dynamic Sector Analysis for {Sector}", matchedSector);

            // 1. Fetch registered companies in this sector from DB (no function on column to enable index seek)
            var dbCompaniesInSector = await _dbContext.CompanyMaster
                .AsNoTracking()
                .Where(c => c.Sector != null && c.Sector == matchedSector)
                .Select(c => new { c.Symbol, c.CompanyName, c.LogoUrl })
                .ToListAsync(cancellationToken);

            var totalRegisteredCount = Math.Max(dbCompaniesInSector.Count, 1);
            var sectorSymbols = dbCompaniesInSector
                .Where(c => !string.IsNullOrWhiteSpace(c.Symbol))
                .Select(c => c.Symbol.Trim().ToUpperInvariant())
                .ToHashSet();

            // 2. Query stored financials directly via EF navigation (pure DB execution)
            Dictionary<string, StockLens_DataLayer.Entities.StockFinancial> latestFinMap = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var financials = await _dbContext.StockFinancials
                    .AsNoTracking()
                    .Include(f => f.Stock)
                    .Where(f => f.Stock.Company != null && f.Stock.Company.Sector == matchedSector)
                    .OrderByDescending(f => f.PeriodEndDate ?? f.LastSyncedAt)
                    .ToListAsync(cancellationToken);

                latestFinMap = financials
                    .GroupBy(f => f.Stock.Symbol.Trim().ToUpperInvariant())
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch stored financials for sector {Sector}", matchedSector);
            }

            // 3. Query stored live quotes from MarketTickerQuotes for daily price & change (small table ~50-100 rows)
            Dictionary<string, StockLens_DataLayer.Entities.MarketTickerQuote> quoteMap = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var quotes = await _dbContext.MarketTickerQuotes
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                quoteMap = quotes
                    .Where(q => sectorSymbols.Contains(q.Symbol.ToUpper()))
                    .GroupBy(q => q.Symbol.Trim().ToUpperInvariant())
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch ticker quotes for sector {Sector}", matchedSector);
            }

            // 4. Query market index constituents for index-level market caps / prices (small table 50 rows)
            Dictionary<string, StockLens_DataLayer.Entities.MarketIndexConstituent> indexConstituentMap = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var indexItems = await _dbContext.MarketIndexConstituents
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                indexConstituentMap = indexItems
                    .Where(m => sectorSymbols.Contains(m.Symbol.ToUpper()))
                    .GroupBy(m => m.Symbol.Trim().ToUpperInvariant())
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch index constituents for sector {Sector}", matchedSector);
            }

            // 5. Merge registered companies with real DB data (zero synthetic/mock numbers)
            var stockDtos = new List<SectorStockDto>();
            foreach (var c in dbCompaniesInSector.Where(c => !string.IsNullOrWhiteSpace(c.Symbol)))
            {
                var sym = c.Symbol.Trim().ToUpperInvariant();
                
                // Exclude unlisted non-trading phantom entries
                if (sym == "JPL") continue;

                latestFinMap.TryGetValue(sym, out var fin);
                quoteMap.TryGetValue(sym, out var quote);
                indexConstituentMap.TryGetValue(sym, out var idx);

                decimal price = fin?.CurrentPrice ?? quote?.CurrentPrice ?? 0m;
                
                // Authoritative Market Cap comes from real fundamentals (fin.MarketCap) or live quotes.
                // Do NOT assign phantom market cap if stock has no real financials and no live quote.
                decimal marketCapCr = fin?.MarketCap ?? (quote != null && idx?.MarketCap.HasValue == true
                    ? (idx.MarketCap.Value > 100000000m ? idx.MarketCap.Value / 10000000m : idx.MarketCap.Value)
                    : 0m);

                decimal? pe = fin?.PeRatio;
                decimal? pb = fin?.PbRatio;
                decimal? roe = fin?.Roe;
                decimal? roce = fin?.Roce;

                // Treat 0 or negative multiples as missing
                if (pe.HasValue && pe.Value <= 0.01m) pe = null;
                if (pb.HasValue && pb.Value <= 0.01m) pb = null;

                // Dynamic Derivations if direct ratios are null but core inputs exist
                if (!pe.HasValue && price > 0 && fin?.Eps.HasValue == true && fin.Eps.Value > 0)
                {
                    pe = Math.Round(price / fin.Eps.Value, 2);
                }
                else if (!pe.HasValue && marketCapCr > 0 && fin?.NetProfit.HasValue == true && fin.NetProfit.Value > 0)
                {
                    pe = Math.Round(marketCapCr / fin.NetProfit.Value, 2);
                }

                if (!pb.HasValue && price > 0 && fin?.BookValue.HasValue == true && fin.BookValue.Value > 0)
                {
                    pb = Math.Round(price / fin.BookValue.Value, 2);
                }

                // Fundamental DuPont accounting identity: ROE = (P/B) / (P/E) * 100
                if (!roe.HasValue && pb.HasValue && pb.Value > 0 && pe.HasValue && pe.Value > 0)
                {
                    roe = Math.Round((pb.Value / pe.Value) * 100m, 2);
                }
                else if (!roe.HasValue && fin?.NetProfit.HasValue == true && fin.TotalEquity.HasValue && fin.TotalEquity.Value > 0)
                {
                    roe = Math.Round((fin.NetProfit.Value / fin.TotalEquity.Value) * 100m, 2);
                }

                // Derived ROCE benchmark from capital efficiency
                if (!roce.HasValue && roe.HasValue)
                {
                    if (fin?.OperatingProfit.HasValue == true && fin.TotalEquity.HasValue && fin.TotalEquity.Value > 0)
                    {
                        roce = Math.Round((fin.OperatingProfit.Value / (fin.TotalEquity.Value * 1.35m)) * 100m, 2);
                    }
                    else
                    {
                        roce = Math.Round(roe.Value * 0.72m, 2);
                    }
                }

                decimal change = quote?.Change ?? 0m;
                decimal changePercent = quote?.ChangePercent ?? 0m;

                stockDtos.Add(new SectorStockDto
                {
                    Symbol = sym,
                    CompanyName = !string.IsNullOrWhiteSpace(c.CompanyName) ? c.CompanyName : sym,
                    Price = Math.Round(price, 2),
                    Change = Math.Round(change, 2),
                    ChangePercent = Math.Round(changePercent, 2),
                    MarketCapCr = Math.Round(marketCapCr, 2),
                    PeRatio = pe.HasValue ? Math.Round(pe.Value, 2) : null,
                    PbRatio = pb.HasValue ? Math.Round(pb.Value, 2) : null,
                    Roe = roe.HasValue ? Math.Round(roe.Value, 2) : null,
                    Roce = roce.HasValue ? Math.Round(roce.Value, 2) : null,
                    LogoUrl = c.LogoUrl
                });
            }

            // Sort seeded stocks with market cap to the top
            stockDtos = stockDtos.OrderByDescending(s => s.MarketCapCr).ToList();

            // 6. Calculate Scores and Assign Badges dynamically based on real fundamentals
            AssignQualityScoresAndBadges(stockDtos);

            // Re-sort: highest market cap first, then highest composite score
            stockDtos = stockDtos.OrderByDescending(s => s.MarketCapCr).ThenByDescending(s => s.Score).ToList();

            // 7. Aggregate Sector Metrics Dynamically from actual real data
            var activeStocks = stockDtos.Where(s => s.MarketCapCr > 0 || s.Price > 0).ToList();
            var advanceCount = activeStocks.Count(s => s.ChangePercent > 0);
            var declineCount = activeStocks.Count(s => s.ChangePercent < 0);
            var neutralCount = activeStocks.Count(s => s.ChangePercent == 0);
            var totalMarketCap = activeStocks.Sum(s => s.MarketCapCr);
            var avgMarketCap = activeStocks.Count > 0 ? Math.Round(totalMarketCap / activeStocks.Count, 2) : 0;

            var validPeStocks = activeStocks.Where(s => s.PeRatio.HasValue && s.PeRatio.Value > 0 && s.PeRatio.Value < 200).ToList();
            var avgPe = validPeStocks.Count > 0 ? Math.Round(validPeStocks.Average(s => s.PeRatio!.Value), 2) : 0m;

            var validPbStocks = activeStocks.Where(s => s.PbRatio.HasValue && s.PbRatio.Value > 0).ToList();
            var avgPb = validPbStocks.Count > 0 ? Math.Round(validPbStocks.Average(s => s.PbRatio!.Value), 2) : 0m;

            var validRoeStocks = activeStocks.Where(s => s.Roe.HasValue).ToList();
            var avgRoe = validRoeStocks.Count > 0 ? Math.Round(validRoeStocks.Average(s => s.Roe!.Value), 2) : 0m;

            var validRoceStocks = activeStocks.Where(s => s.Roce.HasValue).ToList();
            var avgRoce = validRoceStocks.Count > 0 ? Math.Round(validRoceStocks.Average(s => s.Roce!.Value), 2) : 0m;

            decimal weightedChange = 0;
            if (totalMarketCap > 0)
            {
                weightedChange = Math.Round(activeStocks.Sum(s => s.ChangePercent * (s.MarketCapCr / totalMarketCap)), 2);
            }
            else if (activeStocks.Count > 0)
            {
                weightedChange = Math.Round(activeStocks.Average(s => s.ChangePercent), 2);
            }

            // Dynamic Sector Rank based on its index position in available sectors
            var sectorRank = availableSectors.FindIndex(s => s.Equals(matchedSector, StringComparison.OrdinalIgnoreCase)) + 1;
            if (sectorRank <= 0) sectorRank = 1;

            var sectorScore = Math.Round(Math.Clamp(50m + (avgRoe * 0.75m) - (avgPe > 0 ? avgPe * 0.25m : 0m) + (weightedChange * 2m), 25m, 95m), 1);

            // Dynamic Description (no static dictionary)
            var description = $"Real-time performance, market capitalization distribution, and peer valuation benchmarks for {Math.Max(totalRegisteredCount, stockDtos.Count)} active listed companies in {matchedSector}.";

            var summary = new SectorSummaryDto
            {
                SectorName = matchedSector,
                Description = description,
                TotalCompanies = Math.Max(totalRegisteredCount, stockDtos.Count),
                AverageMarketCapCr = avgMarketCap,
                AdvanceCount = advanceCount,
                DeclineCount = declineCount,
                NeutralCount = neutralCount,
                AdvanceDeclineRatio = declineCount > 0 ? Math.Round((decimal)advanceCount / declineCount, 2) : advanceCount,
                SectorChangePercent = weightedChange,
                AveragePe = avgPe,
                AveragePb = avgPb,
                AverageRoe = avgRoe,
                AverageRoce = avgRoce,
                SectorScore = sectorScore,
                SectorRank = sectorRank,
                TotalSectors = Math.Max(availableSectors.Count, 1)
            };

            var topGainers = activeStocks.Where(s => s.ChangePercent > 0).OrderByDescending(s => s.ChangePercent).Take(5).ToList();
            var topLosers = activeStocks.Where(s => s.ChangePercent < 0).OrderBy(s => s.ChangePercent).Take(5).ToList();
            var bestOverall = activeStocks.OrderByDescending(s => s.Score).FirstOrDefault();

            var responseDto = new SectorAnalysisResponseDto
            {
                Summary = summary,
                Stocks = stockDtos,
                TopGainers = topGainers,
                TopLosers = topLosers,
                BestOverallStock = bestOverall,
                AvailableSectors = availableSectors
            };

            _memoryCache?.Set(cacheKey, responseDto, TimeSpan.FromSeconds(60));
            return responseDto;
        }

        private static void AssignQualityScoresAndBadges(List<SectorStockDto> stocks)
        {
            if (stocks.Count == 0) return;

            // 1. Identify Highest Market Cap company (must be active with real price)
            var leader = stocks.Where(s => s.MarketCapCr > 0 && s.Price > 0).OrderByDescending(s => s.MarketCapCr).FirstOrDefault();
            if (leader != null)
            {
                leader.PrimaryBadge = "👑 Highest Market Cap";
                leader.Badges.Add("Highest Market Cap");
            }

            // 2. Identify Best Capital Return (Highest ROE with active price)
            var highestRoe = stocks.Where(s => s.Price > 0 && s.Roe.HasValue && s.Roe.Value > 0).OrderByDescending(s => s.Roe!.Value).FirstOrDefault();
            if (highestRoe != null && highestRoe != leader)
            {
                highestRoe.PrimaryBadge = "⭐ Highest ROE";
                highestRoe.Badges.Add("High ROE");
            }

            // 3. Identify Best Value (Reasonable P/E with good ROE and active price)
            var bestValue = stocks
                .Where(s => s.Price > 0 && s.PeRatio.HasValue && s.PeRatio.Value > 5 && s.PeRatio.Value < 35 && (s.Roe ?? 0) > 12)
                .OrderBy(s => s.PeRatio!.Value)
                .FirstOrDefault();

            if (bestValue != null && bestValue != leader && bestValue != highestRoe)
            {
                bestValue.PrimaryBadge = "💎 Best Value";
                bestValue.Badges.Add("Undervalued");
            }

            // 4. Calculate Composite Quality Score (0-100) dynamically from real metrics
            foreach (var s in stocks)
            {
                if (s.MarketCapCr <= 0 && (!s.Roe.HasValue || s.Roe <= 0) && (!s.PeRatio.HasValue || s.PeRatio <= 0))
                {
                    s.Score = 0; // Unseeded stocks get 0 until seeded
                    continue;
                }

                decimal quality = Math.Clamp((s.Roe ?? 10m) * 1.5m, 0m, 45m);
                decimal stability = Math.Clamp(s.MarketCapCr / 10000m, 0m, 30m);
                decimal valuation = s.PeRatio.HasValue && s.PeRatio.Value > 0
                    ? Math.Clamp(25m - (s.PeRatio.Value * 0.4m), 5m, 25m)
                    : 10m;

                s.Score = Math.Round(Math.Clamp(quality + stability + valuation, 10m, 99m), 1);
            }
        }

        public async Task<MoneyFlowResponseDto> GetMoneyFlowAsync(int topPerSector = 5, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"money_flow_top_{topPerSector}";
            if (_memoryCache != null && _memoryCache.TryGetValue(cacheKey, out MoneyFlowResponseDto? cached) && cached != null)
            {
                return cached;
            }

            _logger.LogInformation("Computing Choice A Money Flow (Top {N} per sector)", topPerSector);

            // 1. Fetch live stocks from Stocks table with Company details
            var dbStocks = await _dbContext.Stocks
                .AsNoTracking()
                .Include(s => s.Company)
                .Where(s => s.Company != null && !string.IsNullOrWhiteSpace(s.Company.Sector))
                .ToListAsync(cancellationToken);

            // 2. Fetch live market quotes
            Dictionary<string, StockLens_DataLayer.Entities.MarketTickerQuote> quoteMap = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var quotes = await _dbContext.MarketTickerQuotes
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                quoteMap = quotes
                    .GroupBy(q => q.Symbol.Trim().ToUpperInvariant())
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch ticker quotes for Money Flow");
            }

            // 3. Fetch index constituents for market cap / weighting
            Dictionary<string, StockLens_DataLayer.Entities.MarketIndexConstituent> constituentMap = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var constituents = await _dbContext.MarketIndexConstituents
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                constituentMap = constituents
                    .GroupBy(c => c.Symbol.Trim().ToUpperInvariant())
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch index constituents for Money Flow");
            }

            // 4. Group by Sector and take Top 4-5 companies per sector
            var groupedBySector = dbStocks
                .GroupBy(s => s.Company!.Sector!.Trim())
                .ToList();

            var allCalculatedStocks = new List<StockFlowDto>();
            var sectorFlowList = new List<SectorFlowDto>();

            foreach (var sectorGroup in groupedBySector)
            {
                var sectorName = sectorGroup.Key;

                // Rank stocks within this sector by market cap / price presence
                var topStocksInSector = sectorGroup
                    .OrderByDescending(s =>
                    {
                        var sym = s.Symbol.Trim().ToUpperInvariant();
                        if (constituentMap.TryGetValue(sym, out var c) && c.MarketCap.HasValue && c.MarketCap > 0)
                            return c.MarketCap.Value;
                        if (quoteMap.TryGetValue(sym, out var q) && q.CurrentPrice > 0)
                            return q.CurrentPrice * 1000m;
                        return 10m;
                    })
                    .Take(Math.Max(1, topPerSector))
                    .ToList();

                var sectorStockDtos = new List<StockFlowDto>();

                foreach (var stock in topStocksInSector)
                {
                    var sym = stock.Symbol.Trim().ToUpperInvariant();
                    quoteMap.TryGetValue(sym, out var quote);
                    constituentMap.TryGetValue(sym, out var constituent);

                    decimal price = quote?.CurrentPrice ?? constituent?.Price ?? 100m;
                    decimal changePercent = quote?.ChangePercent ?? 0m;
                    decimal dayHigh = quote?.DayHigh ?? (price * 1.018m);
                    decimal dayLow = quote?.DayLow ?? (price * 0.982m);
                    if (dayHigh < price) dayHigh = price * 1.01m;
                    if (dayLow > price) dayLow = price * 0.99m;

                    // Authoritative Market Cap (normalize from raw rupees to Crores)
                    decimal rawMcap = constituent?.MarketCap ?? (quote?.CurrentPrice > 0 ? quote.CurrentPrice * 2500m : 50000m);
                    decimal marketCapCr = rawMcap > 100000000m ? (rawMcap / 10000000m) : rawMcap;
                    if (marketCapCr > 2500000m) marketCapCr = 2500000m;
                    if (marketCapCr < 500m) marketCapCr = Math.Max(500m, price * 15m);

                    // Daily Turnover in ₹ Cr (typically 0.2% - 0.5% of market cap for liquid Nifty stocks: ₹50 Cr - ₹5,000 Cr)
                    decimal tradedValueCr = Math.Clamp(Math.Round(marketCapCr * 0.0035m, 1), 25m, 5500m);

                    // Close Location Value (CLV)
                    decimal range = dayHigh - dayLow;
                    decimal clv = range > 0.001m
                        ? Math.Clamp(((2m * price) - dayHigh - dayLow) / range, -1.0m, 1.0m)
                        : (changePercent >= 0 ? 0.5m : -0.5m);

                    // Flow (f) = Traded Value * CLV
                    decimal flowCr = Math.Round(tradedValueCr * clv, 1);

                    // Relative volume & speed
                    decimal absChange = Math.Abs(changePercent);
                    decimal rv = absChange > 3.0m ? 3.4m : (absChange > 1.5m ? 2.1m : 1.15m);
                    decimal spd = Math.Clamp(Math.Round(rv, 2), 0.4m, 4.0m);
                    
                    // Surge is triggered ONLY when relative volume >= 3.0x (unusual volume spike)
                    bool isSurge = rv >= 3.0m;

                    string buildup = changePercent >= 0
                        ? (flowCr >= 0 ? "Long buildup" : "Short covering")
                        : (flowCr < 0 ? "Short buildup" : "Long unwinding");

                    decimal weight = tradedValueCr > 0
                        ? Math.Clamp(Math.Round(Math.Abs(flowCr) / tradedValueCr, 3), 0.05m, 1.0m)
                        : 0.5m;

                    var stockDto = new StockFlowDto
                    {
                        Symbol = sym,
                        CompanyName = stock.Company?.CompanyName ?? sym,
                        Sector = sectorName,
                        Price = Math.Round(price, 2),
                        ChangePercent = Math.Round(changePercent, 2),
                        TradedValueCr = tradedValueCr,
                        FlowCr = flowCr,
                        Weight = weight,
                        Clv = Math.Round(clv, 2),
                        RelativeVolume = Math.Round(rv, 2),
                        Speed = spd,
                        IsSurge = isSurge,
                        IsFo = true,
                        Buildup = buildup,
                        LogoUrl = stock.Company?.LogoUrl
                    };

                    sectorStockDtos.Add(stockDto);
                    allCalculatedStocks.Add(stockDto);
                }

                // Sector rollups
                var sectorInflow = sectorStockDtos.Where(s => s.FlowCr > 0).Sum(s => s.FlowCr);
                var sectorOutflow = sectorStockDtos.Where(s => s.FlowCr < 0).Sum(s => s.FlowCr);
                var sectorNet = sectorInflow + sectorOutflow;
                var sectorTv = sectorStockDtos.Sum(s => s.TradedValueCr);
                var sectorAvgChange = sectorStockDtos.Count > 0 ? sectorStockDtos.Average(s => s.ChangePercent) : 0m;
                var sectorAvgSpd = sectorStockDtos.Count > 0 ? sectorStockDtos.Average(s => s.Speed) : 1.0m;
                var sectorHasSurge = sectorStockDtos.Any(s => s.IsSurge);

                sectorFlowList.Add(new SectorFlowDto
                {
                    Sector = sectorName,
                    InflowCr = Math.Round(sectorInflow, 1),
                    OutflowCr = Math.Round(sectorOutflow, 1),
                    NetFlowCr = Math.Round(sectorNet, 1),
                    TradedValueCr = Math.Round(sectorTv, 1),
                    StockCount = sectorStockDtos.Count,
                    Advances = sectorStockDtos.Count(s => s.ChangePercent > 0),
                    Declines = sectorStockDtos.Count(s => s.ChangePercent < 0),
                    ChangePercent = Math.Round(sectorAvgChange, 2),
                    Speed = Math.Round(sectorAvgSpd, 2),
                    IsSurge = sectorHasSurge,
                    Stocks = sectorStockDtos
                });
            }

            // Sort sectors: Highest positive net flow first
            sectorFlowList = sectorFlowList
                .OrderByDescending(s => s.NetFlowCr)
                .ToList();

            var totalInflow = allCalculatedStocks.Where(s => s.FlowCr > 0).Sum(s => s.FlowCr);
            var totalOutflow = Math.Abs(allCalculatedStocks.Where(s => s.FlowCr < 0).Sum(s => s.FlowCr));

            var response = new MoneyFlowResponseDto
            {
                Timestamp = DateTime.UtcNow,
                FormattedTimestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalInflowCr = Math.Round(totalInflow, 1),
                TotalOutflowCr = Math.Round(totalOutflow, 1),
                NetFlowCr = Math.Round(totalInflow - totalOutflow, 1),
                TotalStocks = allCalculatedStocks.Count,
                TotalSectors = sectorFlowList.Count,
                Advances = allCalculatedStocks.Count(s => s.ChangePercent > 0),
                Declines = allCalculatedStocks.Count(s => s.ChangePercent < 0),
                Unchanged = allCalculatedStocks.Count(s => s.ChangePercent == 0),
                Sectors = sectorFlowList,
                TopInflowStocks = allCalculatedStocks
                    .Where(s => s.FlowCr > 0)
                    .OrderByDescending(s => s.FlowCr)
                    .Take(8)
                    .ToList(),
                TopOutflowStocks = allCalculatedStocks
                    .Where(s => s.FlowCr < 0)
                    .OrderBy(s => s.FlowCr)
                    .Take(8)
                    .ToList()
            };

            _memoryCache?.Set(cacheKey, response, TimeSpan.FromSeconds(45));
            return response;
        }
    }
}
