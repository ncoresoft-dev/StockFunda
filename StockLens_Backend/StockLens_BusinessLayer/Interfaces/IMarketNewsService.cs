using StockLens_BusinessLayer.DTOs;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IMarketNewsService
    {
        /// <summary>
        /// Retrieves the latest market news from the cache.
        /// Guaranteed to never invoke external RSS feeds during user request handling.
        /// </summary>
        MarketNewsResponseDto GetMarketNews(int limit = 10, string? category = null);
    }
}
