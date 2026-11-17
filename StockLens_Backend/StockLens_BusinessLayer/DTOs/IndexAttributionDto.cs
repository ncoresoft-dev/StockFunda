using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StockLens_BusinessLayer.DTOs
{
    public class IndexAttributionResponseDto
    {
        [JsonPropertyName("indexKey")]
        public string IndexKey { get; set; } = string.Empty; // "nifty-50", "sensex", "bank-nifty"

        [JsonPropertyName("indexSymbol")]
        public string IndexSymbol { get; set; } = string.Empty; // "^NSEI", "^BSESN", "^NSEBANK"

        [JsonPropertyName("indexName")]
        public string IndexName { get; set; } = string.Empty; // "NIFTY 50", "BSE SENSEX", "NIFTY BANK"

        [JsonPropertyName("currentPrice")]
        public decimal CurrentPrice { get; set; }

        [JsonPropertyName("previousClose")]
        public decimal PreviousClose { get; set; }

        [JsonPropertyName("change")]
        public decimal Change { get; set; }

        [JsonPropertyName("changePercent")]
        public decimal ChangePercent { get; set; }

        [JsonPropertyName("isPositive")]
        public bool IsPositive { get; set; }

        [JsonPropertyName("formattedPrice")]
        public string FormattedPrice { get; set; } = string.Empty;

        [JsonPropertyName("formattedChange")]
        public string FormattedChange { get; set; } = string.Empty;

        [JsonPropertyName("formattedChangePercent")]
        public string FormattedChangePercent { get; set; } = string.Empty;

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("tugOfWar")]
        public IndexTugOfWarDto TugOfWar { get; set; } = new();

        [JsonPropertyName("topLifters")]
        public List<IndexContributorDto> TopLifters { get; set; } = new();

        [JsonPropertyName("topDraggers")]
        public List<IndexContributorDto> TopDraggers { get; set; } = new();

        [JsonPropertyName("circleSlices")]
        public List<IndexChartSliceDto> CircleSlices { get; set; } = new();

        [JsonPropertyName("contributors")]
        public List<IndexContributorDto> Contributors { get; set; } = new();
    }

    public class IndexTugOfWarDto
    {
        [JsonPropertyName("liftingPoints")]
        public decimal LiftingPoints { get; set; }

        [JsonPropertyName("liftingCount")]
        public int LiftingCount { get; set; }

        [JsonPropertyName("draggingPoints")]
        public decimal DraggingPoints { get; set; } // Negative decimal e.g. -67.01

        [JsonPropertyName("draggingCount")]
        public int DraggingCount { get; set; }

        [JsonPropertyName("netPoints")]
        public decimal NetPoints { get; set; } // LiftingPoints + DraggingPoints e.g. +128.95

        [JsonPropertyName("lifterRatioPct")]
        public decimal LifterRatioPct { get; set; } // Ratio for visual progress bar (0 - 100)

        [JsonPropertyName("biggestSingleMove")]
        public string BiggestSingleMove { get; set; } = string.Empty; // e.g. "HDFCBANK +41.86"

        [JsonPropertyName("summaryText")]
        public string SummaryText { get; set; } = string.Empty; // "Lifters are ahead by 128.95 pts. Biggest single move: HDFCBANK +41.86"
    }

    public class IndexContributorDto
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("companyName")]
        public string CompanyName { get; set; } = string.Empty;

        [JsonPropertyName("sector")]
        public string? Sector { get; set; }

        [JsonPropertyName("logoUrl")]
        public string? LogoUrl { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("change")]
        public decimal Change { get; set; }

        [JsonPropertyName("changePercent")]
        public decimal ChangePercent { get; set; }

        [JsonPropertyName("weightage")]
        public decimal Weightage { get; set; } // Free-float weightage e.g. 10.36

        [JsonPropertyName("pointContribution")]
        public decimal PointContribution { get; set; } // Points contributed to index move e.g. +41.86 or -9.96

        [JsonPropertyName("isLifter")]
        public bool IsLifter { get; set; }

        [JsonPropertyName("formattedPoints")]
        public string FormattedPoints { get; set; } = string.Empty; // "+41.86" or "-9.96"

        [JsonPropertyName("formattedChangePercent")]
        public string FormattedChangePercent { get; set; } = string.Empty; // "+1.55%" or "-8.70%"

        [JsonPropertyName("pointBarPct")]
        public decimal PointBarPct { get; set; } // Scaled 0 - 100 for mini horizontal bar in list row
    }

    public class IndexChartSliceDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty; // "HDFCBANK" or "19 others"

        [JsonPropertyName("points")]
        public decimal Points { get; set; } // Raw signed points (+41.9 or -28.3)

        [JsonPropertyName("absolutePoints")]
        public decimal AbsolutePoints { get; set; } // Positive magnitude for arc angle calculation

        [JsonPropertyName("percentageOfCategory")]
        public decimal PercentageOfCategory { get; set; } // Slice's share of green or red pool

        [JsonPropertyName("isLifter")]
        public bool IsLifter { get; set; }

        [JsonPropertyName("isOther")]
        public bool IsOther { get; set; }

        [JsonPropertyName("otherCount")]
        public int OtherCount { get; set; }
    }
}
