using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class MarketNewsItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Content { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string ArticleUrl { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public DateTime PublishedAt { get; set; }
        public string Category { get; set; } = "Market Updates";
        public string? TimeAgo { get; set; }
        public bool IsHot { get; set; }
    }

    public class MarketNewsResponseDto
    {
        public List<MarketNewsItemDto> News { get; set; } = new();
        public int TotalCount { get; set; }
        public DateTime? LastFetchedAt { get; set; }
        public bool IsCached { get; set; } = true;
        public string Status { get; set; } = "ok";
        public string? Message { get; set; }
        public string Query { get; set; } = "Sensex OR Nifty OR \"stock market\"";
        public int NextRetryInMinutes { get; set; }
        public List<string> AvailableCategories { get; set; } = new()
        {
            "All",
            "Market Indices",
            "Economy & Policy",
            "Corporate",
            "Global Markets"
        };
    }
}
