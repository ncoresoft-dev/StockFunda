using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLens_Infrastructure.ExternalServices.YahooFinanceApi;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockLens_Infrastructure.ExternalServices.IndianApi
{
    public class IndianApiBalanceSheetClient : IIndianApiBalanceSheetClient
    {
        private readonly HttpClient _httpClient;
        private readonly IndianApiSettings _settings;
        private readonly ILogger<IIndianApiBalanceSheetClient> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        public IndianApiBalanceSheetClient(
            HttpClient httpClient,
            IOptions<IndianApiSettings> settings,
            ILogger<IIndianApiBalanceSheetClient> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
            
            _httpClient.BaseAddress = new Uri("https://stock.indianapi.in");
        }

        public async Task<Dictionary<string, Dictionary<string, decimal?>>?> GetBalanceSheetAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.", nameof(symbol));
            }

            var cleanStock = symbol.Trim().ToUpperInvariant();
            var endpoint = $"historical_stats?stock_name={Uri.EscapeDataString(cleanStock)}&stats=balancesheet";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-Api-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Fetching balance sheet from IndianAPI for symbol: {Symbol}", cleanStock);
            
            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                Console.WriteLine($"IndianAPI response status code: {response.StatusCode} for symbol: {cleanStock}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("IndianAPI returned status {StatusCode} for symbol {Symbol}", response.StatusCode, symbol);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"IndianAPI response content for {cleanStock}: {json}");
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, decimal?>>>(json, JsonOptions);
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching balance sheet for {Symbol} from IndianAPI.", symbol);
                return null;
            }
        }

        public async Task<YahooLiveQuoteDto?> GetLiveQuoteAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            var overview = await GetStockFinancialsAndOverviewAsync(symbol, exchange, cancellationToken);
            if (overview == null) return null;

            return new YahooLiveQuoteDto
            {
                Symbol = overview.Symbol,
                Price = overview.CurrentPrice,
                YearHigh = overview.YearHigh,
                YearLow = overview.YearLow
            };
        }

        public async Task<decimal?> GetCurrentPriceAsync(string symbol, string? exchange = "NSE", CancellationToken cancellationToken = default)
        {
            var quote = await GetLiveQuoteAsync(symbol, exchange, cancellationToken);
            return quote?.Price;
        }

        public async Task<IndianApiStockOverviewDto?> GetStockFinancialsAndOverviewAsync(
            string symbol,
            string? exchange = "NSE",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            var cleanStock = symbol.Trim().ToUpperInvariant();
            var endpoint = $"stock?name={Uri.EscapeDataString(cleanStock)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                var cleanApiKey = System.Text.RegularExpressions.Regex.Replace(_settings.ApiKey, @"[^\x20-\x7E]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-Api-Key", cleanApiKey);
                }
            }

            _logger.LogInformation("Fetching stock overview & financials from IndianAPI GET /stock for symbol: {Symbol}", cleanStock);
            Console.WriteLine($"[IndianAPI /stock] Requesting symbol: '{cleanStock}', API Key present: {!string.IsNullOrWhiteSpace(_settings.ApiKey)}");

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                Console.WriteLine($"[IndianAPI /stock] Response status: {response.StatusCode} for symbol: {cleanStock}");
                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"[IndianAPI /stock ERROR Body]: {errBody}");
                    _logger.LogWarning("IndianAPI /stock returned status {StatusCode} for symbol {Symbol}", response.StatusCode, cleanStock);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[IndianAPI /stock] Received JSON payload ({json.Length} chars)");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement target = root;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    target = root[0];
                }

                if (target.ValueKind != JsonValueKind.Object)
                {
                    Console.WriteLine("[IndianAPI /stock] Target is not a JSON object, returning null.");
                    return null;
                }

                var dto = new IndianApiStockOverviewDto
                {
                    Symbol = cleanStock
                };

                // Helper scopes for searching JSON fields
                var scopes = new List<JsonElement> { target };
                if (target.TryGetProperty("stockDetailsReusableData", out var sdrdObj) && sdrdObj.ValueKind == JsonValueKind.Object) scopes.Add(sdrdObj);
                if (target.TryGetProperty("stockTechnicalData", out var techObj))
                {
                    if (techObj.ValueKind == JsonValueKind.Object) scopes.Add(techObj);
                    else if (techObj.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in techObj.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.Object) scopes.Add(el);
                        }
                    }
                }
                if (target.TryGetProperty("technicalData", out var techObj2) && techObj2.ValueKind == JsonValueKind.Object) scopes.Add(techObj2);
                if (target.TryGetProperty("companyProfile", out var cpObj) && cpObj.ValueKind == JsonValueKind.Object) scopes.Add(cpObj);
                if (target.TryGetProperty("stockDetails", out var sdObj) && sdObj.ValueKind == JsonValueKind.Object) scopes.Add(sdObj);
                if (target.TryGetProperty("overview", out var ovObj) && ovObj.ValueKind == JsonValueKind.Object) scopes.Add(ovObj);
                if (target.TryGetProperty("keyMetrics", out var kmObj) && kmObj.ValueKind == JsonValueKind.Object)
                {
                    scopes.Add(kmObj);
                    if (kmObj.TryGetProperty("ratios", out var rObj) && rObj.ValueKind == JsonValueKind.Object) scopes.Add(rObj);
                    if (kmObj.TryGetProperty("valuation", out var vObj) && vObj.ValueKind == JsonValueKind.Object) scopes.Add(vObj);
                    if (kmObj.TryGetProperty("profitability", out var prObj) && prObj.ValueKind == JsonValueKind.Object) scopes.Add(prObj);
                    if (kmObj.TryGetProperty("mgmtEffectiveness", out var meObj) && meObj.ValueKind == JsonValueKind.Object) scopes.Add(meObj);
                    if (kmObj.TryGetProperty("financialstrength", out var fsObj) && fsObj.ValueKind == JsonValueKind.Object) scopes.Add(fsObj);
                    if (kmObj.TryGetProperty("priceandVolume", out var pvObj) && pvObj.ValueKind == JsonValueKind.Object) scopes.Add(pvObj);
                }

                if (TryGetPropertyString(scopes, new[] { "companyName", "name", "company_name" }, out var cName))
                {
                    dto.CompanyName = cName;
                }

                if (TryGetPropertyString(scopes, new[] { "industry", "sector", "mgIndustry", "industry_name" }, out var indName))
                {
                    dto.Industry = indName;
                    dto.SectorName = indName;
                }

                if (TryGetPropertyString(scopes, new[] { "about", "description", "businessSummary", "longBusinessSummary", "companyDescription", "overview", "synopsis", "profile" }, out var aboutText))
                {
                    dto.About = aboutText;
                }

                if (TryGetPropertyString(scopes, new[] { "website", "websiteUrl", "url", "companyWebsite" }, out var webUrl))
                {
                    dto.WebsiteUrl = webUrl;
                }

                // Extract any key points array if available
                foreach (var s in scopes)
                {
                    if (s.TryGetProperty("keyPoints", out var kpArr) && kpArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var kp in kpArr.EnumerateArray())
                        {
                            var kpStr = kp.GetString();
                            if (!string.IsNullOrWhiteSpace(kpStr))
                            {
                                dto.KeyPoints.Add(kpStr.Trim());
                            }
                        }
                        if (dto.KeyPoints.Count > 0) break;
                    }
                }

                // 1. Current Price
                if (target.TryGetProperty("currentPrice", out var cpProp) || target.TryGetProperty("current_price", out cpProp))
                {
                    if (cpProp.ValueKind == JsonValueKind.Object)
                    {
                        var cleanExchange = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange.Trim().ToUpperInvariant();
                        if (cpProp.TryGetProperty(cleanExchange, out var exVal) && TryExtractDecimal(exVal, out var price))
                        {
                            dto.CurrentPrice = price;
                        }
                        else if (cpProp.TryGetProperty("NSE", out var nseVal) && TryExtractDecimal(nseVal, out price))
                        {
                            dto.CurrentPrice = price;
                        }
                        else if (cpProp.TryGetProperty("BSE", out var bseVal) && TryExtractDecimal(bseVal, out price))
                        {
                            dto.CurrentPrice = price;
                        }
                    }
                    else if (TryExtractDecimal(cpProp, out var price))
                    {
                        dto.CurrentPrice = price;
                    }
                }
                if (!dto.CurrentPrice.HasValue && TryGetPropertyDecimal(scopes, new[] { "price", "close", "lastTradedPrice", "last_traded_price", "ltp", "closePrice", "close_price", "regularMarketPrice", "marketPrice" }, out var directPrice))
                {
                    dto.CurrentPrice = directPrice;
                }

                // 2. 52-Week High & Low
                if (TryGetPropertyDecimal(scopes, new[] { "yearHigh", "yhigh", "week52High", "fiftyTwoWeekHigh", "high52", "52WeekHigh", "52_week_high", "year_high", "week_52_high", "high52Week", "52wHigh", "fiftyTwoWeekHighPrice", "yearHighPrice", "dayHigh", "high" }, out var yh))
                {
                    dto.YearHigh = yh;
                }
                if (TryGetPropertyDecimal(scopes, new[] { "yearLow", "ylow", "week52Low", "fiftyTwoWeekLow", "low52", "52WeekLow", "52_week_low", "year_low", "week_52_low", "low52Week", "52wLow", "fiftyTwoWeekLowPrice", "yearLowPrice", "dayLow", "low" }, out var yl))
                {
                    dto.YearLow = yl;
                }

                // 3. Face Value
                if (TryGetPropertyDecimal(scopes, new[] { "faceValue", "face_value", "FaceValue", "Face_Value", "fv", "FV", "face_val", "faceVal" }, out var fv))
                {
                    dto.FaceValue = fv;
                }

                // 4. Market Cap
                if (TryGetPropertyDecimal(scopes, new[] { "marketCap", "market_cap", "MarketCap", "Market_Cap", "mcap", "MCap", "totalMarketCap", "marketCapitalization", "MarketCapitalization" }, out var mc))
                {
                    dto.MarketCap = mc;
                }

                // 5. P/E Ratio
                if (TryGetPropertyDecimal(scopes, new[] { "pPerEBasicExcludingExtraordinaryItemsTTM", "pe", "peRatio", "pe_ratio", "priceToEarnings", "PriceToEarnings", "priceToEarningsValueRatio", "p_e", "ttmPE", "trailingPE", "pe_ttm", "PERatio", "PE" }, out var peVal) && peVal > 0)
                {
                    dto.PeRatio = peVal;
                }

                // 6. ROE
                if (TryGetPropertyDecimal(scopes, new[] { "roe", "returnOnEquity", "returnOnAverageEquity", "returnOnNetWorth", "returnOnAverageEquityTrailing12Month", "ROE", "ReturnOnEquity", "roe_ttm", "trailingROE", "ronw" }, out var roeVal))
                {
                    dto.Roe = roeVal;
                }

                // 7. ROCE
                if (TryGetPropertyDecimal(scopes, new[] { "roce", "returnOnCapitalEmployed", "ROCE", "ReturnOnCapitalEmployed", "roce_ttm", "rocePercent", "roce_pct" }, out var roceVal))
                {
                    dto.Roce = roceVal;
                }

                // 8. P/B Ratio
                if (TryGetPropertyDecimal(scopes, new[] { "pb", "pbRatio", "pb_ratio", "priceToBook", "PriceToBook", "priceToBookValueRatio", "p_b", "pb_ttm", "PBRatio", "PB" }, out var pbVal) && pbVal > 0)
                {
                    dto.PbRatio = pbVal;
                }

                // 9. Dividend Yield
                if (TryGetPropertyDecimal(scopes, new[] { "currentDividendYieldCommonStockPrimaryIssueLTM", "dividendYield", "dividend_yield", "divYield", "DividendYield", "dividendYieldIndicatedAnnualDividend", "div_yield", "yield" }, out var divVal))
                {
                    dto.DividendYield = divVal;
                }

                // 10. Direct Sector PE check
                if (TryGetPropertyDecimal(scopes, new[] { "sectorPE", "sectorPe", "industryPE", "industryPe", "sector_pe", "industry_pe", "SectorPE", "IndustryPE", "sectorPeRatio", "industryPeRatio" }, out var secPe) && secPe > 0)
                {
                    dto.SectorPe = secPe;
                }

                // 11. Peers & Sector PE fallback
                JsonElement peerList = default;
                bool hasPeers = false;
                foreach (var s in scopes)
                {
                    if ((s.TryGetProperty("peerCompanyList", out peerList) ||
                         s.TryGetProperty("peerList", out peerList) ||
                         s.TryGetProperty("peers", out peerList) ||
                         s.TryGetProperty("peerCompanies", out peerList)) && peerList.ValueKind == JsonValueKind.Array)
                    {
                        hasPeers = true;
                        break;
                    }
                }
                if (!hasPeers)
                {
                    hasPeers = (target.TryGetProperty("peerCompanyList", out peerList) ||
                                target.TryGetProperty("peerList", out peerList) ||
                                target.TryGetProperty("peers", out peerList) ||
                                target.TryGetProperty("peerCompanies", out peerList)) && peerList.ValueKind == JsonValueKind.Array;
                }

                if (hasPeers && peerList.ValueKind == JsonValueKind.Array)
                {
                    var peList = new List<decimal>();
                    foreach (var peerEl in peerList.EnumerateArray())
                    {
                        var peer = new IndianApiPeerDto();
                        if (peerEl.TryGetProperty("companyName", out var pName)) peer.CompanyName = pName.GetString();
                        if (peerEl.TryGetProperty("price", out var pPrice) && TryExtractDecimal(pPrice, out var pp)) peer.Price = pp;

                        JsonElement pPe = default;
                        bool hasPeerPe = peerEl.TryGetProperty("priceToEarningsValueRatio", out pPe) ||
                                         peerEl.TryGetProperty("priceToEarnings", out pPe) ||
                                         peerEl.TryGetProperty("peRatio", out pPe) ||
                                         peerEl.TryGetProperty("pe", out pPe) ||
                                         peerEl.TryGetProperty("pe_ratio", out pPe) ||
                                         peerEl.TryGetProperty("p_e", out pPe);

                        if (hasPeerPe && TryExtractDecimal(pPe, out var pe))
                        {
                            peer.PeRatio = pe;
                            if (pe > 0 && pe < 300) peList.Add(pe);
                        }
                        if (peerEl.TryGetProperty("priceToBookValueRatio", out var pPb) && TryExtractDecimal(pPb, out var pb)) peer.PbRatio = pb;
                        if (peerEl.TryGetProperty("marketCap", out var pMc) && TryExtractDecimal(pMc, out var pMcVal)) peer.MarketCap = pMcVal;
                        if (peerEl.TryGetProperty("returnOnAverageEquityTrailing12Month", out var pRoe) && TryExtractDecimal(pRoe, out var roe)) peer.Roe = roe;
                        if (peerEl.TryGetProperty("dividendYieldIndicatedAnnualDividend", out var pDiv) && TryExtractDecimal(pDiv, out var div)) peer.DividendYield = div;
                        if (peerEl.TryGetProperty("totalSharesOutstanding", out var pShares) && TryExtractDecimal(pShares, out var shares)) peer.TotalShares = shares;

                        dto.Peers.Add(peer);
                    }

                    if (!dto.SectorPe.HasValue && peList.Count > 0)
                    {
                        dto.SectorPe = Math.Round(System.Linq.Enumerable.Average(peList), 2);
                    }
                }

                // 12. Parse Financials (Both Annual and Quarterly)
                JsonElement finArray = default;
                bool hasFinancials = target.TryGetProperty("financials", out finArray) || target.TryGetProperty("Financials", out finArray);

                if (hasFinancials && finArray.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"[IndianAPI /stock] Found {finArray.GetArrayLength()} financial periods in payload.");
                    foreach (var finEl in finArray.EnumerateArray())
                    {
                        var rawPeriodType = "Annual";
                        if (finEl.TryGetProperty("Type", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                        {
                            rawPeriodType = tProp.GetString() ?? "Annual";
                        }
                        else if (finEl.TryGetProperty("type", out var tProp2) && tProp2.ValueKind == JsonValueKind.String)
                        {
                            rawPeriodType = tProp2.GetString() ?? "Annual";
                        }
                        else if (finEl.TryGetProperty("PeriodType", out var ptProp) && ptProp.ValueKind == JsonValueKind.String)
                        {
                            rawPeriodType = ptProp.GetString() ?? "Annual";
                        }
                        else if (finEl.TryGetProperty("periodType", out var ptProp2) && ptProp2.ValueKind == JsonValueKind.String)
                        {
                            rawPeriodType = ptProp2.GetString() ?? "Annual";
                        }

                        var isQuarterly = rawPeriodType.IndexOf("quarter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          rawPeriodType.IndexOf("interim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          rawPeriodType.StartsWith("Q", StringComparison.OrdinalIgnoreCase);
                        var isAnnual = rawPeriodType.IndexOf("annual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       rawPeriodType.IndexOf("year", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       rawPeriodType.StartsWith("FY", StringComparison.OrdinalIgnoreCase);

                        if (!isAnnual && !isQuarterly)
                        {
                            continue;
                        }

                        var period = new IndianApiFinancialPeriodDto
                        {
                            PeriodType = isQuarterly ? "quarterly" : "annual"
                        };

                        JsonElement edProp = default;
                        bool hasEd = finEl.TryGetProperty("EndDate", out edProp) || finEl.TryGetProperty("endDate", out edProp) || finEl.TryGetProperty("end_date", out edProp) || finEl.TryGetProperty("date", out edProp);
                        if (hasEd && edProp.ValueKind == JsonValueKind.String)
                        {
                            if (DateTime.TryParse(edProp.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ed))
                            {
                                period.PeriodEndDate = ed;
                                if (isQuarterly)
                                {
                                    period.FiscalYear = ed.ToString("MMM yyyy");
                                }
                                else if (string.IsNullOrWhiteSpace(period.FiscalYear))
                                {
                                    period.FiscalYear = $"FY{ed.Year % 100}";
                                }
                            }
                        }

                        JsonElement fyProp = default;
                        bool hasFy = finEl.TryGetProperty("FiscalYear", out fyProp) || finEl.TryGetProperty("fiscalYear", out fyProp) || finEl.TryGetProperty("fiscal_year", out fyProp) || finEl.TryGetProperty("period", out fyProp);
                        if (hasFy && fyProp.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(period.FiscalYear))
                        {
                            var rawFy = fyProp.GetString() ?? "";
                            if (isQuarterly)
                            {
                                period.FiscalYear = rawFy;
                            }
                            else if (rawFy.Length == 4 && rawFy.StartsWith("20"))
                            {
                                period.FiscalYear = $"FY{rawFy.Substring(2)}";
                            }
                            else
                            {
                                period.FiscalYear = rawFy.StartsWith("FY", StringComparison.OrdinalIgnoreCase) ? rawFy : $"FY{rawFy}";
                            }
                        }

                        if (string.IsNullOrWhiteSpace(period.FiscalYear) && period.PeriodEndDate.HasValue)
                        {
                            period.FiscalYear = isQuarterly ? period.PeriodEndDate.Value.ToString("MMM yyyy") : $"FY{period.PeriodEndDate.Value.Year % 100}";
                        }

                        JsonElement map = default;
                        bool hasMap = finEl.TryGetProperty("stockFinancialMap", out map) || finEl.TryGetProperty("financialMap", out map) || finEl.TryGetProperty("StockFinancialMap", out map);
                        if (hasMap && map.ValueKind == JsonValueKind.Object)
                        {
                            // 1. Cash Flow Statement (CAS)
                            JsonElement casArray = default;
                            bool hasCas = map.TryGetProperty("CAS", out casArray) || map.TryGetProperty("cas", out casArray) || map.TryGetProperty("CashFlow", out casArray);
                            if (hasCas && casArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in casArray.EnumerateArray())
                                {
                                    if (item.TryGetProperty("key", out var k) && item.TryGetProperty("value", out var v))
                                    {
                                        var key = k.GetString();
                                        if ((key == "CashfromOperatingActivities" || key == "CashFromOperations" || key == "OperatingCashFlow") && TryExtractDecimal(v, out var cfo))
                                            period.OperatingCashFlow = cfo;
                                        else if ((key == "CapitalExpenditures" || key == "Capex" || key == "CapitalExpenditure") && TryExtractDecimal(v, out var capex))
                                            period.Capex = Math.Abs(capex);
                                        else if ((key == "NetChangeinCash" || key == "NetCashFlow" || key == "NetChangeInCashAndCashEquivalents") && TryExtractDecimal(v, out var netCash))
                                            period.NetCashFlow = netCash;
                                        else if ((key == "Depreciation/Depletion" || key == "Depreciation/Amortization" || key == "Depreciation" || key == "DepreciationAndAmortization" || key == "DepreciationAmortizationTotal" || key == "DepreciationExpense" || (key != null && key.StartsWith("Depreciation", StringComparison.OrdinalIgnoreCase))) && TryExtractDecimal(v, out var depCas))
                                            period.Depreciation ??= Math.Abs(depCas);
                                        else if ((key == "InterestPaid" || key == "Interest Paid" || key == "FinanceCostsPaid" || key == "Finance Costs Paid" || (key != null && (key.StartsWith("Interest", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Finance Cost", StringComparison.OrdinalIgnoreCase)))) && TryExtractDecimal(v, out var intCas))
                                            period.Interest ??= Math.Abs(intCas);
                                    }
                                }
                            }

                            // 2. Income Statement (INC)
                            if ((map.TryGetProperty("INC", out var incArray) || map.TryGetProperty("inc", out incArray) || map.TryGetProperty("IncomeStatement", out incArray)) && incArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in incArray.EnumerateArray())
                                {
                                    if (item.TryGetProperty("key", out var k) && item.TryGetProperty("value", out var v))
                                    {
                                        var key = k.GetString();
                                        if ((key == "TotalRevenue" || key == "Revenue" || key == "Sales" || key == "NetSales" || key == "RevenueFromOperations") && TryExtractDecimal(v, out var rev))
                                            period.Revenue = rev;
                                        else if ((key == "OperatingIncome" || key == "OperatingProfit" || key == "EBIT") && TryExtractDecimal(v, out var op))
                                            period.OperatingProfit = op;
                                        else if ((key == "Depreciation/Amortization" || key == "Depreciation" || key == "Depreciation/Depletion" || key == "DepreciationAndAmortization" || key == "DepreciationAmortizationTotal" || key == "DepreciationExpense" || key == "Depreciation & Amortization" || (key != null && key.StartsWith("Depreciation", StringComparison.OrdinalIgnoreCase))) && TryExtractDecimal(v, out var dep))
                                            period.Depreciation = Math.Abs(dep);
                                        else if ((key == "TotalInterestExpense" || key == "InterestExpense" || key == "Interest" || key == "FinanceCosts" || key == "Finance Costs" || key == "Finance Cost" || key == "Interest Expense" || key == "Interest & Finance Charges" || (key != null && (key.StartsWith("Interest", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Finance Cost", StringComparison.OrdinalIgnoreCase)))) && TryExtractDecimal(v, out var interest))
                                            period.Interest = Math.Abs(interest);
                                        else if ((key == "NonOperatingIncome" || key == "OtherIncome" || key == "TotalOtherIncomeExpenseNet") && TryExtractDecimal(v, out var oi))
                                            period.OtherIncome = oi;
                                        else if ((key == "NetIncomeBeforeTaxes" || key == "IncomeBeforeTaxes" || key == "EarningsBeforeTaxes" || key == "ProfitBeforeTax" || key == "PBT") && TryExtractDecimal(v, out var pbt))
                                            period.ProfitBeforeTax = pbt;
                                        else if ((key == "ProvisionforIncomeTaxes" || key == "Provision For Income Taxes" || key == "ProvisionForIncomeTaxes" || key == "Provision for Income Taxes" ||
                                                  key == "ProvisionforTax" || key == "Provision For Tax" || key == "ProvisionForTax" || key == "Provision for Tax" ||
                                                  key == "ProvisionforTaxes" || key == "Provision For Taxes" || key == "ProvisionForTaxes" || key == "Provision for Taxes" ||
                                                  key == "IncomeTaxExpense" || key == "Income Tax Expense" || key == "IncomeTax" || key == "Income Tax" ||
                                                  key == "TaxExpense" || key == "Tax Expense" || key == "TotalTaxExpense" || key == "Total Tax Expense" ||
                                                  key == "CurrentTax" || key == "Current Tax" || key == "Tax" || key == "Taxes" || key == "Taxation" ||
                                                  (key != null && key.StartsWith("Provision", StringComparison.OrdinalIgnoreCase) && key.Contains("Tax", StringComparison.OrdinalIgnoreCase)) ||
                                                  (key != null && (key.StartsWith("Tax", StringComparison.OrdinalIgnoreCase) || key.StartsWith("IncomeTax", StringComparison.OrdinalIgnoreCase)) &&
                                                   !key.Contains("%") && !key.Contains("Rate", StringComparison.OrdinalIgnoreCase) && !key.Contains("Percent", StringComparison.OrdinalIgnoreCase) && !key.Contains("Before", StringComparison.OrdinalIgnoreCase)))
                                                 && TryExtractDecimal(v, out var tax))
                                            period.Tax = Math.Abs(tax);
                                        else if ((key == "Tax %" || key == "Tax % (PBT)" || key == "TaxPercent" || key == "TaxPercentage" || key == "TaxRate" || key == "EffectiveTaxRate" ||
                                                  (key != null && key.StartsWith("Tax", StringComparison.OrdinalIgnoreCase) && (key.Contains("%") || key.Contains("Percent", StringComparison.OrdinalIgnoreCase) || key.Contains("Rate", StringComparison.OrdinalIgnoreCase))))
                                                 && TryExtractDecimal(v, out var taxPct))
                                            period.TaxPercentage = taxPct;
                                        else if ((key == "NetIncome" || key == "NetProfit" || key == "PAT") && TryExtractDecimal(v, out var np))
                                            period.NetProfit = np;
                                        else if (key == "NetIncomeAfterTaxes" && TryExtractDecimal(v, out var npat) && !period.NetProfit.HasValue)
                                            period.NetProfit = npat;
                                        else if ((key == "DilutedEPSExcludingExtraOrdItems" || key == "DilutedNormalizedEPS" || key == "DilutedEPS" || key == "BasicEPS" || key == "EPS" || key == "Eps" || (key != null && (key.Contains("EPS", StringComparison.OrdinalIgnoreCase) || key.Contains("EarningsPerShare", StringComparison.OrdinalIgnoreCase)))) && TryExtractDecimal(v, out var eps) && eps > 0)
                                            period.Eps = eps;
                                        else if (key == "MinorityInterest" && TryExtractDecimal(v, out var mi))
                                            period.NetProfitAttributableToMinorityInterest = Math.Abs(mi);
                                    }
                                }

                                // Align Operating Profit to EBITDA standard (Operating Income / EBIT + Depreciation & Amortization)
                                if (period.OperatingProfit.HasValue && period.Depreciation.HasValue && period.Depreciation.Value > 0)
                                {
                                    period.OperatingProfit = period.OperatingProfit.Value + period.Depreciation.Value;
                                }

                                if (period.Revenue.HasValue && period.OperatingProfit.HasValue)
                                {
                                    period.Expenses = period.Revenue.Value - period.OperatingProfit.Value;
                                    if (period.Revenue.Value > 0)
                                    {
                                        period.OperatingProfitMargin = Math.Round((period.OperatingProfit.Value / period.Revenue.Value) * 100m, 2);
                                    }
                                }

                                // Fallback Tax calculations
                                if (!period.Tax.HasValue && period.TaxPercentage.HasValue && period.ProfitBeforeTax.HasValue)
                                {
                                    period.Tax = Math.Round((period.TaxPercentage.Value / 100m) * period.ProfitBeforeTax.Value, 2);
                                }

                                if (!period.Tax.HasValue && period.ProfitBeforeTax.HasValue && period.NetProfit.HasValue)
                                {
                                    var computedTax = period.ProfitBeforeTax.Value - period.NetProfit.Value;
                                    if (computedTax >= 0)
                                    {
                                        period.Tax = Math.Round(computedTax, 2);
                                    }
                                }

                                if (!period.TaxPercentage.HasValue && period.Tax.HasValue && period.ProfitBeforeTax.HasValue && period.ProfitBeforeTax.Value > 0)
                                {
                                    period.TaxPercentage = Math.Round((period.Tax.Value / period.ProfitBeforeTax.Value) * 100m, 2);
                                }
                            }

                            // 3. Balance Sheet (BAL)
                            if ((map.TryGetProperty("BAL", out var balArray) || map.TryGetProperty("bal", out balArray) || map.TryGetProperty("BalanceSheet", out balArray)) && balArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in balArray.EnumerateArray())
                                {
                                    if (item.TryGetProperty("key", out var k) && item.TryGetProperty("value", out var v))
                                    {
                                        var key = k.GetString();
                                        if (string.IsNullOrWhiteSpace(key)) continue;

                                        if ((key == "TotalAssets" || key == "TotalAsset" || key == "TotalLiabilitiesShareholders'Equity") && TryExtractDecimal(v, out var ta))
                                            period.TotalAssets = ta;
                                        else if ((key == "TotalLiabilities" || key == "TotalLiability") && TryExtractDecimal(v, out var tl))
                                            period.TotalLiabilities = tl;
                                        else if ((key == "TotalCurrentLiabilities" || key == "CurrentLiabilities") && TryExtractDecimal(v, out var tcl))
                                            period.TotalCurrentLiabilities = tcl;
                                        else if ((key == "TotalCurrentAssets" || key == "CurrentAssets") && TryExtractDecimal(v, out var tca))
                                            period.TotalCurrentAssets = tca;
                                        else if ((key == "TotalDebt" || key == "Debt") && TryExtractDecimal(v, out var td))
                                            period.TotalDebt = td;
                                        else if ((key == "LongTermDebt" || key == "TotalLongTermDebt" || key == "Borrowings") && TryExtractDecimal(v, out var ltd))
                                            period.LongTermDebt = ltd;
                                        else if ((key == "NotesPayable/ShortTermDebt" || key == "ShortTermDebt" || key == "CurrentPortofLTDebt/CapitalLeases") && TryExtractDecimal(v, out var std))
                                            period.ShortTermDebt = std;
                                        else if ((key == "TotalEquity" || key == "ShareholdersEquity" || key == "NetWorth") && TryExtractDecimal(v, out var te))
                                            period.TotalEquity = te;
                                        else if ((key == "OtherEquityTotal" || key == "OtherEquity" || key == "ReservesAndSurplus" || key == "Reserves" || key == "RetainedEarnings(AccumulatedDeficit)") && TryExtractDecimal(v, out var oe))
                                            period.OtherEquity = oe;
                                        else if ((key == "CommonStockTotal" || key == "EquityCapital" || key == "ShareCapital" || key == "CommonStock") && TryExtractDecimal(v, out var cs))
                                            period.EquityCapital = cs;
                                        else if ((key == "Property/Plant/EquipmentTotal-Net" || key == "Property/Plant/EquipmentTotal-Gross" || key == "FixedAssets") && TryExtractDecimal(v, out var fa))
                                            period.FixedAssets = fa;
                                        else if ((key == "GoodwillNet" || key == "IntangiblesNet" || key == "CWIP" || key == "CapitalWorkInProgress") && TryExtractDecimal(v, out var cw))
                                            period.Cwip = cw;
                                        else if ((key == "LongTermInvestments" || key == "ShortTermInvestments" || key == "Investments") && TryExtractDecimal(v, out var inv))
                                            period.Investments = (period.Investments ?? 0) + inv;
                                        else if ((key == "OtherCurrentAssetsTotal" || key == "OtherLongTermAssetsTotal" || key == "OtherAssets") && TryExtractDecimal(v, out var oa))
                                            period.OtherAssets = (period.OtherAssets ?? 0) + oa;
                                        else if ((key == "OtherLiabilitiesTotal" || key == "OtherCurrentliabilitiesTotal" || key == "OtherLiabilities") && TryExtractDecimal(v, out var ol))
                                            period.OtherLiabilities = (period.OtherLiabilities ?? 0) + ol;
                                        else if ((key == "AccountsReceivable-TradeNet" || key == "TotalReceivablesNet" || key == "TradeReceivables") && TryExtractDecimal(v, out var tr))
                                            period.TradeReceivables = tr;
                                        else if ((key == "TotalInventory" || key == "Inventory") && TryExtractDecimal(v, out var tinv))
                                            period.TotalInventory = tinv;
                                        else if ((key == "AccountsPayable" || key == "TotalPayables" || key == "TradePayables") && TryExtractDecimal(v, out var ap))
                                            period.AccountsPayable = ap;
                                        else if ((key == "TangibleBookValueperShareCommonEq" || key == "BookValuePerShare" || key == "BookValue") && TryExtractDecimal(v, out var tbv))
                                            period.BookValuePerShare = tbv;
                                        else if ((key == "TotalCommonSharesOutstanding" || key == "TotalShares" || key == "SharesOutstanding") && TryExtractDecimal(v, out var tcso))
                                            period.TotalShares = tcso;
                                    }
                                }

                                if (period.TotalEquity.HasValue && period.TotalShares.HasValue && period.TotalShares.Value > 0)
                                {
                                    var shares = period.TotalShares.Value;
                                    period.BookValuePerShare = shares > 10000000
                                        ? Math.Round((period.TotalEquity.Value * 10000000m) / shares, 2)
                                        : Math.Round(period.TotalEquity.Value / shares, 2);
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(period.FiscalYear))
                        {
                            dto.Financials.Add(period);
                        }
                    }
                }

                decimal? directBvQuarter = null;
                decimal? directBvFiscalYear = null;

                // 13. Parse keyMetrics for TTM EPS & Consolidated Book Value
                if (target.TryGetProperty("keyMetrics", out var kmProp) && kmProp.ValueKind == JsonValueKind.Object)
                {
                    if (kmProp.TryGetProperty("persharedata", out var psdArray) && psdArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in psdArray.EnumerateArray())
                        {
                            if (item.TryGetProperty("key", out var k) && item.TryGetProperty("value", out var v))
                            {
                                var key = k.GetString();
                                if ((key == "eEPSExcludingExtraordinaryIitemsTrailing12onth" ||
                                     key == "ePSIncludingExtraOrdinaryItemsTrailing12Month" ||
                                     key == "ePSBasicExcludingExtraordinaryItemsItrailing12Month" ||
                                     key == "ttmEps" || key == "epsTtm") &&
                                    TryExtractDecimal(v, out var ttmEps) && ttmEps > 0)
                                {
                                    dto.TtmEps = ttmEps;
                                }
                                else if (key == "bookValuePerShareMostRecentQuarter" && TryExtractDecimal(v, out var bvq) && bvq > 0)
                                {
                                    directBvQuarter = bvq;
                                }
                                else if (key == "bookValuePerShare MostRecentFiscalYear" && TryExtractDecimal(v, out var bvf) && bvf > 0)
                                {
                                    directBvFiscalYear = bvf;
                                }
                            }
                        }
                    }

                    if (!dto.TtmEps.HasValue && kmProp.TryGetProperty("incomeStatement", out var isArray) && isArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in isArray.EnumerateArray())
                        {
                            if (item.TryGetProperty("key", out var k) && item.TryGetProperty("value", out var v))
                            {
                                var key = k.GetString();
                                if (key == "earningsPerShareNormalizedExcludingExtraordinaryItemsAvgDilutedSharesOutstandingTTM" &&
                                    TryExtractDecimal(v, out var ttmEps) && ttmEps > 0)
                                {
                                    dto.TtmEps = ttmEps;
                                    break;
                                }
                            }
                        }
                    }
                }

                // Sort financials descending by period end date
                dto.Financials = System.Linq.Enumerable.ToList(
                    System.Linq.Enumerable.OrderByDescending(dto.Financials, f => f.PeriodEndDate ?? DateTime.MinValue));

                // Find latest Annual financial statement (fallback to latest period)
                var latestAnnual = System.Linq.Enumerable.FirstOrDefault(dto.Financials, f => f.PeriodType == "annual")
                                   ?? System.Linq.Enumerable.FirstOrDefault(dto.Financials);

                // 14. Book Value
                if (!dto.BookValue.HasValue)
                {
                    dto.BookValue = directBvQuarter ?? directBvFiscalYear ?? latestAnnual?.BookValuePerShare;
                }
                if (!dto.BookValue.HasValue && latestAnnual?.TotalEquity.HasValue == true)
                {
                    if (latestAnnual.TotalShares.HasValue && latestAnnual.TotalShares.Value > 0)
                    {
                        var sh = latestAnnual.TotalShares.Value;
                        dto.BookValue = sh > 10000000 ? Math.Round((latestAnnual.TotalEquity.Value * 10000000m) / sh, 2) : Math.Round(latestAnnual.TotalEquity.Value / sh, 2);
                    }
                    else if (latestAnnual.EquityCapital.HasValue && dto.FaceValue.HasValue && dto.FaceValue.Value > 0)
                    {
                        var shCr = latestAnnual.EquityCapital.Value / dto.FaceValue.Value;
                        if (shCr > 0) dto.BookValue = Math.Round(latestAnnual.TotalEquity.Value / shCr, 2);
                    }
                }
                if (latestAnnual != null && dto.BookValue.HasValue && !latestAnnual.BookValuePerShare.HasValue)
                {
                    latestAnnual.BookValuePerShare = dto.BookValue.Value;
                }

                // 15. Face Value (Dynamic calculation from EquityCapital and TotalShares if missing)
                if (!dto.FaceValue.HasValue)
                {
                    var finWithEq = System.Linq.Enumerable.FirstOrDefault(dto.Financials, f => f.EquityCapital.HasValue && f.TotalShares.HasValue);
                    if (finWithEq != null && finWithEq.EquityCapital.HasValue && finWithEq.TotalShares.HasValue && finWithEq.TotalShares.Value > 0)
                    {
                        var eq = finWithEq.EquityCapital.Value;
                        var sh = finWithEq.TotalShares.Value;
                        dto.FaceValue = sh > 10000000 ? Math.Round((eq * 10000000m) / sh, 2) : Math.Round(eq / sh, 2);
                    }
                    else if (latestAnnual?.EquityCapital.HasValue == true && dto.CurrentPrice.HasValue && dto.MarketCap.HasValue && dto.MarketCap.Value > 0)
                    {
                        // Formula: FaceValue = (EquityCapital * CurrentPrice) / MarketCap
                        dto.FaceValue = Math.Round((latestAnnual.EquityCapital.Value * dto.CurrentPrice.Value) / dto.MarketCap.Value, 0);
                    }
                }

                // 16. Market Cap (Dynamic calculation from TotalShares & CurrentPrice or EquityCapital/FaceValue if missing)
                if (!dto.MarketCap.HasValue && dto.CurrentPrice.HasValue && dto.CurrentPrice.Value > 0)
                {
                    if (latestAnnual?.TotalShares.HasValue == true && latestAnnual.TotalShares.Value > 0)
                    {
                        var sh = latestAnnual.TotalShares.Value;
                        dto.MarketCap = sh > 10000000 ? Math.Round((sh * dto.CurrentPrice.Value) / 10000000m, 2) : Math.Round(sh * dto.CurrentPrice.Value, 2);
                    }
                    else if (latestAnnual?.EquityCapital.HasValue == true && dto.FaceValue.HasValue && dto.FaceValue.Value > 0)
                    {
                        var sharesCr = latestAnnual.EquityCapital.Value / dto.FaceValue.Value;
                        dto.MarketCap = Math.Round(sharesCr * dto.CurrentPrice.Value, 2);
                    }
                }

                // 17. P/E Ratio (Dynamic calculation from Current Price / EPS or Market Cap / Net Profit if missing)
                if (!dto.PeRatio.HasValue && dto.CurrentPrice.HasValue && dto.CurrentPrice.Value > 0)
                {
                    var effectiveEps = dto.TtmEps ?? latestAnnual?.Eps;
                    if (effectiveEps.HasValue && effectiveEps.Value > 0)
                    {
                        dto.PeRatio = Math.Round(dto.CurrentPrice.Value / effectiveEps.Value, 2);
                    }
                    else if (dto.MarketCap.HasValue && latestAnnual?.NetProfit.HasValue == true && latestAnnual.NetProfit.Value > 0)
                    {
                        dto.PeRatio = Math.Round(dto.MarketCap.Value / latestAnnual.NetProfit.Value, 2);
                    }
                }

                // 18. P/B Ratio (Dynamic calculation from Current Price / Book Value if missing)
                if (!dto.PbRatio.HasValue && dto.CurrentPrice.HasValue && dto.CurrentPrice.Value > 0 && dto.BookValue.HasValue && dto.BookValue.Value > 0)
                {
                    dto.PbRatio = Math.Round(dto.CurrentPrice.Value / dto.BookValue.Value, 2);
                }

                // 19. ROE (Dynamic calculation: (Latest Net Profit / Total Equity) * 100 if missing)
                if (!dto.Roe.HasValue && latestAnnual?.NetProfit.HasValue == true && latestAnnual.TotalEquity.HasValue && latestAnnual.TotalEquity.Value > 0)
                {
                    dto.Roe = Math.Round((latestAnnual.NetProfit.Value / latestAnnual.TotalEquity.Value) * 100m, 2);
                }

                // 20. ROCE (Dynamic calculation: (EBIT / Capital Employed) * 100 if missing)
                if (!dto.Roce.HasValue && latestAnnual != null)
                {
                    decimal? ebit = latestAnnual.ProfitBeforeTax.HasValue
                        ? (latestAnnual.ProfitBeforeTax.Value + (latestAnnual.Interest ?? 0))
                        : (latestAnnual.OperatingProfit.HasValue ? latestAnnual.OperatingProfit.Value + (latestAnnual.OtherIncome ?? 0) : null);

                    decimal? capitalEmployed = null;
                    if (latestAnnual.TotalEquity.HasValue && latestAnnual.TotalEquity.Value > 0)
                    {
                        capitalEmployed = latestAnnual.TotalEquity.Value + (latestAnnual.TotalDebt ?? 0);
                    }
                    else if (latestAnnual.TotalAssets.HasValue && latestAnnual.TotalCurrentLiabilities.HasValue)
                    {
                        capitalEmployed = latestAnnual.TotalAssets.Value - latestAnnual.TotalCurrentLiabilities.Value;
                    }

                    if (ebit.HasValue && capitalEmployed.HasValue && capitalEmployed.Value > 0)
                    {
                        dto.Roce = Math.Round((ebit.Value / capitalEmployed.Value) * 100m, 2);
                    }
                }

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching stock financials and overview from IndianAPI for {Symbol}.", cleanStock);
                return null;
            }
        }

        private static bool TryGetPropertyString(IEnumerable<JsonElement> scopes, string[] keys, out string? value)
        {
            value = null;
            foreach (var scope in scopes)
            {
                if (scope.ValueKind != JsonValueKind.Object) continue;
                foreach (var key in keys)
                {
                    if (scope.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                    {
                        var str = prop.GetString();
                        if (!string.IsNullOrWhiteSpace(str))
                        {
                            value = str;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool TryGetPropertyDecimal(IEnumerable<JsonElement> scopes, string[] keys, out decimal value)
        {
            value = 0;
            foreach (var scope in scopes)
            {
                if (scope.ValueKind != JsonValueKind.Object) continue;
                foreach (var key in keys)
                {
                    if (scope.TryGetProperty(key, out var prop) && TryExtractDecimal(prop, out var dec))
                    {
                        value = dec;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryExtractDecimal(JsonElement el, out decimal val)
        {
            val = 0;
            if (el.ValueKind == JsonValueKind.Number)
            {
                return el.TryGetDecimal(out val);
            }
            if (el.ValueKind == JsonValueKind.String)
            {
                var str = el.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    str = str.Replace(",", "").Replace("₹", "").Trim();
                    return decimal.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val);
                }
            }
            return false;
        }
    }
}
