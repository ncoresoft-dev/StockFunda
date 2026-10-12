using Microsoft.EntityFrameworkCore;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.BharatStock;
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

        public MarketIndicesService(
            IIndicesProvider indicesProvider,
            IMarketIndexConstituentRepository repository,
            ICompanyRepository companyRepository,
            StockLensDataContext context)
        {
            _indicesProvider = indicesProvider;
            _repository = repository;
            _companyRepository = companyRepository;
            _context = context;
        }

        public async Task<IEnumerable<MarketIndexCompanyDto>> GetTop50CompaniesAsync(CancellationToken cancellationToken = default)
        {
            // 1. Check DB first
            var existingDbRecords = (await _repository.GetByIndexNameAsync("NIFTY 50", cancellationToken)).ToList();
            if (existingDbRecords.Any())
            {
                var firstRecord = existingDbRecords.First();
                if ((DateTime.UtcNow - firstRecord.LastUpdated).TotalDays <= 30)
                {
                    // Fresh enough, return from DB (enriched with live stock financials if viewed individually)
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
                    .Where(f => symbols.Contains(f.Stock.Symbol.ToUpper()) && f.CurrentPrice != null && f.CurrentPrice > 0)
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
    }
}
