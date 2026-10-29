using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockNewsService
    {
        Task<StockNewsResponseDto> GetLatestNewsBySymbolAsync(string symbol, string? exchange = "NSE", int limit = 5, int page = 1, bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task<IEnumerable<StockDto>> GetAllStocksAsync();
    }
}
