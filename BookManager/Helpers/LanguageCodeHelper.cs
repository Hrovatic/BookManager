using System.Globalization;
using System.Text;

namespace BookManagerApi.Helpers
{
    public static class LanguageCodeHelper
    {
        private static readonly Dictionary<string, string> ThreeLetterAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SLO"] = "SLV",
            ["GER"] = "DEU",
            ["FRE"] = "FRA",
            ["RUM"] = "RON",
            ["CZE"] = "CES",
            ["GRE"] = "ELL",
            ["DUT"] = "NLD",
            ["BAQ"] = "EUS",
            ["ICE"] = "ISL",
            ["MAC"] = "MKD",
            ["MAY"] = "MSA",
            ["BUR"] = "MYA",
            ["TIB"] = "BOD",
            ["WEL"] = "CYM",
            ["ARM"] = "HYE",
            ["ALB"] = "SQI"
        };

        private static readonly Dictionary<string, string> Iso639_1To3 = BuildIso639_1To3Map();
        private static readonly Dictionary<string, string> LanguageNameTo3 = BuildLanguageNameTo3Map();

        public static string? NormalizeBookLanguage(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var trimmed = raw.Trim();
            if (TryNormalizeCultureTag(trimmed, out var byCultureTag))
                return byCultureTag;

            var lettersOnly = ExtractLettersOnly(trimmed);
            if (lettersOnly.Length == 0)
                return null;

            var token = lettersOnly.ToUpperInvariant();

            if (token.Length == 2 && Iso639_1To3.TryGetValue(token, out var iso3))
                return NormalizeIso3(iso3);

            if (token.Length == 3)
                return NormalizeIso3(token);

            var nameKey = BuildLanguageNameKey(trimmed);
            if (!string.IsNullOrWhiteSpace(nameKey) && LanguageNameTo3.TryGetValue(nameKey, out var fromName))
                return fromName;

            if (token.Length >= 3)
                return NormalizeIso3(token[..3]); // fallback

            return token;
        }

        private static string NormalizeIso3(string code)
        {
            var upper = code.ToUpperInvariant();
            if (ThreeLetterAliases.TryGetValue(upper, out var alias))
                return alias;

            return upper;
        }

        private static bool TryNormalizeCultureTag(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var candidate = raw.Trim().Replace('_', '-');
            var firstToken = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstToken))
                return false;

            try
            {
                var culture = CultureInfo.GetCultureInfo(firstToken);
                var three = culture.ThreeLetterISOLanguageName;
                if (string.IsNullOrWhiteSpace(three) || three.Length < 3)
                    return false;

                normalized = NormalizeIso3(three[..3]);
                return true;
            }
            catch (CultureNotFoundException)
            {
                return false;
            }
        }

        private static string ExtractLettersOnly(string value)
            => new(value.Where(char.IsLetter).ToArray());

        private static string BuildLanguageNameKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToUpperInvariant(ch));
            }

            return sb.ToString();
        }

        private static Dictionary<string, string> BuildLanguageNameTo3Map()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
            {
                var three = culture.ThreeLetterISOLanguageName?.Trim();
                if (string.IsNullOrWhiteSpace(three) || three.Length < 3)
                    continue;

                var code = NormalizeIso3(three[..3]);

                AddNameMapping(map, culture.EnglishName, code);
                AddNameMapping(map, culture.NativeName, code);
                AddNameMapping(map, culture.DisplayName, code);
            }

            // Pogosti vnosi v slovenščini in angleščini.
            AddNameMapping(map, "slovenščina", "SLV");
            AddNameMapping(map, "slovenski", "SLV");
            AddNameMapping(map, "angleščina", "ENG");
            AddNameMapping(map, "angleški", "ENG");
            AddNameMapping(map, "nemščina", "DEU");
            AddNameMapping(map, "nemški", "DEU");
            AddNameMapping(map, "španščina", "SPA");
            AddNameMapping(map, "španski", "SPA");
            AddNameMapping(map, "francoščina", "FRA");
            AddNameMapping(map, "francoski", "FRA");

            return map;
        }

        private static void AddNameMapping(Dictionary<string, string> map, string? name, string code)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var normalizedCode = NormalizeIso3(code);
            var key = BuildLanguageNameKey(name);
            if (!string.IsNullOrWhiteSpace(key))
                map[key] = normalizedCode;

            var parts = name
                .Split(new[] { '(', ')', ',', ';', '/', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0);

            foreach (var part in parts)
            {
                var partKey = BuildLanguageNameKey(part);
                if (!string.IsNullOrWhiteSpace(partKey))
                    map[partKey] = normalizedCode;
            }
        }

        private static Dictionary<string, string> BuildIso639_1To3Map()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
            {
                var two = culture.TwoLetterISOLanguageName?.Trim();
                var three = culture.ThreeLetterISOLanguageName?.Trim();

                if (string.IsNullOrWhiteSpace(two) || string.IsNullOrWhiteSpace(three))
                    continue;
                if (two.Length != 2 || three.Length < 3)
                    continue;

                var twoUpper = two.ToUpperInvariant();
                if (map.ContainsKey(twoUpper))
                    continue;

                map[twoUpper] = NormalizeIso3(three[..3]);
            }

            map["EN"] = "ENG";
            map["SL"] = "SLV";

            return map;
        }
    }
}
