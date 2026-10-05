using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class StockCashflowResponseDto
    {
        public int StockId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Exchange { get; set; } = "NSE";
        public string CompanyName { get; set; } = string.Empty;
        public string LatestFiscalYear { get; set; } = string.Empty;
        public string? DataAsOf { get; set; }
        public string Source { get; set; } = "BharatStock";
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        public CashflowSummaryDto Summary { get; set; } = new();
        public StockRatiosDto? Ratios { get; set; }
    }

    public class StockRatiosDto
    {
        public decimal? Roe { get; set; }
        public decimal? RoeYoY { get; set; }
        public decimal? RoeChange { get; set; }
        public decimal? RoePrevYear { get; set; }
        public decimal? Roce { get; set; }
        public decimal? RoceYoY { get; set; }
        public decimal? RoceChange { get; set; }
        public decimal? RocePrevYear { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? TtmEps { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? DividendYield { get; set; }
        public decimal? Week52High { get; set; }
        public decimal? Week52Low { get; set; }
        public decimal? CurrentPrice { get; set; }
        public string? AsOfDate { get; set; }
        public string? FinancialsPeriodType { get; set; }
        public string? FinancialsFiscalYear { get; set; }
        public decimal? FaceValue { get; set; }
        public decimal? EquityCapital { get; set; }
        public decimal? TotalShares { get; set; }
        public decimal? TotalEquity { get; set; }
        public string? TotalEquityPeriod { get; set; }
        public string? TotalEquitySource { get; set; }
        public decimal? BookValue { get; set; }
        public decimal? MarketCap { get; set; }
        public string? MarketCapSource { get; set; }
        public decimal? SectorPe { get; set; }
        public string? SectorPeSector { get; set; }
        public string? SectorPeAsOfDate { get; set; }
        public decimal? PegRatio { get; set; }

        public decimal? DebtorDays { get; set; }
        public decimal? DebtorDaysYoY { get; set; }
        public decimal? InventoryDays { get; set; }
        public decimal? InventoryDaysYoY { get; set; }
        public decimal? PayableDays { get; set; }
        public decimal? PayableDaysYoY { get; set; }
    }


    public class CashflowSummaryDto
    {
        public string FiscalYear { get; set; } = string.Empty;
        public string? PeriodEndDate { get; set; }
        public decimal? OperatingCashFlow { get; set; } // CFO
        public decimal? Capex { get; set; }
        public decimal? FreeCashFlow { get; set; } // FCF = CFO - Capex
        public decimal? NetCashFlow { get; set; } // Net Cash Flow (when provided by API)
        public decimal? Revenue { get; set; }
        public decimal? OperatingProfit { get; set; } // OP
        public decimal? NetProfit { get; set; }
        public decimal? Eps { get; set; }
        public decimal? Interest { get; set; } // Current / Annual Interest expense in ₹ Crores
        public decimal? Depreciation { get; set; } // Current / Annual Depreciation in ₹ Crores
        public decimal? OtherEquity { get; set; }
        public decimal? TotalEquity { get; set; }
        public decimal? CfoToOperatingProfitRatio { get; set; } // CFO / OP ratio
        public decimal? CfoToNetProfitRatio { get; set; } // CFO / Net Profit quality ratio
        public decimal? FcfMarginPercent { get; set; } // FCF / Revenue * 100
        public decimal? CapexToCfoPercent { get; set; } // Capex / CFO * 100
        public string? ConsolidationType { get; set; } = "consolidated";

        public StockRatiosDto? Ratios { get; set; }

        public CashflowYoYChangeDto YoYChange { get; set; } = new();
    }

    public class CashflowYoYChangeDto
    {
        public decimal? FreeCashFlowChange { get; set; }
        public decimal? FreeCashFlowGrowth { get; set; } // YoY %

        public decimal? OperatingCashFlowChange { get; set; }
        public decimal? OperatingCashFlowGrowth { get; set; } // YoY %

        public decimal? CapexChange { get; set; }
        public decimal? CapexGrowth { get; set; } // YoY %

        public decimal? NetProfitChange { get; set; }
        public decimal? NetProfitGrowth { get; set; } // YoY %

        public decimal? EpsChange { get; set; }
        public decimal? EpsGrowth { get; set; } // YoY %

        public decimal? RevenueChange { get; set; }
        public decimal? RevenueGrowth { get; set; } // YoY %

        public decimal? NetCashFlowChange { get; set; }
        public decimal? NetCashFlowGrowth { get; set; } // YoY %
    }
}

