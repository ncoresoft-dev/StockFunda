using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System;
using System.Threading.Tasks;
using System.Linq;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;

namespace StockLens_BusinessLayer.Services
{
    public class MarketIndicesService : IMarketIndicesService
    {
        private readonly IIndicesProvider _indicesProvider;
        private readonly IMarketIndexConstituentRepository _repository;
        private readonly ICompanyRepository _companyRepository;

        public MarketIndicesService(IIndicesProvider indicesProvider, IMarketIndexConstituentRepository repository, ICompanyRepository companyRepository)
        {
            _indicesProvider = indicesProvider;
            _repository = repository;
            _companyRepository = companyRepository;
        }

        public async Task<IEnumerable<MarketIndexCompanyDto>> GetTop50CompaniesAsync(CancellationToken cancellationToken = default)
        {
            // 1. Check DB first
            var existingDbRecords = await _repository.GetByIndexNameAsync("NIFTY 50", cancellationToken);
            if (existingDbRecords.Any())
            {
                var firstRecord = existingDbRecords.First();
                if ((DateTime.UtcNow - firstRecord.LastUpdated).TotalDays <= 30)
                {
                    // Fresh enough, return from DB
                    return existingDbRecords.Select(c => new MarketIndexCompanyDto
                    {
                        Symbol = c.Symbol,
                        CompanyName = c.Company?.CompanyName ?? string.Empty,
                        Sector = c.Company?.Sector ?? string.Empty,
                        LogoUrl = c.Company?.LogoUrl,
                        WebsiteUrl = c.Company?.WebsiteUrl,
                        Price = c.Price,
                        PeRatio = c.PeRatio,
                        MarketCap = c.MarketCap,
                        DividendYield = c.DividendYield,
                        NetProfitTtm = c.NetProfitTtm,
                        ProfitGrowthYoy = c.ProfitGrowthYoy,
                        RevenueTtm = c.RevenueTtm,
                        RevenueGrowthQoq = c.RevenueGrowthQoq,
                        Roce = c.Roce
                    }).ToList();
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
                        PeRatio = item.PeRatio,
                        MarketCap = item.MarketCap,
                        DividendYield = item.DividendYield,
                        NetProfitTtm = item.NetProfitTtm,
                        ProfitGrowthYoy = item.ProfitGrowthYoy,
                        RevenueTtm = item.RevenueTtm,
                        RevenueGrowthQoq = item.RevenueGrowthQoq,
                        Roce = item.Roce,
                        Exchange = "NSE",
                        LastUpdated = DateTime.UtcNow
                    });
                    
                    await _repository.ReplaceIndexConstituentsAsync("NIFTY 50", entities, cancellationToken);

                    var updatedDbRecords = await _repository.GetByIndexNameAsync("NIFTY 50", cancellationToken);
                    if (updatedDbRecords.Any())
                    {
                        return updatedDbRecords.Select(c => new MarketIndexCompanyDto
                        {
                            Symbol = c.Symbol,
                            CompanyName = c.Company?.CompanyName ?? string.Empty,
                            Sector = c.Company?.Sector ?? string.Empty,
                            LogoUrl = c.Company?.LogoUrl,
                            WebsiteUrl = c.Company?.WebsiteUrl,
                            Price = c.Price,
                            PeRatio = c.PeRatio,
                            MarketCap = c.MarketCap,
                            DividendYield = c.DividendYield,
                            NetProfitTtm = c.NetProfitTtm,
                            ProfitGrowthYoy = c.ProfitGrowthYoy,
                            RevenueTtm = c.RevenueTtm,
                            RevenueGrowthQoq = c.RevenueGrowthQoq,
                            Roce = c.Roce
                        }).ToList();
                    }

                    return list.OrderByDescending(x => x.MarketCap ?? 0).ToList();
                }
            }
            catch
            {
                // Fallback below
            }
            
            // If API fails and we have stale DB records, we could return them as fallback, 
            // but for now, we just return what we have (even if stale) to avoid breaking the UI.
            if (existingDbRecords.Any())
            {
                return existingDbRecords.Select(c => new MarketIndexCompanyDto
                {
                    Symbol = c.Symbol,
                    CompanyName = c.Company?.CompanyName ?? string.Empty,
                    Sector = c.Company?.Sector ?? string.Empty,
                    LogoUrl = c.Company?.LogoUrl,
                    WebsiteUrl = c.Company?.WebsiteUrl,
                    Price = c.Price,
                    PeRatio = c.PeRatio,
                    MarketCap = c.MarketCap,
                    DividendYield = c.DividendYield,
                    NetProfitTtm = c.NetProfitTtm,
                    ProfitGrowthYoy = c.ProfitGrowthYoy,
                    RevenueTtm = c.RevenueTtm,
                    RevenueGrowthQoq = c.RevenueGrowthQoq,
                    Roce = c.Roce
                }).ToList();
            }

            return new List<MarketIndexCompanyDto>();
        }
    }
}
