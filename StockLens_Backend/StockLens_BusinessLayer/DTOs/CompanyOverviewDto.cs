using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class CompanyOverviewDto
    {
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? Industry { get; set; }
        public string? Sector { get; set; }
        public string? LogoUrl { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? About { get; set; }
        public List<string> KeyPoints { get; set; } = new();
        public long? EmployeesCount { get; set; }
        public string? BseCode { get; set; }
        public string? NseCode { get; set; }
        public List<string> KeyExecutives { get; set; } = new();
        public string Source { get; set; } = "Hybrid";
        public DateTime? UpdatedAt { get; set; }

        // Corporate Profile Metadata
        public string? Headquarters { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? FoundedYear { get; set; }
    }
}
