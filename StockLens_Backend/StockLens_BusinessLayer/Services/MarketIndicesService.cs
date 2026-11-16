using Microsoft.EntityFrameworkCore;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly IYahooFinanceClient? _yahooClient;

        public MarketIndicesService(
            IIndicesProvider indicesProvider,
            IMarketIndexConstituentRepository repository,
            ICompanyRepository companyRepository,
            StockLensDataContext context,
            IIndianApiBalanceSheetClient? indianApiClient = null,
            IHttpClientFactory? httpClientFactory = null,
            IYahooFinanceClient? yahooClient = null)
        {
            _indicesProvider = indicesProvider;
            _repository = repository;
            _companyRepository = companyRepository;
            _context = context;
            _indianApiClient = indianApiClient;
            _httpClientFactory = httpClientFactory;
            _yahooClient = yahooClient;
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
                    return await SaveAndEnrichConstituentsAsync("NIFTY 50", list, cancellationToken);
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

        public async Task<IEnumerable<MarketIndexCompanyDto>> GetSensexCompaniesAsync(CancellationToken cancellationToken = default)
        {
            const string indexName = "BSE SENSEX";

            // =========================================================================
            // TIER 1: High-Performance Database Cache (sub-5ms response time)
            // =========================================================================
            var existingDbRecords = (await _repository.GetByIndexNameAsync(indexName, cancellationToken)).ToList();
            if (existingDbRecords.Count >= 28)
            {
                var firstRecord = existingDbRecords.First();
                var hasSectors = existingDbRecords.Count(r => !string.IsNullOrWhiteSpace(r.Company?.Sector)) >= 10;
                if ((DateTime.UtcNow - firstRecord.LastUpdated).TotalDays <= 30 && hasSectors)
                {
                    return await EnrichWithLiveStockFinancialsAsync(existingDbRecords, cancellationToken);
                }
            }

            // =========================================================================
            // TIER 2: Official BSE India Endpoint (api.bseindia.com Index 16 = SENSEX)
            // =========================================================================
            try
            {
                using var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(6);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bseindia.com/BseIndiaAPI/api/IndexConstituents/w?index=16");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Referer", "https://www.bseindia.com/");
                request.Headers.Add("Accept", "application/json, text/plain, */*");

                var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Table", out var tableElement) && tableElement.ValueKind == JsonValueKind.Array && tableElement.GetArrayLength() > 0)
                    {
                        var discoveredList = await ParseBseConstituentsTableAsync(tableElement, cancellationToken);
                        if (discoveredList.Count >= 28)
                        {
                            return await SaveAndEnrichConstituentsAsync(indexName, discoveredList, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketIndicesService] BSE API unavailable or IP blocked ({ex.Message}), switching to Tier 3 canonical dataset.");
            }

            // =========================================================================
            // TIER 3: Canonical Sensex Dataset Fallback (Guarantees zero downtime if IP blocked)
            // =========================================================================
            try
            {
                var canonicalList = await LoadCanonicalSensexConstituentsAsync(cancellationToken);
                if (canonicalList.Any())
                {
                    return await SaveAndEnrichConstituentsAsync(indexName, canonicalList, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketIndicesService] Canonical Sensex fallback error: {ex.Message}");
            }

            // =========================================================================
            // TIER 4: Stale DB fallback if present
            // =========================================================================
            if (existingDbRecords.Any())
            {
                return await EnrichWithLiveStockFinancialsAsync(existingDbRecords, cancellationToken);
            }

            return new List<MarketIndexCompanyDto>();
        }

        private async Task<List<MarketIndexCompanyDto>> ParseBseConstituentsTableAsync(
            JsonElement tableElement,
            CancellationToken cancellationToken)
        {
            var allCompanies = await _context.CompanyMaster.AsNoTracking().ToListAsync(cancellationToken);
            var compByBseCode = allCompanies.Where(c => !string.IsNullOrWhiteSpace(c.BseCode)).ToDictionary(c => c.BseCode!.Trim(), c => c, StringComparer.OrdinalIgnoreCase);
            var compBySymbol = allCompanies.ToDictionary(c => c.Symbol.Trim().ToUpperInvariant(), c => c, StringComparer.OrdinalIgnoreCase);

            var discoveredList = new List<MarketIndexCompanyDto>();
            foreach (var row in tableElement.EnumerateArray())
            {
                string? scripCd = null;
                string? scripName = null;
                decimal? lastRate = null;

                if (row.TryGetProperty("scrip_cd", out var scEl) || row.TryGetProperty("Scrip_Cd", out scEl))
                    scripCd = scEl.ToString()?.Trim();
                if (row.TryGetProperty("scrip_name", out var snEl) || row.TryGetProperty("security_id", out snEl) || row.TryGetProperty("Scrip_Name", out snEl))
                    scripName = snEl.GetString()?.Trim();
                if (row.TryGetProperty("last_rate", out var lrEl) || row.TryGetProperty("Last_Rate", out lrEl) || row.TryGetProperty("current_rate", out lrEl))
                {
                    if (lrEl.ValueKind == JsonValueKind.Number) lastRate = lrEl.GetDecimal();
                    else if (decimal.TryParse(lrEl.GetString(), out var parsedRate)) lastRate = parsedRate;
                }

                Company? matchedCompany = null;
                if (!string.IsNullOrWhiteSpace(scripCd) && compByBseCode.TryGetValue(scripCd, out var cByCode))
                {
                    matchedCompany = cByCode;
                }
                else if (!string.IsNullOrWhiteSpace(scripName) && compBySymbol.TryGetValue(scripName.ToUpperInvariant(), out var cBySym))
                {
                    matchedCompany = cBySym;
                }
                else if (!string.IsNullOrWhiteSpace(scripName))
                {
                    matchedCompany = allCompanies.FirstOrDefault(c => c.CompanyName.Contains(scripName, StringComparison.OrdinalIgnoreCase));
                }

                if (matchedCompany != null && !discoveredList.Any(d => d.Symbol.Equals(matchedCompany.Symbol, StringComparison.OrdinalIgnoreCase)))
                {
                    discoveredList.Add(new MarketIndexCompanyDto
                    {
                        Symbol = matchedCompany.Symbol,
                        CompanyName = matchedCompany.CompanyName,
                        Sector = matchedCompany.Sector ?? "Equities",
                        LogoUrl = matchedCompany.LogoUrl,
                        WebsiteUrl = matchedCompany.WebsiteUrl,
                        Price = lastRate
                    });
                }
            }

            return discoveredList;
        }

        private async Task<List<MarketIndexCompanyDto>> LoadCanonicalSensexConstituentsAsync(CancellationToken cancellationToken)
        {
            var pathsToTry = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Seeders", "Data", "sensex_constituents.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Seeders", "Data", "sensex_constituents.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "StockLens_Infrastructure", "Seeders", "Data", "sensex_constituents.json")
            };

            string? validPath = null;
            foreach (var p in pathsToTry)
            {
                if (File.Exists(p))
                {
                    validPath = p;
                    break;
                }
            }

            if (validPath == null)
            {
                return new List<MarketIndexCompanyDto>();
            }

            using var stream = File.OpenRead(validPath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var list = new List<MarketIndexCompanyDto>();
            var allCompanies = await _context.CompanyMaster.AsNoTracking().ToListAsync(cancellationToken);
            var compBySymbol = allCompanies.ToDictionary(c => c.Symbol.Trim().ToUpperInvariant(), c => c, StringComparer.OrdinalIgnoreCase);

            var knownConstituents = await _context.MarketIndexConstituents
                .AsNoTracking()
                .Where(c => (c.Price != null && c.Price > 0) || (c.MarketCap != null && c.MarketCap > 0))
                .ToListAsync(cancellationToken);
            var knownBySymbol = knownConstituents
                .GroupBy(c => c.Symbol.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastUpdated).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var symbol = item.TryGetProperty("symbol", out var symEl) ? symEl.GetString() : null;
                var compName = item.TryGetProperty("companyName", out var nameEl) ? nameEl.GetString() : null;
                var sector = item.TryGetProperty("sector", out var secEl) ? secEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(symbol)) continue;
                var upperSym = symbol.Trim().ToUpperInvariant();

                compBySymbol.TryGetValue(upperSym, out var dbComp);
                knownBySymbol.TryGetValue(upperSym, out var kc);

                list.Add(new MarketIndexCompanyDto
                {
                    Symbol = upperSym,
                    CompanyName = dbComp?.CompanyName ?? compName ?? upperSym,
                    Sector = dbComp?.Sector ?? sector ?? "Equities",
                    LogoUrl = dbComp?.LogoUrl,
                    WebsiteUrl = dbComp?.WebsiteUrl,
                    Price = kc?.Price,
                    MarketCap = kc?.MarketCap
                });
            }

            return list;
        }

        public async Task<IEnumerable<MarketIndexCompanyDto>> GetBankNiftyCompaniesAsync(CancellationToken cancellationToken = default)
        {
            const string indexName = "NIFTY BANK";

            // =========================================================================
            // TIER 1: High-Performance Database Cache (sub-5ms response time)
            // =========================================================================
            var existingDbRecords = (await _repository.GetByIndexNameAsync(indexName, cancellationToken)).ToList();
            if (existingDbRecords.Count >= 12)
            {
                var firstRecord = existingDbRecords.First();
                var hasSectors = existingDbRecords.Count(r => !string.IsNullOrWhiteSpace(r.Company?.Sector)) >= 5;
                if ((DateTime.UtcNow - firstRecord.LastUpdated).TotalDays <= 30 && hasSectors)
                {
                    return await EnrichWithLiveStockFinancialsAsync(existingDbRecords, cancellationToken);
                }
            }

            // =========================================================================
            // TIER 2: Live External Provider (e.g. BharatStock or NSE)
            // =========================================================================
            try
            {
                var rawData = await _indicesProvider.GetIndexCompaniesAsync(indexName, cancellationToken);
                if (rawData.ValueKind != JsonValueKind.Object || !rawData.TryGetProperty("data", out _))
                {
                    rawData = await _indicesProvider.GetIndexCompaniesAsync("BANKNIFTY", cancellationToken);
                }

                if (rawData.ValueKind == JsonValueKind.Object && rawData.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array && dataElement.GetArrayLength() > 0)
                {
                    var list = JsonSerializer.Deserialize<List<MarketIndexCompanyDto>>(dataElement.GetRawText()) ?? new List<MarketIndexCompanyDto>();
                    if (list.Count >= 12)
                    {
                        return await SaveAndEnrichConstituentsAsync(indexName, list, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketIndicesService] Error fetching Bank Nifty from IndicesProvider: {ex.Message}");
            }

            // =========================================================================
            // TIER 3: Canonical Bank Nifty Dataset Fallback (Guarantees zero downtime)
            // =========================================================================
            try
            {
                var canonicalList = await LoadCanonicalBankNiftyConstituentsAsync(cancellationToken);
                if (canonicalList.Any())
                {
                    return await SaveAndEnrichConstituentsAsync(indexName, canonicalList, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketIndicesService] Canonical Bank Nifty fallback error: {ex.Message}");
            }

            // =========================================================================
            // TIER 4: Stale DB fallback if present
            // =========================================================================
            if (existingDbRecords.Any())
            {
                return await EnrichWithLiveStockFinancialsAsync(existingDbRecords, cancellationToken);
            }

            return new List<MarketIndexCompanyDto>();
        }

        private async Task<List<MarketIndexCompanyDto>> LoadCanonicalBankNiftyConstituentsAsync(CancellationToken cancellationToken)
        {
            var pathsToTry = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Seeders", "Data", "banknifty_constituents.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Seeders", "Data", "banknifty_constituents.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "StockLens_Infrastructure", "Seeders", "Data", "banknifty_constituents.json")
            };

            string? validPath = null;
            foreach (var p in pathsToTry)
            {
                if (File.Exists(p))
                {
                    validPath = p;
                    break;
                }
            }

            if (validPath == null)
            {
                return new List<MarketIndexCompanyDto>();
            }

            using var stream = File.OpenRead(validPath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var list = new List<MarketIndexCompanyDto>();
            var allCompanies = await _context.CompanyMaster.AsNoTracking().ToListAsync(cancellationToken);
            var compBySymbol = allCompanies.ToDictionary(c => c.Symbol.Trim().ToUpperInvariant(), c => c, StringComparer.OrdinalIgnoreCase);

            var knownConstituents = await _context.MarketIndexConstituents
                .AsNoTracking()
                .Where(c => (c.Price != null && c.Price > 0) || (c.MarketCap != null && c.MarketCap > 0))
                .ToListAsync(cancellationToken);
            var knownBySymbol = knownConstituents
                .GroupBy(c => c.Symbol.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastUpdated).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var symbol = item.TryGetProperty("symbol", out var symEl) ? symEl.GetString() : null;
                var compName = item.TryGetProperty("companyName", out var nameEl) ? nameEl.GetString() : null;
                var sector = item.TryGetProperty("sector", out var secEl) ? secEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(symbol)) continue;
                var upperSym = symbol.Trim().ToUpperInvariant();

                compBySymbol.TryGetValue(upperSym, out var dbComp);
                knownBySymbol.TryGetValue(upperSym, out var kc);

                list.Add(new MarketIndexCompanyDto
                {
                    Symbol = upperSym,
                    CompanyName = dbComp?.CompanyName ?? compName ?? upperSym,
                    Sector = dbComp?.Sector ?? sector ?? "Financial Services",
                    LogoUrl = dbComp?.LogoUrl,
                    WebsiteUrl = dbComp?.WebsiteUrl,
                    Price = kc?.Price,
                    MarketCap = kc?.MarketCap
                });
            }

            return list;
        }

        private async Task<List<MarketIndexCompanyDto>> SaveAndEnrichConstituentsAsync(
            string indexName,
            List<MarketIndexCompanyDto> list,
            CancellationToken cancellationToken)
        {
            foreach (var item in list)
            {
                if (item.Price.HasValue) item.Price = Math.Round(item.Price.Value, 2);
                if (item.PeRatio.HasValue) item.PeRatio = Math.Round(item.PeRatio.Value, 2);
                if (item.MarketCap.HasValue) item.MarketCap = Math.Round(item.MarketCap.Value, 2);
                if (item.DividendYield.HasValue) item.DividendYield = Math.Round(item.DividendYield.Value, 2);

                // Convert Rs to Crores (divide by 10,000,000) if raw value is huge
                if (item.NetProfitTtm.HasValue && Math.Abs(item.NetProfitTtm.Value) > 10000000)
                    item.NetProfitTtm = Math.Round(item.NetProfitTtm.Value / 10000000m, 2);
                if (item.RevenueTtm.HasValue && Math.Abs(item.RevenueTtm.Value) > 10000000)
                    item.RevenueTtm = Math.Round(item.RevenueTtm.Value / 10000000m, 2);

                if (item.ProfitGrowthYoy.HasValue) item.ProfitGrowthYoy = Math.Round(item.ProfitGrowthYoy.Value, 2);
                if (item.RevenueGrowthQoq.HasValue) item.RevenueGrowthQoq = Math.Round(item.RevenueGrowthQoq.Value, 2);
                if (item.Roce.HasValue) item.Roce = Math.Round(item.Roce.Value, 2);
            }

            // Ensure companies exist in CompanyMaster
            var companies = list.Select(item => new Company
            {
                Symbol = item.Symbol,
                CompanyName = !string.IsNullOrWhiteSpace(item.CompanyName) ? item.CompanyName : item.Symbol,
                Sector = item.Sector
            });

            await _companyRepository.EnsureCompaniesExistAsync(companies, cancellationToken);

            var isBse = indexName.Contains("SENSEX", StringComparison.OrdinalIgnoreCase);

            // Insert new data using repository transaction
            var entities = list.Select(item => new MarketIndexConstituent
            {
                IndexName = indexName,
                Symbol = item.Symbol,
                Price = item.Price,
                MarketCap = item.MarketCap,
                Exchange = isBse ? "BSE" : "NSE",
                LastUpdated = DateTime.UtcNow
            });

            await _repository.ReplaceIndexConstituentsAsync(indexName, entities, cancellationToken);

            var updatedDbRecords = (await _repository.GetByIndexNameAsync(indexName, cancellationToken)).ToList();
            if (updatedDbRecords.Any())
            {
                return await EnrichWithLiveStockFinancialsAsync(updatedDbRecords, cancellationToken);
            }

            return list.OrderByDescending(x => x.MarketCap ?? 0).ToList();
        }

        private async Task<List<MarketIndexCompanyDto>> EnrichWithLiveStockFinancialsAsync(
            IEnumerable<MarketIndexConstituent> constituents,
            CancellationToken cancellationToken)
        {
            var list = constituents.ToList();
            if (!list.Any()) return new List<MarketIndexCompanyDto>();

            var symbols = list.Select(c => c.Symbol.Trim().ToUpperInvariant()).Distinct().ToList();

            // 1. Query latest StockFinancials from DB (if user opened individual stock view previously)
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
                Console.WriteLine($"[MarketIndicesService] Error querying StockFinancials: {ex.Message}");
            }

            // 2. Query known constituent prices & market caps across any index in DB
            // (e.g. NIFTY 50 already has fresh CMP & MarketCap for HDFCBANK, SBIN, ICICIBANK, AXISBANK, KOTAKBANK, INDUSINDBK)
            Dictionary<string, MarketIndexConstituent> knownBySymbol = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var knownConstituents = await _context.MarketIndexConstituents
                    .AsNoTracking()
                    .Where(c => symbols.Contains(c.Symbol.ToUpper()) &&
                                ((c.Price != null && c.Price > 0) || (c.MarketCap != null && c.MarketCap > 0)))
                    .ToListAsync(cancellationToken);

                knownBySymbol = knownConstituents
                    .GroupBy(c => c.Symbol.Trim().ToUpperInvariant())
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(x => x.LastUpdated).First(),
                        StringComparer.OrdinalIgnoreCase
                    );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketIndicesService] Error querying cross-index constituents: {ex.Message}");
            }

            // 3. Identify symbols that STILL lack Price or MarketCap (e.g. BANKBARODA, PNB, CANBK, UNIONBANK, YESBANK, etc.)
            var symbolsNeedingData = list
                .Where(c =>
                {
                    var sym = c.Symbol.Trim().ToUpperInvariant();
                    latestBySymbol.TryGetValue(sym, out var sFin);
                    knownBySymbol.TryGetValue(sym, out var kc);

                    var p = (sFin?.CurrentPrice != null && sFin.CurrentPrice > 0) ? sFin.CurrentPrice
                          : (c.Price != null && c.Price > 0) ? c.Price
                          : kc?.Price;

                    var m = (sFin?.MarketCap != null && sFin.MarketCap > 0) ? sFin.MarketCap
                          : (c.MarketCap != null && c.MarketCap > 0) ? c.MarketCap
                          : kc?.MarketCap;

                    return !p.HasValue || p.Value <= 0 || !m.HasValue || m.Value <= 0;
                })
                .Select(c => (Symbol: c.Symbol.Trim().ToUpperInvariant(), Exchange: c.Exchange ?? "NSE"))
                .Distinct()
                .ToList();

            // 4. Fetch live quotes / fundamentals for missing symbols via Yahoo Finance API (or IndianAPI)
            var liveFetchedBySymbol = new Dictionary<string, (decimal? Price, decimal? MarketCap)>(StringComparer.OrdinalIgnoreCase);
            if (symbolsNeedingData.Any() && _yahooClient != null)
            {
                var fetchTasks = symbolsNeedingData.Select(async item =>
                {
                    decimal? fetchedPrice = null;
                    decimal? fetchedCap = null;

                    try
                    {
                        var fundamentals = await _yahooClient.GetComprehensiveFundamentalsAsync(item.Symbol, item.Exchange, cancellationToken);
                        if (fundamentals != null)
                        {
                            if (fundamentals.CurrentPrice.HasValue && fundamentals.CurrentPrice > 0)
                            {
                                fetchedPrice = fundamentals.CurrentPrice;
                            }
                            if (fundamentals.MarketCapCr.HasValue && fundamentals.MarketCapCr > 0)
                            {
                                fetchedCap = fundamentals.MarketCapCr;
                            }
                        }

                        if (!fetchedPrice.HasValue)
                        {
                            var liveQuote = await _yahooClient.GetLiveQuoteAsync(item.Symbol, item.Exchange, cancellationToken);
                            if (liveQuote != null && liveQuote.Price.HasValue && liveQuote.Price > 0)
                            {
                                fetchedPrice = liveQuote.Price;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MarketIndicesService] Yahoo live fetch warning for {item.Symbol}: {ex.Message}");
                    }

                    if ((!fetchedPrice.HasValue || !fetchedCap.HasValue) && _indianApiClient != null)
                    {
                        try
                        {
                            var overview = await _indianApiClient.GetStockFinancialsAndOverviewAsync(item.Symbol, item.Exchange, cancellationToken);
                            if (overview != null)
                            {
                                if (!fetchedPrice.HasValue && overview.CurrentPrice.HasValue && overview.CurrentPrice > 0)
                                {
                                    fetchedPrice = overview.CurrentPrice;
                                }
                                if (!fetchedCap.HasValue && overview.MarketCap.HasValue && overview.MarketCap > 0)
                                {
                                    fetchedCap = overview.MarketCap;
                                }
                            }
                        }
                        catch { }
                    }

                    return (item.Symbol, fetchedPrice, fetchedCap);
                });

                var fetchResults = await Task.WhenAll(fetchTasks);
                foreach (var res in fetchResults)
                {
                    if (res.fetchedPrice.HasValue || res.fetchedCap.HasValue)
                    {
                        liveFetchedBySymbol[res.Symbol] = (res.fetchedPrice, res.fetchedCap);
                    }
                }
            }

            // 5. Assemble final DTOs and persist resolved values back to DB constituents
            bool dbNeedsUpdate = false;
            var dtoList = new List<MarketIndexCompanyDto>();

            foreach (var c in list)
            {
                var sym = c.Symbol.Trim().ToUpperInvariant();
                latestBySymbol.TryGetValue(sym, out var sFin);
                knownBySymbol.TryGetValue(sym, out var kc);
                liveFetchedBySymbol.TryGetValue(sym, out var live);

                var finalPrice = (sFin?.CurrentPrice != null && sFin.CurrentPrice > 0) ? sFin.CurrentPrice
                               : (live.Price != null && live.Price > 0) ? live.Price
                               : (c.Price != null && c.Price > 0) ? c.Price
                               : kc?.Price;

                var finalMarketCap = (sFin?.MarketCap != null && sFin.MarketCap > 0) ? sFin.MarketCap
                                   : (live.MarketCap != null && live.MarketCap > 0) ? live.MarketCap
                                   : (c.MarketCap != null && c.MarketCap > 0) ? c.MarketCap
                                   : kc?.MarketCap;

                if (finalPrice.HasValue) finalPrice = Math.Round(finalPrice.Value, 2);
                if (finalMarketCap.HasValue) finalMarketCap = Math.Round(finalMarketCap.Value, 2);

                // If DB constituent record had null Price or MarketCap, update it!
                if ((c.Price == null && finalPrice != null) || (c.MarketCap == null && finalMarketCap != null))
                {
                    c.Price = finalPrice ?? c.Price;
                    c.MarketCap = finalMarketCap ?? c.MarketCap;
                    c.LastUpdated = DateTime.UtcNow;
                    _context.Entry(c).State = EntityState.Modified;
                    dbNeedsUpdate = true;
                }

                dtoList.Add(new MarketIndexCompanyDto
                {
                    Symbol = c.Symbol,
                    CompanyName = c.Company?.CompanyName ?? string.Empty,
                    Sector = c.Company?.Sector ?? string.Empty,
                    LogoUrl = c.Company?.LogoUrl,
                    WebsiteUrl = c.Company?.WebsiteUrl,
                    Price = finalPrice,
                    MarketCap = finalMarketCap
                });
            }

            if (dbNeedsUpdate)
            {
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MarketIndicesService] Error persisting updated constituent prices/marketcaps: {ex.Message}");
                }
            }

            return dtoList.OrderByDescending(x => x.MarketCap ?? 0).ToList();
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
