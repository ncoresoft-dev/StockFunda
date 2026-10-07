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

        public async Task<List<PeerDto>> GetStockPeersByStockIdAsync(int stockId, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var stock = await _stockRepository.GetByIdAsync(stockId);
            if (stock == null)
            {
                throw new KeyNotFoundException($"Stock with ID {stockId} not found.");
            }

            if (!forceRefresh)
            {
                var dbPeers = await _stockPeerRepository.GetPeersByStockIdAsync(stockId);
                if (dbPeers != null && dbPeers.Count > 0)
                {
                    var lastSynced = dbPeers.First().LastSyncedAt;
                    // If data is less than 24 hours old, return it from DB
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
            var sector = company?.Sector;

            try
            {
                var stockDetails = await _financialProvider.GetStockDetailsAsync(cleanSymbol, cleanExchange, cancellationToken);
                if (!string.IsNullOrWhiteSpace(stockDetails?.Sector))
                {
                    sector = stockDetails.Sector;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch stock details from BharatStock for symbol {Symbol} to get the Sector. Falling back to DB sector.", cleanSymbol);
            }

            bool fetchedFromBharatStock = false;

            // First Priority: BharatStock API
            if (!string.IsNullOrWhiteSpace(sector))
            {
                try
                {
                    var peerRecords = await _compareProvider.GetPeersBySectorAsync(sector, "market_cap", 10, cancellationToken);
                    
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
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch peers from BharatStock for sector {Sector}. Falling back to IndianApi.", sector);
                }
            }

            // Fallback: Indian API
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
    }
}
