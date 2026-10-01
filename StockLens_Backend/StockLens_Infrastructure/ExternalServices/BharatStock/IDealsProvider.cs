using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.BharatStock
{
    public interface IDealsProvider
    {
        Task<IReadOnlyList<BharatStockDealRecord>> GetBulkDealsAsync(
            string ticker,
            string? exchange = "NSE",
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<BharatStockDealRecord>> GetBlockDealsAsync(
            string ticker,
            string? exchange = "NSE",
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<BharatStockInsiderTradeRecord>> GetInsiderTradesAsync(
            string ticker,
            string? exchange = "NSE",
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);
    }
}
