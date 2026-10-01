using StockLens_DataLayer.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_DataLayer.Interfaces
{
    public interface IStockDealsRepository
    {
        Task<List<StockDeal>> GetRecentDealsByStockIdAsync(int stockId, string category, int limit = 50, CancellationToken cancellationToken = default);
        Task AddRangeAsync(IEnumerable<StockDeal> deals, CancellationToken cancellationToken = default);
        Task RemoveRangeAsync(IEnumerable<StockDeal> deals, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
