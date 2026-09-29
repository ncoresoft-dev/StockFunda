using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class BreakoutEvaluationDto
    {
        public string SummaryText { get; set; } = string.Empty;
        public string OverallSignal { get; set; } = string.Empty; // "Bullish", "Bearish", "Neutral"
        public string SignalBadgeText { get; set; } = string.Empty; 
        public string SignalStrength { get; set; } = string.Empty;
        public int BullishCount { get; set; }
        public int BearishCount { get; set; }
        
        public decimal CurrentPrice { get; set; }
        public decimal AvgVolume1Month { get; set; }
        public decimal TodayVolume { get; set; }
        
        public decimal VolumeRatio { get; set; }
        public string VolumeStatusText { get; set; } = string.Empty;
        public string VolumeStatusColor { get; set; } = string.Empty;
        public string VolumeStatusClass { get; set; } = string.Empty;
        public string VolumeSummaryTitle { get; set; } = string.Empty;
        
        public BreakoutLevelsDto Levels { get; set; } = new BreakoutLevelsDto();
        
        public List<BreakoutTimeframeDto> Timeframes { get; set; } = new List<BreakoutTimeframeDto>();
        public List<PrioritySignalDto> PrioritySignals { get; set; } = new List<PrioritySignalDto>();
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
    
    public class BreakoutTimeframeDto
    {
        public string Period { get; set; } = string.Empty;
        public string Trend { get; set; } = string.Empty; // "BULLISH", "BEARISH", "NEUTRAL"
        public string VolText { get; set; } = string.Empty;
        public string VolTrend { get; set; } = string.Empty; // "bullish", "bearish", "neutral"
        public string BreakoutText { get; set; } = string.Empty;
        public string BreakoutTrend { get; set; } = string.Empty; // "bullish", "bearish", "neutral"
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal ProgressPercent { get; set; }
        
        public bool IsBreakout { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    public class PrioritySignalDto
    {
        public int Rank { get; set; }
        public string Level { get; set; } = string.Empty; // "Highest", "High", "Medium", "Low"
        public string Description { get; set; } = string.Empty;
        public string SubDescription { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "BULLISH", "BEARISH", "NEUTRAL"
    }
}

