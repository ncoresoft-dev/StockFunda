using StockLens_Infrastructure.ExternalServices.Screener.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.Screener
{
    public interface IScreenerClient
    {
        /// <summary>
        /// Fetches historical and latest Annual Reports with direct PDF download links.
        /// </summary>
        Task<IReadOnlyList<ScreenerDocumentItem>> GetAnnualReportsAsync(string symbol, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches quarterly Conference Call transcripts, PPTs, and audio recordings.
        /// </summary>
        Task<IReadOnlyList<ScreenerDocumentItem>> GetConcallsAsync(string symbol, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches all filings and documents available for a stock (Annual Reports, Concalls, Credit Ratings).
        /// </summary>
        Task<IReadOnlyList<ScreenerDocumentItem>> GetAllDocumentsAsync(string symbol, CancellationToken cancellationToken = default);
    }
}
