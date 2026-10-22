using StockLens_BusinessLayer.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockAnnualReportService
    {
        Task<StockAnnualReportsResponseDto> GetAnnualReportsBySymbolAsync(string symbol, bool refresh = false, CancellationToken cancellationToken = default);
        Task<StockAnnualReportsResponseDto> RequestAnnualReportAsync(string symbol, CancellationToken cancellationToken = default);
        Task<StockAnnualReportsResponseDto> AdminSaveAndExtractAnnualReportAsync(AdminSaveAndExtractReportRequestDto request, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AnnualReportDirectoryItemDto>> GetAnnualReportsDirectoryAsync(string? search = null, string? status = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AnnualReportDirectoryItemDto>> GetRequestedReportsAsync(CancellationToken cancellationToken = default);
        Task<string?> SuggestScreenerPdfUrlAsync(string symbol, CancellationToken cancellationToken = default);
    }
}
