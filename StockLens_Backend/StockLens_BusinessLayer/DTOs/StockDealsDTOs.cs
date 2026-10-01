using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class StockDealsSummaryDto
    {
        public string Symbol { get; set; } = string.Empty;
        public List<DealItemDto> BulkDeals { get; set; } = new();
        public List<DealItemDto> BlockDeals { get; set; } = new();
        public List<InsiderTradeItemDto> InsiderTrades { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class DealItemDto
    {
        public string DealDate { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public decimal AveragePrice { get; set; }
    }

    public class InsiderTradeItemDto
    {
        public string IntimationDate { get; set; } = string.Empty;
        public string AcquirerName { get; set; } = string.Empty;
        public string PersonCategory { get; set; } = string.Empty;
        public bool IsPromoter { get; set; }
        public string TransactionType { get; set; } = string.Empty; // Action
        public long Quantity { get; set; }
        public decimal TotalValue { get; set; }
        public decimal SharesAfterPct { get; set; }
        public string Mode { get; set; } = string.Empty;
    }
}
