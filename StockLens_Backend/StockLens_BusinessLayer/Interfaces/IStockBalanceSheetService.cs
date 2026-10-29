using StockLens_BusinessLayer.Models;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockBalanceSheetService
    {
        Task<BalanceSheetResponseDto> GetBalanceSheetAsync(
            string symbol,
            string? exchange = "NSE",
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);
    }
}
