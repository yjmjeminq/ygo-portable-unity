using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YGOPortable.UI
{
    internal readonly struct ShopSearchKey
    {
        private readonly string _exact;
        private readonly string _similar;

        private ShopSearchKey(string exact)
        {
            _exact = exact;
            _similar = FoldKoreanVowels(exact);
        }

        internal static ShopSearchKey FromName(string name) => new ShopSearchKey(CompactName(name));

        // Keep names separated so a query cannot match across two different cards in a pack.
        internal static ShopSearchKey FromNames(IEnumerable<string> names) =>
            new ShopSearchKey(string.Join("\n", names.Select(CompactName)));

        internal static string NormalizeInput(string value) => (value ?? "").Normalize(NormalizationForm.FormKC).Trim();

        internal bool CanTrySimilar => _exact?.Length >= 2 && _exact.Any(letter => letter is >= '가' and <= '힣');

        internal bool Contains(ShopSearchKey query, bool similar = false)
        {
            string needle = similar ? query._similar : query._exact;
            string text = similar ? _similar : _exact;
            return !string.IsNullOrEmpty(needle) && text?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CompactName(string value) =>
            new string(NormalizeInput(value).Where(char.IsLetterOrDigit).ToArray());

        private static string FoldKoreanVowels(string text)
        {
            const int finalCount = 28;
            const int vowelCount = 21;
            char[] folded = null;
            for (int i = 0; i < text.Length; i++)
            {
                char syllable = text[i];
                if (syllable is < '가' or > '힣') continue;
                int vowel = (syllable - '가') / finalCount % vowelCount;
                // Fold ㅐ to ㅔ and ㅒ to ㅖ while retaining the initial and final consonants.
                if (vowel is 1 or 3)
                    (folded ??= text.ToCharArray())[i] = (char)(syllable + 4 * finalCount);
            }
            return folded == null ? text : new string(folded);
        }
    }
}
