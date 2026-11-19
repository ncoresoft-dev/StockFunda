using System;
using System.Collections.Generic;

namespace StockLens_BusinessLayer.DTOs
{
    public class StockAnnouncementDto
    {
        public int Id { get; set; }
        public string SeqId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? Isin { get; set; }
        public string? Industry { get; set; }
        public string? Subject { get; set; }
        public string? Details { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentFileSize { get; set; }
        public bool HasXbrl { get; set; }
        public DateTime? BroadcastDateTime { get; set; }
        public string? DisseminationTime { get; set; }
        public string? Difference { get; set; }
        public string? TimeAgo { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StockAnnouncementResponseDto
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int Limit { get; set; }
        public int TotalPages => Limit > 0 ? (int)Math.Ceiling((double)TotalCount / Limit) : 1;
        public DateTime? LastSyncedAt { get; set; }
        public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
        public IReadOnlyList<StockAnnouncementDto> Announcements { get; set; } = Array.Empty<StockAnnouncementDto>();
    }
}
