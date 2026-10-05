using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using Xunit;

namespace StockLens_UnitTests
{
    public class StockPriceHistoryServiceTests
    {
        private readonly Mock<IStockPriceHistoryRepository> _mockPriceHistoryRepo;
        private readonly Mock<IStockRepository> _mockStockRepo;
        private readonly Mock<ICompanyRepository> _mockCompanyRepo;
        private readonly Mock<IIndianApiHistoricalDataClient> _mockApiClient;
        private readonly Mock<IYahooFinanceClient> _mockYahooClient;
        private readonly Mock<ILogger<StockPriceHistoryService>> _mockLogger;
        private readonly StockPriceHistoryService _service;

        public StockPriceHistoryServiceTests()
        {
            _mockPriceHistoryRepo = new Mock<IStockPriceHistoryRepository>();
            _mockStockRepo = new Mock<IStockRepository>();
            _mockCompanyRepo = new Mock<ICompanyRepository>();
            _mockApiClient = new Mock<IIndianApiHistoricalDataClient>();
            _mockYahooClient = new Mock<IYahooFinanceClient>();
            _mockLogger = new Mock<ILogger<StockPriceHistoryService>>();

            _service = new StockPriceHistoryService(
                _mockPriceHistoryRepo.Object,
                _mockStockRepo.Object,
                _mockCompanyRepo.Object,
                _mockApiClient.Object,
                _mockYahooClient.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public async Task GetPriceHistoryBySymbol_ShouldFetchOhlcFromYahooFinance_AndPopulateAllOhlcFields()
        {
            // Arrange
            var symbol = "RELIANCE";
            var stock = new Stock { Id = 1, Symbol = symbol, Exchange = "NSE" };

            _mockStockRepo.Setup(r => r.GetOrCreateStockAsync(symbol, "NSE", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            _mockPriceHistoryRepo.Setup(r => r.GetByStockIdAsync(stock.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<StockPriceHistory>());

            var yahooRecords = new List<IndianApiPriceRecord>
            {
                new IndianApiPriceRecord { DateString = "2026-09-01", Open = 2950m, High = 3010m, Low = 2940m, Close = 3000m, Volume = 5000000 },
                new IndianApiPriceRecord { DateString = "2026-09-02", Open = 3005m, High = 3050m, Low = 2990m, Close = 3040m, Volume = 6000000 }
                // new IndianApiPriceRecord { DateString = d1, Open = 2950m, High = 3010m, Low = 2940m, Close = 3000m, Volume = 5000000 },
                // new IndianApiPriceRecord { DateString = d2, Open = 3005m, High = 3050m, Low = 2990m, Close = 3040m, Volume = 6000000 }
            };

            _mockYahooClient.Setup(y => y.GetHistoricalPricesAsync(symbol, "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(yahooRecords);

            // Act
            var result = await _service.GetPriceHistoryBySymbolAsync(symbol, "NSE", "1m", forceRefresh: true);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(symbol, result.Symbol);
            Assert.Equal(2, result.Dates.Count);
            Assert.Equal(2, result.Opens.Count);
            Assert.Equal(2, result.Highs.Count);
            Assert.Equal(2, result.Lows.Count);
            Assert.Equal(2, result.ClosePrices.Count);
            Assert.Equal(2, result.Volumes.Count);

            Assert.Equal(2950m, result.Opens[0]);
            Assert.Equal(3010m, result.Highs[0]);
            Assert.Equal(2940m, result.Lows[0]);
            Assert.Equal(3000m, result.ClosePrices[0]);

            Assert.Equal(3005m, result.Opens[1]);
            Assert.Equal(3050m, result.Highs[1]);
            Assert.Equal(2990m, result.Lows[1]);
            Assert.Equal(3040m, result.ClosePrices[1]);

            _mockYahooClient.Verify(y => y.GetHistoricalPricesAsync(symbol, "NSE", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPriceHistoryBySymbol_ShouldFallbackToIndianApi_WhenYahooFinanceReturnsEmpty()
        {
            // Arrange
            var symbol = "TATAMOTORS";
            var stock = new Stock { Id = 2, Symbol = symbol, Exchange = "NSE" };

            _mockStockRepo.Setup(r => r.GetOrCreateStockAsync(symbol, "NSE", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            _mockPriceHistoryRepo.Setup(r => r.GetByStockIdAsync(stock.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<StockPriceHistory>());

            _mockYahooClient.Setup(y => y.GetHistoricalPricesAsync(symbol, "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<IndianApiPriceRecord>());

            var indianApiRecords = new List<IndianApiPriceRecord>
            {
                new IndianApiPriceRecord { DateString = "2026-09-01", Open = 980m, High = 1000m, Low = 970m, Close = 995m, Volume = 8000000 }
            };

            _mockApiClient.Setup(a => a.GetHistoricalPricesAsync(symbol, "5yr", "NSE", "price", It.IsAny<CancellationToken>()))
                .ReturnsAsync(indianApiRecords);

            // Act
            var result = await _service.GetPriceHistoryBySymbolAsync(symbol, "NSE", "1m", forceRefresh: true);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(symbol, result.Symbol);
            Assert.Single(result.Dates);
            Assert.Equal(995m, result.ClosePrices[0]);
            Assert.Equal("IndianAPI", result.Source);
        }

        [Theory]
        [InlineData("1m")]
        [InlineData("3m")]
        [InlineData("1yr")]
        [InlineData("3yr")]
        [InlineData("5yr")]
        public async Task GetPriceHistoryBySymbol_ShouldSupportAllPeriods(string period)
        {
            // Arrange
            var symbol = "INFY";
            var stock = new Stock { Id = 3, Symbol = symbol, Exchange = "NSE" };

            _mockStockRepo.Setup(r => r.GetOrCreateStockAsync(symbol, "NSE", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            _mockPriceHistoryRepo.Setup(r => r.GetByStockIdAsync(stock.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<StockPriceHistory>
                {
                    new StockPriceHistory
                    {
                        Date = DateTime.UtcNow.AddMonths(-1),
                        Open = 1800,
                        High = 1850,
                        Low = 1790,
                        Close = 1820,
                        Volume = 2000000,
                        Source = "YahooFinance"
                    }
                });

            // Act
            var result = await _service.GetPriceHistoryBySymbolAsync(symbol, "NSE", period, forceRefresh: false);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.ErrorMessage);
        }
    }
}
