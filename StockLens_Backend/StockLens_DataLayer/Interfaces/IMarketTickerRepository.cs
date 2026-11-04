using StockLens_DataLayer.Entities;

namespace StockLens_DataLayer.Interfaces
{
    public interface IMarketTickerRepository
    {
        Task<List<MarketTickerQuote>> GetAllQuotesAsync(CancellationToken cancellationToken = default);
        Task<MarketTickerQuote?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default);
        Task SaveOrUpdateQuotesAsync(IEnumerable<MarketTickerQuote> quotes, CancellationToken cancellationToken = default);
    }
}
