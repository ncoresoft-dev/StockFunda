using System.Text.Json.Serialization;

namespace StockLens_Infrastructure.ExternalServices.Nse.Models
{
    public class NseAnnouncementRawItem
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("dt")]
        public string? Dt { get; set; }

        [JsonPropertyName("exchdisstime")]
        public string? Exchdisstime { get; set; }

        [JsonPropertyName("attchmntFile")]
        public string? AttchmntFile { get; set; }

        [JsonPropertyName("attchmntText")]
        public string? AttchmntText { get; set; }

        [JsonPropertyName("difference")]
        public string? Difference { get; set; }

        [JsonPropertyName("sm_name")]
        public string? SmName { get; set; }

        [JsonPropertyName("sm_isin")]
        public string? SmIsin { get; set; }

        [JsonPropertyName("smIndustry")]
        public string? SmIndustry { get; set; }

        [JsonPropertyName("an_dt")]
        public string? AnDt { get; set; }

        [JsonPropertyName("fileSize")]
        public string? FileSize { get; set; }

        [JsonPropertyName("attFileSize")]
        public string? AttFileSize { get; set; }

        [JsonPropertyName("hasXbrl")]
        public bool? HasXbrl { get; set; }

        [JsonPropertyName("sort_date")]
        public string? SortDate { get; set; }

        [JsonPropertyName("seq_id")]
        public string? SeqId { get; set; }
    }
}
