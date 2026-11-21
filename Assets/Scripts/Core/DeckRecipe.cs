using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace YGOPortable.Core
{
    public static class DeckRecipe
    {
        private enum Section { Automatic, Main, Extra, Side }

        private const int MaxTextLength = 24000;
        private static readonly Regex LeadingCount = new(
            @"^(?<count>\d+)(?:\s*[x×장]\s*|\s+)(?<card>.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex TrailingCount = new(
            @"^(?<card>.+?)\s*(?:\s+[x×]\s*(?<count>\d+)|\s+(?<count>\d+)\s*장|\s+(?<count>\d+))$",
            RegexOptions.IgnoreCase);
        private static readonly Regex CodeSuffix = new(@"^(?<card>.*?)\s*[\[(](?<code>\d{1,10})[\])]\s*$");
        private static readonly Regex HeadingCount = new(@"\s*[\[(]?\s*\d+\s*(?:장|cards?)?\s*[\])]?\s*$",
            RegexOptions.IgnoreCase);
        private static readonly Regex Ruby = new(@"\([^)]*\)");

        public static string ExportText(UserDeck deck)
        {
            var text = new StringBuilder();
            string name = (deck.name ?? "").Replace('\r', ' ').Replace('\n', ' ');
            text.AppendLine($"덱 이름: {name}");
            Append("메인 덱", deck.main);
            Append("엑스트라 덱", deck.extra);
            Append("사이드 덱", deck.side);
            return text.ToString();

            void Append(string heading, List<uint> cards)
            {
                text.AppendLine().AppendLine($"{heading} ({cards.Count}장)");
                for (int first = 0; first < cards.Count;)
                {
                    uint code = cards[first];
                    int next = first + 1;
                    while (next < cards.Count && cards[next] == code) next++;
                    text.AppendLine($"{next - first}x {CardDatabase.GetName(code)} [{code}]");
                    first = next;
                }
            }
        }

        public static bool TryImport(string text, out UserDeck deck, out string error)
        {
            deck = null;
            error = "";
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            {
                error = "24,000자 이내의 YDK 또는 카드 목록을 붙여넣으세요.";
                return false;
            }

            var result = new UserDeck { name = "가져온 덱" };
            var lines = text.Split('\n');
            var section = Section.Automatic;
            var names = new Lazy<ILookup<string, CardInfo>>(() => CardDatabase.AllCards
                .Where(card => DeckCard(card) != null).ToLookup(card => NameKey(card.name), StringComparer.Ordinal));
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().TrimStart('\uFEFF').Trim();
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith('\x0060') ||
                    line.StartsWith("#created by", StringComparison.OrdinalIgnoreCase)) continue;

                if (line.StartsWith("덱 이름:", StringComparison.Ordinal) || line.StartsWith("Deck name:", StringComparison.OrdinalIgnoreCase))
                {
                    string name = line.Substring(line.IndexOf(':') + 1).Trim();
                    if (name.Length > 0) result.name = name;
                    continue;
                }
                if (TryReadHeading(line, out var next, out bool changeSection))
                {
                    if (changeSection) section = next;
                    continue;
                }

                line = line.TrimStart('-', '•', '*', '·', ' ').TrimEnd('*').Trim();
                if (!TryReadCard(line, names, out var card, out int count))
                {
                    error = $"{i + 1}번째 줄의 카드 이름 또는 번호를 확인하세요.\n{line}";
                    return false;
                }

                var destination = section == Section.Automatic ? card.IsExtra ? Section.Extra : Section.Main : section;
                if (destination != Section.Side && card.IsExtra != (destination == Section.Extra))
                {
                    error = $"{i + 1}번째 줄의 '{card.name}' 카드가 덱 구역과 맞지 않습니다.";
                    return false;
                }
                var list = destination == Section.Main ? result.main : destination == Section.Extra ? result.extra : result.side;
                int capacity = destination == Section.Main ? 60 : 15;
                if (count > capacity - list.Count)
                {
                    error = $"{i + 1}번째 줄에서 덱 구역의 최대 {capacity}장을 넘었습니다.";
                    return false;
                }
                for (int copy = 0; copy < count; copy++)
                    list.Add(card.code);
            }

            if (result.main.Count + result.extra.Count + result.side.Count == 0)
            {
                error = "목록에 카드가 없습니다.";
                return false;
            }
            deck = result;
            return true;
        }

        private static bool TryReadHeading(string line, out Section section, out bool changeSection)
        {
            string key = HeadingCount.Replace(line, "").Trim('#', '!', '[', ']', '*', ':', ' ', '\t');
            key = key.Replace(" ", "").ToLowerInvariant();
            section = key switch
            {
                "main" or "maindeck" or "메인" or "메인덱" => Section.Main,
                "extra" or "extradeck" or "엑스트라" or "엑스트라덱" => Section.Extra,
                "side" or "sidedeck" or "사이드" or "사이드덱" => Section.Side,
                _ => Section.Automatic,
            };
            changeSection = section != Section.Automatic;
            return changeSection || key is "몬스터" or "마법" or "함정" or "monsters" or "spells" or "traps";
        }

        private static bool TryReadCard(string line, Lazy<ILookup<string, CardInfo>> names, out CardInfo card, out int count)
        {
            count = 1;
            bool numbered = CodeSuffix.IsMatch(line);
            card = numbered ? null : ResolveCard(line, names);
            if (card != null) return true;

            var match = LeadingCount.Match(line);
            if (!match.Success && !numbered) match = TrailingCount.Match(line);
            if (match.Success)
            {
                if (!int.TryParse(match.Groups["count"].Value, out count) || count < 1) return false;
                card = ResolveCard(match.Groups["card"].Value.Trim(), names);
                return card != null;
            }

            card = numbered ? ResolveCard(line, names) : null;
            return card != null;
        }

        private static CardInfo ResolveCard(string value, Lazy<ILookup<string, CardInfo>> names)
        {
            if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint code))
                return DeckCard(CardDatabase.Get(code));
            var suffix = CodeSuffix.Match(value);
            if (suffix.Success && uint.TryParse(suffix.Groups["code"].Value, out code))
                return DeckCard(CardDatabase.Get(code));

            string key = NameKey(value);
            if (key.Length == 0) return null;
            CardInfo found = null;
            foreach (var card in names.Value[key])
            {
                if (found != null && CollectionProgress.GetDeckCopyKey(found.code) != CollectionProgress.GetDeckCopyKey(card.code))
                    return null;
                if (found == null || (found.alias != 0 && card.alias == 0)) found = card;
            }
            return found;
        }

        private static CardInfo DeckCard(CardInfo card) =>
            card != null && (card.type & YgoConstants.TYPE_TOKEN) == 0 ? card : null;

        private static string NameKey(string value) => string.IsNullOrEmpty(value) ? "" :
            new string(Ruby.Replace(value, "").Normalize(NormalizationForm.FormKC)
                .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        internal static double OpeningHandMissChance(int deckCount, int starterCount, int handSize)
        {
            int nonStarters = deckCount - starterCount;
            if (handSize > nonStarters) return 0d;
            double chance = 1d;
            for (int i = 0; i < handSize; i++)
                chance *= (double)(nonStarters - i) / (deckCount - i);
            return chance;
        }

        public static uint[] DrawOpeningCards(IReadOnlyList<uint> main, Random random)
        {
            var pool = main.ToArray();
            int size = Math.Min(6, pool.Length);
            for (int i = 0; i < size; i++)
            {
                int picked = random.Next(i, pool.Length);
                (pool[i], pool[picked]) = (pool[picked], pool[i]);
            }
            return pool.Take(size).ToArray();
        }
    }
}
