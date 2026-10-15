using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.Screener;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Services
{
    public class StockAnnualReportService : IStockAnnualReportService
    {
        private readonly IScreenerClient _screenerClient;
        private readonly IStockRepository _stockRepository;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<StockAnnualReportService> _logger;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(7);

        public StockAnnualReportService(
            IScreenerClient screenerClient,
            IStockRepository stockRepository,
            IMemoryCache memoryCache,
            ILogger<StockAnnualReportService> logger)
        {
            _screenerClient = screenerClient;
            _stockRepository = stockRepository;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task<StockAnnualReportsResponseDto> GetAnnualReportsBySymbolAsync(
            string symbol,
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var cacheKey = $"screener_current_annual_report_{cleanSymbol}";

            if (!refresh && _memoryCache.TryGetValue(cacheKey, out StockAnnualReportsResponseDto? cachedResponse) && cachedResponse != null)
            {
                _logger.LogInformation("Returning cached annual report for symbol: {Symbol}", cleanSymbol);
                return cachedResponse;
            }

            // Look up stock in local database if available to get company name and id
            var stock = await _stockRepository.GetBySymbolAsync(cleanSymbol);
            var companyName = stock?.Company?.CompanyName;

            var docs = await _screenerClient.GetAnnualReportsAsync(cleanSymbol, cancellationToken);

            // Sirf current / latest year ki report lena hai
            var latestReport = docs.Select(d => new AnnualReportDto
            {
                Title = d.Title,
                Year = d.Year,
                PdfUrl = d.Url,
                Source = d.Source ?? "BSE"
            })
            .OrderByDescending(r => r.Year)
            .Take(1)
            .ToList();

            var response = new StockAnnualReportsResponseDto
            {
                StockId = stock?.Id,
                Symbol = cleanSymbol,
                CompanyName = companyName,
                TotalCount = latestReport.Count,
                Reports = latestReport
            };

            if (latestReport.Count > 0)
            {
                _memoryCache.Set(cacheKey, response, CacheDuration);
            }

            return response;
        }

        public async Task<StockAnnualReportsResponseDto> GetAnnualReportsByStockIdAsync(
            int stockId,
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            var stock = await _stockRepository.GetByIdAsync(stockId);
            if (stock == null)
            {
                throw new KeyNotFoundException($"Stock with ID {stockId} was not found.");
            }

            return await GetAnnualReportsBySymbolAsync(stock.Symbol, refresh, cancellationToken);
        }
    }
}
