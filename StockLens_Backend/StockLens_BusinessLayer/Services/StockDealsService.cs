using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.BharatStock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockDealsService : IStockDealsService
    {
        private readonly IDealsProvider _dealsProvider;
        private readonly IStockRepository _stockRepository;
        private readonly IStockDealsRepository _dealsRepository;
        private readonly ILogger<StockDealsService> _logger;

        public StockDealsService(
            IDealsProvider dealsProvider,
            IStockRepository stockRepository,
            IStockDealsRepository dealsRepository,
            ILogger<StockDealsService> logger)
        {
            _dealsProvider = dealsProvider;
            _stockRepository = stockRepository;
            _dealsRepository = dealsRepository;
            _logger = logger;
        }

        public async Task<StockDealsSummaryDto> GetDealsByStockIdAsync(
            int stockId, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var stock = await _stockRepository.GetByIdAsync(stockId);
                if (stock == null)
                {
                    throw new KeyNotFoundException($"Stock with ID {stockId} was not found.");
                }

                return await ProcessDealsAsync(stock, forceRefresh, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deals for Stock ID {StockId}", stockId);
                return new StockDealsSummaryDto { ErrorMessage = "An error occurred while fetching deal data." };
            }
        }

        public async Task<StockDealsSummaryDto> GetDealsBySymbolAsync(
            string symbol, string? exchange = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var cleanSymbol = symbol.Trim().ToUpperInvariant();
                var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
                
                var stock = await _stockRepository.GetBySymbolAsync(cleanSymbol, cleanExchange);
                if (stock == null)
                {
                    stock = await _stockRepository.GetOrCreateStockAsync(cleanSymbol, cleanExchange, cleanSymbol, "", cancellationToken);
                }

                return await ProcessDealsAsync(stock, forceRefresh, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deals for {Symbol}", symbol);
                return new StockDealsSummaryDto { Symbol = symbol, ErrorMessage = "An error occurred while fetching deal data." };
            }
        }

        private async Task<StockDealsSummaryDto> ProcessDealsAsync(
            Stock stock, bool forceRefresh, CancellationToken cancellationToken)
        {
            var result = new StockDealsSummaryDto { Symbol = stock.Symbol };

            var existingBulk = await _dealsRepository.GetRecentDealsByStockIdAsync(stock.Id, DealCategories.Bulk, 50, cancellationToken);
            var existingBlock = await _dealsRepository.GetRecentDealsByStockIdAsync(stock.Id, DealCategories.Block, 50, cancellationToken);
            var existingInsider = await _dealsRepository.GetRecentDealsByStockIdAsync(stock.Id, DealCategories.Insider, 50, cancellationToken);

            var oldestSync = DateTime.UtcNow;
            if (existingBulk.Any()) oldestSync = existingBulk.Min(d => d.LastSyncedAt);
            if (existingBlock.Any() && existingBlock.Min(d => d.LastSyncedAt) < oldestSync) oldestSync = existingBlock.Min(d => d.LastSyncedAt);
            if (existingInsider.Any() && existingInsider.Min(d => d.LastSyncedAt) < oldestSync) oldestSync = existingInsider.Min(d => d.LastSyncedAt);

            bool needsRefresh = forceRefresh || (DateTime.UtcNow - oldestSync).TotalHours > 24 || (!existingBulk.Any() && !existingBlock.Any() && !existingInsider.Any());

            if (needsRefresh)
            {
                _logger.LogInformation("Fetching Deals for {Symbol} from BharatStock API", stock.Symbol);

                // Fetch APIs sequentially (one by one)
                var bulkData = await _dealsProvider.GetBulkDealsAsync(stock.Symbol, stock.Exchange, 1, 50, cancellationToken);
                var blockData = await _dealsProvider.GetBlockDealsAsync(stock.Symbol, stock.Exchange, 1, 50, cancellationToken);
                var insiderData = await _dealsProvider.GetInsiderTradesAsync(stock.Symbol, stock.Exchange, 1, 50, cancellationToken);

                var newDeals = new List<StockDeal>();
                var now = DateTime.UtcNow;

                if (bulkData != null)
                {
                    newDeals.AddRange(bulkData.Select(d => new StockDeal
                    {
                        StockId = stock.Id,
                        DealCategory = DealCategories.Bulk,
                        DealDate = ParseDate(d.DealDate),
                        ClientName = d.ClientName ?? "",
                        Action = d.BuySell?.ToUpper() ?? "",
                        Quantity = d.Quantity ?? 0,
                        AveragePrice = d.AvgPrice ?? 0m,
                        LastSyncedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    }));
                }

                if (blockData != null)
                {
                    newDeals.AddRange(blockData.Select(d => new StockDeal
                    {
                        StockId = stock.Id,
                        DealCategory = DealCategories.Block,
                        DealDate = ParseDate(d.DealDate),
                        ClientName = d.ClientName ?? "",
                        Action = d.BuySell?.ToUpper() ?? "",
                        Quantity = d.Quantity ?? 0,
                        AveragePrice = d.AvgPrice ?? 0m,
                        LastSyncedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    }));
                }

                if (insiderData != null)
                {
                    newDeals.AddRange(insiderData.Select(d => new StockDeal
                    {
                        StockId = stock.Id,
                        DealCategory = DealCategories.Insider,
                        DealDate = ParseDate(d.IntimationDate),
                        ClientName = d.AcquirerName ?? "",
                        PersonCategory = d.PersonCategory,
                        IsPromoter = d.IsPromoter,
                        Action = d.TransactionType?.ToUpper() ?? "",
                        Quantity = d.Quantity ?? 0,
                        TotalValue = d.Value ?? 0m,
                        SharesAfterPct = d.SharesAfterPct ?? 0m,
                        Mode = d.Mode ?? "",
                        LastSyncedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    }));
                }

                if (newDeals.Any())
                {
                    var oldDeals = new List<StockDeal>();
                    oldDeals.AddRange(existingBulk);
                    oldDeals.AddRange(existingBlock);
                    oldDeals.AddRange(existingInsider);
                    
                    await _dealsRepository.RemoveRangeAsync(oldDeals, cancellationToken);
                    await _dealsRepository.AddRangeAsync(newDeals, cancellationToken);
                    await _dealsRepository.SaveChangesAsync(cancellationToken);
                    
                    existingBulk = newDeals.Where(d => d.DealCategory == DealCategories.Bulk).ToList();
                    existingBlock = newDeals.Where(d => d.DealCategory == DealCategories.Block).ToList();
                    existingInsider = newDeals.Where(d => d.DealCategory == DealCategories.Insider).ToList();
                }
            }
            else
            {
                 _logger.LogInformation("Serving {Symbol} Deals from DB Cache", stock.Symbol);
            }

            // Map Bulk Deals
            result.BulkDeals = existingBulk.Select(d => new DealItemDto
            {
                DealDate = d.DealDate?.ToString("yyyy-MM-dd") ?? "",
                ClientName = d.ClientName,
                Action = d.Action,
                Quantity = d.Quantity ?? 0,
                AveragePrice = d.AveragePrice ?? 0m
            }).ToList();

            // Map Block Deals
            result.BlockDeals = existingBlock.Select(d => new DealItemDto
            {
                DealDate = d.DealDate?.ToString("yyyy-MM-dd") ?? "",
                ClientName = d.ClientName,
                Action = d.Action,
                Quantity = d.Quantity ?? 0,
                AveragePrice = d.AveragePrice ?? 0m
            }).ToList();

            // Map Insider Trades
            result.InsiderTrades = existingInsider.Select(d => new InsiderTradeItemDto
            {
                IntimationDate = d.DealDate?.ToString("yyyy-MM-dd") ?? "",
                AcquirerName = d.ClientName,
                PersonCategory = d.PersonCategory ?? "",
                IsPromoter = d.IsPromoter ?? false,
                TransactionType = d.Action,
                Quantity = d.Quantity ?? 0,
                TotalValue = d.TotalValue ?? 0m,
                SharesAfterPct = d.SharesAfterPct ?? 0m,
                Mode = d.Mode ?? ""
            }).ToList();

            return result;
        }
        
        private DateTime? ParseDate(string? dateStr)
        {
            if (DateTime.TryParse(dateStr, out var d)) return d;
            return null;
        }
    }
}
