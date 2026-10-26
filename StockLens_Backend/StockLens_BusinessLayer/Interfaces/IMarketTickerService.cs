using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IMarketTickerService
    {
        Task<IEnumerable<MarketTickerQuoteDto>> GetMarketTickerQuotesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    }
}
