using System;

namespace StockLens_DataLayer.Entities
{
    public class StockAnnouncement
    {
        public int Id { get; set; }

        /// <summary>
        /// Unique sequence identifier assigned by NSE (e.g., "106865074").
        /// Used for primary deduplication.
        /// </summary>
        public string SeqId { get; set; } = string.Empty;

        /// <summary>
        /// Ticker symbol (e.g., "RELIANCE", "TCS").
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// Full name of the issuing company.
        /// </summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// ISIN code of the security (e.g., "INE002A01018").
        /// </summary>
        public string? Isin { get; set; }

        /// <summary>
        /// Industry name (e.g., "Miscellaneous", "IT - Software").
        /// </summary>
        public string? Industry { get; set; }

        /// <summary>
        /// Announcement category / subject (e.g., "Financial Results", "Board Meeting", "Trading Window", "Updates").
        /// Mapped from NSE's 'desc' field.
        /// </summary>
        public string? Subject { get; set; }

        /// <summary>
        /// Detailed description or press summary text.
        /// Mapped from NSE's 'attchmntText' field.
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// Direct HTTPS link to the exchange attachment / disclosure document (PDF).
        /// Mapped from NSE's 'attchmntFile' field.
        /// </summary>
        public string? AttachmentUrl { get; set; }

        /// <summary>
        /// Human-readable attachment file size (e.g., "354.27 KB", "3.45 MB").
        /// Mapped from NSE's 'attFileSize' / 'fileSize' field.
        /// </summary>
        public string? AttachmentFileSize { get; set; }

        /// <summary>
        /// Indicates if an XBRL disclosure file exists for this filing.
        /// </summary>
        public bool HasXbrl { get; set; }

        /// <summary>
        /// Parsed timestamp of the announcement broadcast.
        /// </summary>
        public DateTime? BroadcastDateTime { get; set; }

        /// <summary>
        /// Original exchange dissemination timestamp string (e.g., "18-Nov-2026 17:28:45").
        /// </summary>
        public string? DisseminationTime { get; set; }

        /// <summary>
        /// Exchange dissemination difference delay string (e.g., "00:00:01").
        /// </summary>
        public string? Difference { get; set; }

        /// <summary>
        /// Timestamp when recorded in our local database (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
