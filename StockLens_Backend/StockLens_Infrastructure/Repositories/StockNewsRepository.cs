using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class StockNewsRepository : IStockNewsRepository
    {
        private readonly StockLensDataContext _context;

        public StockNewsRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<StockNews>> GetNewsByStockIdAsync(int stockId, int limit = 5, int page = 1)
        {
            var query = _context.StockNews
                .AsNoTracking()
                .Where(sn => sn.StockId == stockId)
                .OrderByDescending(sn => sn.PublishedAt);

            // If limit > 0, paginate. If limit <= 0 (for testing), return all news without limit.
            if (limit > 0)
            {
                if (page < 1) page = 1;
                return await query
                    .Skip((page - 1) * limit)
                    .Take(limit)
                    .ToListAsync();
            }

            return await query.ToListAsync();
        }

        public async Task<DateTime?> GetLatestFetchedAtByStockIdAsync(int stockId)
        {
            return await _context.StockNews
                .AsNoTracking()
                .Where(sn => sn.StockId == stockId)
                .OrderByDescending(sn => sn.FetchedAt)
                .Select(sn => (DateTime?)sn.FetchedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<HashSet<string>> GetExistingExternalNewsIdsAsync(int stockId, IEnumerable<string> externalNewsIds)
        {
            var ids = externalNewsIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (!ids.Any()) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var existing = await _context.StockNews
                .AsNoTracking()
                .Where(sn => sn.StockId == stockId && sn.ExternalNewsId != null && ids.Contains(sn.ExternalNewsId))
                .Select(sn => sn.ExternalNewsId!)
                .ToListAsync();

            return new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<HashSet<string>> GetExistingSourceUrlsAsync(int stockId, IEnumerable<string> sourceUrls)
        {
            var urls = sourceUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
            if (!urls.Any()) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var existing = await _context.StockNews
                .AsNoTracking()
                .Where(sn => sn.StockId == stockId && urls.Contains(sn.SourceUrl))
                .Select(sn => sn.SourceUrl)
                .ToListAsync();

            return new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        }

        public async Task AddRangeAsync(IEnumerable<StockNews> newsItems)
        {
            await _context.StockNews.AddRangeAsync(newsItems);
        }

        public async Task DeleteOldNewsAsync(int stockId, int keepCount)
        {
            // If keepCount <= 0, do not delete any news (for testing / unlimited retention)
            if (keepCount <= 0) return;

            // DB mein strictly `keepCount` items hi rakhne ke liye baki sab delete karo
            var oldNews = await _context.StockNews
                .Where(sn => sn.StockId == stockId)
                .OrderByDescending(sn => sn.PublishedAt)
                .Skip(keepCount)
                .ToListAsync();

            if (oldNews.Any())
            {
                _context.StockNews.RemoveRange(oldNews);
            }
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }
    }
}
