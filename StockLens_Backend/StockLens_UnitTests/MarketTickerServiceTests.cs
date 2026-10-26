using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StockLens_UnitTests
{
    public class MarketTickerServiceTests
    {
        private readonly Mock<IMarketTickerRepository> _repoMock = new();
        private readonly Mock<IYahooFinanceClient> _yahooMock = new();
        private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

        [Fact]
        public async Task GetMarketTickerQuotesAsync_DbIsFresh_ReturnsFromDb_WithoutCallingYahoo()
        {
            // Arrange
            var freshQuotes = new List<MarketTickerQuote>
            {
                new() { Symbol = "^NSEI", DisplayName = "NIFTY 50", ShortName = "NIFTY", CurrentPrice = 24350, Change = 121, ChangePercent = 0.5m, LastUpdated = DateTime.UtcNow, DisplayOrder = 1 },
                new() { Symbol = "^BSESN", DisplayName = "SENSEX", ShortName = "SENSEX", CurrentPrice = 80120, Change = 319, ChangePercent = 0.4m, LastUpdated = DateTime.UtcNow, DisplayOrder = 2 },
                new() { Symbol = "^NSEBANK", DisplayName = "NIFTY BANK", ShortName = "BANK NIFTY", CurrentPrice = 52410, Change = -52, ChangePercent = -0.1m, LastUpdated = DateTime.UtcNow, DisplayOrder = 3 },
                new() { Symbol = "^CNXIT", DisplayName = "NIFTY IT", ShortName = "NIFTY IT", CurrentPrice = 38900, Change = -274, ChangePercent = -0.7m, LastUpdated = DateTime.UtcNow, DisplayOrder = 4 },
                new() { Symbol = "INR=X", DisplayName = "USD/INR", ShortName = "USD/INR", CurrentPrice = 83.9m, Change = 0.08m, ChangePercent = 0.1m, InstrumentType = "CURRENCY", LastUpdated = DateTime.UtcNow, DisplayOrder = 5 },
                new() { Symbol = "^INDIAVIX", DisplayName = "INDIA VIX", ShortName = "INDIA VIX", CurrentPrice = 15.8m, Change = 0.6m, ChangePercent = 3.9m, LastUpdated = DateTime.UtcNow, DisplayOrder = 6 },
                new() { Symbol = "^CNXAUTO", DisplayName = "NIFTY AUTO", ShortName = "NIFTY AUTO", CurrentPrice = 24862, Change = -149, ChangePercent = -0.6m, LastUpdated = DateTime.UtcNow, DisplayOrder = 7 },
                new() { Symbol = "^CNXPHARMA", DisplayName = "NIFTY PHARMA", ShortName = "NIFTY PHARMA", CurrentPrice = 21500, Change = 110, ChangePercent = 0.5m, LastUpdated = DateTime.UtcNow, DisplayOrder = 8 }
            };

            _repoMock.Setup(r => r.GetAllQuotesAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(freshQuotes);

            var service = new MarketTickerService(
                _repoMock.Object,
                _yahooMock.Object,
                _memoryCache,
                NullLogger<MarketTickerService>.Instance);

            // Act
            var result = (await service.GetMarketTickerQuotesAsync()).ToList();

            // Assert
            result.Should().HaveCount(8);
            result.First(r => r.Symbol == "^NSEI").ShortName.Should().Be("NIFTY");
            result.First(r => r.Symbol == "INR=X").InstrumentType.Should().Be("CURRENCY");

            // Verify Yahoo client was NEVER called because DB data is fresh
            _yahooMock.Verify(y => y.GetLiveQuoteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetMarketTickerQuotesAsync_DbIsEmpty_CallsYahoo_AndSavesToDb()
        {
            // Arrange
            _repoMock.Setup(r => r.GetAllQuotesAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<MarketTickerQuote>());

            _yahooMock.Setup(y => y.GetLiveQuoteAsync("^NSEI", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooLiveQuoteDto
                      {
                          Symbol = "^NSEI",
                          Price = 24350,
                          PreviousClose = 24228.85m,
                          Change = 121.15m,
                          ChangePercent = 0.5m
                      });

            _yahooMock.Setup(y => y.GetLiveQuoteAsync("INR=X", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new YahooLiveQuoteDto
                      {
                          Symbol = "INR=X",
                          Price = 83.9m,
                          PreviousClose = 83.82m,
                          Change = 0.08m,
                          ChangePercent = 0.1m
                      });

            var service = new MarketTickerService(
                _repoMock.Object,
                _yahooMock.Object,
                _memoryCache,
                NullLogger<MarketTickerService>.Instance);

            // Act
            var result = (await service.GetMarketTickerQuotesAsync()).ToList();

            // Assert
            result.Should().NotBeEmpty();
            var nifty = result.FirstOrDefault(r => r.Symbol == "^NSEI");
            nifty.Should().NotBeNull();
            nifty!.CurrentPrice.Should().Be(24350);

            // Verify repository saved the quotes
            _repoMock.Verify(r => r.SaveOrUpdateQuotesAsync(It.IsAny<IEnumerable<MarketTickerQuote>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
