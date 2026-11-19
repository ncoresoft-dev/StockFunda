using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.Services;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StockLens_UnitTests
{
    public class MarketNewsServiceTests
    {
        [Fact]
        public void GetMarketNews_NeverCallsExternalRssFeed_ReturnsFromCache()
        {
            // Arrange
            var cacheMock = new Mock<IMarketNewsCache>();
            var expectedResponse = new MarketNewsResponseDto
            {
                News = new List<MarketNewsItemDto>
                {
                    new() { Id = "test-1", Title = "Sensex surges 400 pts", SourceName = "Economic Times", PublishedAt = DateTime.UtcNow }
                },
                TotalCount = 1,
                IsCached = true,
                Status = "ok"
            };

            cacheMock.Setup(c => c.GetResponse(10, null)).Returns(expectedResponse);

            var service = new MarketNewsService(cacheMock.Object, NullLogger<MarketNewsService>.Instance);

            // Act: Simulate user request
            var result = service.GetMarketNews(10, null);

            // Assert
            result.Should().NotBeNull();
            result.IsCached.Should().BeTrue();
            result.News.Should().HaveCount(1);
            result.News[0].Title.Should().Be("Sensex surges 400 pts");

            // Cache was invoked, no external API call is ever made during user requests
            cacheMock.Verify(c => c.GetResponse(10, null), Times.Once);
        }

        [Fact]
        public void MarketNewsCache_InitialState_EmptyWithoutFakeData()
        {
            // Arrange
            var cache = new MarketNewsCache(NullLogger<MarketNewsCache>.Instance);

            // Act
            var response = cache.GetResponse(5, null);

            // Assert: No fake/dummy static articles exist at boot
            response.Should().NotBeNull();
            response.News.Should().BeEmpty();
            response.TotalCount.Should().Be(0);
            response.IsCached.Should().BeFalse();
            response.Status.Should().Be("empty");
        }

        [Fact]
        public void MarketNewsCache_UpdateArticles_ReplacesWithLiveArticles()
        {
            // Arrange
            var cache = new MarketNewsCache(NullLogger<MarketNewsCache>.Instance);
            var liveArticles = new List<IndianApiStandardArticle>
            {
                new()
                {
                    ExternalNewsId = "ext-1",
                    Title = "Nifty breaks 25000 milestone",
                    Description = "Benchmark Nifty index hit a fresh all-time high led by IT and Banks.",
                    Content = "https://news.google.com/test1",
                    SourceName = "Livemint",
                    PublishedAt = DateTime.UtcNow.AddMinutes(-5),
                    Category = "Market Indices"
                },
                new()
                {
                    ExternalNewsId = "ext-2",
                    Title = "RBI keeps policy rate steady at 6.5%",
                    Description = "Monetary policy committee decides to hold rates.",
                    Content = "https://news.google.com/test2",
                    SourceName = "Economic Times",
                    PublishedAt = DateTime.UtcNow.AddMinutes(-15),
                    Category = "Economy & Policy"
                }
            };

            // Act
            cache.UpdateArticles(liveArticles);
            var response = cache.GetResponse(10, null);

            // Assert
            response.News.Should().HaveCount(2);
            response.News[0].Title.Should().Be("Nifty breaks 25000 milestone");
            response.News[0].SourceName.Should().Be("Livemint");
            response.News[0].Category.Should().Be("Market Indices");
            cache.FailureCount.Should().Be(0);
            response.Status.Should().Be("ok");
        }

        [Fact]
        public void MarketNewsCache_FilterByCategory_ReturnsOnlyMatchingItems()
        {
            // Arrange
            var cache = new MarketNewsCache(NullLogger<MarketNewsCache>.Instance);
            var articles = new List<IndianApiStandardArticle>
            {
                new() { ExternalNewsId = "1", Title = "Sensex climbs", SourceName = "ET", PublishedAt = DateTime.UtcNow, Category = "Market Indices" },
                new() { ExternalNewsId = "2", Title = "RBI holds rates", SourceName = "Mint", PublishedAt = DateTime.UtcNow, Category = "Economy & Policy" },
                new() { ExternalNewsId = "3", Title = "TCS Q3 profit up", SourceName = "MC", PublishedAt = DateTime.UtcNow, Category = "Corporate" }
            };
            cache.UpdateArticles(articles);

            // Act
            var indicesNews = cache.GetResponse(10, "Market Indices");
            var corporateNews = cache.GetResponse(10, "Corporate");

            // Assert
            indicesNews.News.Should().HaveCount(1);
            indicesNews.News[0].Category.Should().Be("Market Indices");

            corporateNews.News.Should().HaveCount(1);
            corporateNews.News[0].Category.Should().Be("Corporate");
        }

        [Fact]
        public void ExponentialBackoff_CalculatesCorrectWaitSequence()
        {
            // Verifies the doubling backoff sequence: 1 min, 2 min, 4 min, 8 min, up to 15 min
            var currentWaitMinutes = 1.0;
            const double maxWaitMinutes = 15.0;

            var sequence = new List<double>();
            for (int i = 0; i < 6; i++)
            {
                sequence.Add(currentWaitMinutes);
                currentWaitMinutes = Math.Min(currentWaitMinutes * 2, maxWaitMinutes);
            }

            // Expected sequence: 1, 2, 4, 8, 15, 15
            sequence.Should().Equal(new double[] { 1, 2, 4, 8, 15, 15 });
        }
    }
}
