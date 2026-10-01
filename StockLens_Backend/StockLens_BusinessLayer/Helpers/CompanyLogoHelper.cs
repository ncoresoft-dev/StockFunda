using System;

namespace StockLens_BusinessLayer.Helpers
{
    /// <summary>
    /// Helper for constructing Indian Listed Company Logo URLs via the SharePerks Logo API
    /// Format: https://company-logo.shareperks.in/logo/{ISIN}/icon.svg
    /// </summary>
    public static class CompanyLogoHelper
    {
        public const string BaseLogoUrl = "https://company-logo.shareperks.in/logo/";

        /// <summary>
        /// Builds the full Logo API URL for a given ISIN string.
        /// </summary>
        public static string? BuildLogoUrl(string? isin)
        {
            if (string.IsNullOrWhiteSpace(isin)) return null;
            return $"{BaseLogoUrl}{isin.Trim()}/icon.svg";
        }
    }
}
