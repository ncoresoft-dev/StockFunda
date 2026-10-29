using StockLens_BusinessLayer.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockDealsService
    {
        Task<StockDealsSummaryDto> GetDealsBySymbolAsync(
            string symbol, 
            string? exchange = null, 
            bool forceRefresh = false, 
            CancellationToken cancellationToken = default);
    }
}
