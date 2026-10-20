using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.AnnualReportInsights;
using StockLens_Infrastructure.ExternalServices.Screener;
using StockLens_Infrastructure.ExternalServices.Screener.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StockLens_UnitTests
{
    public class StockAnnualReportServiceTests
    {
        private readonly Mock<IStockRepository> _stockRepoMock = new();
        private readonly Mock<IStockAnnualReportRepository> _annualReportRepoMock = new();
        private readonly Mock<IScreenerClient> _screenerClientMock = new();
        private readonly Mock<IAnnualReportInsightExtractor> _insightExtractorMock = new();
        private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

        [Fact]
        public async Task GetAnnualReportsBySymbolAsync_NewStock_CreatesStock_SavesToDb_AndExtractsInsights()
        {
            // Arrange
            var symbol = "RELIANCE";
            var stock = new Stock
            {
                Id = 101,
                Symbol = symbol,
                Exchange = "NSE",
                Company = new Company { CompanyName = "Reliance Industries Ltd" }
            };

            // Stock does not exist initially; GetOrCreateStockAsync returns newly created stock
            _stockRepoMock
                .Setup(r => r.GetOrCreateStockAsync(symbol, "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            // DB has no existing report initially
            _annualReportRepoMock
                .Setup(r => r.GetLatestByStockIdAsync(stock.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockAnnualReport?)null);

            // Screener returns latest report
            IReadOnlyList<ScreenerDocumentItem> screenerDocs = new List<ScreenerDocumentItem>
            {
                new ScreenerDocumentItem
                {
                    Title = "Annual Report 2026",
                    Year = "2026",
                    Url = "https://www.bseindia.com/annualreport/reliance2026.pdf",
                    Source = "BSE",
                    DocumentType = ScreenerDocumentType.AnnualReport
                }
            };
            _screenerClientMock
                .Setup(s => s.GetAnnualReportsAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(screenerDocs);

            // Extractor returns JSON
            var dummyJson = "{\"executiveSummary\":\"Strong growth across Jio and Retail\",\"keyHighlights\":[\"Net Profit grew 15% YoY\"]}";
            _insightExtractorMock
                .Setup(e => e.ExtractInsightsJsonAsync(symbol, screenerDocs[0].Url, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dummyJson);

            StockAnnualReport? savedReport = null;
            _annualReportRepoMock
                .Setup(r => r.AddAsync(It.IsAny<StockAnnualReport>(), It.IsAny<CancellationToken>()))
                .Callback<StockAnnualReport, CancellationToken>((r, _) => {
                    r.Id = 1;
                    savedReport = r;
                })
                .Returns(Task.CompletedTask);

            _annualReportRepoMock
                .Setup(r => r.UpdateAsync(It.IsAny<StockAnnualReport>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = new StockAnnualReportService(
                _screenerClientMock.Object,
                _stockRepoMock.Object,
                _annualReportRepoMock.Object,
                _insightExtractorMock.Object,
                _memoryCache,
                NullLogger<StockAnnualReportService>.Instance);

            // Act
            var response = await service.GetAnnualReportsBySymbolAsync(symbol);

            // Assert
            response.Should().NotBeNull();
            response.Symbol.Should().Be(symbol);
            response.StockId.Should().Be(101);
            response.ExtractionStatus.Should().Be("Completed");
            response.Insights.Should().NotBeNull();
            response.Insights!.ExecutiveSummary.Should().Contain("Strong growth across Jio and Retail");

            // Verify DB AddAsync was called (Row inserted in table!)
            _annualReportRepoMock.Verify(r => r.AddAsync(It.Is<StockAnnualReport>(ar =>
                ar.StockId == 101 &&
                ar.PdfUrl == screenerDocs[0].Url &&
                ar.Year == "2026"), It.IsAny<CancellationToken>()), Times.Once);

            // Verify DB UpdateAsync was called to save extracted insights
            _annualReportRepoMock.Verify(r => r.UpdateAsync(It.Is<StockAnnualReport>(ar =>
                ar.ExtractedInsightsJson == dummyJson &&
                ar.ExtractionStatus == "Completed"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task LiveTest_RealDatabaseAndGemini_RelianceAnnualReport()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<StockLens_Infrastructure.DataContext.StockLensDataContext>()
                .UseSqlServer("Server=DESKTOP-J2CVHIQ;Database=StockLensTestDB;Trusted_Connection=True;Encrypt=False;")
                .Options;

            using var dbContext = new StockLens_Infrastructure.DataContext.StockLensDataContext(options);
            var stockRepo = new StockLens_Infrastructure.Repositories.StockRepository(dbContext);
            var annualReportRepo = new StockLens_Infrastructure.Repositories.StockAnnualReportRepository(dbContext);

            var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var screenerClient = new ScreenerClient(httpClient, Microsoft.Extensions.Options.Options.Create(new ScreenerSettings()), NullLogger<ScreenerClient>.Instance);

            var geminiSettings = Microsoft.Extensions.Options.Options.Create(new GeminiSettings
            {
                ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty,
                ModelName = "gemini-flash-latest",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                TimeoutSeconds = 90
            });

            var insightExtractor = new AnnualReportInsightExtractor(httpClient, geminiSettings, NullLogger<AnnualReportInsightExtractor>.Instance);
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            var service = new StockAnnualReportService(
                screenerClient,
                stockRepo,
                annualReportRepo,
                insightExtractor,
                memoryCache,
                NullLogger<StockAnnualReportService>.Instance);

            // Execute service for RELIANCE
            var response = await service.GetAnnualReportsBySymbolAsync("RELIANCE", refresh: true);

            response.Should().NotBeNull();
            response.ExtractionStatus.Should().Be("Completed");
            response.Insights.Should().NotBeNull();
            response.Insights!.ExecutiveSummary.Should().NotBeNullOrWhiteSpace();

            // Verify Database record
            var stock = await stockRepo.GetBySymbolAsync("RELIANCE");
            stock.Should().NotBeNull();
            var dbReport = await annualReportRepo.GetLatestByStockIdAsync(stock!.Id);
            dbReport.Should().NotBeNull();
            dbReport!.ExtractionStatus.Should().Be("Completed");
            dbReport.ExtractedInsightsJson.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task LiveTest_RealDatabaseAndGemini_TcsAnnualReport()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<StockLens_Infrastructure.DataContext.StockLensDataContext>()
                .UseSqlServer("Server=DESKTOP-J2CVHIQ;Database=StockLensTestDB;Trusted_Connection=True;Encrypt=False;")
                .Options;

            using var dbContext = new StockLens_Infrastructure.DataContext.StockLensDataContext(options);
            var stockRepo = new StockLens_Infrastructure.Repositories.StockRepository(dbContext);
            var annualReportRepo = new StockLens_Infrastructure.Repositories.StockAnnualReportRepository(dbContext);

            var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var screenerClient = new ScreenerClient(httpClient, Microsoft.Extensions.Options.Options.Create(new ScreenerSettings()), NullLogger<ScreenerClient>.Instance);

            var geminiSettings = Microsoft.Extensions.Options.Options.Create(new GeminiSettings
            {
                ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty,
                ModelName = "gemini-3.8-flash",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                TimeoutSeconds = 45
            });

            var insightExtractor = new AnnualReportInsightExtractor(httpClient, geminiSettings, NullLogger<AnnualReportInsightExtractor>.Instance);
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            var service = new StockAnnualReportService(
                screenerClient,
                stockRepo,
                annualReportRepo,
                insightExtractor,
                memoryCache,
                NullLogger<StockAnnualReportService>.Instance);

            // Execute service for TCS
            var response = await service.GetAnnualReportsBySymbolAsync("TCS", refresh: true);

            response.Should().NotBeNull();
            response.ExtractionStatus.Should().Be("Completed");
            response.Insights.Should().NotBeNull();
        }

        [Fact]
        public void ProcessAndPrioritizeInsights_StructuredExtraction_ValidationAndClassification()
        {
            // Arrange
            var json = @"{
                ""executiveSummary"": ""Tata Motors demonstrated record operational momentum and robust free cash flow."",
                ""companyExpansion"": [
                    {
                        ""title"": ""New EV Manufacturing Facility in Sanand"",
                        ""summary"": ""Commissioned 300,000 unit capacity electric vehicle plant in Sanand, Gujarat."",
                        ""category"": ""Company Expansion"",
                        ""reportingPeriod"": ""FY24"",
                        ""status"": ""Completed"",
                        ""financialValue"": 2200,
                        ""financialValueUnit"": ""Cr"",
                        ""expectedBenefit"": ""Tripling annual EV production volume"",
                        ""expectedDate"": ""Q1 FY25"",
                        ""sourcePage"": 48,
                        ""sourceText"": ""The Sanand facility acquisition was completed with an outlay of Rs 2,200 Cr.""
                    }
                ],
                ""majorOrdersAndContracts"": [
                    {
                        ""title"": ""5,000 Electric Buses Supply Order from DTC"",
                        ""summary"": ""Awarded single largest e-bus order in India for 5,000 units by Delhi Transport Corporation."",
                        ""category"": ""Major Orders & Contracts"",
                        ""status"": ""Approved"",
                        ""financialValue"": 7500,
                        ""financialValueUnit"": ""Cr"",
                        ""sourcePage"": 62
                    }
                ]
            }";

            // Act
            var insights = System.Text.Json.JsonSerializer.Deserialize<StockLens_BusinessLayer.DTOs.AnnualReportInsightsDto>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            insights.Should().NotBeNull();
            StockAnnualReportService.ProcessAndPrioritizeInsights(insights!);

            // Assert
            insights!.CompanyExpansion.Should().HaveCount(1);
            var exp = insights.CompanyExpansion[0];
            exp.Title.Should().Be("New EV Manufacturing Facility in Sanand");
            exp.Status.Should().Be("Completed");
            exp.FinancialValue.Should().Be(2200);
            exp.FinancialValueUnit.Should().Be("Cr");
            exp.SourcePage.Should().Be(48);
            exp.DisplayGroup.Should().Be("Expansion & Capacity");

            insights.MajorOrdersAndContracts.Should().HaveCount(1);
            var ord = insights.MajorOrdersAndContracts[0];
            ord.Status.Should().Be("Approved");
            ord.FinancialValue.Should().Be(7500);
            ord.DisplayGroup.Should().Be("Major Deals & Partnerships");

            // Top High-Impact Insights should be populated
            insights.TopHighImpactInsights.Should().NotBeEmpty();
            insights.TopHighImpactInsights.Should().Contain(i => i.Title.Contains("Sanand"));
        }

        [Fact]
        public void ProcessAndPrioritizeInsights_DeduplicationAndMerging_MergesDuplicateEvents()
        {
            // Arrange: Same Gigafactory project appears in CompanyExpansion AND CapacityExpansion
            var insights = new StockLens_BusinessLayer.DTOs.AnnualReportInsightsDto
            {
                CompanyExpansion = new List<StockLens_BusinessLayer.DTOs.AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Dhirubhai Ambani Green Energy Giga Complex",
                        Summary = "Setting up integrated 20GW solar giga factory in Jamnagar Gujarat.",
                        Category = "Company Expansion",
                        FinancialValue = 75000,
                        FinancialValueUnit = "Cr"
                    }
                },
                CapacityExpansion = new List<StockLens_BusinessLayer.DTOs.AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Dhirubhai Ambani Green Energy Giga Complex Jamnagar",
                        Summary = "Setting up integrated 20GW solar giga factory in Jamnagar with planned additions.",
                        Category = "Capacity Expansion",
                        ExpectedDate = "FY26",
                        SourcePage = 54,
                        SourceText = "Phase 1 commissioning targeted for end of FY26."
                    }
                }
            };

            // Act
            StockAnnualReportService.ProcessAndPrioritizeInsights(insights);

            // Assert: Cross-category deduplication must merge details into a single item
            var totalCount = insights.CompanyExpansion.Count + insights.CapacityExpansion.Count;
            totalCount.Should().Be(1);

            var mergedItem = insights.CompanyExpansion.Count > 0 ? insights.CompanyExpansion[0] : insights.CapacityExpansion[0];
            mergedItem.FinancialValue.Should().Be(75000);
            mergedItem.ExpectedDate.Should().Be("FY26");
            mergedItem.SourcePage.Should().Be(54);
        }

        [Fact]
        public void ProcessAndPrioritizeInsights_RemovesLowValueBoilerplate()
        {
            // Arrange: Generic boilerplate strings that should be filtered out
            var insights = new StockLens_BusinessLayer.DTOs.AnnualReportInsightsDto
            {
                CompanyExpansion = new List<StockLens_BusinessLayer.DTOs.AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Expansion Details",
                        Summary = "Expansion details available in the official Annual Report document.",
                        Category = "Company Expansion"
                    },
                    new()
                    {
                        Title = "New Chemical Facility in Dahej",
                        Summary = "Constructing 50,000 MT specialty chemicals plant in Dahej, Gujarat.",
                        Category = "Company Expansion",
                        FinancialValue = 450,
                        FinancialValueUnit = "Cr"
                    }
                }
            };

            // Act
            StockAnnualReportService.ProcessAndPrioritizeInsights(insights);

            // Assert: Generic boilerplate removed, concrete project retained
            insights.CompanyExpansion.Should().HaveCount(1);
            insights.CompanyExpansion[0].Title.Should().Be("New Chemical Facility in Dahej");
        }

        [Fact]
        public void ProcessAndPrioritizeInsights_PrioritizationEngine_RanksHighImpactFirst()
        {
            // Arrange
            var insights = new StockLens_BusinessLayer.DTOs.AnnualReportInsightsDto
            {
                ManagementAndWorkforce = new List<StockLens_BusinessLayer.DTOs.AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Routine Staff Training Initiatives",
                        Summary = "Conducted routine corporate compliance training for employees across regional hubs.",
                        Category = "Management & Workforce"
                    }
                },
                CompanyExpansion = new List<StockLens_BusinessLayer.DTOs.AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Mega Semiconductor Fabrication Plant",
                        Summary = "Commenced construction of India's first semiconductor fab unit with Taiwanese partner.",
                        Category = "Company Expansion",
                        FinancialValue = 91000,
                        FinancialValueUnit = "Cr",
                        Status = "Ongoing",
                        ExpectedDate = "2026",
                        SourcePage = 34
                    }
                }
            };

            // Act
            StockAnnualReportService.ProcessAndPrioritizeInsights(insights);

            // Assert: Mega Fab project must have higher PriorityScore than routine staff training
            var fab = insights.CompanyExpansion[0];
            var staff = insights.ManagementAndWorkforce[0];
            fab.PriorityScore.Should().BeGreaterThan(staff.PriorityScore);
            insights.TopHighImpactInsights[0].Title.Should().Be("Mega Semiconductor Fabrication Plant");
        }

        [Fact]
        public void DeserializeInsights_LegacyStringArrayJson_DeserializesBackwardCompatibly()
        {
            // Arrange: Older database JSON containing plain string array
            var legacyJson = @"{
                ""executiveSummary"": ""Robust financial year."",
                ""companyExpansion"": [
                    ""New automated warehouse in Bhiwandi with 500,000 sq ft space.""
                ]
            }";

            // Act
            var insights = System.Text.Json.JsonSerializer.Deserialize<StockLens_BusinessLayer.DTOs.AnnualReportInsightsDto>(legacyJson, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            insights.Should().NotBeNull();
            StockAnnualReportService.ProcessAndPrioritizeInsights(insights!);

            // Assert
            insights!.CompanyExpansion.Should().HaveCount(1);
            insights.CompanyExpansion[0].Summary.Should().Contain("warehouse in Bhiwandi");
        }
    }
}
