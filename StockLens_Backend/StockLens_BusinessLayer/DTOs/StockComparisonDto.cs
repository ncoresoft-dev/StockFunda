using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class ComparisonPresetDto
    {
        public string Stock1 { get; set; } = string.Empty;
        public string Stock2 { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    public class StockComparisonPricePointDto
    {
        public string Date { get; set; } = string.Empty;
        public decimal? StockAPercentReturn { get; set; }
        public decimal? StockBPercentReturn { get; set; }
        public decimal? StockAPrice { get; set; }
        public decimal? StockBPrice { get; set; }
    }

    public class StockComparisonProfileDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? Industry { get; set; }
        public string? Sector { get; set; }
        public string? LogoUrl { get; set; }
        public string? WebsiteUrl { get; set; }

        // Pricing & Valuation
        public decimal? CurrentPrice { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? Roe { get; set; }
        public decimal? Roce { get; set; }
        public decimal? DividendYield { get; set; }

        // Health Score & Verdict
        public int HealthScore { get; set; }
        public string Signal { get; set; } = "Neutral / Hold";
        public string SignalClass { get; set; } = "badge-warning";

        // Performance & Growth
        public decimal? SalesQuarterlyGrowthYoY { get; set; }
        public decimal? SalesQuarterlyGrowthQoQ { get; set; }
        public decimal? ProfitQuarterlyGrowthYoY { get; set; }
        public decimal? ProfitQuarterlyGrowthQoQ { get; set; }
        public decimal? OperatingMargin { get; set; }

        // Shareholding Structure (%)
        public decimal? PromoterHolding { get; set; }
        public decimal? FiiHolding { get; set; }
        public decimal? DiiHolding { get; set; }
        public decimal? PublicHolding { get; set; }

        // Key Strengths & Risks
        public List<string> Pros { get; set; } = new();
        public List<string> Cons { get; set; } = new();
        public List<string> KeyPoints { get; set; } = new();
    }

    public class StockComparisonHeadToHeadDto
    {
        public string OverallScoreWinner { get; set; } = "Tie";
        public string ValuationAdvantage { get; set; } = "Tie";
        public string ProfitabilityAdvantage { get; set; } = "Tie";
        public string GrowthAdvantage { get; set; } = "Tie";
        public string SummaryVerdict { get; set; } = string.Empty;
    }

    public class StockComparisonDto
    {
        public StockComparisonProfileDto StockA { get; set; } = new();
        public StockComparisonProfileDto StockB { get; set; } = new();
        public StockComparisonHeadToHeadDto HeadToHead { get; set; } = new();
        public List<StockComparisonPricePointDto> NormalizedHistory { get; set; } = new();
        public string Period { get; set; } = "1yr";
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}

