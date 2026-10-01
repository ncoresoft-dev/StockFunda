using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.YahooFinanceApi
{
    public class YahooFinanceClient : IYahooFinanceClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<YahooFinanceClient> _logger;

        public YahooFinanceClient(HttpClient httpClient, ILogger<YahooFinanceClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.BaseAddress = new Uri("https://query2.finance.yahoo.com");
            // Add required headers to prevent blocking
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public async Task<(string? CompanyName, string? Industry)> GetCompanyDetailsAsync(string symbol, string exchange, CancellationToken cancellationToken = default)
        {
            try
            {
                var suffix = exchange.Equals("BSE", StringComparison.OrdinalIgnoreCase) ? ".BO" : ".NS";
                var searchSymbol = $"{symbol.Trim().ToUpperInvariant()}{suffix}";

                var endpoint = $"/v1/finance/search?q={Uri.EscapeDataString(searchSymbol)}&quotesCount=1";
                
                _logger.LogInformation("Fetching real company name for {Symbol} via Yahoo Finance API", searchSymbol);
                var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var document = JsonDocument.Parse(json);
                    
                    if (document.RootElement.TryGetProperty("quotes", out var quotesElement) && 
                        quotesElement.ValueKind == JsonValueKind.Array && 
                        quotesElement.GetArrayLength() > 0)
                    {
                        var firstQuote = quotesElement[0];
                        string? companyName = null;
                        string? industry = null;

                        if (firstQuote.TryGetProperty("longname", out var longNameElement))
                        {
                            companyName = longNameElement.GetString();
                        }
                        else if (firstQuote.TryGetProperty("shortname", out var shortNameElement))
                        {
                            companyName = shortNameElement.GetString();
                        }

                        if (firstQuote.TryGetProperty("industryDisp", out var industryElement) || 
                            firstQuote.TryGetProperty("industry", out industryElement))
                        {
                            industry = industryElement.GetString();
                        }

                        return (companyName, industry);
                    }
                }
                else
                {
                    _logger.LogWarning("Yahoo Finance API returned status code {StatusCode} for {Symbol}", response.StatusCode, searchSymbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching company name from Yahoo Finance API for {Symbol}", symbol);
            }

            return (null, null);
        }

        public async Task<YahooCompanyProfileDto?> GetCompanyProfileAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            try
            {
                var suffix = (exchange != null && exchange.Equals("BSE", StringComparison.OrdinalIgnoreCase)) ? ".BO" : ".NS";
                var cleanSymbol = symbol.Trim().ToUpperInvariant();
                var searchSymbol = cleanSymbol.EndsWith(".NS") || cleanSymbol.EndsWith(".BO") ? cleanSymbol : $"{cleanSymbol}{suffix}";

                await EnsureCrumbAsync(cancellationToken);

                var endpoint = !string.IsNullOrWhiteSpace(_cachedCrumb)
                    ? $"/v10/finance/quoteSummary/{Uri.EscapeDataString(searchSymbol)}?modules=assetProfile,summaryProfile,quoteType&crumb={Uri.EscapeDataString(_cachedCrumb)}"
                    : $"/v10/finance/quoteSummary/{Uri.EscapeDataString(searchSymbol)}?modules=assetProfile,summaryProfile,quoteType";

                _logger.LogInformation("Fetching company profile & about from Yahoo Finance API for {Symbol}", searchSymbol);

                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                if (!string.IsNullOrWhiteSpace(_cachedCookie))
                {
                    req.Headers.Add("Cookie", _cachedCookie);
                }

                var response = await _httpClient.SendAsync(req, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Yahoo Finance API profile returned status {StatusCode} for {Symbol}", response.StatusCode, searchSymbol);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("quoteSummary", out var quoteSummary) &&
                    quoteSummary.TryGetProperty("result", out var resultArr) &&
                    resultArr.ValueKind == JsonValueKind.Array &&
                    resultArr.GetArrayLength() > 0)
                {
                    var root = resultArr[0];
                    var profile = new YahooCompanyProfileDto { Symbol = cleanSymbol };

                    if (root.TryGetProperty("quoteType", out var qtObj))
                    {
                        if (qtObj.TryGetProperty("longName", out var ln)) profile.CompanyName = ln.GetString();
                        else if (qtObj.TryGetProperty("shortName", out var sn)) profile.CompanyName = sn.GetString();
                    }

                    if (root.TryGetProperty("assetProfile", out var apObj))
                    {
                        if (apObj.TryGetProperty("longBusinessSummary", out var lbs)) profile.LongBusinessSummary = lbs.GetString();
                        if (apObj.TryGetProperty("website", out var web)) profile.Website = web.GetString();
                        if (apObj.TryGetProperty("industry", out var ind)) profile.Industry = ind.GetString();
                        if (apObj.TryGetProperty("sector", out var sec)) profile.Sector = sec.GetString();
                        if (apObj.TryGetProperty("fullTimeEmployees", out var fte) && fte.TryGetInt64(out var empCount)) profile.FullTimeEmployees = empCount;
                        if (apObj.TryGetProperty("city", out var city)) profile.City = city.GetString();
                        if (apObj.TryGetProperty("country", out var country)) profile.Country = country.GetString();

                        if (apObj.TryGetProperty("companyOfficers", out var officersArr) && officersArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var off in officersArr.EnumerateArray())
                            {
                                if (off.TryGetProperty("name", out var oName))
                                {
                                    var title = off.TryGetProperty("title", out var oTitle) ? $" ({oTitle.GetString()})" : "";
                                    profile.KeyExecutives.Add($"{oName.GetString()}{title}");
                                }
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(profile.LongBusinessSummary) && root.TryGetProperty("summaryProfile", out var spObj))
                    {
                        if (spObj.TryGetProperty("longBusinessSummary", out var lbs2)) profile.LongBusinessSummary = lbs2.GetString();
                        if (string.IsNullOrWhiteSpace(profile.Website) && spObj.TryGetProperty("website", out var web2)) profile.Website = web2.GetString();
                    }

                    return profile;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching company profile from Yahoo Finance API for {Symbol}", symbol);
            }

            return null;
        }

        public async Task<System.Collections.Generic.List<StockLens_Infrastructure.ExternalServices.IndianApi.Models.IndianApiPriceRecord>> GetHistoricalPricesAsync(string symbol, string exchange, CancellationToken cancellationToken = default)
        {
            var records = new System.Collections.Generic.List<StockLens_Infrastructure.ExternalServices.IndianApi.Models.IndianApiPriceRecord>();
            try
            {
                var suffix = exchange.Equals("BSE", StringComparison.OrdinalIgnoreCase) ? ".BO" : ".NS";
                var cleanSymbol = symbol.Trim().ToUpperInvariant();
                var searchSymbol = cleanSymbol.EndsWith(".NS") || cleanSymbol.EndsWith(".BO") ? cleanSymbol : $"{cleanSymbol}{suffix}";

                var endpoint = $"/v8/finance/chart/{Uri.EscapeDataString(searchSymbol)}?range=5y&interval=1d";
                _logger.LogInformation("Fetching historical price chart from Yahoo Finance API for {Symbol}", searchSymbol);

                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                if (!string.IsNullOrWhiteSpace(_cachedCookie))
                {
                    req.Headers.Add("Cookie", _cachedCookie);
                }

                var response = await _httpClient.SendAsync(req, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    // Retry with crumb if initial request fails
                    await EnsureCrumbAsync(cancellationToken);
                    var retryEndpoint = !string.IsNullOrWhiteSpace(_cachedCrumb)
                        ? $"{endpoint}&crumb={Uri.EscapeDataString(_cachedCrumb)}"
                        : endpoint;

                    using var retryReq = new HttpRequestMessage(HttpMethod.Get, retryEndpoint);
                    if (!string.IsNullOrWhiteSpace(_cachedCookie))
                    {
                        retryReq.Headers.Add("Cookie", _cachedCookie);
                    }
                    response = await _httpClient.SendAsync(retryReq, cancellationToken);
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Yahoo Finance Chart API returned status code {StatusCode} for {Symbol}", response.StatusCode, searchSymbol);
                    return records;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("chart", out var chartObj) &&
                    chartObj.TryGetProperty("result", out var resultArr) &&
                    resultArr.ValueKind == JsonValueKind.Array &&
                    resultArr.GetArrayLength() > 0)
                {
                    var firstRes = resultArr[0];
                    if (firstRes.TryGetProperty("timestamp", out var tsArr) &&
                        firstRes.TryGetProperty("indicators", out var indObj) &&
                        indObj.TryGetProperty("quote", out var quoteArr) &&
                        quoteArr.ValueKind == JsonValueKind.Array &&
                        quoteArr.GetArrayLength() > 0)
                    {
                        var quote = quoteArr[0];
                        quote.TryGetProperty("open", out var openArr);
                        quote.TryGetProperty("high", out var highArr);
                        quote.TryGetProperty("low", out var lowArr);
                        quote.TryGetProperty("close", out var closeArr);
                        quote.TryGetProperty("volume", out var volArr);

                        var count = tsArr.GetArrayLength();
                        for (int i = 0; i < count; i++)
                        {
                            var ts = tsArr[i].GetInt64();
                            var date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;

                            decimal? open = GetDecimalAt(openArr, i);
                            decimal? high = GetDecimalAt(highArr, i);
                            decimal? low = GetDecimalAt(lowArr, i);
                            decimal? close = GetDecimalAt(closeArr, i);
                            long? vol = GetLongAt(volArr, i);

                            if (close.HasValue && close.Value > 0)
                            {
                                var c = Math.Round(close.Value, 2);
                                var o = (open.HasValue && open.Value > 0) ? Math.Round(open.Value, 2) : c;
                                var h = (high.HasValue && high.Value > 0) ? Math.Round(Math.Max(high.Value, Math.Max(o, c)), 2) : Math.Max(o, c);
                                var l = (low.HasValue && low.Value > 0) ? Math.Round(Math.Min(low.Value, Math.Min(o, c)), 2) : Math.Min(o, c);

                                records.Add(new StockLens_Infrastructure.ExternalServices.IndianApi.Models.IndianApiPriceRecord
                                {
                                    DateString = date.ToString("yyyy-MM-dd"),
                                    Open = o,
                                    High = h,
                                    Low = l,
                                    Close = c,
                                    Volume = vol ?? 0
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching historical prices from Yahoo Finance API for {Symbol}", symbol);
            }

            return records;
        }

        public async Task<YahooLiveQuoteDto?> GetLiveQuoteAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            try
            {
                var suffix = (exchange != null && exchange.Equals("BSE", StringComparison.OrdinalIgnoreCase)) ? ".BO" : ".NS";
                var cleanSymbol = symbol.Trim().ToUpperInvariant();
                var searchSymbol = cleanSymbol.EndsWith(".NS") || cleanSymbol.EndsWith(".BO") ? cleanSymbol : $"{cleanSymbol}{suffix}";

                var endpoint = $"/v8/finance/chart/{Uri.EscapeDataString(searchSymbol)}?range=1d&interval=1m";
                _logger.LogInformation("Fetching live quote for {Symbol} via Yahoo Finance API", searchSymbol);

                var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Yahoo Finance API live quote returned status {StatusCode} for {Symbol}", response.StatusCode, searchSymbol);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("chart", out var chartObj) &&
                    chartObj.TryGetProperty("result", out var resultArr) &&
                    resultArr.ValueKind == JsonValueKind.Array &&
                    resultArr.GetArrayLength() > 0)
                {
                    var firstRes = resultArr[0];
                    var quote = new YahooLiveQuoteDto { Symbol = cleanSymbol };

                    if (firstRes.TryGetProperty("meta", out var meta))
                    {
                        if (meta.TryGetProperty("regularMarketPrice", out var p) && p.TryGetDecimal(out var price)) quote.Price = price;
                        else if (meta.TryGetProperty("fulldayPrice", out var fp) && fp.TryGetDecimal(out var fprice)) quote.Price = fprice;

                        if (meta.TryGetProperty("regularMarketDayHigh", out var dh) && dh.TryGetDecimal(out var dayHigh)) quote.DayHigh = dayHigh;
                        if (meta.TryGetProperty("regularMarketDayLow", out var dl) && dl.TryGetDecimal(out var dayLow)) quote.DayLow = dayLow;
                        if (meta.TryGetProperty("fiftyTwoWeekHigh", out var yh) && yh.TryGetDecimal(out var yearHigh)) quote.YearHigh = yearHigh;
                        if (meta.TryGetProperty("fiftyTwoWeekLow", out var yl) && yl.TryGetDecimal(out var yearLow)) quote.YearLow = yearLow;
                        if (meta.TryGetProperty("previousClose", out var pc) && pc.TryGetDecimal(out var prevClose)) quote.PreviousClose = prevClose;
                        else if (meta.TryGetProperty("chartPreviousClose", out var cpc) && cpc.TryGetDecimal(out var chartPrevClose)) quote.PreviousClose = chartPrevClose;
                        if (meta.TryGetProperty("regularMarketChangePercent", out var cp) && cp.TryGetDecimal(out var changePercent)) quote.ChangePercent = changePercent;
                        if (meta.TryGetProperty("regularMarketVolume", out var v) && v.TryGetInt64(out var vol)) quote.Volume = vol;
                    }

                    // Fallback to latest close indicator if regularMarketPrice is missing
                    if (!quote.Price.HasValue &&
                        firstRes.TryGetProperty("indicators", out var indObj) &&
                        indObj.TryGetProperty("quote", out var qArr) &&
                        qArr.ValueKind == JsonValueKind.Array &&
                        qArr.GetArrayLength() > 0)
                    {
                        var q0 = qArr[0];
                        if (q0.TryGetProperty("close", out var closeArr) && closeArr.ValueKind == JsonValueKind.Array)
                        {
                            for (int i = closeArr.GetArrayLength() - 1; i >= 0; i--)
                            {
                                if (closeArr[i].TryGetDecimal(out var lastC) && lastC > 0)
                                {
                                    quote.Price = lastC;
                                    break;
                                }
                            }
                        }
                    }

                    if (quote.Price.HasValue && quote.Price.Value > 0)
                    {
                        return quote;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching live quote from Yahoo Finance API for {Symbol}", symbol);
            }

            return null;
        }

        private static string? _cachedCookie;
        private static string? _cachedCrumb;
        private static readonly SemaphoreSlim _crumbLock = new(1, 1);

        private async Task EnsureCrumbAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_cachedCrumb) && !string.IsNullOrWhiteSpace(_cachedCookie)) return;

            await _crumbLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_cachedCrumb) && !string.IsNullOrWhiteSpace(_cachedCookie)) return;

                using var handler = new HttpClientHandler { UseCookies = true, AllowAutoRedirect = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                try
                {
                    var initRes = await client.GetAsync("https://fc.yahoo.com", cancellationToken);
                    var cookies = handler.CookieContainer.GetCookies(new Uri("https://fc.yahoo.com"));
                    if (cookies.Count > 0)
                    {
                        _cachedCookie = string.Join("; ", cookies.Cast<System.Net.Cookie>().Select(c => $"{c.Name}={c.Value}"));
                    }
                }
                catch { }

                using var crumbReq = new HttpRequestMessage(HttpMethod.Get, "https://query2.finance.yahoo.com/v1/test/getcrumb");
                if (!string.IsNullOrWhiteSpace(_cachedCookie))
                {
                    crumbReq.Headers.Add("Cookie", _cachedCookie);
                }
                crumbReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var crumbRes = await client.SendAsync(crumbReq, cancellationToken);
                if (crumbRes.IsSuccessStatusCode)
                {
                    _cachedCrumb = (await crumbRes.Content.ReadAsStringAsync(cancellationToken)).Trim();
                    Console.WriteLine($"[YahooFinance] Successfully acquired session crumb: {_cachedCrumb}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not acquire Yahoo crumb, trying direct request.");
            }
            finally
            {
                _crumbLock.Release();
            }
        }

        public async Task<System.Collections.Generic.List<StockLens_Infrastructure.ExternalServices.IndianApi.IndianApiFinancialPeriodDto>> GetQuarterlyIncomeStatementsAsync(
            string symbol,
            string? exchange = "NSE",
            CancellationToken cancellationToken = default)
        {
            var results = new System.Collections.Generic.List<StockLens_Infrastructure.ExternalServices.IndianApi.IndianApiFinancialPeriodDto>();
            if (string.IsNullOrWhiteSpace(symbol)) return results;

            try
            {
                var suffix = (exchange != null && exchange.Equals("BSE", StringComparison.OrdinalIgnoreCase)) ? ".BO" : ".NS";
                var cleanSymbol = symbol.Trim().ToUpperInvariant();
                var searchSymbol = cleanSymbol.EndsWith(".NS") || cleanSymbol.EndsWith(".BO") ? cleanSymbol : $"{cleanSymbol}{suffix}";

                await EnsureCrumbAsync(cancellationToken);

                var endpoint = !string.IsNullOrWhiteSpace(_cachedCrumb)
                    ? $"/v10/finance/quoteSummary/{Uri.EscapeDataString(searchSymbol)}?modules=incomeStatementHistoryQuarterly&crumb={Uri.EscapeDataString(_cachedCrumb)}"
                    : $"/v10/finance/quoteSummary/{Uri.EscapeDataString(searchSymbol)}?modules=incomeStatementHistoryQuarterly";

                _logger.LogInformation("Fetching quarterly income statements from Yahoo Finance API for {Symbol}", searchSymbol);
                Console.WriteLine($"[YahooFinance] Fetching quarterly income statements for {searchSymbol} via {endpoint}");

                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                if (!string.IsNullOrWhiteSpace(_cachedCookie))
                {
                    req.Headers.Add("Cookie", _cachedCookie);
                }

                var response = await _httpClient.SendAsync(req, cancellationToken);
                Console.WriteLine($"[YahooFinance] Response status: {response.StatusCode} for {searchSymbol}");
                if (!response.IsSuccessStatusCode)
                {
                    var errStr = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"[YahooFinance ERROR]: {errStr}");
                    _logger.LogWarning("Yahoo Finance quoteSummary API returned status {StatusCode} for {Symbol}", response.StatusCode, searchSymbol);
                    return results;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[YahooFinance] Received {json.Length} chars payload for {searchSymbol}");
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("quoteSummary", out var qs) &&
                    qs.TryGetProperty("result", out var resArray) &&
                    resArray.ValueKind == JsonValueKind.Array &&
                    resArray.GetArrayLength() > 0)
                {
                    var firstRes = resArray[0];
                    if (firstRes.TryGetProperty("incomeStatementHistoryQuarterly", out var ishq) &&
                        ishq.TryGetProperty("incomeStatementHistory", out var histArr) &&
                        histArr.ValueKind == JsonValueKind.Array)
                    {
                        const decimal croreDivisor = 10000000m; // Convert INR rupees to INR Crores

                        foreach (var stmt in histArr.EnumerateArray())
                        {
                            var period = new StockLens_Infrastructure.ExternalServices.IndianApi.IndianApiFinancialPeriodDto
                            {
                                PeriodType = "quarterly"
                            };

                            if (stmt.TryGetProperty("endDate", out var edObj))
                            {
                                if (edObj.TryGetProperty("fmt", out var edFmt) && edFmt.ValueKind == JsonValueKind.String &&
                                    DateTime.TryParse(edFmt.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ed))
                                {
                                    period.PeriodEndDate = ed;
                                    period.FiscalYear = ed.ToString("MMM yyyy");
                                }
                                else if (edObj.TryGetProperty("raw", out var edRaw) && edRaw.TryGetInt64(out var ts))
                                {
                                    var edDate = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                                    period.PeriodEndDate = edDate;
                                    period.FiscalYear = edDate.ToString("MMM yyyy");
                                }
                            }

                            // Revenue
                            if (ExtractRawDecimal(stmt, "totalRevenue", out var rev))
                                period.Revenue = Math.Round(rev / croreDivisor, 2);

                            // Operating Profit / Operating Income
                            if (ExtractRawDecimal(stmt, "operatingIncome", out var op))
                                period.OperatingProfit = Math.Round(op / croreDivisor, 2);

                            // Depreciation & Amortization
                            if (ExtractRawDecimal(stmt, "depreciationAndAmortization", out var dep) ||
                                ExtractRawDecimal(stmt, "depreciation", out dep))
                                period.Depreciation = Math.Round(Math.Abs(dep) / croreDivisor, 2);

                            // Profit Before Tax
                            if (ExtractRawDecimal(stmt, "incomeBeforeTax", out var pbt) ||
                                ExtractRawDecimal(stmt, "pretaxIncome", out pbt) ||
                                ExtractRawDecimal(stmt, "profitBeforeTax", out pbt))
                                period.ProfitBeforeTax = Math.Round(pbt / croreDivisor, 2);

                            // Tax Expense
                            if (ExtractRawDecimal(stmt, "incomeTaxExpense", out var tax) ||
                                ExtractRawDecimal(stmt, "taxProvision", out tax) ||
                                ExtractRawDecimal(stmt, "taxExpense", out tax) ||
                                ExtractRawDecimal(stmt, "incomeTax", out tax) ||
                                ExtractRawDecimal(stmt, "taxEffectOfUnusualItems", out tax))
                                period.Tax = Math.Round(Math.Abs(tax) / croreDivisor, 2);

                            // Net Profit / Net Income
                            if (ExtractRawDecimal(stmt, "netIncome", out var np))
                                period.NetProfit = Math.Round(np / croreDivisor, 2);
                            else if (ExtractRawDecimal(stmt, "netIncomeFromContinuingOperations", out var npCont))
                                period.NetProfit = Math.Round(npCont / croreDivisor, 2);
                            else if (ExtractRawDecimal(stmt, "netIncomeCommonStockholders", out var npStock))
                                period.NetProfit = Math.Round(npStock / croreDivisor, 2);

                            // EPS
                            if (ExtractRawDecimal(stmt, "dilutedEPS", out var eps) ||
                                ExtractRawDecimal(stmt, "basicEPS", out eps))
                                period.Eps = Math.Round(eps, 2);

                            // Interest Expense
                            if (ExtractRawDecimal(stmt, "interestExpense", out var interest))
                                period.Interest = Math.Round(Math.Abs(interest) / croreDivisor, 2);

                            // Other Income
                            if (ExtractRawDecimal(stmt, "totalOtherIncomeExpenseNet", out var oi))
                                period.OtherIncome = Math.Round(oi / croreDivisor, 2);

                            // Derived fields
                            if (period.Revenue.HasValue && period.OperatingProfit.HasValue)
                            {
                                period.Expenses = period.Revenue.Value - period.OperatingProfit.Value;
                                if (period.Revenue.Value > 0)
                                {
                                    period.OperatingProfitMargin = Math.Round((period.OperatingProfit.Value / period.Revenue.Value) * 100m, 2);
                                }
                            }

                            // Fallback Tax calculations
                            if (!period.Tax.HasValue && period.ProfitBeforeTax.HasValue && period.NetProfit.HasValue)
                            {
                                var computedTax = period.ProfitBeforeTax.Value - period.NetProfit.Value;
                                if (computedTax >= 0)
                                {
                                    period.Tax = Math.Round(computedTax, 2);
                                }
                            }

                            if (period.Tax.HasValue && period.ProfitBeforeTax.HasValue && period.ProfitBeforeTax.Value > 0)
                            {
                                period.TaxPercentage = Math.Round((period.Tax.Value / period.ProfitBeforeTax.Value) * 100m, 2);
                            }

                            if (period.PeriodEndDate.HasValue)
                            {
                                results.Add(period);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching quarterly income statements from Yahoo Finance API for {Symbol}", symbol);
            }

            return results;
        }

        private static bool ExtractRawDecimal(JsonElement parent, string propName, out decimal value)
        {
            value = 0;
            if (parent.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Object && prop.TryGetProperty("raw", out var rawProp) && rawProp.TryGetDecimal(out value))
                {
                    return true;
                }
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out value))
                {
                    return true;
                }
            }
            return false;
        }

        private static decimal? GetDecimalAt(JsonElement array, int index)
        {
            if (array.ValueKind == JsonValueKind.Array && index < array.GetArrayLength())
            {
                var el = array[index];
                if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                {
                    return d;
                }
            }
            return null;
        }

        private static long? GetLongAt(JsonElement array, int index)
        {
            if (array.ValueKind == JsonValueKind.Array && index < array.GetArrayLength())
            {
                var el = array[index];
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var l))
                {
                    return l;
                }
            }
            return null;
        }

        public async Task<System.Collections.Generic.List<YahooDebtResponse>> GetHistoricalDebtAsync(string symbol, string exchange, CancellationToken cancellationToken = default)
        {
            var responses = new System.Collections.Generic.List<YahooDebtResponse>();
            try
            {
                var suffix = (exchange ?? "NSE").Equals("BSE", StringComparison.OrdinalIgnoreCase) ? ".BO" : ".NS";
                var cleanSymbol = symbol.Trim().ToUpperInvariant();
                var searchSymbol = cleanSymbol.EndsWith(".NS") || cleanSymbol.EndsWith(".BO") ? cleanSymbol : $"{cleanSymbol}{suffix}";
                
                await EnsureCrumbAsync(cancellationToken);
                var crumb = _cachedCrumb;
                
                long period1 = DateTimeOffset.UtcNow.AddYears(-5).ToUnixTimeSeconds();
                long period2 = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

                var url = $"https://query2.finance.yahoo.com/ws/fundamentals-timeseries/v1/finance/timeseries/{searchSymbol}?symbol={searchSymbol}&period1={period1}&period2={period2}&type=annualLongTermDebt,annualCurrentDebt&crumb={crumb}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Yahoo API returned {StatusCode} for {Symbol}", response.StatusCode, symbol);
                    return responses;
                }

                var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(jsonString);

                var root = document.RootElement;
                if (!root.TryGetProperty("timeseries", out var timeseries) || 
                    !timeseries.TryGetProperty("result", out var resultList) || 
                    resultList.GetArrayLength() == 0)
                {
                    return responses;
                }

                var debtByYear = new System.Collections.Generic.Dictionary<int, YahooDebtResponse>();

                foreach (var result in resultList.EnumerateArray())
                {
                    if (result.TryGetProperty("annualLongTermDebt", out var ltdArray) && ltdArray.GetArrayLength() > 0)
                    {
                        foreach (var item in ltdArray.EnumerateArray())
                        {
                            if (item.TryGetProperty("reportedValue", out var reportedValue) && reportedValue.TryGetProperty("raw", out var raw) &&
                                item.TryGetProperty("asOfDate", out var asOfDate))
                            {
                                if (DateTime.TryParse(asOfDate.GetString(), out var date))
                                {
                                    if (!debtByYear.TryGetValue(date.Year, out var d))
                                    {
                                        d = new YahooDebtResponse { Year = date.Year };
                                        debtByYear[date.Year] = d;
                                    }
                                    d.LongTermDebt = raw.GetDecimal() / 10000000m;
                                }
                            }
                        }
                    }

                    if (result.TryGetProperty("annualCurrentDebt", out var cdArray) && cdArray.GetArrayLength() > 0)
                    {
                        foreach (var item in cdArray.EnumerateArray())
                        {
                            if (item.TryGetProperty("reportedValue", out var reportedValue) && reportedValue.TryGetProperty("raw", out var raw) &&
                                item.TryGetProperty("asOfDate", out var asOfDate))
                            {
                                if (DateTime.TryParse(asOfDate.GetString(), out var date))
                                {
                                    if (!debtByYear.TryGetValue(date.Year, out var d))
                                    {
                                        d = new YahooDebtResponse { Year = date.Year };
                                        debtByYear[date.Year] = d;
                                    }
                                    d.ShortTermDebt = raw.GetDecimal() / 10000000m;
                                }
                            }
                        }
                    }
                }

                responses.AddRange(debtByYear.Values);
                return responses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Yahoo debt for {Symbol}", symbol);
                return responses;
            }
        }
    }
}
