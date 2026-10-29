using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class PeerService : IPeerService
    {
        private readonly ICompanyRepository _companyRepository;
        private readonly IStockRepository _stockRepository;
        private readonly IStockPeerRepository _stockPeerRepository;
        private readonly ICompareProvider _compareProvider;
        private readonly IFinancialProvider _financialProvider;
        private readonly IIndianApiBalanceSheetClient _indianApiClient;
        private readonly ILogger<PeerService> _logger;

        public PeerService(
            ICompanyRepository companyRepository,
            IStockRepository stockRepository,
            IStockPeerRepository stockPeerRepository,
            ICompareProvider compareProvider,
            IFinancialProvider financialProvider,
            IIndianApiBalanceSheetClient indianApiClient,
            ILogger<PeerService> logger)
        {
            _companyRepository = companyRepository;
            _stockRepository = stockRepository;
            _stockPeerRepository = stockPeerRepository;
            _compareProvider = compareProvider;
            _financialProvider = financialProvider;
            _indianApiClient = indianApiClient;
            _logger = logger;
        }

        public async Task<List<PeerDto>> GetStockPeersAsync(string symbol, string? exchange = "NSE", bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, exchange, null, null, cancellationToken);
            return await GetStockPeersByStockIdAsync(stock.Id, forceRefresh, cancellationToken);
        }

        private async Task<List<PeerDto>> GetStockPeersByStockIdAsync(int stockId, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var stock = await _stockRepository.GetByIdAsync(stockId);
            if (stock == null)
            {
                throw new KeyNotFoundException($"Stock with ID {stockId} not found.");
            }

            if (!forceRefresh)
            {
                var dbPeers = await _stockPeerRepository.GetPeersByStockIdAsync(stockId);
                // If data has full peer list (> 3 peers) and is less than 24 hours old, return from DB
                if (dbPeers != null && dbPeers.Count > 3)
                {
                    var lastSynced = dbPeers.First().LastSyncedAt;
                    if ((DateTime.UtcNow - lastSynced).TotalHours < 24)
                    {
                        return dbPeers.Select(p => new PeerDto
                        {
                            CompanyName = p.CompanyName,
                            Price = p.Price,
                            PeRatio = p.PeRatio,
                            PbRatio = p.PbRatio,
                            MarketCap = p.MarketCap,
                            Roe = p.Roe,
                            Roce = p.Roce,
                            DividendYield = p.DividendYield,
                            TotalShares = p.TotalShares
                        }).ToList();
                    }
                    else
                    {
                        _logger.LogInformation("Peer data for stock {StockId} is older than 24 hours. Fetching fresh data from API.", stockId);
                    }
                }
            }

            var cleanSymbol = stock.Symbol;
            var cleanExchange = stock.Exchange;

            var resultList = new List<PeerDto>();
            var company = await _companyRepository.GetCompanyBySymbolAsync(cleanSymbol);

            // Build prioritized candidate sectors for BharatStock Compare API
            var candidateSectors = new List<string>();

            // 1. Direct from BharatStock Company Details (exact native taxonomy)
            try
            {
                var stockDetails = await _financialProvider.GetStockDetailsAsync(cleanSymbol, cleanExchange, cancellationToken);
                if (!string.IsNullOrWhiteSpace(stockDetails?.Sector))
                {
                    candidateSectors.Add(stockDetails.Sector.Trim());
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch BharatStock company details for {Symbol}", cleanSymbol);
            }

            // 2. Normalized from CompanyMaster Sector and Industry
            var normSector = NormalizeSector(company?.Sector);
            if (!string.IsNullOrWhiteSpace(normSector) && !candidateSectors.Contains(normSector, StringComparer.OrdinalIgnoreCase))
            {
                candidateSectors.Add(normSector);
            }

            var normIndustry = NormalizeSector(company?.Industry);
            if (!string.IsNullOrWhiteSpace(normIndustry) && !candidateSectors.Contains(normIndustry, StringComparer.OrdinalIgnoreCase))
            {
                candidateSectors.Add(normIndustry);
            }

            // 3. Raw Sector and Industry as fallbacks
            if (!string.IsNullOrWhiteSpace(company?.Sector) && !candidateSectors.Contains(company.Sector.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                candidateSectors.Add(company.Sector.Trim());
            }

            if (!string.IsNullOrWhiteSpace(company?.Industry) && !candidateSectors.Contains(company.Industry.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                candidateSectors.Add(company.Industry.Trim());
            }

            bool fetchedFromBharatStock = false;

            // First Priority: Query BharatStock API with candidates
            foreach (var sec in candidateSectors)
            {
                try
                {
                    var peerRecords = await _compareProvider.GetPeersBySectorAsync(sec, "market_cap", 10, cancellationToken);
                    if (peerRecords != null && peerRecords.Count > 0)
                    {
                        resultList = peerRecords.Select(p => new PeerDto
                        {
                            CompanyName = p.CompanyName ?? p.Symbol ?? string.Empty,
                            Price = p.Price,
                            PeRatio = p.PeRatio,
                            PbRatio = p.PbRatio,
                            MarketCap = p.MarketCap,
                            Roe = p.Roe,
                            Roce = p.Roce,
                            DividendYield = null, 
                            TotalShares = null
                        }).ToList();

                        fetchedFromBharatStock = true;
                        _logger.LogInformation("Successfully fetched {Count} peers from BharatStock for {Symbol} using sector '{Sector}'", resultList.Count, cleanSymbol, sec);

                        // Save verified working sector back to CompanyMaster
                        if (company != null && company.Sector != sec)
                        {
                            company.Sector = sec;
                            await _companyRepository.SaveOrUpdateCompanyAsync(company);
                        }

                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "BharatStock compare lookup returned no peers for sector '{Sector}'", sec);
                }
            }

            // Fallback: Indian API (used only if BharatStock returned 0 peers)
            if (!fetchedFromBharatStock)
            {
                try
                {
                    var indianOverview = await _indianApiClient.GetStockFinancialsAndOverviewAsync(cleanSymbol, cleanExchange, cancellationToken);
                    
                    if (indianOverview?.Peers != null && indianOverview.Peers.Count > 0)
                    {
                        resultList = indianOverview.Peers.Select(p => new PeerDto
                        {
                            CompanyName = p.CompanyName ?? string.Empty,
                            Price = p.Price,
                            PeRatio = p.PeRatio,
                            PbRatio = p.PbRatio,
                            MarketCap = p.MarketCap,
                            Roe = p.Roe,
                            Roce = null,
                            DividendYield = p.DividendYield,
                            TotalShares = p.TotalShares
                        }).ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch peers from IndianApi for symbol {Symbol}.", cleanSymbol);
                }
            }

            // Fallback 2: If both APIs failed, return any existing cached DB peers
            if (resultList.Count == 0)
            {
                var cachedDbPeers = await _stockPeerRepository.GetPeersByStockIdAsync(stockId);
                if (cachedDbPeers != null && cachedDbPeers.Count > 0)
                {
                    return cachedDbPeers.Select(p => new PeerDto
                    {
                        CompanyName = p.CompanyName,
                        Price = p.Price,
                        PeRatio = p.PeRatio,
                        PbRatio = p.PbRatio,
                        MarketCap = p.MarketCap,
                        Roe = p.Roe,
                        Roce = p.Roce,
                        DividendYield = p.DividendYield,
                        TotalShares = p.TotalShares
                    }).ToList();
                }
            }

            // Save to DB if we found peers from either API
            if (resultList.Count > 0)
            {
                var entitiesToSave = resultList.Select(p => new StockPeer
                {
                    CompanyName = p.CompanyName ?? string.Empty,
                    Price = p.Price,
                    PeRatio = p.PeRatio,
                    PbRatio = p.PbRatio,
                    MarketCap = p.MarketCap,
                    Roe = p.Roe,
                    Roce = p.Roce,
                    DividendYield = p.DividendYield,
                    TotalShares = p.TotalShares,
                    Source = fetchedFromBharatStock ? "BharatStock" : "IndianApi"
                }).ToList();
                
                await _stockPeerRepository.SavePeersAsync(stockId, entitiesToSave);
            }

            return resultList;
        }

        private static string? NormalizeSector(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var s = raw.Trim();

            // Oil, Gas & Fuels
            if (s.Contains("Oil", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Gas", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Refiner", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Petroleum", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Fuel", StringComparison.OrdinalIgnoreCase))
            {
                return "Oil Gas & Consumable Fuels";
            }

            // Information Technology
            if (s.Contains("Software", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Information Tech", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("IT -", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Computers", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("IT", StringComparison.OrdinalIgnoreCase))
            {
                return "Information Technology";
            }

            // Financial Services & Banking
            if (s.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Finance", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Financial", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Insurance", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("NBFC", StringComparison.OrdinalIgnoreCase))
            {
                return "Financial Services";
            }

            // Automobile
            if (s.Contains("Auto", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Motor", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Vehicle", StringComparison.OrdinalIgnoreCase))
            {
                return "Automobile and Auto Components";
            }

            // Healthcare & Pharma
            if (s.Contains("Pharma", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Health", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Drug", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Hospital", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Medical", StringComparison.OrdinalIgnoreCase))
            {
                return "Healthcare";
            }

            // FMCG
            if (s.Contains("Consumer Goods", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("FMCG", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Food", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Beverage", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Tobacco", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Dairy", StringComparison.OrdinalIgnoreCase))
            {
                return "Fast Moving Consumer Goods";
            }

            // Metals & Mining
            if (s.Contains("Metal", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Mining", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Steel", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Aluminium", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Iron", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Mineral", StringComparison.OrdinalIgnoreCase))
            {
                return "Metals & Mining";
            }

            // Power & Energy
            if (s.Contains("Power", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Electricity", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Renewable", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Energy", StringComparison.OrdinalIgnoreCase))
            {
                return "Power";
            }

            // Telecommunication
            if (s.Contains("Telecom", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Cellular", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Communication", StringComparison.OrdinalIgnoreCase))
            {
                return "Telecommunication";
            }

            // Construction Materials / Cement
            if (s.Contains("Cement", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Construction Material", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Building Material", StringComparison.OrdinalIgnoreCase))
            {
                return "Construction Materials";
            }

            // Chemicals
            if (s.Contains("Chemical", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Fertilizer", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Pesticide", StringComparison.OrdinalIgnoreCase))
            {
                return "Chemicals";
            }

            // Capital Goods / Engineering / Defence
            if (s.Contains("Capital Goods", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Defence", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Defense", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Aerospace", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Engineering", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Electrical Equipment", StringComparison.OrdinalIgnoreCase))
            {
                return "Capital Goods";
            }

            // Consumer Services / Retail
            if (s.Contains("Retail", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Consumer Service", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Hotel", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Restaurant", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Tourism", StringComparison.OrdinalIgnoreCase))
            {
                return "Consumer Services";
            }

            // Consumer Durables
            if (s.Contains("Durable", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Electronics", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Appliance", StringComparison.OrdinalIgnoreCase))
            {
                return "Consumer Durables";
            }

            return s;
        }
    }
}
