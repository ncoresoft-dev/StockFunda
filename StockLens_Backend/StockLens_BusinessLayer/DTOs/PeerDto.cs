namespace StockLens_BusinessLayer.DTOs
{
    public class PeerDto
    {
        public string CompanyName { get; set; } = string.Empty;
        public decimal? Price { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? Roe { get; set; }
        public decimal? Roce { get; set; }
        public decimal? DividendYield { get; set; }
        public decimal? TotalShares { get; set; }
    }
}
