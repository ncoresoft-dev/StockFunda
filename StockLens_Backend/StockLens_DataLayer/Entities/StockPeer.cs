using System;

namespace StockLens_DataLayer.Entities
{
    public class StockPeer
    {
        public int Id { get; set; }
        
        public string CompanyName { get; set; } = string.Empty;
        public decimal? Price { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? Roe { get; set; }
        public decimal? Roce { get; set; }
        public decimal? DividendYield { get; set; }
        public decimal? TotalShares { get; set; }
        
        public string Source { get; set; } = "BharatStock";
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Foreign Key
        public int StockId { get; set; }
        public Stock Stock { get; set; } = null!;
    }
}
