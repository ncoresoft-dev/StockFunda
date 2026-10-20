using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace StockLens_Infrastructure.ExternalServices.AnnualReportInsights
{
    public class AnnualReportInsightExtractor : IAnnualReportInsightExtractor
    {
        private readonly HttpClient _httpClient;
        private readonly GeminiSettings _settings;
        private readonly ILogger<AnnualReportInsightExtractor> _logger;

        public AnnualReportInsightExtractor(
            HttpClient httpClient,
            IOptions<GeminiSettings> settings,
            ILogger<AnnualReportInsightExtractor> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<string?> ExtractInsightsJsonAsync(string symbol, string pdfUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pdfUrl))
            {
                _logger.LogWarning("No PDF URL provided for symbol {Symbol}", symbol);
                return null;
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), $"ar_{symbol}_{Guid.NewGuid():N}.pdf");

            try
            {
                _logger.LogInformation("Downloading Annual Report PDF for {Symbol} from {Url} to temp file", symbol, pdfUrl);

                // 1. Download PDF to temp file with browser headers
                var downloaded = await DownloadPdfToTempFileAsync(pdfUrl, tempFilePath, cancellationToken);
                if (!downloaded || !File.Exists(tempFilePath))
                {
                    _logger.LogWarning("Failed to download PDF for symbol {Symbol}", symbol);
                    return BuildFallbackJson("Annual report document retrieved. Could not download the filing for AI processing; please download the official PDF directly.");
                }

                // 2. Extract high-priority text sections using PdfPig
                _logger.LogInformation("Extracting targeted text sections with PdfPig for {Symbol}", symbol);
                var targetedText = ExtractTargetedTextFromPdf(tempFilePath, symbol);

                if (string.IsNullOrWhiteSpace(targetedText) || targetedText.Length < 200)
                {
                    _logger.LogWarning("Insufficient readable text extracted from PDF for {Symbol}. May be scanned or image-only PDF.", symbol);
                    return BuildFallbackJson("Annual report is image-only or scanned. Please view the original PDF document.");
                }

                _logger.LogInformation("Extracted {CharCount} characters of targeted text for {Symbol}", targetedText.Length, symbol);

                // 3. Generate structured insights via Gemini API
                if (string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    _logger.LogWarning("Gemini API Key is not configured in user secrets / appsettings. Returning initial structured placeholder for {Symbol}.", symbol);
                    return BuildFallbackJson("Gemini API Key is not configured yet. Configure 'Gemini:ApiKey' in secret manager to enable AI insights.");
                }

                _logger.LogInformation("Calling Gemini AI to generate structured insights for {Symbol}", symbol);
                var insightsJson = await CallGeminiGenerateInsightsAsync(symbol, targetedText, cancellationToken);

                return insightsJson;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while extracting annual report insights for {Symbol}", symbol);
                return BuildFallbackJson("Annual report document retrieved. Insights extraction encountered a temporary issue.");
            }
            finally
            {
                // Always clean up temp file to keep disk and memory safe
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogDebug(cleanupEx, "Could not delete temp PDF file {Path}", tempFilePath);
                }
            }
        }

        private async Task<bool> DownloadPdfToTempFileAsync(string pdfUrl, string destinationPath, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pdfUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "application/pdf,*/*");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PDF download failed with status {StatusCode} for URL {Url}", response.StatusCode, pdfUrl);
                return false;
            }

            await using var streamToReadFrom = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var streamToWriteTo = File.Create(destinationPath);
            await streamToReadFrom.CopyToAsync(streamToWriteTo, cancellationToken);

            return true;
        }

        private string ExtractTargetedTextFromPdf(string pdfFilePath, string symbol)
        {
            var textBuilder = new StringBuilder();
            const int maxCharacters = 35000;

            using var document = PdfDocument.Open(pdfFilePath);
            var totalPages = document.NumberOfPages;
            _logger.LogInformation("PDF for {Symbol} has {TotalPages} total pages", symbol, totalPages);

            var highValueRegex = new Regex(
                @"\b(Management\s+Discussion|Chairman'?s\s+(Statement|Message|Speech)|CEO'?s\s+Statement|Managing\s+Director'?s\s+Statement|Letter\s+to\s+Shareholders|Business\s+Review|Operational\s+Review|Strategic\s+Review|At\s+a\s+Glance)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var dimensionRegex = new Regex(
                @"\b(Capacity\s+Expansion|Company\s+Expansion|Capital\s+Expenditure|Capex|Order\s+Book|Major\s+Orders|Major\s+Contracts|Government\s+Benefits|PLI\s+Scheme|Subsidies|Tax\s+Exemption|Strategic\s+Partnerships|Joint\s+Venture|New\s+Business|New\s+Products|Acquisitions|Investments|Market\s+Expansion|Workforce\s+Growth|Growth\s+Plans|Strategic\s+Priorities|Risks\s+and\s+Concerns|Business\s+Risks|Giga\s*factory|Gigafactory|Retail\s+Ventures|Retail\s+Stores|Jio\s+Platforms|5G\s+Network|Data\s+Center)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var noticeRegex = new Regex(
                @"\b(Notice\s+dated|Notice\s+of\s+the\s+\d+|NoticeSr\.|Annexures?\s+to\s+the\s+Notice|REMOTE\s+E-VOTING|Procedure\s+for\s+joining\s+the\s+AGM|Attendance\s+Slip|Proxy\s+Form|Withholding\s+tax\s+rates|e-voting\s+facility|Instructions\s+for\s+voting|Item\s+No\.\s+\d+.*Ordinary\s+Resolution)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // In Indian Annual Reports, MD&A, Chairman Message and strategic expansion are within the first 110 pages.
            // Statutory balance sheet notes and auditor accounts fill pages 120-500.
            int scanMaxPage = Math.Min(110, totalPages);
            var pageCache = new Dictionary<int, string>();
            var pageScores = new Dictionary<int, int>();

            for (int p = 1; p <= scanMaxPage; p++)
            {
                try
                {
                    var page = document.GetPage(p);
                    var pageText = page.Text;
                    if (string.IsNullOrWhiteSpace(pageText)) continue;

                    if (noticeRegex.IsMatch(pageText)) continue;

                    pageCache[p] = pageText;

                    int score = 0;
                    if (highValueRegex.IsMatch(pageText)) score += 100;
                    if (dimensionRegex.IsMatch(pageText)) score += 70;

                    if (score > 0)
                    {
                        pageScores[p] = Math.Max(pageScores.GetValueOrDefault(p, 0), score);
                        if (p + 1 <= scanMaxPage && !pageScores.ContainsKey(p + 1)) pageScores[p + 1] = 40;
                        if (p + 2 <= scanMaxPage && !pageScores.ContainsKey(p + 2)) pageScores[p + 2] = 30;
                    }
                }
                catch
                {
                    // Ignore page read error
                }
            }

            if (pageScores.Count == 0 && pageCache.Count > 0)
            {
                foreach (var p in pageCache.Keys.OrderBy(k => k).Take(14))
                {
                    pageScores[p] = 50;
                }
            }

            var selectedPages = pageScores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(14)
                .Select(kv => kv.Key)
                .OrderBy(p => p)
                .ToList();

            _logger.LogInformation("Selected {PageCount} targeted pages for AI insight generation for {Symbol}", selectedPages.Count, symbol);

            foreach (var pageNumber in selectedPages)
            {
                if (textBuilder.Length >= maxCharacters)
                    break;

                if (pageCache.TryGetValue(pageNumber, out var cachedText))
                {
                    textBuilder.AppendLine($"--- [Page {pageNumber}] ---");
                    textBuilder.AppendLine(cachedText);
                }
            }

            return textBuilder.ToString();
        }

        private async Task<string?> CallGeminiGenerateInsightsAsync(string symbol, string extractedText, CancellationToken cancellationToken)
        {
            var prompt = $@"
You are a senior equity research analyst extracting verifiable, factual business intelligence and company growth insights from the official Annual Report of {symbol}.

OBJECTIVE:
Extract concrete business developments that help investors understand future opportunities, expansion plans, capacity additions, contracts, government benefits, strategic partnerships, new products, and risks.
DO NOT repeat standard dashboard financial metrics (Revenue, Net Profit, ROE, ROCE, P/E, Cash Flow) unless directly tied to a specific project capex or contract value.

STRICT EXTRACTION & VERIFIABILITY RULES:
1. Extract facts ONLY and EXCLUSIVELY from the provided text below. Do NOT invent, assume, or extrapolate facts.
2. If specific data is not mentioned in the text for a category, return an empty array []. Never fabricate missing values.
3. Keep extraction detailed and actionable, but filter out low-value, routine, generic, or duplicate boilerplate.
4. Merge overlapping mentions of the same project across sections into a single comprehensive entry.
5. SOURCE TRACEABILITY:
   - Identify the source page from the closest preceding '--- [Page X] ---' marker.
   - Include a 1-2 sentence verbatim excerpt in 'sourceText' that validates the insight.
6. CLASSIFY STATUS ACCURATELY:
   - Use one of: 'Planned', 'Approved', 'Ongoing', 'Completed'. If not stated, use null.
   - Clearly distinguish between completed developments, ongoing projects, and proposed/targeted plans.
7. FINANCIAL VALUES:
   - Extract numeric values only if explicitly stated (e.g. capex of ₹10,000 Cr -> financialValue: 10000, financialValueUnit: 'Cr'). If undisclosed, leave null.

Return ONLY a valid, raw JSON object matching the exact schema below. Do not wrap in markdown or backticks.

SCHEMA:
{{
  ""executiveSummary"": ""Concise 2-3 sentence overview of corporate trajectory, major growth drivers, and strategic direction from the report."",
  ""companyExpansion"": [
    {{
      ""title"": ""Concise headline of expansion project"",
      ""summary"": ""Clear factual explanation of new factories, plants, stores, branches, offices, or facilities."",
      ""category"": ""Company Expansion"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Planned | Approved | Ongoing | Completed"",
      ""financialValue"": 1500,
      ""financialValueUnit"": ""Cr"",
      ""expectedBenefit"": ""Disclosed expected operational or market benefit"",
      ""expectedDate"": ""Q3 FY26"",
      ""sourcePage"": 42,
      ""sourceText"": ""Verbatim quote from annual report proving this point.""
    }}
  ],
  ""capacityExpansion"": [
    {{
      ""title"": ""Capacity addition / expansion project"",
      ""summary"": ""Existing vs planned production capacity, capex allocated, and commissioning schedules."",
      ""category"": ""Capacity Expansion"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Planned | Approved | Ongoing | Completed"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""majorOrdersAndContracts"": [
    {{
      ""title"": ""Contract / order award headline"",
      ""summary"": ""Significant government or private contract, customer name, order value, and execution period."",
      ""category"": ""Major Orders & Contracts"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Completed | Ongoing | Approved"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""governmentBenefits"": [
    {{
      ""title"": ""Benefit / incentive scheme title"",
      ""summary"": ""Approved subsidies, tax holidays/exemptions, PLI benefits, or state incentives."",
      ""category"": ""Government Benefits"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Approved | Ongoing"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""strategicPartnerships"": [
    {{
      ""title"": ""Partner name & alliance objective"",
      ""summary"": ""Major strategic partnership, joint venture, consortium, or technology collaboration."",
      ""category"": ""Strategic Partnerships"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Ongoing | Completed | Approved"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""newBusinessAndProducts"": [
    {{
      ""title"": ""New product / service / business segment"",
      ""summary"": ""Newly launched products, innovative services, new business segments, or R&D breakthroughs."",
      ""category"": ""New Business & Products"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Completed | Ongoing | Planned"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""acquisitionsAndInvestments"": [
    {{
      ""title"": ""Acquisition / investment target"",
      ""summary"": ""M&A deals, equity investments, newly acquired subsidiaries, and strategic stakes."",
      ""category"": ""Acquisitions & Investments"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Completed | Ongoing | Approved"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""marketExpansion"": [
    {{
      ""title"": ""Market / territory / retail footprint expansion"",
      ""summary"": ""New countries, states, cities entered, export growth, or retail network expansion."",
      ""category"": ""Market Expansion"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Completed | Ongoing | Planned"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""managementAndWorkforce"": [
    {{
      ""title"": ""Key leadership appointment / talent initiative"",
      ""summary"": ""Key executive appointments (CEO/CFO/CTO/Board), major workforce scaling, or strategic org changes."",
      ""category"": ""Management & Workforce"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Completed | Ongoing"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""futureGrowthPlans"": [
    {{
      ""title"": ""Management growth target / strategic initiative"",
      ""summary"": ""Multi-year strategic roadmap, future capex targets, clean energy, or technology milestones."",
      ""category"": ""Future Growth Plans"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Planned | Ongoing"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""governmentProjectsAndPolicies"": [
    {{
      ""title"": ""National scheme / policy alignment"",
      ""summary"": ""Government policies, national missions, or infrastructure schemes directly benefiting the business."",
      ""category"": ""Govt Projects & Policies"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Ongoing | Planned"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ],
  ""majorBusinessRisks"": [
    {{
      ""title"": ""Identified material risk & potential impact"",
      ""summary"": ""Key operational, financial, regulatory, legal, or supply chain risks and disclosed mitigations."",
      ""category"": ""Major Business Risks"",
      ""reportingPeriod"": ""FY24"",
      ""status"": ""Ongoing"",
      ""financialValue"": null,
      ""financialValueUnit"": null,
      ""expectedBenefit"": null,
      ""expectedDate"": null,
      ""sourcePage"": null,
      ""sourceText"": null
    }}
  ]
}}

Annual Report Extracted Text:
{extractedText}
";

            var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/models/{_settings.ModelName}:generateContent?key={_settings.ApiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.2
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Simple retry policy for transient 429 rate limits
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var response = await _httpClient.PostAsync(endpoint, jsonContent, cancellationToken);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Gemini API rate limit (429) hit. Attempt {Attempt} of 3. Waiting 4 seconds...", attempt);
                        await Task.Delay(4000, cancellationToken);
                        continue;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                        response.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                        response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
                    {
                        _logger.LogWarning("Gemini API temporary server issue ({StatusCode}). Attempt {Attempt} of 3. Waiting 3 seconds...", response.StatusCode, attempt);
                        await Task.Delay(3000, cancellationToken);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errText = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Gemini API returned error status {StatusCode}: {Error}", response.StatusCode, errText);
                        return BuildFallbackJson($"AI insights generation returned status {response.StatusCode}. Official Annual Report document is available for download.");
                    }

                    var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(responseString);

                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0 &&
                        candidates[0].TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0 &&
                        parts[0].TryGetProperty("text", out var textElement))
                    {
                        var rawText = textElement.GetString()?.Trim();
                        if (string.IsNullOrWhiteSpace(rawText))
                            return null;

                        // Clean any markdown formatting if present
                        if (rawText.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                        {
                            rawText = rawText.Substring(7);
                        }
                        if (rawText.StartsWith("```", StringComparison.OrdinalIgnoreCase))
                        {
                            rawText = rawText.Substring(3);
                        }
                        if (rawText.EndsWith("```", StringComparison.OrdinalIgnoreCase))
                        {
                            rawText = rawText.Substring(0, rawText.Length - 3);
                        }

                        return rawText.Trim();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gemini API call attempt {Attempt} failed", attempt);
                    if (attempt == 3) throw;
                    await Task.Delay(2000, cancellationToken);
                }
            }

            return BuildFallbackJson("AI insights generation is temporarily unavailable. Official Annual Report document is available for download.");
        }

        private static string BuildFallbackJson(string summary)
        {
            var fallback = new
            {
                executiveSummary = summary,
                companyExpansion = Array.Empty<object>(),
                capacityExpansion = Array.Empty<object>(),
                majorOrdersAndContracts = Array.Empty<object>(),
                governmentBenefits = Array.Empty<object>(),
                strategicPartnerships = Array.Empty<object>(),
                newBusinessAndProducts = Array.Empty<object>(),
                acquisitionsAndInvestments = Array.Empty<object>(),
                marketExpansion = Array.Empty<object>(),
                managementAndWorkforce = Array.Empty<object>(),
                futureGrowthPlans = Array.Empty<object>(),
                governmentProjectsAndPolicies = Array.Empty<object>(),
                majorBusinessRisks = Array.Empty<object>(),

                // Backward compatibility
                keyHighlights = Array.Empty<string>(),
                keyRisks = Array.Empty<string>(),
                segmentPerformance = Array.Empty<object>()
            };

            return JsonSerializer.Serialize(fallback);
        }
    }
}
