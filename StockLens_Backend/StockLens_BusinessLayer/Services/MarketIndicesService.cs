using Microsoft.EntityFrameworkCore;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class MarketIndicesService : IMarketIndicesService
    {
        private readonly IIndicesProvider _indicesProvider;
        private readonly IMarketIndexConstituentRepository _repository;
        private readonly ICompanyRepository _companyRepository;
        private readonly StockLensDataContext _context;
        private readonly IIndianApiBalanceSheetClient? _indianApiClient;

        public MarketIndicesService(
            IIndicesProvider indicesProvider,
            IMarketIndexConstituentRepository repository,
            ICompanyRepository companyRepository,
            StockLensDataContext context,
            IIndianApiBalanceSheetClient? indianApiClient = null)
        {
            _indicesProvider = indicesProvider;
            _repository = repository;
            _companyRepository = companyRepository;
            _context = context;
            _indianApiClient = indianApiClient;
        }

        public async Task<IEnumerable<MarketIndexCompanyDto>> GetTop50CompaniesAsync(CancellationToken cancellationToken = default)
        {
            // 1. Check DB first
            var existingDbRecords = (await _repository.GetByIndexNameAsync("NIFTY 50", cancellationToken)).ToList();
            if (existingDbRecords.Any())
            {
                var firstRecord = existingDbRecords.First();
                var hasSectors = existingDbRecords.Count(r => !string.IsNullOrWhiteSpace(r.Company?.Sector)) >= 10;
                if ((DateTime.UtcNow - firstRecord.LastUpdated).TotalDays <= 30 && hasSectors)
                {
                    // Fresh enough and has valid sector metadata, return from DB (enriched with live stock financials if viewed individually)
                    return await EnrichWithLiveStockFinancialsAsync(existingDbRecords, cancellationToken);
                }
            }

            // 2. Fetch from API if not in DB or older than 30 days
            try
            {
                var rawData = await _indicesProvider.GetTop50CompaniesAsync(cancellationToken);
                if (rawData.ValueKind == JsonValueKind.Object && rawData.TryGetProperty("data", out JsonElement dataElement))
                {
                    var list = JsonSerializer.Deserialize<List<MarketIndexCompanyDto>>(dataElement.GetRawText()) ?? new List<MarketIndexCompanyDto>();
                    foreach (var item in list)
                    {
                        if (item.Price.HasValue) item.Price = Math.Round(item.Price.Value, 2);
                        if (item.PeRatio.HasValue) item.PeRatio = Math.Round(item.PeRatio.Value, 2);
                        if (item.MarketCap.HasValue) item.MarketCap = Math.Round(item.MarketCap.Value, 2);
                        if (item.DividendYield.HasValue) item.DividendYield = Math.Round(item.DividendYield.Value, 2);
                        
                        // Convert Rs to Crores (divide by 10,000,000)
                        if (item.NetProfitTtm.HasValue) item.NetProfitTtm = Math.Round(item.NetProfitTtm.Value / 10000000m, 2);
                        if (item.RevenueTtm.HasValue) item.RevenueTtm = Math.Round(item.RevenueTtm.Value / 10000000m, 2);
                        
                        if (item.ProfitGrowthYoy.HasValue) item.ProfitGrowthYoy = Math.Round(item.ProfitGrowthYoy.Value, 2);
                        if (item.RevenueGrowthQoq.HasValue) item.RevenueGrowthQoq = Math.Round(item.RevenueGrowthQoq.Value, 2);
                        if (item.Roce.HasValue) item.Roce = Math.Round(item.Roce.Value, 2);
                    }
                    
                    // Ensure that all these companies exist in CompanyMaster
                    var companies = list.Select(item => new Company
                    {
                        Symbol = item.Symbol,
                        CompanyName = !string.IsNullOrWhiteSpace(item.CompanyName) ? item.CompanyName : item.Symbol,
                        Sector = item.Sector
                    });
                    
                    await _companyRepository.EnsureCompaniesExistAsync(companies, cancellationToken);
                    
                    // Insert new data using repository transaction
                    var entities = list.Select(item => new MarketIndexConstituent
                    {
                        IndexName = "NIFTY 50",
                        Symbol = item.Symbol,
                        Price = item.Price,
                        MarketCap = item.MarketCap,
                        Exchange = "NSE",
                        LastUpdated = DateTime.UtcNow
                    });
                    
                    await _repository.ReplaceIndexConstituentsAsync("NIFTY 50", entities, cancellationToken);

                    var updatedDbRecords = (await _repository.GetByIndexNameAsync("NIFTY 50", cancellationToken)).ToList();
                    if (updatedDbRecords.Any())
                    {
                        return await EnrichWithLiveStockFinancialsAsync(updatedDbRecords, cancellationToken);
                    }

                    return list.OrderByDescending(x => x.MarketCap ?? 0).ToList();
                }
            }
            catch
            {
                // Fallback below
            }
            
            // If API fails and we have stale DB records, return them as fallback
            if (existingDbRecords.Any())
            {
                return await EnrichWithLiveStockFinancialsAsync(existingDbRecords, cancellationToken);
            }

            return new List<MarketIndexCompanyDto>();
        }

        private async Task<List<MarketIndexCompanyDto>> EnrichWithLiveStockFinancialsAsync(
            IEnumerable<MarketIndexConstituent> constituents,
            CancellationToken cancellationToken)
        {
            var list = constituents.ToList();
            if (!list.Any()) return new List<MarketIndexCompanyDto>();

            var symbols = list.Select(c => c.Symbol.Trim().ToUpperInvariant()).ToList();

            Dictionary<string, StockFinancial> latestBySymbol = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var liveFinancials = await _context.StockFinancials
                    .AsNoTracking()
                    .Include(f => f.Stock)
                    .Where(f => symbols.Contains(f.Stock.Symbol.ToUpper()) &&
                                ((f.CurrentPrice != null && f.CurrentPrice > 0) || (f.MarketCap != null && f.MarketCap > 0)))
                    .ToListAsync(cancellationToken);

                latestBySymbol = liveFinancials
                    .GroupBy(f => f.Stock.Symbol.Trim().ToUpperInvariant())
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(x => x.LastSyncedAt)
                              .ThenByDescending(x => x.PeriodEndDate ?? DateTime.MinValue)
                              .First(),
                        StringComparer.OrdinalIgnoreCase
                    );
            }
            catch (Exception ex)
            {
                // In case of any query error, fall back gracefully to constituents' stored numbers
                Console.WriteLine($"[MarketIndicesService] Error enriching with live financials: {ex.Message}");
            }

            return list.Select(c =>
            {
                var sym = c.Symbol.Trim().ToUpperInvariant();
                var hasLive = latestBySymbol.TryGetValue(sym, out var sFin);

                var finalPrice = (hasLive && sFin?.CurrentPrice != null && sFin.CurrentPrice > 0)
                    ? sFin.CurrentPrice
                    : c.Price;

                var finalMarketCap = (hasLive && sFin?.MarketCap != null && sFin.MarketCap > 0)
                    ? sFin.MarketCap
                    : c.MarketCap;

                return new MarketIndexCompanyDto
                {
                    Symbol = c.Symbol,
                    CompanyName = c.Company?.CompanyName ?? string.Empty,
                    Sector = c.Company?.Sector ?? string.Empty,
                    LogoUrl = c.Company?.LogoUrl,
                    WebsiteUrl = c.Company?.WebsiteUrl,
                    Price = finalPrice,
                    MarketCap = finalMarketCap
                };
            })
            .OrderByDescending(x => x.MarketCap ?? 0)
            .ToList();
        }

        public async Task<IEnumerable<MarketIndexCompanyDto>> GetStocksBySymbolsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
        {
            if (symbols == null)
            {
                return Enumerable.Empty<MarketIndexCompanyDto>();
            }

            var cleanSymbols = symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            if (!cleanSymbols.Any())
            {
                return Enumerable.Empty<MarketIndexCompanyDto>();
            }

            // 1. Query NIFTY 50 constituents in DB (which already have logo, price, and market cap cached)
            var constituents = await _context.MarketIndexConstituents
                .AsNoTracking()
                .Include(m => m.Company)
                .Where(m => cleanSymbols.Contains(m.Symbol.ToUpper()))
                .ToListAsync(cancellationToken);

            var constituentsDict = constituents
                .GroupBy(c => c.Symbol.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 2. Query Stocks + CompanyMaster for any non-index stocks (like BANDHANBNK, etc.)
            var dbStocks = await _context.Stocks
                .AsNoTracking()
                .Include(s => s.Company)
                .Where(s => cleanSymbols.Contains(s.Symbol.ToUpper()))
                .ToListAsync(cancellationToken);

            var stocksDict = dbStocks
                .GroupBy(s => s.Symbol.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 3. Also query CompanyMaster directly in case stock is not yet in Stocks table
            var companies = await _context.CompanyMaster
                .AsNoTracking()
                .Where(c => cleanSymbols.Contains(c.Symbol.ToUpper()))
                .ToListAsync(cancellationToken);

            var companiesDict = companies
                .GroupBy(c => c.Symbol.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 4. Query live financials for these symbols to get latest CurrentPrice and MarketCap from DB
            var liveFinancials = await _context.StockFinancials
                .AsNoTracking()
                .Include(f => f.Stock)
                .Where(f => cleanSymbols.Contains(f.Stock.Symbol.ToUpper()) &&
                            ((f.CurrentPrice != null && f.CurrentPrice > 0) || (f.MarketCap != null && f.MarketCap > 0)))
                .ToListAsync(cancellationToken);

            var latestFinancialsBySymbol = liveFinancials
                .GroupBy(f => f.Stock.Symbol.Trim().ToUpperInvariant())
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.LastSyncedAt)
                          .ThenByDescending(x => x.PeriodEndDate ?? DateTime.MinValue)
                          .First(),
                    StringComparer.OrdinalIgnoreCase
                );

            // 5. Assemble response list
            var results = new List<MarketIndexCompanyDto>();

            foreach (var sym in cleanSymbols)
            {
                constituentsDict.TryGetValue(sym, out var constituent);
                stocksDict.TryGetValue(sym, out var stock);
                companiesDict.TryGetValue(sym, out var company);
                latestFinancialsBySymbol.TryGetValue(sym, out var fin);

                var comp = constituent?.Company ?? stock?.Company ?? company;

                // Priority 1: DB StockFinancials (what is saved and shown inside the stock details view)
                // Priority 2: MarketIndexConstituents (from cached 3rd party index screener)
                var price = (fin?.CurrentPrice != null && fin.CurrentPrice > 0) ? fin.CurrentPrice : constituent?.Price;
                var marketCap = (fin?.MarketCap != null && fin.MarketCap > 0) ? fin.MarketCap : constituent?.MarketCap;
                var companyName = comp?.CompanyName ?? constituent?.Symbol ?? sym;
                var logoUrl = comp?.LogoUrl;
                var sector = comp?.Sector ?? "Equities";
                var websiteUrl = comp?.WebsiteUrl;

                // Priority 3 (Fallback): If stock is not in DB StockFinancials AND not in constituents, query 3rd party API (IndianAPI)
                if ((!price.HasValue || !marketCap.HasValue) && _indianApiClient != null)
                {
                    try
                    {
                        var overview = await _indianApiClient.GetStockFinancialsAndOverviewAsync(sym, "NSE", cancellationToken);
                        if (overview != null)
                        {
                            if (!price.HasValue && overview.CurrentPrice.HasValue && overview.CurrentPrice > 0)
                            {
                                price = overview.CurrentPrice;
                            }
                            if (!marketCap.HasValue && overview.MarketCap.HasValue && overview.MarketCap > 0)
                            {
                                marketCap = overview.MarketCap;
                            }
                            if (string.IsNullOrWhiteSpace(companyName) || companyName == sym)
                            {
                                companyName = !string.IsNullOrWhiteSpace(overview.CompanyName) ? overview.CompanyName : sym;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback failure handled gracefully
                    }
                }

                results.Add(new MarketIndexCompanyDto
                {
                    Symbol = sym,
                    CompanyName = companyName,
                    Sector = sector,
                    LogoUrl = logoUrl,
                    WebsiteUrl = websiteUrl,
                    Price = price.HasValue ? Math.Round(price.Value, 2) : null,
                    MarketCap = marketCap.HasValue ? Math.Round(marketCap.Value, 2) : null
                });
            }

            return results;
        }
    }
}
