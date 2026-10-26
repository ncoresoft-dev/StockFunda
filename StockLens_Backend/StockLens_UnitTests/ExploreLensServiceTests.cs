using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.Services;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StockLens_UnitTests
{
    public class ExploreLensServiceTests
    {
        private readonly Mock<IYahooFinanceClient> _yahooMock = new();
        private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

        [Fact]
        public async Task GetExploreLensDataAsync_ReturnsAllTrackedCompanies_WithSparklines()
        {
            // Arrange: mock Yahoo sparkline quote and growth metrics for INFY
            _yahooMock.Setup(y => y.GetSparklineQuoteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((string sym, string? ex, int days, CancellationToken ct) => new YahooSparklineQuoteDto
                      {
                          Symbol = sym,
                          Price = 1500m,
                          PreviousClose = 1480m,
                          Change = 20m,
                          ChangePercent = 1.35m,
                          ClosingPrices = new List<decimal> { 1450m, 1460m, 1475m, 1470m, 1480m, 1490m, 1500m }
                      });

            _yahooMock.Setup(y => y.GetGrowthMetricsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((string sym, string? ex, CancellationToken ct) => new YahooGrowthMetricsDto
                      {
                          Symbol = sym,
                          SalesGrowthYoY = 14.5m,
                          ProfitGrowthYoY = 18.2m,
                          OneYearReturn = -12.4m
                      });

            var service = new ExploreLensService(
                _yahooMock.Object,
                _memoryCache,
                NullLogger<ExploreLensService>.Instance);

            // Act
            var result = await service.GetExploreLensDataAsync("Growth");

            // Assert
            result.Should().NotBeNull();
            result.ActiveLens.Should().Be("Growth");
            result.Companies.Should().HaveCount(8);

            var infy = result.Companies.First(c => c.Symbol == "INFY");
            infy.CurrentPrice.Should().Be(1500m);
            infy.TrendPath.Should().StartWith("M 10");
            infy.TrendPositive.Should().BeTrue();
            infy.Metrics.Should().HaveCount(3);
            infy.Metrics.Select(m => m.Label).Should().Contain(new[] { "Sales Growth YoY", "Profit Growth YoY", "1-Year Return" });
            infy.Insight.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task GetExploreLensDataAsync_CashFlow_ReturnsDynamicMetricsAndNonOverlappingRadii()
        {
            // Arrange
            _yahooMock.Setup(y => y.GetSparklineQuoteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooSparklineQuoteDto { Symbol = "TCS", Price = 4000m, ClosingPrices = new List<decimal> { 3900m, 4000m } });
            _yahooMock.Setup(y => y.GetGrowthMetricsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooGrowthMetricsDto
                      {
                          Symbol = "TCS",
                          OperatingMargin = 24.5m,
                          FreeCashFlowFormatted = "₹42,000 Cr",
                          OperatingCashFlowFormatted = "₹52,000 Cr"
                      });

            var service = new ExploreLensService(_yahooMock.Object, _memoryCache, NullLogger<ExploreLensService>.Instance);

            // Act
            var result = await service.GetExploreLensDataAsync("Cash Flow");

            // Assert
            result.ActiveLens.Should().Be("Cash Flow");
            result.Companies.Should().HaveCount(8);
            var first = result.Companies.First();
            first.Metrics.Select(m => m.Label).Should().Contain(new[] { "Operating Margin", "Free Cash Flow", "Operating Cash Flow" });
            first.Insight.Should().NotBeNullOrWhiteSpace();

            // Verify radii are all unique and within [95, 175]
            var radii = result.Companies.Select(c => c.OrbitRadius).ToList();
            radii.Should().OnlyHaveUniqueItems();
            radii.Min().Should().Be(95);
            radii.Max().Should().Be(175);
        }

        [Fact]
        public async Task GetExploreLensDataAsync_Ownership_ReturnsDynamicMetricsAndNonOverlappingRadii()
        {
            // Arrange
            _yahooMock.Setup(y => y.GetSparklineQuoteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooSparklineQuoteDto { Symbol = "INFY", Price = 1500m, ClosingPrices = new List<decimal> { 1450m, 1500m } });
            _yahooMock.Setup(y => y.GetGrowthMetricsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooGrowthMetricsDto
                      {
                          Symbol = "INFY",
                          PromoterHolding = 16.0m,
                          InstitutionalHolding = 51.3m,
                          InstitutionsCount = 345
                      });

            var service = new ExploreLensService(_yahooMock.Object, _memoryCache, NullLogger<ExploreLensService>.Instance);

            // Act
            var result = await service.GetExploreLensDataAsync("Ownership");

            // Assert
            result.ActiveLens.Should().Be("Ownership");
            var first = result.Companies.First();
            first.Metrics.Select(m => m.Label).Should().Contain(new[] { "Promoter Holding", "Institutional Holding", "Institutions Count" });
            first.Insight.Should().NotBeNullOrWhiteSpace();

            var radii = result.Companies.Select(c => c.OrbitRadius).ToList();
            radii.Should().OnlyHaveUniqueItems();
            radii.Min().Should().Be(95);
            radii.Max().Should().Be(175);
        }

        [Fact]
        public async Task GetExploreLensDataAsync_Risk_ReturnsDynamicMetricsAndNonOverlappingRadii()
        {
            // Arrange
            _yahooMock.Setup(y => y.GetSparklineQuoteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooSparklineQuoteDto { Symbol = "SUNPHARMA", Price = 1700m, ClosingPrices = new List<decimal> { 1650m, 1700m } });
            _yahooMock.Setup(y => y.GetGrowthMetricsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooGrowthMetricsDto
                      {
                          Symbol = "SUNPHARMA",
                          DebtToEquity = 0.06m,
                          Beta = 0.14m,
                          CurrentRatio = 2.60m
                      });

            var service = new ExploreLensService(_yahooMock.Object, _memoryCache, NullLogger<ExploreLensService>.Instance);

            // Act
            var result = await service.GetExploreLensDataAsync("Risk");

            // Assert
            result.ActiveLens.Should().Be("Risk");
            var first = result.Companies.First();
            first.Metrics.Select(m => m.Label).Should().Contain(new[] { "Debt to Equity", "Beta (Volatility)", "Current Ratio" });
            first.Insight.Should().NotBeNullOrWhiteSpace();

            var radii = result.Companies.Select(c => c.OrbitRadius).ToList();
            radii.Should().OnlyHaveUniqueItems();
            radii.Min().Should().Be(95);
            radii.Max().Should().Be(175);
        }

        [Theory]
        [InlineData("Growth")]
        [InlineData("Cash Flow")]
        [InlineData("Ownership")]
        [InlineData("Risk")]
        public async Task GetExploreLensDataAsync_SupportsAllFourLenses(string lens)
        {
            // Arrange
            _yahooMock.Setup(y => y.GetSparklineQuoteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooSparklineQuoteDto
                      {
                          Symbol = "TEST",
                          Price = 1000m,
                          PreviousClose = 990m,
                          Change = 10m,
                          ChangePercent = 1.0m,
                          ClosingPrices = new List<decimal> { 980m, 985m, 990m, 995m, 1000m }
                      });

            var service = new ExploreLensService(
                _yahooMock.Object,
                _memoryCache,
                NullLogger<ExploreLensService>.Instance);

            // Act
            var result = await service.GetExploreLensDataAsync(lens);

            // Assert
            result.ActiveLens.Should().Be(lens);
            result.Companies.Should().NotBeEmpty();
            result.Companies.All(c => c.Metrics.Count > 0).Should().BeTrue();
        }
    }
}
