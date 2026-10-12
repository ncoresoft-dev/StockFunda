using Microsoft.Extensions.Logging;
using StockLens_Infrastructure.ExternalServices.BharatStock.Exceptions;
using StockLens_Infrastructure.ExternalServices.BharatStock.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.BharatStock
{
    public class MockShareholdingProvider : IShareholdingProvider
    {
        private readonly ILogger<MockShareholdingProvider> _logger;

        public MockShareholdingProvider(ILogger<MockShareholdingProvider> logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<BharatStockShareholdingRecord>> GetShareholdingAsync(
            string ticker,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                throw new ArgumentException("Ticker is required.", nameof(ticker));
            }

            var clean = ticker.Trim().ToUpperInvariant();
            _logger.LogInformation("[DEV MOCK] Generating realistic shareholding history for {Ticker}.", clean);

            var list = GenerateMockHistory(clean);
            return Task.FromResult<IReadOnlyList<BharatStockShareholdingRecord>>(list);
        }

        public static List<BharatStockShareholdingRecord> GenerateMockHistory(string ticker)
        {
            // Tailored realistic baseline holdings for popular stocks
            (decimal promoter, decimal fii, decimal dii, decimal pub) baseHolding = ticker switch
            {
                "RELIANCE" => (50.25m, 18.20m, 16.30m, 15.25m),
                "TCS" => (72.30m, 12.45m, 10.15m, 5.10m),
                "INFY" => (14.65m, 32.80m, 36.45m, 16.10m),
                "TATAMOTORS" => (46.36m, 18.90m, 17.50m, 17.24m),
                "HDFCBANK" => (0.00m, 52.10m, 30.60m, 17.30m),
                "ICICIBANK" => (0.00m, 44.50m, 45.20m, 10.30m),
                _ => (52.00m, 18.00m, 15.00m, 15.00m)
            };

            var quarters = new[]
            {
                (period: "Q1 FY2026", date: new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc), pDiff: 0.00m, fDiff: 0.00m, dDiff: 0.00m, pubDiff: 0.00m),
                (period: "Q4 FY2025", date: new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc), pDiff: -0.45m, fDiff: 0.90m, dDiff: -0.50m, pubDiff: 0.05m),
                (period: "Q3 FY2025", date: new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc), pDiff: -0.60m, fDiff: 1.20m, dDiff: -0.80m, pubDiff: 0.20m),
                (period: "Q2 FY2025", date: new DateTime(2024, 9, 30, 0, 0, 0, DateTimeKind.Utc), pDiff: -0.50m, fDiff: 1.50m, dDiff: -1.20m, pubDiff: 0.20m),
                (period: "Q1 FY2025", date: new DateTime(2024, 6, 30, 0, 0, 0, DateTimeKind.Utc), pDiff: -0.80m, fDiff: 1.80m, dDiff: -1.30m, pubDiff: 0.30m),
            };

            var records = new List<BharatStockShareholdingRecord>();

            foreach (var q in quarters)
            {
                var p = Math.Max(0, baseHolding.promoter + q.pDiff);
                var f = Math.Max(0, baseHolding.fii + q.fDiff);
                var d = Math.Max(0, baseHolding.dii + q.dDiff);
                var pub = Math.Max(0, baseHolding.pub + q.pubDiff);

                records.Add(new BharatStockShareholdingRecord
                {
                    Period = q.period,
                    PeriodDateString = q.date.ToString("yyyy-MM-dd"),
                    PeriodType = "Quarterly",
                    PromoterHolding = p,
                    FiiHolding = f,
                    DiiHolding = d,
                    PublicHolding = pub,
                    Source = "BharatStock (Mock Dev Provider)"
                });
            }

            return records;
        }
    }
}
