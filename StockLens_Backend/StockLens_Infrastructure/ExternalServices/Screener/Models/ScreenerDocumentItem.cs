using System;

namespace StockLens_Infrastructure.ExternalServices.Screener.Models
{
    public enum ScreenerDocumentType
    {
        AnnualReport,
        ConcallTranscript,
        ConcallPpt,
        ConcallAudio,
        CreditRating,
        Other
    }

    public class ScreenerDocumentItem
    {
        public string Title { get; set; } = string.Empty;
        public string? Year { get; set; }
        public string Url { get; set; } = string.Empty;
        public string? Source { get; set; }
        public ScreenerDocumentType DocumentType { get; set; }
        public DateTime? DocumentDate { get; set; }
    }
}
