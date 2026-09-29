using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLens_Infrastructure.ExternalServices.IndianApi.Models;

namespace StockLens_Infrastructure.ExternalServices.IndianApi
{
    public class IndianApiHistoricalDataClient : IIndianApiHistoricalDataClient
    {
        private readonly HttpClient _httpClient;
        private readonly IndianApiSettings _settings;
        private readonly ILogger<IndianApiHistoricalDataClient> _logger;

        public IndianApiHistoricalDataClient(
            HttpClient httpClient,
            IOptions<IndianApiSettings> settings,
            ILogger<IndianApiHistoricalDataClient> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
            
            var baseUrl = _settings.BaseUrl?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://api.indianapi.in";
            
            _httpClient.BaseAddress = new Uri(baseUrl);
            if (_settings.TimeoutSeconds > 0)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
            }
        }

        public async Task<List<IndianApiPriceRecord>> GetHistoricalPricesAsync(string cleanTicker, string period = "5yr", string? exchange = null, string filter = "price", CancellationToken cancellationToken = default)
        {
            var allRecords = new List<IndianApiPriceRecord>();
            
            var pureTicker = cleanTicker.Replace(".NS", "").Replace(".BO", "");
            
            // Default to 5yr if an invalid or empty period is passed
            if (string.IsNullOrWhiteSpace(period)) period = "5yr";
            
            var endpoint = $"/historical_data?stock_name={Uri.EscapeDataString(pureTicker)}&period={Uri.EscapeDataString(period)}&filter={Uri.EscapeDataString(filter)}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                
                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    request.Headers.Add("X-Api-Key", _settings.ApiKey);
                }
                
                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Indian API returned error {StatusCode} for ticker {Ticker}. Response: {ErrorBody}", response.StatusCode, cleanTicker, errorBody);
                    return allRecords;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("datasets", out var datasetsArray) && datasetsArray.ValueKind == JsonValueKind.Array)
                {
                    var dateToRecordMap = new Dictionary<string, IndianApiPriceRecord>();

                    foreach (var dataset in datasetsArray.EnumerateArray())
                    {
                        var metric = dataset.GetProperty("metric").GetString();
                        if (dataset.TryGetProperty("values", out var valuesArray) && valuesArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in valuesArray.EnumerateArray())
                            {
                                if (item.GetArrayLength() >= 2)
                                {
                                    var dateStr = item[0].GetString();
                                    if (string.IsNullOrWhiteSpace(dateStr)) continue;

                                    if (!dateToRecordMap.TryGetValue(dateStr, out var record))
                                    {
                                        record = new IndianApiPriceRecord { DateString = dateStr };
                                        dateToRecordMap[dateStr] = record;
                                    }

                                    if (metric == "Price")
                                    {
                                        if (item[1].ValueKind == JsonValueKind.String && decimal.TryParse(item[1].GetString(), out var price))
                                        {
                                            record.Close = price;
                                            record.Open = price; 
                                            record.High = price;
                                            record.Low = price;
                                        }
                                        else if (item[1].ValueKind == JsonValueKind.Number)
                                        {
                                            record.Close = item[1].GetDecimal();
                                            record.Open = record.Close;
                                            record.High = record.Close;
                                            record.Low = record.Close;
                                        }
                                    }
                                    else if (metric == "Volume")
                                    {
                                        if (item[1].ValueKind == JsonValueKind.Number)
                                        {
                                            record.Volume = item[1].GetInt64();
                                        }
                                        else if (item[1].ValueKind == JsonValueKind.String && long.TryParse(item[1].GetString(), out var vol))
                                        {
                                            record.Volume = vol;
                                        }

                                        if (item.GetArrayLength() >= 3 && item[2].ValueKind == JsonValueKind.Object)
                                        {
                                            if (item[2].TryGetProperty("delivery", out var deliveryProp))
                                            {
                                                if (deliveryProp.ValueKind == JsonValueKind.Number)
                                                {
                                                    record.DeliveryPercentage = deliveryProp.GetDecimal();
                                                }
                                                else if (deliveryProp.ValueKind == JsonValueKind.String && decimal.TryParse(deliveryProp.GetString(), out var delVal))
                                                {
                                                    record.DeliveryPercentage = delVal;
                                                }
                                            }
                                        }

                                        if (record.Volume.HasValue && record.DeliveryPercentage.HasValue)
                                        {
                                            record.DeliveryVolume = (long)Math.Round(record.Volume.Value * (record.DeliveryPercentage.Value / 100m));
                                        }
                                    }
                                    else if (metric == "DMA50")
                                    {
                                        if (item[1].ValueKind == JsonValueKind.String && decimal.TryParse(item[1].GetString(), out var dma50))
                                        {
                                            record.Dma50 = dma50;
                                        }
                                        else if (item[1].ValueKind == JsonValueKind.Number)
                                        {
                                            record.Dma50 = item[1].GetDecimal();
                                        }
                                    }
                                    else if (metric == "DMA200")
                                    {
                                        if (item[1].ValueKind == JsonValueKind.String && decimal.TryParse(item[1].GetString(), out var dma200))
                                        {
                                            record.Dma200 = dma200;
                                        }
                                        else if (item[1].ValueKind == JsonValueKind.Number)
                                        {
                                            record.Dma200 = item[1].GetDecimal();
                                        }
                                    }
                                }
                            }
                        }
                    }

                    allRecords.AddRange(dateToRecordMap.Values);
                }
                
                _logger.LogInformation("Successfully fetched {Count} records from Indian API for {Symbol}", allRecords.Count, cleanTicker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while calling Indian API for ticker {Ticker}", cleanTicker);
            }
            
            return allRecords;
        }
    }
}
