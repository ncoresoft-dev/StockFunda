using StockLens_BusinessLayer.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockAnnualReportService
    {
        Task<StockAnnualReportsResponseDto> GetAnnualReportsBySymbolAsync(string symbol, bool refresh = false, CancellationToken cancellationToken = default);
    }
}
