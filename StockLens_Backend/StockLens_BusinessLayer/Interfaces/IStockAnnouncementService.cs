using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockAnnouncementService
    {
        /// <summary>
        /// Retrieves corporate announcements from the database with optional symbol, search, and category filters.
        /// Does NOT call NSE directly from user requests.
        /// </summary>
        Task<StockAnnouncementResponseDto> GetAnnouncementsAsync(
            string? symbol = null,
            string? search = null,
            string? category = null,
            int limit = 30,
            int page = 1,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves corporate announcements filtered by multiple stock symbols (e.g. user watchlist).
        /// </summary>
        Task<StockAnnouncementResponseDto> GetAnnouncementsAsync(
            string? symbol,
            IEnumerable<string>? symbols,
            string? search,
            string? category,
            int limit,
            int page,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves unique announcement categories/subjects stored in the database.
        /// </summary>
        Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches the latest announcements from the NSE feed, dedupes against existing records,
        /// and persists newly discovered announcements into the local database (defaults to past 3 months).
        /// </summary>
        Task<int> SyncLatestAnnouncementsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches announcements from the NSE feed for the specified number of months back (e.g. 3 months).
        /// </summary>
        Task<int> SyncLatestAnnouncementsAsync(int monthsBack, CancellationToken cancellationToken = default);
    }
}
