using StockLens_DataLayer.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_DataLayer.Interfaces
{
    public interface IMarketIndexConstituentRepository
    {
        Task<IEnumerable<MarketIndexConstituent>> GetByIndexNameAsync(string indexName, CancellationToken cancellationToken = default);
        Task ReplaceIndexConstituentsAsync(string indexName, IEnumerable<MarketIndexConstituent> constituents, CancellationToken cancellationToken = default);
    }
}
