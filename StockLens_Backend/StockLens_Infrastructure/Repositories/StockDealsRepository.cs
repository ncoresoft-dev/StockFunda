using Microsoft.EntityFrameworkCore;
using StockLens_Infrastructure.DataContext;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    // Make sure to add DbSet<StockDeal> StockDeals to your AppDbContext later!
    public class StockDealsRepository : IStockDealsRepository
    {
        private readonly StockLensDataContext _context;

        public StockDealsRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<List<StockDeal>> GetRecentDealsByStockIdAsync(int stockId, string category, int limit = 50, CancellationToken cancellationToken = default)
        {
            return await _context.Set<StockDeal>()
                .Where(d => d.StockId == stockId && d.DealCategory == category)
                .OrderByDescending(d => d.DealDate)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task AddRangeAsync(IEnumerable<StockDeal> deals, CancellationToken cancellationToken = default)
        {
            await _context.Set<StockDeal>().AddRangeAsync(deals, cancellationToken);
        }

        public async Task RemoveRangeAsync(IEnumerable<StockDeal> deals, CancellationToken cancellationToken = default)
        {
            _context.Set<StockDeal>().RemoveRange(deals);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
