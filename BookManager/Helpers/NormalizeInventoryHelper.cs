using System;
using System.Globalization;
using System.Linq;

namespace BookManagerApi.Helpers
{
    public static class NormalizeInventoryHelper
    {
        public static string NormalizeInventory(string raw)
        {
            if (!TryNormalizeInventory(raw, out var normalized))
                throw new ArgumentException("Invalid inventoryNumber.", nameof(raw));

            return normalized;
        }

        public static bool TryNormalizeInventory(string? raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var text = raw.Trim();

            // If display format is used (e.g. "2-000123"), take the final segment.
            var lastSegment = text.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            var candidate = string.IsNullOrWhiteSpace(lastSegment) ? text : lastSegment!;

            candidate = candidate.Replace(" ", "");
            if (candidate.Length == 0) return false;
            if (!candidate.All(char.IsDigit)) return false;
            if (candidate.Length > 6) return false;

            if (!int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return false;
            if (n < 0 || n > 999999) return false;

            normalized = n.ToString("D6", CultureInfo.InvariantCulture);
            return true;
        }
    }
}
