using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockLens_DataLayer.Entities
{
    public class MarketIndexConstituent
    {
        [Key]
        public int Id { get; set; }

        public string IndexName { get; set; } = "NIFTY 50";

        public string Symbol { get; set; } = string.Empty;
        
        public virtual Company? Company { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Price { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarketCap { get; set; }

        public string Exchange { get; set; } = "NSE";

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
