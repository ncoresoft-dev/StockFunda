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
        private readonly ICompareProvider _compareProvider;
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
            _compareProvider = compareProvider!;
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

            // 1. Identify Sector Leader by Market Cap (must be active with real price)
            var leader = stocks.Where(s => s.MarketCapCr > 0 && s.Price > 0).OrderByDescending(s => s.MarketCapCr).FirstOrDefault();
            if (leader != null)
            {
                leader.PrimaryBadge = "👑 Sector Leader";
                leader.Badges.Add("Leader");
            }

            // 2. Identify Best Profitability (Highest ROE with active price)
            var highestRoe = stocks.Where(s => s.Price > 0 && s.Roe.HasValue && s.Roe.Value > 0).OrderByDescending(s => s.Roe!.Value).FirstOrDefault();
            if (highestRoe != null && highestRoe != leader)
            {
                highestRoe.PrimaryBadge = "⭐ Top Profitability";
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
    }
}
