using StockLens_BusinessLayer.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockComparisonService
    {
        Task<StockComparisonDto> CompareStocksAsync(
            string symbolA,
            string symbolB,
            string? exchange = "NSE",
            string period = "1yr",
            bool refresh = false,
            CancellationToken cancellationToken = default);

        IReadOnlyList<ComparisonPresetDto> GetComparisonPresets();
    }
}

