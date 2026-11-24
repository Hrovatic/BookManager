using System;
using System.Collections.Generic;
using System.Linq;

namespace BookManagerApi.Helpers
{
    public static class CategoryPrefixHelper
    {
        // Main category code -> inventory prefix
        // 1 Naravoslovje, 2 Tehnika, 4 Biotehnika, 5 Druzboslovje,
        // 6 Humanistika, 7 Interdisciplinarne, 9 Drugo, 10 Serijske publikacije
        private static readonly IReadOnlyDictionary<string, string> MainPrefixMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = "N",
                ["2"] = "T",
                ["4"] = "B",
                ["5"] = "D",
                ["6"] = "H",
                ["7"] = "I",
                ["10"] = "S",
                ["9"] = "O"
            };

        public static string FromCategoryCode(string? categoryCode)
        {
            if (string.IsNullOrWhiteSpace(categoryCode))
                return "X";

            var token = categoryCode.Trim();

            if (token.Contains('.'))
                token = token.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? token;

            token = token.Trim();
            if (token.Length == 0) return "X";

            if (MainPrefixMap.TryGetValue(token, out var mapped))
                return mapped;

            // Fallback for non-standard category codes.
            if (token.All(char.IsLetterOrDigit))
                return token.ToUpperInvariant();

            return "X";
        }
    }
}
