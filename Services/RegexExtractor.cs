using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KerioControlWeb.Services
{
    public static class RegexExtractor
    {
        // Простейший доменный регекс: xxxx.xxx (без проверки TLD)
        private static readonly Regex DomainRegex = new Regex(
            @"\b(?:[a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex IpRegex = new Regex(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            RegexOptions.IgnoreCase);

        public static List<string> ExtractAll(string text)
        {
            text = text.Replace("[.]", ".");

            var set = new HashSet<string>();

            foreach (Match m in IpRegex.Matches(text))
                set.Add(m.Value);

            foreach (Match m in DomainRegex.Matches(text))
                set.Add(m.Value.ToLower());

            return set.ToList();
        }
    }
}
