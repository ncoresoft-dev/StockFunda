using StockLens_DataLayer.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_DataLayer.Interfaces
{
    public interface IStockAnnualReportRepository
    {
        Task<StockAnnualReport?> GetLatestByStockIdAsync(int stockId, CancellationToken cancellationToken = default);
        Task<StockAnnualReport?> GetLatestBySymbolAsync(string symbol, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<StockAnnualReport>> GetAllReportsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<StockAnnualReport>> GetRequestedReportsAsync(CancellationToken cancellationToken = default);
        Task AddAsync(StockAnnualReport report, CancellationToken cancellationToken = default);
        Task UpdateAsync(StockAnnualReport report, CancellationToken cancellationToken = default);
    }
}
