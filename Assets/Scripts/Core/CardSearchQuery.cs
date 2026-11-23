using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace YGOPortable.Core
{
    internal readonly struct CardSearchQuery
    {
        internal const int NoMatch = int.MaxValue;
        private static readonly ConditionalWeakTable<CardInfo, IndexedCard> Index = new();
        private readonly string _compact;
        private readonly string _similar;
        private readonly uint _passcode;

        internal CardSearchQuery(string query)
        {
            string normalized = Normalize(query);
            _compact = Compact(normalized);
            _similar = FoldKoreanVowels(_compact);
            uint.TryParse(normalized.Trim(), out _passcode);
        }

        internal bool IsEmpty => string.IsNullOrEmpty(_compact);

        internal int Rank(uint code)
        {
            if (IsEmpty) return 0;
            if (_passcode != 0) return code == _passcode ? 0 : NoMatch;
            return Rank(CardDatabase.Get(code));
        }

        internal int Rank(CardInfo card)
        {
            if (IsEmpty) return 0;
            if (card == null) return NoMatch;
            if (_passcode != 0) return card.code == _passcode ? 0 : NoMatch;

            var indexed = Index.GetValue(card, info => new IndexedCard(info));
            int nameRank = RankName(indexed.Name);
            if (nameRank < 5) return nameRank;
            if (indexed.Description.IndexOf(_compact, StringComparison.Ordinal) >= 0) return 5;
            return nameRank;
        }

        internal int RankName(string name) => IsEmpty ? 0 : RankName(new IndexedName(name));

        internal bool MatchesNameOrPasscode(uint code, string name) =>
            IsEmpty || (_passcode != 0 ? code == _passcode : RankName(name) != NoMatch);

        internal bool MatchesEffect(CardInfo card) => IsEmpty ||
            (card != null && Index.GetValue(card, info => new IndexedCard(info)).Description
                .IndexOf(_compact, StringComparison.Ordinal) >= 0);

        internal IEnumerable<T> Order<T>(IEnumerable<T> source, Func<T, uint> code)
        {
            if (IsEmpty) return source;
            var query = this;
            return source.Select(item => (Item: item, Rank: query.Rank(code(item))))
                .Where(match => match.Rank != NoMatch).OrderBy(match => match.Rank).Select(match => match.Item);
        }

        private int RankName(IndexedName name)
        {
            if (name.Compact == _compact) return 0;
            int index = name.Compact.IndexOf(_compact, StringComparison.Ordinal);
            if (index >= 0)
            {
                bool prefix = index == 0;
                for (; index >= 0; index = name.Compact.IndexOf(_compact, index + 1, StringComparison.Ordinal))
                {
                    if (name.WordStarts.Contains(index) && name.WordEnds.Contains(index + _compact.Length))
                        return index == 0 ? 1 : 2;
                }
                return prefix ? 3 : 4;
            }
            return _similar.Length >= 2 && _similar.Any(letter => letter is >= '가' and <= '힣') &&
                name.Similar.IndexOf(_similar, StringComparison.Ordinal) >= 0 ? 6 : NoMatch;
        }

        private static string Normalize(string value) => (value ?? "").Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        private static string Compact(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray());

        private sealed class IndexedCard
        {
            internal readonly IndexedName Name;
            private readonly string _effect;
            private string _description;

            internal IndexedCard(CardInfo card)
            {
                Name = new IndexedName(card.name);
                _effect = card.desc;
            }

            internal string Description => _description ??= Compact(Normalize(_effect));
        }

        private sealed class IndexedName
        {
            internal readonly string Compact;
            internal readonly string Similar;
            internal readonly List<int> WordStarts = new();
            internal readonly List<int> WordEnds = new();

            internal IndexedName(string value)
            {
                var result = new StringBuilder();
                bool inWord = false;
                foreach (char letter in Normalize(value))
                {
                    if (char.IsLetterOrDigit(letter))
                    {
                        if (!inWord) WordStarts.Add(result.Length);
                        result.Append(letter);
                        inWord = true;
                    }
                    else if (inWord)
                    {
                        WordEnds.Add(result.Length);
                        inWord = false;
                    }
                }
                if (inWord) WordEnds.Add(result.Length);
                Compact = result.ToString();
                Similar = FoldKoreanVowels(Compact);
            }
        }

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
                // Keep the existing Korean search tolerance for ㅐ/ㅔ and ㅒ/ㅖ.
                if (vowel is 1 or 3)
                    (folded ??= text.ToCharArray())[i] = (char)(syllable + 4 * finalCount);
            }
            return folded == null ? text : new string(folded);
        }
    }
}
