using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StockLens_Infrastructure.ExternalServices.BharatStock.Models
{
    public class BharatStockCompareRecord
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("company_name")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("sector")]
        public string? Sector { get; set; }

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [JsonPropertyName("market_cap")]
        public decimal? MarketCap { get; set; }

        [JsonPropertyName("pe_ratio")]
        public decimal? PeRatio { get; set; }

        [JsonPropertyName("pb_ratio")]
        public decimal? PbRatio { get; set; }

        [JsonPropertyName("roe")]
        public decimal? Roe { get; set; }

        [JsonPropertyName("roce")]
        public decimal? Roce { get; set; }
    }

    public class BharatStockCompareApiResponseWrapper
    {
        [JsonPropertyName("data")]
        public List<BharatStockCompareRecord>? Data { get; set; }
    }
}
