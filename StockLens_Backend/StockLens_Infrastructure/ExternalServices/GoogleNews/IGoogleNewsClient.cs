using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.GoogleNews
{
    public interface IGoogleNewsClient
    {
        Task<IReadOnlyList<IndianApiStandardArticle>> GetStockNewsAsync(
            string symbol,
            string? companyName = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches market-wide news from the Google News RSS feed.
        /// Guaranteed not to be fetched twice within the same minute.
        /// </summary>
        Task<IReadOnlyList<IndianApiStandardArticle>> GetMarketNewsRssAsync(
            string? rssUrl = null,
            CancellationToken cancellationToken = default);
    }
}

