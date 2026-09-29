using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class BreakoutEvaluationDto
    {
        public string SummaryText { get; set; } = string.Empty;
        public string OverallSignal { get; set; } = string.Empty; // "Bullish", "Bearish", "Neutral"
        public string SignalBadgeText { get; set; } = string.Empty; 
        public int BullishCount { get; set; }
        public int BearishCount { get; set; }
        
        public decimal CurrentPrice { get; set; }
        public decimal AvgVolume1Month { get; set; }
        public decimal TodayVolume { get; set; }
        
        public BreakoutLevelsDto Levels { get; set; } = new BreakoutLevelsDto();
    }

    public class BreakoutLevelsDto
    {
        public decimal? Year1High { get; set; }
        public decimal? Year1Low { get; set; }
        public decimal? Year2High { get; set; }
        public decimal? Year2Low { get; set; }
        public decimal? Year3High { get; set; }
        public decimal? Year3Low { get; set; }
    }
}
