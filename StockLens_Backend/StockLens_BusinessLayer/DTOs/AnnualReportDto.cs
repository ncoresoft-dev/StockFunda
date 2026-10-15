using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class AnnualReportDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Year { get; set; }
        public string PdfUrl { get; set; } = string.Empty;
        public string? Source { get; set; }
    }

    public class StockAnnualReportsResponseDto
    {
        public int? StockId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public int TotalCount { get; set; }
        public IReadOnlyList<AnnualReportDto> Reports { get; set; } = new List<AnnualReportDto>();
    }
}
