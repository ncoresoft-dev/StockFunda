using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockLens_BusinessLayer.DTOs
{
    public class AnnualReportDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Year { get; set; }
        public string PdfUrl { get; set; } = string.Empty;
        public string? Source { get; set; }
    }

    public class ManagementVisionDto
    {
        public string? CeoMessage { get; set; }
        public string? FutureGrowthGuidance { get; set; }
        public List<string> StrategicPriorities { get; set; } = new();
    }

    public class SegmentHighlightDto
    {
        public string Segment { get; set; } = string.Empty;
        public string? Growth { get; set; }
        public string? Summary { get; set; }
    }

    /// <summary>
    /// Structured business insight extracted from an Annual Report PDF.
    /// Captures concrete growth initiatives, capex, contracts, subsidies, partnerships, and risks with source traceability.
    /// </summary>
    [JsonConverter(typeof(AnnualReportInsightItemConverter))]
    public class AnnualReportInsightItemDto
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? DisplayGroup { get; set; }
        public string? ReportingPeriod { get; set; }
        public string? Status { get; set; } // Planned | Approved | Ongoing | Completed
        public decimal? FinancialValue { get; set; }
        public string? FinancialValueUnit { get; set; } // Cr | Lakh | $M | %
        public string? ExpectedBenefit { get; set; }
        public string? ExpectedDate { get; set; }
        public int? SourcePage { get; set; }
        public string? SourceText { get; set; }
        public int PriorityScore { get; set; }
    }

    /// <summary>
    /// JsonConverter to seamlessly support both new structured insight objects and legacy plain-string array formats.
    /// </summary>
    public class AnnualReportInsightItemConverter : JsonConverter<AnnualReportInsightItemDto>
    {
        public override AnnualReportInsightItemDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString() ?? string.Empty;
                var colonIndex = text.IndexOf(':');
                var title = colonIndex > 0 && colonIndex < 80 ? text[..colonIndex].Trim() : (text.Length > 80 ? text[..80].Trim() + "..." : text);
                return new AnnualReportInsightItemDto
                {
                    Title = title,
                    Summary = text,
                    Category = "General"
                };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var jsonDoc = JsonDocument.ParseValue(ref reader);
                var root = jsonDoc.RootElement;

                var item = new AnnualReportInsightItemDto();

                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                    item.Title = titleProp.GetString() ?? string.Empty;

                if (root.TryGetProperty("summary", out var summaryProp) && summaryProp.ValueKind == JsonValueKind.String)
                    item.Summary = summaryProp.GetString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(item.Summary))
                {
                    item.Title = item.Summary.Length > 70 ? item.Summary[..70].Trim() + "..." : item.Summary;
                }

                if (root.TryGetProperty("category", out var categoryProp) && categoryProp.ValueKind == JsonValueKind.String)
                    item.Category = categoryProp.GetString() ?? string.Empty;

                if (root.TryGetProperty("displayGroup", out var dgProp) && dgProp.ValueKind == JsonValueKind.String)
                    item.DisplayGroup = dgProp.GetString();

                if (root.TryGetProperty("reportingPeriod", out var rpProp) && rpProp.ValueKind == JsonValueKind.String)
                    item.ReportingPeriod = rpProp.GetString();

                if (root.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String)
                    item.Status = statusProp.GetString();

                if (root.TryGetProperty("financialValue", out var fvProp))
                {
                    if (fvProp.ValueKind == JsonValueKind.Number && fvProp.TryGetDecimal(out var dVal))
                        item.FinancialValue = dVal;
                    else if (fvProp.ValueKind == JsonValueKind.String && decimal.TryParse(fvProp.GetString()?.Replace(",", "").Trim(), out var pVal))
                        item.FinancialValue = pVal;
                }

                if (root.TryGetProperty("financialValueUnit", out var fvuProp) && fvuProp.ValueKind == JsonValueKind.String)
                    item.FinancialValueUnit = fvuProp.GetString();

                if (root.TryGetProperty("expectedBenefit", out var ebProp) && ebProp.ValueKind == JsonValueKind.String)
                    item.ExpectedBenefit = ebProp.GetString();

                if (root.TryGetProperty("expectedDate", out var edProp) && edProp.ValueKind == JsonValueKind.String)
                    item.ExpectedDate = edProp.GetString();

                if (root.TryGetProperty("sourcePage", out var spProp))
                {
                    if (spProp.ValueKind == JsonValueKind.Number && spProp.TryGetInt32(out var pNum))
                        item.SourcePage = pNum;
                    else if (spProp.ValueKind == JsonValueKind.String)
                    {
                        var str = spProp.GetString();
                        if (!string.IsNullOrEmpty(str))
                        {
                            var digitsOnly = System.Text.RegularExpressions.Regex.Match(str, @"\d+");
                            if (digitsOnly.Success && int.TryParse(digitsOnly.Value, out var parsedPage))
                                item.SourcePage = parsedPage;
                        }
                    }
                }

                if (root.TryGetProperty("sourceText", out var stProp) && stProp.ValueKind == JsonValueKind.String)
                    item.SourceText = stProp.GetString();

                if (root.TryGetProperty("priorityScore", out var psProp) && psProp.ValueKind == JsonValueKind.Number && psProp.TryGetInt32(out var psVal))
                    item.PriorityScore = psVal;

                return item;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, AnnualReportInsightItemDto value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("title", value.Title);
            writer.WriteString("summary", value.Summary);
            writer.WriteString("category", value.Category);
            
            if (!string.IsNullOrWhiteSpace(value.DisplayGroup))
                writer.WriteString("displayGroup", value.DisplayGroup);

            if (!string.IsNullOrWhiteSpace(value.ReportingPeriod))
                writer.WriteString("reportingPeriod", value.ReportingPeriod);

            if (!string.IsNullOrWhiteSpace(value.Status))
                writer.WriteString("status", value.Status);

            if (value.FinancialValue.HasValue)
                writer.WriteNumber("financialValue", value.FinancialValue.Value);

            if (!string.IsNullOrWhiteSpace(value.FinancialValueUnit))
                writer.WriteString("financialValueUnit", value.FinancialValueUnit);

            if (!string.IsNullOrWhiteSpace(value.ExpectedBenefit))
                writer.WriteString("expectedBenefit", value.ExpectedBenefit);

            if (!string.IsNullOrWhiteSpace(value.ExpectedDate))
                writer.WriteString("expectedDate", value.ExpectedDate);

            if (value.SourcePage.HasValue)
                writer.WriteNumber("sourcePage", value.SourcePage.Value);

            if (!string.IsNullOrWhiteSpace(value.SourceText))
                writer.WriteString("sourceText", value.SourceText);

            writer.WriteNumber("priorityScore", value.PriorityScore);
            writer.WriteEndObject();
        }
    }

    public class AnnualReportInsightsDto
    {
        public string? ExecutiveSummary { get; set; }

        /// <summary>
        /// Top 5-6 high-impact insights ranked deterministically by relevance and business significance.
        /// </summary>
        public List<AnnualReportInsightItemDto> TopHighImpactInsights { get; set; } = new();

        // 12 Annual Report Business Dimensions (Concrete Structured Insights)
        public List<AnnualReportInsightItemDto> CompanyExpansion { get; set; } = new();
        public List<AnnualReportInsightItemDto> CapacityExpansion { get; set; } = new();
        public List<AnnualReportInsightItemDto> MajorOrdersAndContracts { get; set; } = new();
        public List<AnnualReportInsightItemDto> GovernmentBenefits { get; set; } = new();
        public List<AnnualReportInsightItemDto> StrategicPartnerships { get; set; } = new();
        public List<AnnualReportInsightItemDto> NewBusinessAndProducts { get; set; } = new();
        public List<AnnualReportInsightItemDto> AcquisitionsAndInvestments { get; set; } = new();
        public List<AnnualReportInsightItemDto> MarketExpansion { get; set; } = new();
        public List<AnnualReportInsightItemDto> ManagementAndWorkforce { get; set; } = new();
        public List<AnnualReportInsightItemDto> FutureGrowthPlans { get; set; } = new();
        public List<AnnualReportInsightItemDto> GovernmentProjectsAndPolicies { get; set; } = new();
        public List<AnnualReportInsightItemDto> MajorBusinessRisks { get; set; } = new();

        // Backward compatibility
        public ManagementVisionDto? ManagementVision { get; set; }
        public List<string> KeyHighlights { get; set; } = new();
        public List<string> KeyRisks { get; set; } = new();
        public List<SegmentHighlightDto> SegmentPerformance { get; set; } = new();
        public string? AuditorRemarks { get; set; }
    }

    public class StockAnnualReportsResponseDto
    {
        public int? StockId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public int TotalCount { get; set; }
        public IReadOnlyList<AnnualReportDto> Reports { get; set; } = new List<AnnualReportDto>();

        /// <summary>
        /// ExtractionStatus: "Completed", "Processing", "Pending", "NotAvailable", "Failed".
        /// </summary>
        public string ExtractionStatus { get; set; } = "Pending";
        public AnnualReportInsightsDto? Insights { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? Message { get; set; }
    }

    public class AdminSaveAndExtractReportRequestDto
    {
        public string Symbol { get; set; } = string.Empty;
        public string? Year { get; set; }
        public string? Title { get; set; }
        public string PdfUrl { get; set; } = string.Empty;
        public string? Source { get; set; } = "BSE";
    }

    public class AnnualReportDirectoryItemDto
    {
        public int StockId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string? Industry { get; set; }
        public string? Sector { get; set; }
        public string? Year { get; set; }
        public string? Title { get; set; }
        public string? PdfUrl { get; set; }
        public string? Source { get; set; }
        public string ExtractionStatus { get; set; } = "Pending";
        public DateTime? CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
