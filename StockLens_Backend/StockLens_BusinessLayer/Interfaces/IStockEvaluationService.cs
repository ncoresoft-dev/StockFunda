using System.Threading;
using System.Threading.Tasks;
using StockLens_BusinessLayer.DTOs;

namespace StockLens_BusinessLayer.Interfaces
{
    public interface IStockEvaluationService
    {
        /// <summary>
        /// Evaluates a stock against the 23 fundamental, technical, and smart-money metrics to generate a 0-100 score and Buy/Hold/Avoid signal.
        /// </summary>
        Task<StockHealthScoreDto> EvaluateStockAsync(string symbol, string? exchange = "NSE", bool refresh = false, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Evaluates Multi-Year Breakouts and Volume Spikes for a stock.
        /// </summary>
        Task<BreakoutEvaluationDto> GetBreakoutAnalysisAsync(string symbol, string? exchange = "NSE", bool refresh = false, CancellationToken cancellationToken = default);
    }
}
