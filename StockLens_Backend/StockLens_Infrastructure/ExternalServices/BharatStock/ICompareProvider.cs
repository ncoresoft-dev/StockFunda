using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.BharatStock
{
    public interface ICompareProvider
    {
        Task<IReadOnlyList<BharatStockCompareRecord>> GetPeersBySectorAsync(
            string sector,
            string sort = "market_cap",
            int limit = 10,
            CancellationToken cancellationToken = default);
    }
}
