using System;

namespace StockLens_DataLayer.Entities
{
    public static class DealCategories
    {
        public const string Bulk = "Bulk";
        public const string Block = "Block";
        public const string Insider = "Insider";
    }

    public class StockDeal
    {
        public int Id { get; set; }
        
        public int StockId { get; set; }
        public Stock Stock { get; set; } = null!;

        /// <summary>
        /// Deal Type: "Bulk", "Block", or "Insider"
        /// </summary>
        public string DealCategory { get; set; } = string.Empty;

        /// <summary>
        /// Exact date of the deal (or intimation date for insider trades).
        /// </summary>
        public DateTime? DealDate { get; set; }

        /// <summary>
        /// Name of the Investor, Fund, or Acquirer.
        /// </summary>
        public string ClientName { get; set; } = string.Empty;

        /// <summary>
        /// Action: "BUY", "SELL", "Acquisition", or "Disposal".
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Number of shares traded.
        /// </summary>
        public long? Quantity { get; set; }

        /// <summary>
        /// Average price per share (primarily for Bulk/Block deals).
        /// </summary>
        public decimal? AveragePrice { get; set; }

        /// <summary>
        /// Total value of the transaction (primarily for Insider deals).
        /// </summary>
        public decimal? TotalValue { get; set; }

        /// <summary>
        /// Category of the person/entity (e.g., "Promoters").
        /// </summary>
        public string? PersonCategory { get; set; }

        /// <summary>
        /// Indicates if the acquirer is a promoter.
        /// </summary>
        public bool? IsPromoter { get; set; }

        /// <summary>
        /// Percentage of shares held after the transaction (for Insider deals).
        /// </summary>
        public decimal? SharesAfterPct { get; set; }

        /// <summary>
        /// Mode of transaction (e.g., "Market Purchase").
        /// </summary>
        public string? Mode { get; set; }

        public string Source { get; set; } = "BharatStock";
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
