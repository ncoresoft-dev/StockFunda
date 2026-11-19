using StockLens_DataLayer.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_DataLayer.Interfaces
{
    public interface IStockAnnouncementRepository
    {
        Task<IEnumerable<StockAnnouncement>> GetAnnouncementsAsync(
            string? symbol = null,
            string? search = null,
            string? category = null,
            int limit = 50,
            int page = 1,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<StockAnnouncement>> GetAnnouncementsAsync(
            string? symbol,
            IEnumerable<string>? symbols,
            string? search,
            string? category,
            int limit,
            int page,
            CancellationToken cancellationToken = default);

        Task<int> GetTotalCountAsync(
            string? symbol = null,
            string? search = null,
            string? category = null,
            CancellationToken cancellationToken = default);

        Task<int> GetTotalCountAsync(
            string? symbol,
            IEnumerable<string>? symbols,
            string? search,
            string? category,
            CancellationToken cancellationToken = default);

        Task<int> SaveAnnouncementsAsync(
            IEnumerable<StockAnnouncement> announcements,
            CancellationToken cancellationToken = default);

        Task<DateTime?> GetLatestBroadcastDateTimeAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(CancellationToken cancellationToken = default);
    }
}
