using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockLens_DataLayer.Entities
{
    public class MarketTickerQuote
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string ShortName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal CurrentPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PreviousClose { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Change { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? ChangePercent { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? DayHigh { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? DayLow { get; set; }

        [MaxLength(50)]
        public string InstrumentType { get; set; } = "INDEX";

        public int DisplayOrder { get; set; } = 0;

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
