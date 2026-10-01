using System;
using System.Collections.Generic;

namespace StockLens_Infrastructure.ExternalServices.IndianApi
{
    public class IndianApiStockOverviewDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string? Industry { get; set; }
        public decimal? CurrentPrice { get; set; }
        public decimal? YearHigh { get; set; }
        public decimal? YearLow { get; set; }
        public decimal? TtmEps { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? Roe { get; set; }
        public decimal? Roce { get; set; }
        public decimal? DividendYield { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? FaceValue { get; set; }
        public decimal? BookValue { get; set; }
        public decimal? SectorPe { get; set; }
        public string? SectorName { get; set; }
        public string? About { get; set; }
        public string? WebsiteUrl { get; set; }
        public List<string> KeyPoints { get; set; } = new();
        public List<IndianApiFinancialPeriodDto> Financials { get; set; } = new();
        public List<IndianApiPeerDto> Peers { get; set; } = new();
    }

    public class IndianApiFinancialPeriodDto
    {
        public string FiscalYear { get; set; } = string.Empty;
        public DateTime? PeriodEndDate { get; set; }
        public string PeriodType { get; set; } = "annual";
        public string PeriodKey => $"{PeriodType?.ToLowerInvariant() ?? "annual"}-{FiscalYear}";

        // Income Statement
        public decimal? Revenue { get; set; }
        public decimal? Expenses { get; set; }
        public decimal? OperatingProfit { get; set; }
        public decimal? OperatingProfitMargin { get; set; }
        public decimal? OtherIncome { get; set; }
        public decimal? Interest { get; set; }
        public decimal? Depreciation { get; set; }
        public decimal? ProfitBeforeTax { get; set; }
        public decimal? Tax { get; set; }
        public decimal? TaxPercentage { get; set; }
        public decimal? NetProfit { get; set; }
        public decimal? Eps { get; set; }
        public decimal? NetProfitAttributableToMinorityInterest { get; set; }

        // Cash Flow Statement
        public decimal? OperatingCashFlow { get; set; }
        public decimal? Capex { get; set; }
        public decimal? FreeCashFlow => (OperatingCashFlow.HasValue && Capex.HasValue)
            ? (OperatingCashFlow.Value - Capex.Value)
            : OperatingCashFlow;
        public decimal? NetCashFlow { get; set; }

        // Balance Sheet
        public decimal? TotalAssets { get; set; }
        public decimal? TotalLiabilities { get; set; }
        public decimal? TotalCurrentLiabilities { get; set; }
        public decimal? TotalCurrentAssets { get; set; }
        public decimal? TotalDebt { get; set; }
        public decimal? LongTermDebt { get; set; }
        public decimal? ShortTermDebt { get; set; }
        public decimal? TotalEquity { get; set; }
        public decimal? OtherEquity { get; set; }
        public decimal? EquityCapital { get; set; }
        public decimal? FixedAssets { get; set; }
        public decimal? Cwip { get; set; }
        public decimal? Investments { get; set; }
        public decimal? OtherAssets { get; set; }
        public decimal? OtherLiabilities { get; set; }
        public decimal? TradeReceivables { get; set; }
        public decimal? TotalInventory { get; set; }
        public decimal? AccountsPayable { get; set; }
        public decimal? BookValuePerShare { get; set; }
        public decimal? TotalShares { get; set; }
        public string ConsolidationType { get; set; } = "consolidated";
    }

    public class IndianApiPeerDto
    {
        public string? CompanyName { get; set; }
        public decimal? Price { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? Roe { get; set; }
        public decimal? DividendYield { get; set; }
        public decimal? TotalShares { get; set; }
    }
}
