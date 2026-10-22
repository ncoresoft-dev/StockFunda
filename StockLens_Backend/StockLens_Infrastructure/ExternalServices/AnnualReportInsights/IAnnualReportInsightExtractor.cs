using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.AnnualReportInsights
{
    public interface IAnnualReportInsightExtractor
    {
        /// <summary>
        /// Downloads the PDF from the provided URL, extracts text from high-priority sections via PdfPig,
        /// generates structured insights using Gemini AI, and returns the serialized JSON.
        /// </summary>
        Task<string?> ExtractInsightsJsonAsync(string symbol, string pdfUrl, string? reportYear = null, CancellationToken cancellationToken = default);
    }
}
