using Microsoft.Extensions.Logging;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using System;

namespace StockLens_BusinessLayer.Services
{
    public class MarketNewsService : IMarketNewsService
    {
        private readonly IMarketNewsCache _cache;
        private readonly ILogger<MarketNewsService> _logger;

        public MarketNewsService(IMarketNewsCache cache, ILogger<MarketNewsService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public MarketNewsResponseDto GetMarketNews(int limit = 10, string? category = null)
        {
            _logger.LogDebug("Serving market news from cache (limit: {Limit}, category: {Category}) without outbound HTTP request.", limit, category);
            return _cache.GetResponse(limit, category);
        }
    }
}
