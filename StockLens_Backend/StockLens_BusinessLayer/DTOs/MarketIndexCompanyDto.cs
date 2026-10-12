using System.Text.Json.Serialization;

namespace StockLens_BusinessLayer.DTOs
{
    public class MarketIndexCompanyDto
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("company_name")]
        public string CompanyName { get; set; } = string.Empty;

        [JsonPropertyName("sector")]
        public string? Sector { get; set; }

        [JsonPropertyName("logo_url")]
        public string? LogoUrl { get; set; }

        [JsonPropertyName("website_url")]
        public string? WebsiteUrl { get; set; }

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [JsonPropertyName("market_cap")]
        public decimal? MarketCap { get; set; }

        [JsonPropertyName("pe_ratio")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? PeRatio { get; set; }

        [JsonPropertyName("dividend_yield")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? DividendYield { get; set; }

        [JsonPropertyName("net_profit_ttm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? NetProfitTtm { get; set; }

        [JsonPropertyName("profit_growth_yoy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? ProfitGrowthYoy { get; set; }

        [JsonPropertyName("revenue_ttm")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? RevenueTtm { get; set; }

        [JsonPropertyName("revenue_growth_qoq")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? RevenueGrowthQoq { get; set; }

        [JsonPropertyName("roce")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? Roce { get; set; }
    }
}
