using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class MarketPulseResponseDto
    {
        public string Timeframe { get; set; } = "1D";
        public string MarketSummary { get; set; } = string.Empty;
        public string SectorMapRoute { get; set; } = "/sector-analysis";
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public List<MarketPulseIndexDto> Indices { get; set; } = new();
    }

    public class MarketPulseIndexDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public string FormattedPrice { get; set; } = string.Empty;
        public decimal Change { get; set; }
        public string FormattedPointsChange { get; set; } = string.Empty;
        public decimal ChangePercent { get; set; }
        public string FormattedChangePercent { get; set; } = string.Empty;
        public bool IsPositive { get; set; }
        public decimal DayLow { get; set; }
        public string FormattedDayLow { get; set; } = string.Empty;
        public decimal DayHigh { get; set; }
        public string FormattedDayHigh { get; set; } = string.Empty;
        public double RangeProgressPct { get; set; }
        public List<decimal> SparklinePrices { get; set; } = new();
        public string TrendPath { get; set; } = string.Empty;
        public string TakeawayNote { get; set; } = string.Empty;
    }
}
