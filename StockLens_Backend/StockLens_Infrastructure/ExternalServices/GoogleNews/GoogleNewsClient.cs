using Microsoft.Extensions.Logging;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
                //var query = Uri.EscapeDataString($"{symbol} share price");
                var query = Uri.EscapeDataString($"{symbol} {companyName}");
                var rssUrl = $"https://news.google.com/rss/search?q={query}&hl=en-IN&gl=IN&ceid=IN:en";

                _logger.LogInformation("Fetching Google News RSS for {Symbol}: {Url}", symbol, rssUrl);

                var response = await _httpClient.GetAsync(rssUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Google News RSS returned status code {StatusCode} for {Symbol}", response.StatusCode, symbol);
                    return articles; // Return empty list
                }

                var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = XDocument.Parse(xmlContent);

                var items = doc.Descendants("item");

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

                    // Extract actual source from the title if available (Google News appends it often like "Headline - SourceName")
                    var sourceName = source;
                    if (title.Contains(" - "))
                    {
                        var parts = title.Split(" - ");
                        sourceName = parts.Last().Trim();
                        // Clean up title
                        title = string.Join(" - ", parts.Take(parts.Length - 1)).Trim();
                    }

                    // --- SMART NEWS CLASSIFIER ---
                    var titleLower = title.ToLowerInvariant();
                    string smartCategory = "Market News";

                    if (new[] { "resign", "step down", "quits", "fired", "sacked", "ousted", "fraud", "scam", "sebi", "cbi", "ed raid", "it raid", "auditor quits", "default", "bankrupt", "promoter sells" }.Any(k => titleLower.Contains(k)))
                        smartCategory = "Key Update";

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

                var sortedArticles = articles
                    .OrderByDescending(a => a.PublishedAt)
                    .ToList();

                var distinctArticles = new List<IndianApiStandardArticle>();
                
                foreach (var article in sortedArticles)
                {
                    bool isDuplicate = false;
                    foreach (var existing in distinctArticles)
                    {
                        if (article.Category != "Market News" && article.Category == existing.Category && article.PublishedAt.Date == existing.PublishedAt.Date)
                        {
                            isDuplicate = true;
                            break;
                        }
                        
                        var words1 = article.Title.ToLower().Split(new[] { ' ', '-', ':', ',' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 4);
                        var words2 = existing.Title.ToLower().Split(new[] { ' ', '-', ':', ',' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 4);
                        if (words1.Intersect(words2).Count() >= 3)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }

                    if (!isDuplicate)
                    {
                        distinctArticles.Add(article);
                    }
                }

                // 1 Day Filter Logic for Normal News, 15 Day Filter for Key Updates
                var recentNews = distinctArticles.Where(a => 
                    (DateTime.UtcNow - a.PublishedAt).TotalDays <= 1 || 
                    (a.Category == "Key Update" && (DateTime.UtcNow - a.PublishedAt).TotalDays <= 15)
                ).ToList();
                
                // Fallback: If not enough recent news, take the top 5 absolute latest available
                var finalNews = recentNews.Count >= 5 ? recentNews.Take(5).ToList() : distinctArticles.Take(5).ToList();

                // Out of the final 5, force any Key Updates to the very top
                return finalNews
                    .OrderByDescending(a => a.Category == "Key Update" ? 1 : 0)
                    .ThenByDescending(a => a.PublishedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching or parsing Google News RSS for {Symbol}", symbol);
            }

            return articles;
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
