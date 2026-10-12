using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StockLens_Infrastructure.ExternalServices.BharatStock.Models
{
    public class BharatStockIndexModel
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }
    }
}
