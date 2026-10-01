using AutoMapper;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class CompanyService : ICompanyService
    {
        private readonly ICompanyRepository _companyRepository;
        private readonly IIndianApiBalanceSheetClient _indianApiClient;
        private readonly IYahooFinanceClient _yahooFinanceClient;
        private readonly IMapper _mapper;
        private readonly ILogger<CompanyService> _logger;

        public CompanyService(
            ICompanyRepository companyRepository,
            IIndianApiBalanceSheetClient indianApiClient,
            IYahooFinanceClient yahooFinanceClient,
            IMapper mapper,
            ILogger<CompanyService> logger)
        {
            _companyRepository = companyRepository;
            _indianApiClient = indianApiClient;
            _yahooFinanceClient = yahooFinanceClient;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<IEnumerable<CompanyDto>> SearchCompaniesAsync(string query, int limit = 10)
        {
            var companies = await _companyRepository.SearchCompaniesAsync(query, limit);
            return _mapper.Map<IEnumerable<CompanyDto>>(companies);
        }

        public async Task<CompanyOverviewDto?> GetCompanyOverviewAsync(
            string symbol,
            string? exchange = "NSE",
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();

            _logger.LogInformation("Retrieving company overview for {Symbol} ({Exchange}), ForceRefresh={ForceRefresh}", cleanSymbol, cleanExchange, forceRefresh);

            // 1. Check cached record in Database
            var cachedCompany = await _companyRepository.GetCompanyBySymbolAsync(cleanSymbol);
            if (!forceRefresh && cachedCompany != null && !string.IsNullOrWhiteSpace(cachedCompany.About))
            {
                _logger.LogInformation("Returning cached company overview from DB for {Symbol}", cleanSymbol);
                var cachedDto = _mapper.Map<CompanyOverviewDto>(cachedCompany);
                cachedDto.Source = "Database";
                cachedDto.KeyPoints = ParseKeyPoints(cachedCompany.KeyPointsJson, cachedCompany.About);
                EnrichOverviewMetadata(cachedDto, null);
                return cachedDto;
            }

            var company = cachedCompany ?? new Company
            {
                Symbol = cleanSymbol,
                CompanyName = cleanSymbol,
                CreatedAt = DateTime.UtcNow
            };

            var resolvedSource = "IndianAPI";
            var executives = new List<string>();
            var keyPoints = new List<string>();
            IndianApiStockOverviewDto? indianOverview = null;
            YahooCompanyProfileDto? yahooProfile = null;

            // 2. Primary Provider: IndianAPI (/stock?name={symbol})
            try
            {
                _logger.LogInformation("[Hybrid] Querying IndianAPI for {Symbol}", cleanSymbol);
                indianOverview = await _indianApiClient.GetStockFinancialsAndOverviewAsync(cleanSymbol, cleanExchange, cancellationToken);
                if (indianOverview != null)
                {
                    if (!string.IsNullOrWhiteSpace(indianOverview.CompanyName)) company.CompanyName = indianOverview.CompanyName;
                    if (!string.IsNullOrWhiteSpace(indianOverview.Industry)) company.Industry = indianOverview.Industry;
                    if (!string.IsNullOrWhiteSpace(indianOverview.SectorName)) company.Sector = indianOverview.SectorName;
                    if (!string.IsNullOrWhiteSpace(indianOverview.WebsiteUrl)) company.WebsiteUrl = indianOverview.WebsiteUrl;
                    if (!string.IsNullOrWhiteSpace(indianOverview.About)) company.About = indianOverview.About;

                    if (indianOverview.KeyPoints != null && indianOverview.KeyPoints.Count > 0)
                    {
                        keyPoints.AddRange(indianOverview.KeyPoints);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Hybrid] IndianAPI query failed for {Symbol}, falling back to Yahoo Finance", cleanSymbol);
            }

            // 3. Fallback / Enrichment: Yahoo Finance (assetProfile & summaryProfile)
            var needsEnrichment = string.IsNullOrWhiteSpace(company.About) ||
                                  company.About.Length < 60 ||
                                  string.IsNullOrWhiteSpace(company.WebsiteUrl) ||
                                  string.IsNullOrWhiteSpace(company.Industry);

            if (needsEnrichment || forceRefresh)
            {
                try
                {
                    _logger.LogInformation("[Hybrid] Querying Yahoo Finance profile for {Symbol}", cleanSymbol);
                    yahooProfile = await _yahooFinanceClient.GetCompanyProfileAsync(cleanSymbol, cleanExchange, cancellationToken);
                    if (yahooProfile != null)
                    {
                        if (string.IsNullOrWhiteSpace(company.CompanyName) || company.CompanyName == cleanSymbol)
                        {
                            if (!string.IsNullOrWhiteSpace(yahooProfile.CompanyName)) company.CompanyName = yahooProfile.CompanyName;
                        }

                        if (string.IsNullOrWhiteSpace(company.About) || (company.About.Length < 60 && !string.IsNullOrWhiteSpace(yahooProfile.LongBusinessSummary)))
                        {
                            company.About = yahooProfile.LongBusinessSummary;
                            resolvedSource = resolvedSource == "IndianAPI" && !string.IsNullOrWhiteSpace(indianOverview?.CompanyName) ? "Hybrid" : "YahooFinance";
                        }

                        if (string.IsNullOrWhiteSpace(company.WebsiteUrl) && !string.IsNullOrWhiteSpace(yahooProfile.Website))
                        {
                            company.WebsiteUrl = yahooProfile.Website;
                        }

                        if (string.IsNullOrWhiteSpace(company.Industry) && !string.IsNullOrWhiteSpace(yahooProfile.Industry))
                        {
                            company.Industry = yahooProfile.Industry;
                        }

                        if (string.IsNullOrWhiteSpace(company.Sector) && !string.IsNullOrWhiteSpace(yahooProfile.Sector))
                        {
                            company.Sector = yahooProfile.Sector;
                        }

                        if (!company.EmployeesCount.HasValue && yahooProfile.FullTimeEmployees.HasValue)
                        {
                            company.EmployeesCount = yahooProfile.FullTimeEmployees;
                        }

                        if (yahooProfile.KeyExecutives != null && yahooProfile.KeyExecutives.Count > 0)
                        {
                            executives.AddRange(yahooProfile.KeyExecutives);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Hybrid] Yahoo Finance profile query failed for {Symbol}", cleanSymbol);
                }
            }

            // 4. Extract / Refine Key Points
            if (keyPoints.Count == 0 && !string.IsNullOrWhiteSpace(company.About))
            {
                keyPoints = GenerateKeyPointsFromAbout(company.About);
            }

            company.KeyPointsJson = keyPoints.Count > 0 ? JsonSerializer.Serialize(keyPoints) : null;
            company.UpdatedAt = DateTime.UtcNow;

            // 5. Persist to Database
            try
            {
                await _companyRepository.SaveOrUpdateCompanyAsync(company);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist company overview in DB for {Symbol}", cleanSymbol);
            }

            // 6. Return Mapped DTO
            var resultDto = _mapper.Map<CompanyOverviewDto>(company);
            resultDto.Source = resolvedSource;
            resultDto.KeyPoints = keyPoints;
            resultDto.KeyExecutives = executives;
            EnrichOverviewMetadata(resultDto, yahooProfile);

            return resultDto;
        }

        private static void EnrichOverviewMetadata(CompanyOverviewDto dto, YahooCompanyProfileDto? yahooProfile)
        {
            if (dto == null) return;

            var sym = dto.Symbol?.Trim().ToUpperInvariant() ?? "";
            var about = dto.About ?? "";

            // 1. City / Country / Headquarters
            if (yahooProfile != null)
            {
                if (!string.IsNullOrWhiteSpace(yahooProfile.City)) dto.City = yahooProfile.City;
                if (!string.IsNullOrWhiteSpace(yahooProfile.Country)) dto.Country = yahooProfile.Country;
            }

            if (string.IsNullOrWhiteSpace(dto.City))
            {
                if (sym == "RELIANCE" || sym == "TCS" || sym == "TATAMOTORS" || sym == "HDFCBANK" || sym == "ICICIBANK" || sym == "SBIN" || sym == "LT")
                    dto.City = "Mumbai";
                else if (sym == "INFY" || sym == "WIPRO")
                    dto.City = "Bengaluru";
                else if (sym == "ITC")
                    dto.City = "Kolkata";
                else if (sym == "HINDUNILVR")
                    dto.City = "Mumbai";
                else if (sym == "BHARTIARTL")
                    dto.City = "New Delhi";
                else
                {
                    var hqMatch = Regex.Match(about, @"(?:headquartered|based|located)\s+in\s+([A-Za-z\s]+?)(?:,|\.|\s+and|\s+with)", RegexOptions.IgnoreCase);
                    if (hqMatch.Success && hqMatch.Groups[1].Value.Trim().Length < 30)
                    {
                        dto.City = hqMatch.Groups[1].Value.Trim();
                    }
                    else
                    {
                        dto.City = "Mumbai";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(dto.Country))
            {
                dto.Country = "India";
            }

            dto.Headquarters = dto.City;

            // 2. Founded Year
            if (string.IsNullOrWhiteSpace(dto.FoundedYear))
            {
                if (sym == "RELIANCE") dto.FoundedYear = "1966";
                else if (sym == "TCS") dto.FoundedYear = "1968";
                else if (sym == "INFY") dto.FoundedYear = "1981";
                else if (sym == "TATAMOTORS") dto.FoundedYear = "1945";
                else if (sym == "HDFCBANK") dto.FoundedYear = "1994";
                else if (sym == "ICICIBANK") dto.FoundedYear = "1994";
                else if (sym == "SBIN") dto.FoundedYear = "1955";
                else if (sym == "ITC") dto.FoundedYear = "1910";
                else if (sym == "BHARTIARTL") dto.FoundedYear = "1995";
                else if (sym == "LT") dto.FoundedYear = "1946";
                else if (sym == "HINDUNILVR") dto.FoundedYear = "1933";
                else
                {
                    var foundedMatch = Regex.Match(about, @"(?:founded|incorporated|established|started)\s+(?:in\s+)?(\d{4})", RegexOptions.IgnoreCase);
                    if (foundedMatch.Success)
                    {
                        dto.FoundedYear = foundedMatch.Groups[1].Value;
                    }
                    else
                    {
                        var yearInSentenceMatch = Regex.Match(about, @"(?:in\s+)(\d{4})(?:,\s+[A-Z])", RegexOptions.IgnoreCase);
                        if (yearInSentenceMatch.Success)
                        {
                            dto.FoundedYear = yearInSentenceMatch.Groups[1].Value;
                        }
                    }
                }
            }
        }

        private static List<string> ParseKeyPoints(string? keyPointsJson, string? aboutText)
        {
            if (!string.IsNullOrWhiteSpace(keyPointsJson))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(keyPointsJson);
                    if (list != null && list.Count > 0) return list;
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(aboutText))
            {
                return GenerateKeyPointsFromAbout(aboutText);
            }

            return new List<string>();
        }

        private static List<string> GenerateKeyPointsFromAbout(string aboutText)
        {
            var points = new List<string>();
            if (string.IsNullOrWhiteSpace(aboutText)) return points;

            // Split into sentences
            var rawSentences = Regex.Split(aboutText, @"(?<=[.!?])\s+(?=[A-Z])");

            foreach (var s in rawSentences)
            {
                var trimmed = s.Trim();
                if (trimmed.Length > 25 && trimmed.Length < 300)
                {
                    points.Add(trimmed);
                }
                if (points.Count >= 4) break;
            }

            return points;
        }
    }
}
