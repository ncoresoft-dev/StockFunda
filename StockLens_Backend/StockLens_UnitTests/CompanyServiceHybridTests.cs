using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.MapperProfile;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.IndianApi;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StockLens_UnitTests
{
    public class CompanyServiceHybridTests
    {
        private readonly IMapper _mapper;
        private readonly Mock<ICompanyRepository> _repoMock;
        private readonly Mock<IIndianApiBalanceSheetClient> _indianApiMock;
        private readonly Mock<IYahooFinanceClient> _yahooMock;
        private readonly CompanyService _service;

        public CompanyServiceHybridTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAutoMapper(cfg => cfg.AddProfile<MapperProfile>());
            var sp = services.BuildServiceProvider();
            _mapper = sp.GetRequiredService<IMapper>();
            _repoMock = new Mock<ICompanyRepository>();
            _indianApiMock = new Mock<IIndianApiBalanceSheetClient>();
            _yahooMock = new Mock<IYahooFinanceClient>();

            _service = new CompanyService(
                _repoMock.Object,
                _indianApiMock.Object,
                _yahooMock.Object,
                _mapper,
                NullLogger<CompanyService>.Instance);
        }

        [Fact]
        public async Task GetCompanyOverview_WhenCachedInDb_ShouldReturnDirectlyWithoutExternalCalls()
        {
            // Arrange
            var existing = new Company
            {
                Symbol = "RELIANCE",
                CompanyName = "Reliance Industries Ltd",
                Industry = "Conglomerate",
                Sector = "Energy",
                About = "Reliance Industries Limited is engaged in hydrocarbon exploration and retail services.",
                KeyPointsJson = "[\"Hydrocarbon exploration leader\",\"Major telecom player via Jio\"]",
                WebsiteUrl = "https://www.ril.com"
            };

            _repoMock.Setup(r => r.GetCompanyBySymbolAsync("RELIANCE"))
                .ReturnsAsync(existing);

            // Act
            var result = await _service.GetCompanyOverviewAsync("RELIANCE", "NSE", forceRefresh: false);

            // Assert
            result.Should().NotBeNull();
            result!.Symbol.Should().Be("RELIANCE");
            result.Source.Should().Be("Database");
            result.About.Should().Contain("Reliance Industries Limited");
            result.KeyPoints.Should().HaveCount(2);
            result.WebsiteUrl.Should().Be("https://www.ril.com");

            _indianApiMock.Verify(a => a.GetStockFinancialsAndOverviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _yahooMock.Verify(y => y.GetCompanyProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetCompanyOverview_WhenIndianApiHasFullProfile_ShouldUseIndianApi()
        {
            // Arrange
            _repoMock.Setup(r => r.GetCompanyBySymbolAsync("TCS"))
                .ReturnsAsync((Company?)null);

            var indianOverview = new IndianApiStockOverviewDto
            {
                Symbol = "TCS",
                CompanyName = "Tata Consultancy Services Ltd",
                Industry = "Information Technology",
                SectorName = "Technology",
                WebsiteUrl = "https://www.tcs.com",
                About = "Tata Consultancy Services is an Indian multinational information technology services and consulting company headquartered in Mumbai.",
                KeyPoints = new List<string> { "Global IT services powerhouse", "Strong operating margin above 25%" }
            };

            _indianApiMock.Setup(a => a.GetStockFinancialsAndOverviewAsync("TCS", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(indianOverview);

            _repoMock.Setup(r => r.SaveOrUpdateCompanyAsync(It.IsAny<Company>()))
                .ReturnsAsync((Company c) => c);

            // Act
            var result = await _service.GetCompanyOverviewAsync("TCS", "NSE", forceRefresh: true);

            // Assert
            result.Should().NotBeNull();
            result!.Symbol.Should().Be("TCS");
            result.CompanyName.Should().Be("Tata Consultancy Services Ltd");
            result.About.Should().Contain("consulting company headquartered in Mumbai");
            result.KeyPoints.Should().HaveCount(2);
            result.WebsiteUrl.Should().Be("https://www.tcs.com");

            _repoMock.Verify(r => r.SaveOrUpdateCompanyAsync(It.Is<Company>(c => c.Symbol == "TCS" && c.WebsiteUrl == "https://www.tcs.com")), Times.Once);
        }

        [Fact]
        public async Task GetCompanyOverview_WhenIndianApiAboutMissing_ShouldFallbackToYahooFinance()
        {
            // Arrange
            _repoMock.Setup(r => r.GetCompanyBySymbolAsync("INFY"))
                .ReturnsAsync((Company?)null);

            // IndianAPI only returns basic names/financials without description
            var indianOverview = new IndianApiStockOverviewDto
            {
                Symbol = "INFY",
                CompanyName = "Infosys Limited",
                Industry = "Computers - Software",
                SectorName = "Information Technology"
            };

            _indianApiMock.Setup(a => a.GetStockFinancialsAndOverviewAsync("INFY", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(indianOverview);

            // Yahoo Finance returns rich description & website
            var yahooProfile = new YahooCompanyProfileDto
            {
                Symbol = "INFY",
                CompanyName = "Infosys Limited",
                Industry = "Information Technology Services",
                Sector = "Technology",
                Website = "https://www.infosys.com",
                LongBusinessSummary = "Infosys Limited, together with its subsidiaries, provides digital consulting, technology, and next-generation services worldwide across finance, healthcare, and retail sectors.",
                FullTimeEmployees = 317240,
                KeyExecutives = new List<string> { "Salil Parekh (CEO & MD)", "Jayesh Sanghrajka (CFO)" }
            };

            _yahooMock.Setup(y => y.GetCompanyProfileAsync("INFY", "NSE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(yahooProfile);

            _repoMock.Setup(r => r.SaveOrUpdateCompanyAsync(It.IsAny<Company>()))
                .ReturnsAsync((Company c) => c);

            // Act
            var result = await _service.GetCompanyOverviewAsync("INFY", "NSE", forceRefresh: true);

            // Assert
            result.Should().NotBeNull();
            result!.Symbol.Should().Be("INFY");
            result.CompanyName.Should().Be("Infosys Limited");
            result.Source.Should().Be("Hybrid");
            result.About.Should().Contain("digital consulting, technology");
            result.WebsiteUrl.Should().Be("https://www.infosys.com");
            result.EmployeesCount.Should().Be(317240);
            result.KeyExecutives.Should().Contain("Salil Parekh (CEO & MD)");
            result.KeyPoints.Should().NotBeEmpty();

            _repoMock.Verify(r => r.SaveOrUpdateCompanyAsync(It.Is<Company>(c => c.Symbol == "INFY" && c.EmployeesCount == 317240)), Times.Once);
        }

        [Fact]
        public void CompanyLogoHelper_BuildLogoUrl_ShouldReturnCorrectSharePerksUrl()
        {
            // Reliance ISIN
            var relianceUrl = StockLens_BusinessLayer.Helpers.CompanyLogoHelper.BuildLogoUrl("INE002A01018");
            relianceUrl.Should().Be("https://company-logo.shareperks.in/logo/INE002A01018/icon.svg");
        }

        [Fact]
        public async Task GetCompanyOverview_ShouldMapLogoUrlDirectlyFromDatabase()
        {
            // Arrange
            var existing = new Company
            {
                Symbol = "RELIANCE",
                CompanyName = "Reliance Industries Ltd",
                LogoUrl = "https://company-logo.shareperks.in/logo/INE002A01018/icon.svg",
                About = "Reliance Industries is an Indian multinational conglomerate."
            };

            _repoMock.Setup(r => r.GetCompanyBySymbolAsync("RELIANCE"))
                .ReturnsAsync(existing);

            // Act
            var result = await _service.GetCompanyOverviewAsync("RELIANCE", "NSE", forceRefresh: false);

            // Assert
            result.Should().NotBeNull();
            result!.LogoUrl.Should().Be("https://company-logo.shareperks.in/logo/INE002A01018/icon.svg");
        }
    }
}
