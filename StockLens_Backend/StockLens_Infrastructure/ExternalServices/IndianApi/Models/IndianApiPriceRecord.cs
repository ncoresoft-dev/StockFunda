using System;

namespace StockLens_Infrastructure.ExternalServices.IndianApi.Models
{
    public class IndianApiPriceRecord
    {
        public string DateString { get; set; } = string.Empty;
        public decimal? Open { get; set; }
        public decimal? High { get; set; }
        public decimal? Low { get; set; }
        public decimal? Close { get; set; }
        public long? Volume { get; set; }
        
        public decimal? Dma50 { get; set; }
        public decimal? Dma200 { get; set; }

        public decimal? DeliveryPercentage { get; set; }
        public long? DeliveryVolume { get; set; }
        public long? TotalTradedVolume { get; set; }

        public DateTime? ResolvedDate
        {
            get
            {
                if (DateTime.TryParse(DateString, out var date))
                    return date;
                return null;
            }
        }
    }
}
