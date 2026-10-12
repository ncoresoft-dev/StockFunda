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

        [JsonPropertyName("pe_ratio")]
        public decimal? PeRatio { get; set; }

        [JsonPropertyName("market_cap")]
        public decimal? MarketCap { get; set; }

        [JsonPropertyName("dividend_yield")]
        public decimal? DividendYield { get; set; }

        [JsonPropertyName("net_profit_ttm")]
        public decimal? NetProfitTtm { get; set; }

        [JsonPropertyName("profit_growth_yoy")]
        public decimal? ProfitGrowthYoy { get; set; }

        [JsonPropertyName("revenue_ttm")]
        public decimal? RevenueTtm { get; set; }

        [JsonPropertyName("revenue_growth_qoq")]
        public decimal? RevenueGrowthQoq { get; set; }

        [JsonPropertyName("roce")]
        public decimal? Roce { get; set; }

    }
}
