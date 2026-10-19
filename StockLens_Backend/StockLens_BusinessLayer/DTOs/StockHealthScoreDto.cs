using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class CategoryScoreDto
    {
        public string CategoryName { get; set; } = string.Empty;
        public int EarnedPoints { get; set; }
        public int MaxPoints { get; set; }
        public double Percentage => MaxPoints > 0 ? Math.Round((double)EarnedPoints / MaxPoints * 100, 1) : 0;
        public string Status { get; set; } = "Good"; // "Excellent", "Good", "Average", "Poor"
        public List<string> Highlights { get; set; } = new();
    }

    public class ScoreAdjustmentDto
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "Bonus", "Penalty", "CircuitBreakerCap"
        public int PointsDelta { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class StockHealthScoreDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string Exchange { get; set; } = "NSE";
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// Raw base score (0 - 100) before bonuses, deductions, and circuit breaker caps.
        /// Equals the exact sum of EarnedPoints across all 6 categories.
        /// </summary>
        public int RawTotalScore { get; set; }

        /// <summary>
        /// Total composite score out of 100 after reconciling all bonuses, penalties, and caps.
        /// </summary>
        public int TotalScore { get; set; }

        /// <summary>
        /// Ordered sequence of score adjustments applied to RawTotalScore to arrive at TotalScore.
        /// Reconciles mathematically: TotalScore = RawTotalScore + Sum(PointsDelta).
        /// </summary>
        public List<ScoreAdjustmentDto> ScoreAdjustments { get; set; } = new();

        /// <summary>
        /// Signal verdict: "Strong Buy", "Good for Buy", "Neutral / Hold", "Risky / Avoid"
        /// </summary>
        public string Signal { get; set; } = "Neutral / Hold";

        /// <summary>
        /// UI CSS Badge class: "badge-success", "badge-good", "badge-warning", "badge-danger"
        /// </summary>
        public string SignalClass { get; set; } = "badge-warning";

        public string SummaryText { get; set; } = string.Empty;

        /// <summary>
        /// Percentage of sector-applicable metrics backed by verified data (0 - 100%).
        /// </summary>
        public decimal DataCoveragePercent { get; set; } = 0m;

        /// <summary>
        /// Data reliability verdict: "High", "Moderate", "Low", "Inconclusive"
        /// </summary>
        public string DataConfidence { get; set; } = "Inconclusive";

        /// <summary>
        /// Total count of indicators applicable to this stock's sector archetype (e.g. 20 for Industrials, 18 for IT, 17 for Financials).
        /// </summary>
        public int TotalApplicableMetrics { get; set; }

        /// <summary>
        /// Count of applicable indicators backed by verified provider data.
        /// </summary>
        public int AvailableMetricsCount { get; set; }

        /// <summary>
        /// List of applicable indicators for which data was missing in provider feeds.
        /// </summary>
        public List<string> MissingMetrics { get; set; } = new();

        /// <summary>
        /// List of indicators genuinely inapplicable to this stock's sector business model.
        /// </summary>
        public List<string> InapplicableMetrics { get; set; } = new();

        // Sub-breakdowns
        public CategoryScoreDto ProfitabilityScore { get; set; } = new();
        public CategoryScoreDto ValuationScore { get; set; } = new();
        public CategoryScoreDto SolvencyScore { get; set; } = new();
        public CategoryScoreDto GrowthScore { get; set; } = new();
        public CategoryScoreDto SmartMoneyScore { get; set; } = new();
        public CategoryScoreDto EfficiencyScore { get; set; } = new();

        public List<string> Pros { get; set; } = new();
        public List<string> Cons { get; set; } = new();
        public List<string> RedFlags { get; set; } = new();

        public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    }
}
