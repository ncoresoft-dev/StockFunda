using StockLens_DataLayer.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.Nse
{
    public interface INseAnnouncementClient
    {
        /// <summary>
        /// Fetches corporate announcements from the official NSE API feed.
        /// Primes and reuses session cookies for several minutes.
        /// </summary>
        /// <param name="fromDate">Optional start date (defaults to today's date in IST if null).</param>
        /// <param name="toDate">Optional end date (defaults to today's date in IST if null).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<IReadOnlyList<StockAnnouncement>> FetchAnnouncementsAsync(
            DateTime? fromDate = null,
            DateTime? toDate = null,
            CancellationToken cancellationToken = default);
    }
}
