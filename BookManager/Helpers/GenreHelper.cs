using System;
using System.Collections.Generic;
using System.Linq;

namespace BookManagerApi.Helpers
{
    public static class GenreHelper
    {
        public static List<string> CleanGenres(IEnumerable<string?>? genres)
        {
            if (genres == null) return new List<string>();

            return genres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
