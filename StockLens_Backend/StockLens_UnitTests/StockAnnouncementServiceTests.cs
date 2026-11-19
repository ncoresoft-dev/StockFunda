using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using StockLens_BusinessLayer.MapperProfile;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.Nse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StockLens_UnitTests
{
    public class StockAnnouncementServiceTests
    {
        private readonly IMapper _mapper;

        public StockAnnouncementServiceTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAutoMapper(cfg => cfg.AddProfile<MapperProfile>());
            var sp = services.BuildServiceProvider();
            _mapper = sp.GetRequiredService<IMapper>();
        }

        [Fact]
        public async Task GetAnnouncementsAsync_ReadsFromDatabase_NeverCallsNseDirectly()
        {
            // Arrange: Ensure NSE client is never called on user queries
            var repoMock = new Mock<IStockAnnouncementRepository>();
            var nseClientMock = new Mock<INseAnnouncementClient>();

            var mockAnnouncements = new List<StockAnnouncement>
            {
                new()
                {
                    Id = 1,
                    SeqId = "106865074",
                    Symbol = "RELIANCE",
                    CompanyName = "Reliance Industries Limited",
                    Subject = "Board Meeting",
                    Details = "Board of Directors meeting scheduled for financial results.",
                    BroadcastDateTime = DateTime.UtcNow.AddMinutes(-10),
                    AttachmentUrl = "https://nsearchives.nseindia.in/corporate/reliance.pdf",
                    HasXbrl = true
                }
            };

            repoMock.Setup(r => r.GetAnnouncementsAsync(null, null, null, 30, 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockAnnouncements);
            repoMock.Setup(r => r.GetTotalCountAsync(null, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            repoMock.Setup(r => r.GetLatestBroadcastDateTimeAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DateTime.UtcNow.AddMinutes(-5));
            repoMock.Setup(r => r.GetDistinctCategoriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "Board Meeting" });

            var service = new StockAnnouncementService(
                repoMock.Object,
                nseClientMock.Object,
                _mapper,
                NullLogger<StockAnnouncementService>.Instance);

            // Act: Simulate user query
            var result = await service.GetAnnouncementsAsync();

            // Assert
            result.Should().NotBeNull();
            result.TotalCount.Should().Be(1);
            result.Announcements.Should().HaveCount(1);
            result.Announcements[0].Symbol.Should().Be("RELIANCE");
            result.Announcements[0].Subject.Should().Be("Board Meeting");
            result.Announcements[0].TimeAgo.Should().NotBeNullOrEmpty();

            // Verify NSE external client is NEVER called on user queries
            nseClientMock.Verify(c => c.FetchAnnouncementsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
            repoMock.Verify(r => r.GetAnnouncementsAsync(null, null, null, 30, 1, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SyncLatestAnnouncementsAsync_FetchesFromNseAndSavesToDatabase()
        {
            // Arrange
            var repoMock = new Mock<IStockAnnouncementRepository>();
            var nseClientMock = new Mock<INseAnnouncementClient>();

            var nseFeedItems = new List<StockAnnouncement>
            {
                new()
                {
                    SeqId = "200001",
                    Symbol = "TCS",
                    CompanyName = "Tata Consultancy Services Limited",
                    Subject = "Financial Results",
                    BroadcastDateTime = DateTime.UtcNow
                },
                new()
                {
                    SeqId = "200002",
                    Symbol = "INFY",
                    CompanyName = "Infosys Limited",
                    Subject = "Updates",
                    BroadcastDateTime = DateTime.UtcNow
                }
            };

            nseClientMock.Setup(c => c.FetchAnnouncementsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(nseFeedItems);

            repoMock.Setup(r => r.SaveAnnouncementsAsync(nseFeedItems, It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);

            var service = new StockAnnouncementService(
                repoMock.Object,
                nseClientMock.Object,
                _mapper,
                NullLogger<StockAnnouncementService>.Instance);

            // Act: Background service runs sync
            var saved = await service.SyncLatestAnnouncementsAsync();

            // Assert
            saved.Should().Be(2);
            nseClientMock.Verify(c => c.FetchAnnouncementsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
            repoMock.Verify(r => r.SaveAnnouncementsAsync(nseFeedItems, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAnnouncementsAsync_WithMultipleSymbols_CallsMultiSymbolRepoMethod()
        {
            // Arrange
            var repoMock = new Mock<IStockAnnouncementRepository>();
            var nseClientMock = new Mock<INseAnnouncementClient>();

            var symbols = new[] { "RELIANCE", "TCS" };
            var mockAnnouncements = new List<StockAnnouncement>
            {
                new()
                {
                    Id = 1,
                    SeqId = "1001",
                    Symbol = "RELIANCE",
                    CompanyName = "Reliance Industries Limited",
                    Subject = "Investor Meet",
                    BroadcastDateTime = DateTime.UtcNow.AddHours(-1)
                },
                new()
                {
                    Id = 2,
                    SeqId = "1002",
                    Symbol = "TCS",
                    CompanyName = "Tata Consultancy Services Limited",
                    Subject = "Press Release",
                    BroadcastDateTime = DateTime.UtcNow.AddHours(-2)
                }
            };

            repoMock.Setup(r => r.GetAnnouncementsAsync(null, symbols, null, null, 30, 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockAnnouncements);
            repoMock.Setup(r => r.GetTotalCountAsync(null, symbols, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);
            repoMock.Setup(r => r.GetLatestBroadcastDateTimeAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DateTime.UtcNow);
            repoMock.Setup(r => r.GetDistinctCategoriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "Investor Meet", "Press Release" });

            var service = new StockAnnouncementService(
                repoMock.Object,
                nseClientMock.Object,
                _mapper,
                NullLogger<StockAnnouncementService>.Instance);

            // Act
            var result = await service.GetAnnouncementsAsync(null, symbols, null, null, 30, 1);

            // Assert
            result.Should().NotBeNull();
            result.TotalCount.Should().Be(2);
            result.Announcements.Should().HaveCount(2);
            result.Announcements.Select(a => a.Symbol).Should().Contain(new[] { "RELIANCE", "TCS" });
            repoMock.Verify(r => r.GetAnnouncementsAsync(null, symbols, null, null, 30, 1, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
