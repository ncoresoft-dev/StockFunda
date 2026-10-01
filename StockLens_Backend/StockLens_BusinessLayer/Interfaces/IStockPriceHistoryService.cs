using System.Threading;
using System.Threading.Tasks;
using StockLens_BusinessLayer.DTOs;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockPriceHistoryService
    {
        Task<PriceHistoryResponseDto> GetPriceHistoryByStockIdAsync(int stockId, string period = "5yr", bool forceRefresh = false, string filter = "price", CancellationToken cancellationToken = default);
        Task<PriceHistoryResponseDto> GetPriceHistoryBySymbolAsync(string symbol, string? exchange = null, string period = "5yr", bool forceRefresh = false, string filter = "price", CancellationToken cancellationToken = default);
    }
}
