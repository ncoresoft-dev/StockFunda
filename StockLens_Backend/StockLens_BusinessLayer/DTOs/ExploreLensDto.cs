using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class ExploreLensCompanyDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public decimal CurrentPrice { get; set; }
        public string FormattedPrice { get; set; } = string.Empty;
        public decimal? Change { get; set; }
        public decimal? ChangePercent { get; set; }
        public string FormattedChange { get; set; } = string.Empty;
        public bool IsPositive { get; set; }
        public int Score { get; set; } // 1 to 5, closer to center = stronger
        public int OrbitRadius { get; set; } // in pixels
        public int OrbitAngle { get; set; } // in degrees (0 - 360)
        public string ColorBg { get; set; } = string.Empty;
        public string ColorBorder { get; set; } = string.Empty;
        public List<decimal> SevenDayPrices { get; set; } = new();
        public decimal? SevenDayChangePercent { get; set; }
        public string FormattedSevenDayChange { get; set; } = string.Empty;
        public string TrendPath { get; set; } = string.Empty;
        public bool TrendPositive { get; set; }
        public List<LensMetricDto> Metrics { get; set; } = new();
        public string Insight { get; set; } = string.Empty;
    }

    public class LensMetricDto
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool? IsPositive { get; set; }
    }

    public class ExploreLensResponseDto
    {
        public string ActiveLens { get; set; } = "Growth";
        public List<ExploreLensCompanyDto> Companies { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
