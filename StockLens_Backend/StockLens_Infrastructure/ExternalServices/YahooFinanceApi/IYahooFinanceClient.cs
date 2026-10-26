using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.YahooFinanceApi
{
    public class YahooLiveQuoteDto
    {
        public string? Symbol { get; set; }
        public decimal? Price { get; set; }
        public decimal? DayHigh { get; set; }
        public decimal? DayLow { get; set; }
        public decimal? YearHigh { get; set; }
        public decimal? YearLow { get; set; }
        public decimal? PreviousClose { get; set; }
        public decimal? Change { get; set; }
        public decimal? ChangePercent { get; set; }
        public long? Volume { get; set; }
    }

    public class YahooDebtResponse
    {
        public decimal? LongTermDebt { get; set; }
        public decimal? ShortTermDebt { get; set; }
        public int Year { get; set; }
    }

    public class YahooCompanyProfileDto
    {
        public string? Symbol { get; set; }
        public string? CompanyName { get; set; }
        public string? LongBusinessSummary { get; set; }
        public string? Website { get; set; }
        public string? Industry { get; set; }
        public string? Sector { get; set; }
        public long? FullTimeEmployees { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public System.Collections.Generic.List<string> KeyExecutives { get; set; } = new();
    }

    public interface IYahooFinanceClient
    {
        /// <summary>
        /// Fetches the real company name (shortname or longname) and industry from Yahoo Finance API.
        /// </summary>
        /// <param name="symbol">The stock symbol (e.g. INFY)</param>
        /// <param name="exchange">The exchange (e.g. NSE or BSE)</param>
        /// <returns>A tuple containing the company name and industry</returns>
        Task<(string? CompanyName, string? Industry)> GetCompanyDetailsAsync(string symbol, string exchange, CancellationToken cancellationToken = default);
        Task<YahooCompanyProfileDto?> GetCompanyProfileAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default);
        Task<System.Collections.Generic.List<StockLens_Infrastructure.ExternalServices.IndianApi.Models.IndianApiPriceRecord>> GetHistoricalPricesAsync(string symbol, string exchange, CancellationToken cancellationToken = default);
        Task<YahooLiveQuoteDto?> GetLiveQuoteAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default);
        Task<System.Collections.Generic.List<StockLens_Infrastructure.ExternalServices.IndianApi.IndianApiFinancialPeriodDto>> GetQuarterlyIncomeStatementsAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default);
        Task<System.Collections.Generic.List<YahooDebtResponse>> GetHistoricalDebtAsync(string symbol, string exchange, CancellationToken cancellationToken = default);
        Task<YahooSparklineQuoteDto?> GetSparklineQuoteAsync(string symbol, string? exchange = "NSE", int days = 7, CancellationToken cancellationToken = default);
        Task<YahooGrowthMetricsDto?> GetGrowthMetricsAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default);
    }

    public class YahooSparklineQuoteDto
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal PreviousClose { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public System.Collections.Generic.List<decimal> ClosingPrices { get; set; } = new();
    }

    public class YahooGrowthMetricsDto
    {
        public string Symbol { get; set; } = string.Empty;

        // Growth Lens
        public decimal? SalesGrowthYoY { get; set; }
        public decimal? ProfitGrowthYoY { get; set; }
        public decimal? OneYearReturn { get; set; }

        // Cash Flow Lens
        public decimal? FreeCashFlowRaw { get; set; }
        public string? FreeCashFlowFormatted { get; set; }
        public decimal? OperatingCashFlowRaw { get; set; }
        public string? OperatingCashFlowFormatted { get; set; }
        public decimal? OperatingMargin { get; set; }

        // Ownership Lens
        public decimal? PromoterHolding { get; set; }
        public decimal? InstitutionalHolding { get; set; }
        public int? InstitutionsCount { get; set; }

        // Risk Lens
        public decimal? DebtToEquity { get; set; }
        public decimal? CurrentRatio { get; set; }
        public decimal? Beta { get; set; }
    }
}
