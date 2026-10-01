using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StockLens_Infrastructure.ExternalServices.BharatStock.Models
{
    public class BharatStockDealRecord
    {
        [JsonPropertyName("deal_date")]
        public string? DealDate { get; set; }

        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("buy_sell")]
        public string? BuySell { get; set; }

        [JsonPropertyName("quantity")]
        public long? Quantity { get; set; }

        [JsonPropertyName("avg_price")]
        public decimal? AvgPrice { get; set; }
        
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }
    }

    public class BharatStockInsiderTradeRecord
    {
        [JsonPropertyName("acquirer_name")]
        public string? AcquirerName { get; set; }

        [JsonPropertyName("person_category")]
        public string? PersonCategory { get; set; }

        [JsonPropertyName("is_promoter")]
        public bool? IsPromoter { get; set; }

        [JsonPropertyName("transaction_type")]
        public string? TransactionType { get; set; }

        [JsonPropertyName("quantity")]
        public long? Quantity { get; set; }

        [JsonPropertyName("value")]
        public decimal? Value { get; set; }

        [JsonPropertyName("shares_after_pct")]
        public decimal? SharesAfterPct { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("intimation_date")]
        public string? IntimationDate { get; set; }
        
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }
    }

    public class BharatStockDealsApiResponseWrapper<T>
    {
        [JsonPropertyName("data")]
        public List<T>? Data { get; set; }
        
        [JsonPropertyName("pagination")]
        public BharatStockPagination? Pagination { get; set; }
    }

    public class BharatStockPagination
    {
        [JsonPropertyName("page")]
        public int? Page { get; set; }

        [JsonPropertyName("page_size")]
        public int? PageSize { get; set; }

        [JsonPropertyName("total_items")]
        public int? TotalItems { get; set; }

        [JsonPropertyName("total_pages")]
        public int? TotalPages { get; set; }
    }
}
