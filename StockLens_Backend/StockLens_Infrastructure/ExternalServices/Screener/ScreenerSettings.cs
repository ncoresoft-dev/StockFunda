namespace StockLens_Infrastructure.ExternalServices.Screener
{
    public class ScreenerSettings
    {
        public const string SectionName = "Screener";

        public string BaseUrl { get; set; } = "https://www.screener.in";
        public int TimeoutSeconds { get; set; } = 15;
        public int CacheHours { get; set; } = 24;
    }
}
