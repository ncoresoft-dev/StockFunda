using Microsoft.Extensions.Logging;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace StockLens_Infrastructure.ExternalServices.GoogleNews
{
    public class GoogleNewsClient : IGoogleNewsClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GoogleNewsClient> _logger;

        public GoogleNewsClient(HttpClient httpClient, ILogger<GoogleNewsClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<IReadOnlyList<IndianApiStandardArticle>> GetStockNewsAsync(
            string symbol,
            string? companyName = null,
            CancellationToken cancellationToken = default)
        {
            var articles = new List<IndianApiStandardArticle>();

            try
            {
                // Clean company name (remove legal suffixes like Limited, Ltd, etc.)
                var cleanCompanyName = companyName?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(cleanCompanyName))
                {
                    cleanCompanyName = Regex.Replace(
                        cleanCompanyName,
                        @"\b(Limited|Ltd\.?|L\.?P\.?|Corp\.?|Corporation|Inc\.?)\b",
                        "",
                        RegexOptions.IgnoreCase).Trim();
                }

                // Wrap in quotes to force Google News to match the exact entity name
                string query;
                if (!string.IsNullOrWhiteSpace(cleanCompanyName) && !cleanCompanyName.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                {
                    query = Uri.EscapeDataString($"\"{cleanCompanyName}\"");
                }
                else
                {
                    query = Uri.EscapeDataString($"\"{symbol}\"");
                }

                var rssUrl = $"https://news.google.com/rss/search?q={query}&hl=en-IN&gl=IN&ceid=IN:en";

                _logger.LogInformation("Fetching Google News RSS for {Symbol} (Query: {Query}): {Url}", symbol, query, rssUrl);

                var response = await _httpClient.GetAsync(rssUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Google News RSS returned status code {StatusCode} for {Symbol}", response.StatusCode, symbol);
                    return articles; // Return empty list
                }

                var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = XDocument.Parse(xmlContent);
                var items = doc.Descendants("item");

                var junkSources = new[]
                {
                    "ad-hoc-news", "stock titan", "siam.in", "techgraph", "univest",
                    "kalkine", "finanzen", "marketscreener", "ticker report",
                    "defense world", "zacks", "investorplace", "gurufocus", "marketbeat"
                };

                var spamPatterns = new[]
                {
                    "share price - live",
                    "live nse/bse",
                    "forecast — price target",
                    "forecast - price target",
                    "price to sales forward",
                    "option chain",
                    "technical analysis and expert forecasts",
                    "share price news: fundamentals",
                    "share price nse",
                    "share price bse"
                };

                // Stopwords for deduplication comparison (ignore common generic stock terms)
                var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "share", "shares", "price", "prices", "stock", "stocks", "today", "market", "markets",
                    "limited", "target", "india", "live", "about", "after", "before", "under", "ahead", "outlook"
                };
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    stopWords.Add(symbol.ToLowerInvariant());
                }
                if (!string.IsNullOrWhiteSpace(cleanCompanyName))
                {
                    foreach (var w in cleanCompanyName.ToLowerInvariant().Split(new[] { ' ', '.', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (w.Length > 2) stopWords.Add(w);
                    }
                }

                foreach (var item in items)
                {
                    var title = item.Element("title")?.Value?.Trim() ?? string.Empty;
                    var link = item.Element("link")?.Value?.Trim() ?? string.Empty;
                    var pubDateStr = item.Element("pubDate")?.Value?.Trim();
                    var description = item.Element("description")?.Value?.Trim() ?? string.Empty;
                    var source = item.Element("source")?.Value?.Trim() ?? "Google News";
                    var guid = item.Element("guid")?.Value?.Trim() ?? link;

                    DateTime publishedAt = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(pubDateStr) && DateTime.TryParse(pubDateStr, out var parsedDate))
                    {
                        publishedAt = parsedDate.ToUniversalTime();
                    }

                    // Extract actual source from the title if available (Google News appends it like "Headline - SourceName")
                    var sourceName = source;
                    var lastDashIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (lastDashIndex > 0)
                    {
                        sourceName = title.Substring(lastDashIndex + 3).Trim();
                        title = title.Substring(0, lastDashIndex).Trim();
                    }

                    var titleLower = title.ToLowerInvariant();
                    var srcLower = sourceName.ToLowerInvariant();
                    var linkLower = link.ToLowerInvariant();

                    // 1. Filter out known foreign aggregators & junk bot scrapers
                    if (junkSources.Any(j => srcLower.Contains(j) || linkLower.Contains(j)))
                    {
                        continue;
                    }

                    // 2. Filter out generic live ticker & chart spam pages
                    if (spamPatterns.Any(p => titleLower.Contains(p)))
                    {
                        continue;
                    }

                    // 3. Title relevance guard: Reject any article where the headline does not mention the company name or symbol
                    if (!IsArticleRelevant(title, symbol, cleanCompanyName))
                    {
                        continue;
                    }

                    // --- SMART NEWS CLASSIFIER ---
                    string smartCategory = "Market News";
                    if (new[] { 
                        "resign", "step down", "quits", "fired", "sacked", "ousted", 
                        "fraud", "scam", "sebi", "cbi", "ed raid", "it raid", "auditor quits", 
                        "default", "bankrupt", "promoter sells", "promoter stake", "stake sale", 
                        "stake acquisition", "acquisition", "acquire", "takeover", "merger", "order win" 
                    }.Any(k => titleLower.Contains(k)))
                    {
                        smartCategory = "Key Update";
                    }

                    var article = new IndianApiStandardArticle
                    {
                        ExternalNewsId = ComputeSha256Hash(guid),
                        Title = title,
                        Description = description,
                        Content = link,
                        SourceName = sourceName,
                        SourceUrl = link.Length > 400 ? link.Substring(0, 400) : link,
                        ImageUrl = "",
                        PublishedAt = publishedAt,
                        Category = smartCategory
                    };

                    articles.Add(article);
                }

                // Sort by PublishedAt descending
                var sortedArticles = articles
                    .OrderByDescending(a => a.PublishedAt)
                    .ToList();

                var distinctArticles = new List<IndianApiStandardArticle>();

                foreach (var article in sortedArticles)
                {
                    bool isDuplicate = false;
                    var aWords = article.Title.ToLowerInvariant()
                        .Split(new[] { ' ', '-', ':', ',', ';', '(', ')', '"', '\'', '/' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 3 && !stopWords.Contains(w))
                        .ToList();

                    foreach (var existing in distinctArticles)
                    {
                        // Duplicate check only applies if published on the exact same date
                        if (article.PublishedAt.Date == existing.PublishedAt.Date)
                        {
                            var exWords = existing.Title.ToLowerInvariant()
                                .Split(new[] { ' ', '-', ':', ',', ';', '(', ')', '"', '\'', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(w => w.Length > 3 && !stopWords.Contains(w))
                                .ToList();

                            var overlap = aWords.Intersect(exWords).Count();
                            if (overlap >= 3)
                            {
                                isDuplicate = true;
                                break;
                            }
                        }
                    }

                    if (!isDuplicate)
                    {
                        distinctArticles.Add(article);
                    }
                }

                // Selection: Prioritize top-tier Indian financial & national media if available (within last 7 days)
                var cutoff = DateTime.UtcNow.AddDays(-7);
                var recentPreferred = distinctArticles
                    .Where(a => GetSourcePriority(a.SourceName, a.Content) >= 1 && a.PublishedAt >= cutoff)
                    .OrderByDescending(a => a.PublishedAt)
                    .ToList();

                List<IndianApiStandardArticle> finalSelection;
                if (recentPreferred.Count >= 5)
                {
                    finalSelection = recentPreferred.Take(5).ToList();
                }
                else
                {
                    // If fewer than 5 preferred sources, take all preferred and backfill with freshest genuine articles
                    var filled = new List<IndianApiStandardArticle>(recentPreferred);
                    foreach (var other in distinctArticles.OrderByDescending(a => a.PublishedAt))
                    {
                        if (!filled.Any(f => f.ExternalNewsId == other.ExternalNewsId))
                        {
                            filled.Add(other);
                            if (filled.Count == 5) break;
                        }
                    }
                    finalSelection = filled.OrderByDescending(a => a.PublishedAt).ToList();
                }

                return finalSelection;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching or parsing Google News RSS for {Symbol}", symbol);
            }

            return articles;
        }

        private static int GetSourcePriority(string sourceName, string url)
        {
            var text = (sourceName + " " + url).ToLowerInvariant();

            // Tier 2: Top Indian Financial Media
            if (text.Contains("economic times") || text.Contains("economictimes") ||
                text.Contains("moneycontrol") ||
                text.Contains("livemint") || text.Contains("mint") ||
                text.Contains("ndtv profit") || text.Contains("ndtv") ||
                text.Contains("business today") || text.Contains("businesstoday") ||
                text.Contains("business standard") || text.Contains("business-standard") ||
                text.Contains("cnbc") || text.Contains("cnbctv18") ||
                text.Contains("zee business") || text.Contains("zeebiz") ||
                text.Contains("businessline") || text.Contains("thehindubusinessline") ||
                text.Contains("financial express") || text.Contains("financialexpress"))
            {
                return 2;
            }

            // Tier 1: Reputable General Indian National Media
            if (text.Contains("the hindu") || text.Contains("thehindu") ||
                text.Contains("indian express") || text.Contains("indianexpress") ||
                text.Contains("times of india") || text.Contains("timesofindia") ||
                text.Contains("hindustan times") || text.Contains("hindustantimes") ||
                text.Contains("deccan") || text.Contains("reuters") || text.Contains("bloomberg"))
            {
                return 1;
            }

            return 0;
        }

        private static bool IsArticleRelevant(string title, string symbol, string? cleanCompanyName)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;

            var titleLower = title.ToLowerInvariant();
            var symLower = symbol.ToLowerInvariant();

            // 1. Exact ticker symbol in title (e.g. "HDFCBANK", "BANDHANBNK", "RELIANCE")
            if (titleLower.Contains(symLower)) return true;

            // 2. Full clean company name in title (e.g. "hdfc bank", "bandhan bank", "tata motors")
            if (!string.IsNullOrWhiteSpace(cleanCompanyName))
            {
                var cleanLower = cleanCompanyName.ToLowerInvariant();
                if (cleanLower.Length >= 3 && titleLower.Contains(cleanLower)) return true;

                // 3. Significant keywords from company name (excluding generic words)
                var genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "bank", "india", "limited", "ltd", "corporation", "corp", "company", "co",
                    "industries", "industry", "services", "service", "technologies", "tech",
                    "holdings", "holding", "finance", "financial", "solutions", "enterprises",
                    "international", "global", "group"
                };

                var words = cleanLower.Split(new[] { ' ', '-', '.', '&', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 3 && !genericWords.Contains(w))
                    .ToList();

                if (words.Any(w => titleLower.Contains(w)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ComputeSha256Hash(string rawData)
        {
            if (string.IsNullOrEmpty(rawData)) return string.Empty;
            
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
