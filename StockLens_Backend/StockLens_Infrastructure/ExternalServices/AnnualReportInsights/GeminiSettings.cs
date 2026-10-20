namespace StockLens_Infrastructure.ExternalServices.AnnualReportInsights
{
    public class GeminiSettings
    {
        public const string SectionName = "Gemini";
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "gemini-flash-latest";
        public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
        public int TimeoutSeconds { get; set; } = 90;
    }
}
