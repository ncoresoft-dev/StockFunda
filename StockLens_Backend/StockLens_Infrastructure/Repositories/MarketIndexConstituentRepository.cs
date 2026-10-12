using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class MarketIndexConstituentRepository : IMarketIndexConstituentRepository
    {
        private readonly StockLensDataContext _context;

        public MarketIndexConstituentRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<MarketIndexConstituent>> GetByIndexNameAsync(string indexName, CancellationToken cancellationToken = default)
        {
            return await _context.MarketIndexConstituents
                .Include(c => c.Company)
                .Where(c => c.IndexName == indexName)
                .OrderByDescending(c => c.MarketCap)
                .ToListAsync(cancellationToken);
        }

        public async Task ReplaceIndexConstituentsAsync(string indexName, IEnumerable<MarketIndexConstituent> constituents, CancellationToken cancellationToken = default)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var existing = await _context.MarketIndexConstituents
                    .Where(c => c.IndexName == indexName)
                    .ToListAsync(cancellationToken);
                
                if (existing.Any())
                {
                    _context.MarketIndexConstituents.RemoveRange(existing);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                if (constituents.Any())
                {
                    await _context.MarketIndexConstituents.AddRangeAsync(constituents, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
