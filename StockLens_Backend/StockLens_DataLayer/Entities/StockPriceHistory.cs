using System;

namespace StockLens_DataLayer.Entities
{
    public class StockPriceHistory
    {
        public int Id { get; set; }
        public int StockId { get; set; }
        public Stock Stock { get; set; } = null!;

        public DateTime Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
        public decimal? DeliveryPercentage { get; set; }
        public long? DeliveryVolume { get; set; }
        public long? TotalTradedVolume { get; set; }
        
        public decimal? Dma50 { get; set; }
        public decimal? Dma200 { get; set; }
        
        public string Source { get; set; } = "IndianAPI";

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
