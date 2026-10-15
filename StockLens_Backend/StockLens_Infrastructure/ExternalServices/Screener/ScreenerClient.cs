using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLens_Infrastructure.ExternalServices.Screener.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.Screener
{
    public class ScreenerClient : IScreenerClient
    {
        private readonly HttpClient _httpClient;
        private readonly ScreenerSettings _settings;
        private readonly ILogger<ScreenerClient> _logger;

        private static readonly Regex DocumentSectionRegex = new(
            @"<section\b[^>]*id=""documents""[^>]*>(?<content>.*?)</section>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex SubSectionRegex = new(
            @"<h3>(?<title>[^<]+)</h3>\s*<div[^>]*>\s*<ul[^>]*>(?<items>.*?)</ul>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex ListItemRegex = new(
            @"<li[^>]*>\s*<a[^>]*href=""(?<url>[^""]+)""[^>]*>(?<title>[^<]+)</a>(?:\s*<div[^>]*>\s*<span[^>]*>(?<meta>[^<]+)</span>)?",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex YearExtractRegex = new(
            @"\b(19\d{2}|20\d{2})\b",
            RegexOptions.Compiled);

        public ScreenerClient(
            HttpClient httpClient,
            IOptions<ScreenerSettings> settings,
            ILogger<ScreenerClient> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;

            if (_httpClient.BaseAddress == null)
            {
                var baseUri = string.IsNullOrWhiteSpace(_settings.BaseUrl)
                    ? "https://www.screener.in/"
                    : _settings.BaseUrl.TrimEnd('/') + "/";
                _httpClient.BaseAddress = new Uri(baseUri);
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
            {
                _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            }
        }

        public async Task<IReadOnlyList<ScreenerDocumentItem>> GetAnnualReportsAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var allDocs = await GetAllDocumentsAsync(symbol, cancellationToken);
            return allDocs.Where(d => d.DocumentType == ScreenerDocumentType.AnnualReport).ToList();
        }

        public async Task<IReadOnlyList<ScreenerDocumentItem>> GetConcallsAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var allDocs = await GetAllDocumentsAsync(symbol, cancellationToken);
            return allDocs.Where(d => d.DocumentType == ScreenerDocumentType.ConcallTranscript 
                                   || d.DocumentType == ScreenerDocumentType.ConcallPpt 
                                   || d.DocumentType == ScreenerDocumentType.ConcallAudio).ToList();
        }

        public async Task<IReadOnlyList<ScreenerDocumentItem>> GetAllDocumentsAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Array.Empty<ScreenerDocumentItem>();
            }

            var cleanSymbol = symbol.Trim().ToUpperInvariant();
            var path = $"company/{Uri.EscapeDataString(cleanSymbol)}/";
            var results = new List<ScreenerDocumentItem>();

            try
            {
                _logger.LogInformation("Fetching documents from Screener for symbol: {Symbol}", cleanSymbol);

                using var response = await _httpClient.GetAsync(path, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Screener returned status {StatusCode} for symbol {Symbol}", response.StatusCode, cleanSymbol);
                    return results;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var documentsSection = DocumentSectionRegex.Match(html);

                var searchScope = documentsSection.Success ? documentsSection.Groups["content"].Value : html;

                var subSections = SubSectionRegex.Matches(searchScope);
                foreach (Match subSection in subSections)
                {
                    var sectionTitle = subSection.Groups["title"].Value.Trim().ToLowerInvariant();
                    var itemsContent = subSection.Groups["items"].Value;

                    var docType = ScreenerDocumentType.Other;
                    if (sectionTitle.Contains("annual report"))
                    {
                        docType = ScreenerDocumentType.AnnualReport;
                    }
                    else if (sectionTitle.Contains("concall"))
                    {
                        docType = ScreenerDocumentType.ConcallTranscript;
                    }
                    else if (sectionTitle.Contains("credit rating"))
                    {
                        docType = ScreenerDocumentType.CreditRating;
                    }

                    var itemMatches = ListItemRegex.Matches(itemsContent);
                    foreach (Match item in itemMatches)
                    {
                        var url = item.Groups["url"].Value.Trim();
                        var title = item.Groups["title"].Value.Trim();
                        var meta = item.Groups["meta"].Value.Trim();

                        if (string.IsNullOrWhiteSpace(url))
                            continue;

                        // Resolve relative URLs if any
                        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            url = new Uri(_httpClient.BaseAddress!, url).ToString();
                        }

                        // Determine Year
                        string? year = null;
                        var yearMatch = YearExtractRegex.Match(title);
                        if (yearMatch.Success)
                        {
                            year = yearMatch.Value;
                        }

                        // Determine source (e.g., "from bse", "from nse")
                        string? source = null;
                        if (!string.IsNullOrWhiteSpace(meta))
                        {
                            source = meta.Replace("from", "", StringComparison.OrdinalIgnoreCase).Trim().ToUpperInvariant();
                        }
                        else if (url.Contains("bseindia.com", StringComparison.OrdinalIgnoreCase))
                        {
                            source = "BSE";
                        }
                        else if (url.Contains("nseindia.com", StringComparison.OrdinalIgnoreCase))
                        {
                            source = "NSE";
                        }

                        results.Add(new ScreenerDocumentItem
                        {
                            Title = title,
                            Year = year,
                            Url = url,
                            Source = source,
                            DocumentType = docType
                        });
                    }
                }

                // Fallback: If section regex didn't catch due to layout changes, find all direct Annual Report links
                if (!results.Any(r => r.DocumentType == ScreenerDocumentType.AnnualReport))
                {
                    var fallbackRegex = new Regex(
                        @"<a\s+[^>]*href=""(?<url>[^""]+)""[^>]*>\s*(?<title>Annual\s+Report\s+(?<year>\d{4}))\s*</a>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    var fallbackMatches = fallbackRegex.Matches(html);
                    foreach (Match m in fallbackMatches)
                    {
                        var url = m.Groups["url"].Value.Trim();
                        var title = m.Groups["title"].Value.Trim();
                        var year = m.Groups["year"].Value.Trim();

                        if (!results.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new ScreenerDocumentItem
                            {
                                Title = title,
                                Year = year,
                                Url = url,
                                Source = url.Contains("bseindia", StringComparison.OrdinalIgnoreCase) ? "BSE" : "NSE",
                                DocumentType = ScreenerDocumentType.AnnualReport
                            });
                        }
                    }
                }

                _logger.LogInformation("Successfully parsed {Count} documents from Screener for {Symbol}", results.Count, cleanSymbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching/parsing documents from Screener for symbol: {Symbol}", cleanSymbol);
            }

            return results;
        }
    }
}
