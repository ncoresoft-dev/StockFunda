using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class PriceHistoryResponseDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        // X-axis dates
        public List<string> Dates { get; set; } = new();

        // Arrays for chart series
        public List<decimal> Opens { get; set; } = new();
        public List<decimal> Highs { get; set; } = new();
        public List<decimal> Lows { get; set; } = new();
        public List<decimal> ClosePrices { get; set; } = new();
        public List<long> Volumes { get; set; } = new();

        public List<decimal?> Dma50 { get; set; } = new();
        public List<decimal?> Dma200 { get; set; } = new();
        
        public string Source { get; set; } = "YahooFinance";
        public string LastSyncedAt { get; set; } = string.Empty;
        
        public List<PatternEventDto> DetectedPatterns { get; set; } = new();
        public Dictionary<string, int> PatternCounts { get; set; } = new();

    }

    public class PatternPointDto
    {
        public string Date { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public class PatternEventDto
    {
        public string Date { get; set; } = string.Empty;
        public string PatternName { get; set; } = string.Empty;
        public string Signal { get; set; } = string.Empty; // "Bullish" or "Bearish"
        public List<PatternPointDto> Coordinates { get; set; } = new();
    }
}
