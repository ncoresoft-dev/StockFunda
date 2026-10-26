using System;

namespace StockLens_BusinessLayer.DTOs
{
    public class MarketTickerQuoteDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal? PreviousClose { get; set; }
        public decimal? Change { get; set; }
        public decimal? ChangePercent { get; set; }
        public decimal? DayHigh { get; set; }
        public decimal? DayLow { get; set; }
        public string InstrumentType { get; set; } = "INDEX"; // "INDEX" or "CURRENCY"
        public int DisplayOrder { get; set; }
        public DateTime LastUpdated { get; set; }
        public string FormattedPrice { get; set; } = string.Empty;
        public string FormattedChange { get; set; } = string.Empty;
        public bool IsPositive => (Change ?? 0) >= 0;
    }
}
