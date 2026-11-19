using Microsoft.EntityFrameworkCore;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.DataContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.Repositories
{
    public class StockAnnouncementRepository : IStockAnnouncementRepository
    {
        private readonly StockLensDataContext _context;

        public StockAnnouncementRepository(StockLensDataContext context)
        {
            _context = context;
        }

        public Task<IEnumerable<StockAnnouncement>> GetAnnouncementsAsync(
            string? symbol = null,
            string? search = null,
            string? category = null,
            int limit = 50,
            int page = 1,
            CancellationToken cancellationToken = default)
        {
            return GetAnnouncementsAsync(symbol, null, search, category, limit, page, cancellationToken);
        }

        public async Task<IEnumerable<StockAnnouncement>> GetAnnouncementsAsync(
            string? symbol,
            IEnumerable<string>? symbols,
            string? search,
            string? category,
            int limit,
            int page,
            CancellationToken cancellationToken = default)
        {
            var query = BuildQuery(symbol, symbols, search, category);

            if (page < 1) page = 1;
            if (limit <= 0) limit = 50;
            if (limit > 100) limit = 100;

            return await query
                .OrderByDescending(a => a.BroadcastDateTime)
                .ThenByDescending(a => a.Id)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public Task<int> GetTotalCountAsync(
            string? symbol = null,
            string? search = null,
            string? category = null,
            CancellationToken cancellationToken = default)
        {
            return GetTotalCountAsync(symbol, null, search, category, cancellationToken);
        }

        public async Task<int> GetTotalCountAsync(
            string? symbol,
            IEnumerable<string>? symbols,
            string? search,
            string? category,
            CancellationToken cancellationToken = default)
        {
            var query = BuildQuery(symbol, symbols, search, category);
            return await query.CountAsync(cancellationToken);
        }

        public async Task<int> SaveAnnouncementsAsync(
            IEnumerable<StockAnnouncement> announcements,
            CancellationToken cancellationToken = default)
        {
            if (announcements == null) return 0;

            var incomingList = announcements
                .Where(a => !string.IsNullOrWhiteSpace(a.SeqId))
                .GroupBy(a => a.SeqId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (!incomingList.Any()) return 0;

            var incomingSeqIds = incomingList.Select(a => a.SeqId).Distinct().ToList();

            // Query existing SeqIds from database in batches to avoid SQL parameter limit
            var existingSeqIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int batchSize = 1000;
            for (int i = 0; i < incomingSeqIds.Count; i += batchSize)
            {
                var batch = incomingSeqIds.Skip(i).Take(batchSize).ToList();
                var found = await _context.StockAnnouncements
                    .AsNoTracking()
                    .Where(a => batch.Contains(a.SeqId))
                    .Select(a => a.SeqId)
                    .ToListAsync(cancellationToken);

                foreach (var id in found)
                {
                    existingSeqIds.Add(id);
                }
            }

            var newItems = incomingList
                .Where(a => !existingSeqIds.Contains(a.SeqId))
                .ToList();

            if (!newItems.Any()) return 0;

            foreach (var item in newItems)
            {
                item.BroadcastDateTime ??= item.CreatedAt;
                item.Symbol = item.Symbol.Trim().ToUpperInvariant();
            }

            await _context.StockAnnouncements.AddRangeAsync(newItems, cancellationToken);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<DateTime?> GetLatestBroadcastDateTimeAsync(CancellationToken cancellationToken = default)
        {
            return await _context.StockAnnouncements
                .AsNoTracking()
                .OrderByDescending(a => a.BroadcastDateTime)
                .Select(a => a.BroadcastDateTime)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(CancellationToken cancellationToken = default)
        {
            return await _context.StockAnnouncements
                .AsNoTracking()
                .Where(a => !string.IsNullOrEmpty(a.Subject))
                .Select(a => a.Subject!)
                .Distinct()
                .OrderBy(s => s)
                .Take(50)
                .ToListAsync(cancellationToken);
        }

        private IQueryable<StockAnnouncement> BuildQuery(string? symbol, IEnumerable<string>? symbols, string? search, string? category)
        {
            var query = _context.StockAnnouncements.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var trimmedSymbol = symbol.Trim().ToUpperInvariant();
                query = query.Where(a => a.Symbol == trimmedSymbol);
            }
            else if (symbols != null && symbols.Any())
            {
                var upperSymbols = symbols
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();

                if (upperSymbols.Any())
                {
                    // SARGABLE: Use a.Symbol directly without scalar function so SQL Server can seek index IX_StockAnnouncements_Symbol
                    query = query.Where(a => upperSymbols.Contains(a.Symbol));
                }
            }

            if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                var trimmedCat = category.Trim();
                query = query.Where(a => a.Subject != null && a.Subject.Contains(trimmedCat));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(a =>
                    a.Symbol.Contains(term) ||
                    a.CompanyName.Contains(term) ||
                    (a.Subject != null && a.Subject.Contains(term)) ||
                    (a.Details != null && a.Details.Contains(term)));
            }

            return query;
        }
    }
}
