using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Services;
using StockLens_DataLayer.Entities;
using StockLens_DataLayer.Interfaces;
using StockLens_Infrastructure.ExternalServices.AnnualReportInsights;
using StockLens_Infrastructure.ExternalServices.Screener;
using System.Text.Json;
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
        public async Task GetAnnualReportsBySymbolAsync_StockNotInDb_ReturnsNotAvailable_WithoutCallingScreenerOrGemini()
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

            _stockRepoMock
                .Setup(r => r.GetOrCreateStockAsync(symbol, "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            _annualReportRepoMock
                .Setup(r => r.GetLatestByStockIdAsync(stock.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockAnnualReport?)null);

            var service = new StockAnnualReportService(
                _screenerClientMock.Object,
                _stockRepoMock.Object,
                _annualReportRepoMock.Object,
                _insightExtractorMock.Object,
                _memoryCache,
                NullLogger<StockAnnualReportService>.Instance);

            // Act
            var response = await service.GetAnnualReportsBySymbolAsync(symbol);

            // Assert - Should return NotAvailable immediately without auto-scraping Screener or calling Gemini
            response.Should().NotBeNull();
            response.Symbol.Should().Be(symbol);
            response.ExtractionStatus.Should().Be("NotAvailable");
            response.Insights.Should().BeNull();

            // Verify Screener and InsightExtractor were NEVER called
            _screenerClientMock.Verify(s => s.GetAnnualReportsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _insightExtractorMock.Verify(e => e.ExtractInsightsJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AdminSaveAndExtractAnnualReportAsync_ExtractsInsights_AndSavesToDb()
        {
            // Arrange
            var symbol = "RELIANCE";
            var pdfUrl = "https://www.bseindia.com/annualreport/reliance2026.pdf";
            var stock = new Stock
            {
                Id = 101,
                Symbol = symbol,
                Exchange = "NSE",
                Company = new Company { CompanyName = "Reliance Industries Ltd" }
            };

            _stockRepoMock
                .Setup(r => r.GetOrCreateStockAsync(symbol, "NSE", null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stock);

            _annualReportRepoMock
                .Setup(r => r.GetLatestByStockIdAsync(stock.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockAnnualReport?)null);

            var dummyJson = "{\"executiveSummary\":\"Strong growth across Jio and Retail\",\"keyHighlights\":[\"Net Profit grew 15% YoY\"]}";
            _insightExtractorMock
                .Setup(e => e.ExtractInsightsJsonAsync(symbol, pdfUrl, "2026", It.IsAny<CancellationToken>()))
                .ReturnsAsync(dummyJson);

            _annualReportRepoMock
                .Setup(r => r.AddAsync(It.IsAny<StockAnnualReport>(), It.IsAny<CancellationToken>()))
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
            var response = await service.AdminSaveAndExtractAnnualReportAsync(new AdminSaveAndExtractReportRequestDto
            {
                Symbol = symbol,
                Year = "2026",
                Title = "Annual Report 2026",
                PdfUrl = pdfUrl,
                Source = "BSE"
            });

            // Assert
            response.Should().NotBeNull();
            response.Symbol.Should().Be(symbol);
            response.ExtractionStatus.Should().Be("Completed");
            response.Insights.Should().NotBeNull();
            response.Insights!.ExecutiveSummary.Should().Contain("Strong growth across Jio and Retail");

            // Verify extractor was called
            _insightExtractorMock.Verify(e => e.ExtractInsightsJsonAsync(symbol, pdfUrl, "2026", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task LiveTest_RealDatabaseAndGemini_RelianceAnnualReport()
        {
            var options = new DbContextOptionsBuilder<StockLens_Infrastructure.DataContext.StockLensDataContext>()
                .UseSqlServer("Server=DESKTOP-J2CVHIQ;Database=StockLensTestDB;Trusted_Connection=True;Encrypt=False;")
                .Options;

            using var dbContext = new StockLens_Infrastructure.DataContext.StockLensDataContext(options);
            var stockRepo = new StockLens_Infrastructure.Repositories.StockRepository(dbContext);
            var annualReportRepo = new StockLens_Infrastructure.Repositories.StockAnnualReportRepository(dbContext);

            var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var screenerClient = new ScreenerClient(httpClient, Microsoft.Extensions.Options.Options.Create(new ScreenerSettings()), NullLogger<ScreenerClient>.Instance);

            var geminiSettings = Microsoft.Extensions.Options.Options.Create(new GeminiSettings
            {
                ApiKey = GetGeminiApiKey(),
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
            var options = new DbContextOptionsBuilder<StockLens_Infrastructure.DataContext.StockLensDataContext>()
                .UseSqlServer("Server=DESKTOP-J2CVHIQ;Database=StockLensTestDB;Trusted_Connection=True;Encrypt=False;")
                .Options;

            using var dbContext = new StockLens_Infrastructure.DataContext.StockLensDataContext(options);
            var stockRepo = new StockLens_Infrastructure.Repositories.StockRepository(dbContext);
            var annualReportRepo = new StockLens_Infrastructure.Repositories.StockAnnualReportRepository(dbContext);

            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var screenerClient = new ScreenerClient(httpClient, Microsoft.Extensions.Options.Options.Create(new ScreenerSettings()), NullLogger<ScreenerClient>.Instance);

            var geminiSettings = Microsoft.Extensions.Options.Options.Create(new GeminiSettings
            {
                ApiKey = GetGeminiApiKey(),
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
        public async Task Diagnose_RailTel_Extraction()
        {
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            
            var apiKey = GetGeminiApiKey();
            var pdfUrl = "https://www.bseindia.com/xml-data/corpfiling/AttachHis/7044d17d-6a4f-4276-bb80-83225794f6b4.pdf";
            
            var tempFile = Path.Combine(Path.GetTempPath(), "railtel_test.pdf");
            if (!File.Exists(tempFile))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, pdfUrl);
                req.Headers.Add("User-Agent", "Mozilla/5.0");
                using var resp = await httpClient.SendAsync(req);
                await using var fs = File.Create(tempFile);
                await resp.Content.CopyToAsync(fs);
            }

            var geminiSettings = Microsoft.Extensions.Options.Options.Create(new GeminiSettings
            {
                ApiKey = apiKey,
                ModelName = "gemini-flash-latest", // Primary model hits 429 daily quota
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                TimeoutSeconds = 90
            });

            var insightExtractor = new AnnualReportInsightExtractor(httpClient, geminiSettings, NullLogger<AnnualReportInsightExtractor>.Instance);
            var result = await insightExtractor.ExtractInsightsJsonAsync("RAILTEL", pdfUrl);
            Console.WriteLine("EXTRACTOR RESULT: " + result);
            result.Should().NotBeNullOrWhiteSpace();
            result.Should().NotContain("Insights extraction encountered a temporary issue");
            result.Should().NotContain("AI insights generation is temporarily unavailable");
            result.Should().Contain("RailTel");
        }

        [Fact]
        public void IsFailedOrFallbackInsights_IdentifiesFailureSummaries()
        {
            var fallback = "{\"executiveSummary\":\"Annual report document retrieved. Insights extraction encountered a temporary issue.\",\"companyExpansion\":[]}";
            StockAnnualReportService.IsFailedOrFallbackInsights(fallback).Should().BeTrue();

            var success = "{\"executiveSummary\":\"Strong growth across business segments.\",\"companyExpansion\":[{\"title\":\"New Plant\"}]}";
            StockAnnualReportService.IsFailedOrFallbackInsights(success).Should().BeFalse();
        }

        [Fact]
        public void ProcessAndPrioritizeInsights_NormalizesReportingPeriod_ToMatchReportYear()
        {
            // Arrange: Items with hallucinated / legacy FY24 from template schema
            var insights = new AnnualReportInsightsDto
            {
                CompanyExpansion = new List<AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Bengaluru Phase - I Hotel Development",
                        Summary = "Opening first phase of Bengaluru hotel with 238 keys.",
                        ReportingPeriod = "FY24" // Artifact from prompt schema
                    }
                },
                StrategicPartnerships = new List<AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Marriott Brand Sign-on for Bengaluru",
                        Summary = "Signed Marriott's Westin brand.",
                        ReportingPeriod = "FY24"
                    }
                }
            };

            // Act: Process with 2026 annual report year
            StockAnnualReportService.ProcessAndPrioritizeInsights(insights, "2026");

            // Assert: reportingPeriod should now be normalized to FY26
            insights.CompanyExpansion[0].ReportingPeriod.Should().Be("FY26");
            insights.StrategicPartnerships[0].ReportingPeriod.Should().Be("FY26");
            insights.TopHighImpactInsights[0].ReportingPeriod.Should().Be("FY26");
        }

        [Fact]
        public async Task LiveTest_RealDatabase_RailTel_Healing()
        {
            var options = new DbContextOptionsBuilder<StockLens_Infrastructure.DataContext.StockLensDataContext>()
                .UseSqlServer("Server=DESKTOP-J2CVHIQ;Database=StockLensTestDB;Trusted_Connection=True;Encrypt=False;")
                .Options;

            using var dbContext = new StockLens_Infrastructure.DataContext.StockLensDataContext(options);
            var stockRepo = new StockLens_Infrastructure.Repositories.StockRepository(dbContext);
            var annualReportRepo = new StockLens_Infrastructure.Repositories.StockAnnualReportRepository(dbContext);

            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var screenerClient = new ScreenerClient(httpClient, Microsoft.Extensions.Options.Options.Create(new ScreenerSettings()), NullLogger<ScreenerClient>.Instance);

            var apiKey = GetGeminiApiKey();
            var geminiSettings = Microsoft.Extensions.Options.Options.Create(new GeminiSettings
            {
                ApiKey = apiKey,
                ModelName = "gemini-flash-lite-latest",
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

            // Execute service for RAILTEL without refresh flag - should auto-detect old failure and heal it!
            var response = await service.GetAnnualReportsBySymbolAsync("RAILTEL", refresh: false);

            response.Should().NotBeNull();
            response.ExtractionStatus.Should().Be("Completed");
            response.Insights.Should().NotBeNull();
            response.Insights!.ExecutiveSummary.Should().NotContain("temporary issue");
            response.Insights.ExecutiveSummary.Should().Contain("RailTel");
            response.TotalCount.Should().BeGreaterThan(0);
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
            var insights = JsonSerializer.Deserialize<AnnualReportInsightsDto>(json, new JsonSerializerOptions
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
            var insights = new AnnualReportInsightsDto
            {
                CompanyExpansion = new List<AnnualReportInsightItemDto>
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
                CapacityExpansion = new List<AnnualReportInsightItemDto>
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
            var insights = new AnnualReportInsightsDto
            {
                CompanyExpansion = new List<AnnualReportInsightItemDto>
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
            var insights = new AnnualReportInsightsDto
            {
                ManagementAndWorkforce = new List<AnnualReportInsightItemDto>
                {
                    new()
                    {
                        Title = "Routine Staff Training Initiatives",
                        Summary = "Conducted routine corporate compliance training for employees across regional hubs.",
                        Category = "Management & Workforce"
                    }
                },
                CompanyExpansion = new List<AnnualReportInsightItemDto>
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
            var insights = JsonSerializer.Deserialize<AnnualReportInsightsDto>(legacyJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            insights.Should().NotBeNull();
            StockAnnualReportService.ProcessAndPrioritizeInsights(insights!);

            // Assert
            insights!.CompanyExpansion.Should().HaveCount(1);
            insights.CompanyExpansion[0].Summary.Should().Contain("warehouse in Bhiwandi");
        }

        private static string GetGeminiApiKey()
        {
            var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                return envKey;
            }

            try
            {
                var secretPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "UserSecrets", "stocklens-api-e6a839f1-469b-4682-841f-823908c69134", "secrets.json");

                if (File.Exists(secretPath))
                {
                    var secretJson = File.ReadAllText(secretPath);
                    using var doc = JsonDocument.Parse(secretJson);
                    if (doc.RootElement.TryGetProperty("Gemini:ApiKey", out var keyElem))
                    {
                        return keyElem.GetString() ?? string.Empty;
                    }

                    if (doc.RootElement.TryGetProperty("Gemini", out var geminiSection) &&
                        geminiSection.TryGetProperty("ApiKey", out var nestedKey))
                    {
                        return nestedKey.GetString() ?? string.Empty;
                    }
                }
            }
            catch
            {
                // Fallback gracefully
            }

            return string.Empty;
        }
    }
}
