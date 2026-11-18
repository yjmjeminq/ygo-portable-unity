using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace YGOPortable.Core
{
    internal static class StarterArchetypeCatalog
    {
        private static readonly Regex NamedGroup = new Regex(
            @"""([^""\r\n]{2,30})""\s*(?:카드|몬스터|마법|함정)", RegexOptions.Compiled);
        private static readonly Dictionary<ArchetypeInfo, string> SearchNames = new();
        private static IReadOnlyList<ArchetypeInfo> _catalog;

        internal static IReadOnlyList<ArchetypeInfo> GetAll()
        {
            if (_catalog != null) return _catalog;
            if (!CardDatabase.IsLoaded) return Array.Empty<ArchetypeInfo>();
            var catalog = CardDatabase.GetAllArchetypes().ToList();
            var registered = new HashSet<ushort>(catalog.SelectMany(group => group.setcodes));
            var names = new HashSet<string>(catalog.Select(group => group.name), StringComparer.Ordinal);
            var additional = new Dictionary<ushort, List<CardInfo>>();
            foreach (var card in CardDatabase.AllCards)
            {
                if (card.setcode == 0 || (card.type & YgoConstants.TYPE_TOKEN) != 0 ||
                    !(card.IsMonster || card.IsSpell || card.IsTrap))
                    continue;

                foreach (ushort code in CardDatabase.ExtractSetcodes(card.setcode))
                {
                    if (registered.Contains(code)) continue;
                    if (!additional.TryGetValue(code, out var cards))
                        additional.Add(code, cards = new List<CardInfo>());
                    cards.Add(card);
                }
            }

            foreach (var entry in additional.OrderBy(pair => pair.Key))
            {
                var cards = entry.Value;
                var representative = cards.OrderBy(card => card.alias != 0)
                    .ThenByDescending(card => card.IsMonster)
                    .ThenByDescending(card => card.atk)
                    .ThenBy(card => card.code)
                    .First();
                string name = InferName(cards) ?? $"{CardDatabase.GetName(representative.code)} 계열";
                if (!names.Add(name))
                {
                    name = $"{CardDatabase.GetName(representative.code)} 계열";
                    if (!names.Add(name))
                    {
                        name = $"{name} ({entry.Key:X4})";
                        names.Add(name);
                    }
                }
                var group = new ArchetypeInfo
                {
                    name = name,
                    baseName = name,
                    representativeCode = representative.code,
                    MainPoolCount = cards.Count(card => !card.IsExtra),
                    ExtraPoolCount = cards.Count(card => card.IsExtra),
                };
                group.setcodes.Add(entry.Key);
                group.coreCards.AddRange(cards);
                catalog.Add(group);
            }

            return _catalog = catalog;
        }

        internal static IEnumerable<ArchetypeInfo> Search(string query, bool includeSmallGroups)
        {
            var groups = includeSmallGroups ? GetAll() : CardDatabase.GetAllArchetypes();
            string key = Normalize(query);
            return key.Length == 0 ? groups : groups.Where(group => GetSearchNames(group).Contains(key));
        }

        private static string GetSearchNames(ArchetypeInfo group)
        {
            if (!SearchNames.TryGetValue(group, out string names))
            {
                names = string.Join("\n", group.coreCards.Select(card => card.name)
                    .Prepend(group.baseName).Prepend(group.name).Select(Normalize));
                SearchNames.Add(group, names);
            }
            return names;
        }

        private static string Normalize(string text) => new string((text ?? "").Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        private static string InferName(IReadOnlyList<CardInfo> cards)
        {
            if (cards.Count < 2) return null;
            var names = cards.Select(card => card.name ?? "").Distinct(StringComparer.Ordinal).ToArray();
            return cards.SelectMany(card => NamedGroup.Matches(card.desc ?? "").Cast<Match>())
                .Select(match => match.Groups[1].Value.Trim())
                .Where(term => term.Length >= 2 && !term.EndsWith("토큰", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(term => (term, hits: names.Count(name => name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)))
                .Where(candidate => candidate.hits >= 2)
                .OrderByDescending(candidate => candidate.hits)
                .ThenByDescending(candidate => candidate.term.Length)
                .ThenBy(candidate => candidate.term, StringComparer.Ordinal)
                .Select(candidate => candidate.term)
                .FirstOrDefault();
        }
    }
}
