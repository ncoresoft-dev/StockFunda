using System;

namespace StockLens_Infrastructure.ExternalServices.IndianApi.Models
{
    /// <summary>
    /// Standardized News Article model mapped from news feeds and Google News RSS
    /// </summary>
    public class IndianApiStandardArticle
    {
        public string? ExternalNewsId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Content { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public DateTime PublishedAt { get; set; }
        public string? Category { get; set; }
    }
}
