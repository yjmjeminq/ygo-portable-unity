using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace YGOPortable.Core
{
    public enum DeckCardKind
    {
        Any,
        MainMonster,
        Spell,
        Trap,
        ExtraDeck,
    }

    public sealed class DeckCardFilter
    {
        public static IReadOnlyList<(ulong mask, string label)> EffectOptions { get; } = new (ulong, string)[]
        {
            (0, "전체"),
            (1UL << 0, "파괴"),
            (1UL << 1, "제외"),
            (1UL << 2, "묘지로 보내기"),
            (1UL << 3, "패·덱으로 이동"),
            (1UL << 4, "드로우"),
            (1UL << 5, "덱에서 가져오기"),
            (1UL << 6, "특수 소환"),
            (1UL << 7, "토큰 생성"),
            (1UL << 8, "무효"),
            (1UL << 9, "LP 데미지"),
            (1UL << 10, "LP 회복"),
            (1UL << 11, "표시 형식 변경"),
            (1UL << 12, "공격력·수비력 변경"),
            (1UL << 13, "컨트롤 변경"),
            (1UL << 14, "묘지에서 가져오기"),
            (1UL << 15, "덱 파괴"),
            (1UL << 16, "패 파괴·버리기"),
            (1UL << 17, "카운터"),
            (1UL << 18, "융합 관련"),
            (1UL << 19, "관통"),
            (1UL << 20, "직접 공격"),
            (1UL << 21, "연속 공격"),
        };

        private readonly string _query;
        private readonly string _compactNameQuery;
        private readonly uint _passcode;
        private readonly string _effectText;
        private readonly DeckCardKind _kind;
        private readonly uint _requiredTypes;
        private readonly uint _excludedTypes;
        private readonly ulong _race;
        private readonly uint _attribute;
        private readonly int? _level;
        private readonly int? _rank;
        private readonly int? _linkRating;
        private readonly ulong _effectCategories;
        private readonly int? _minAttack;
        private readonly int? _maxAttack;
        private readonly int? _minDefense;
        private readonly int? _maxDefense;
        private readonly int? _pendulumScale;
        private readonly uint _linkMarker;

        public DeckCardFilter(string query, string effectText, DeckCardKind kind = DeckCardKind.Any,
            uint requiredTypes = 0, uint excludedTypes = 0, ulong race = 0, uint attribute = 0,
            int? level = null, int? rank = null, int? linkRating = null, ulong effectCategories = 0,
            int? minAttack = null, int? maxAttack = null, int? minDefense = null, int? maxDefense = null,
            int? pendulumScale = null, uint linkMarker = 0)
        {
            _query = query?.Trim() ?? "";
            uint.TryParse(_query, out _passcode);
            _compactNameQuery = new string(_query.Where(c => !IsNameSeparator(c)).Select(char.ToUpperInvariant).ToArray());
            _effectText = effectText?.Trim() ?? "";
            _kind = kind;
            _requiredTypes = requiredTypes;
            _excludedTypes = excludedTypes;
            _race = race;
            _attribute = attribute;
            _level = level;
            _rank = rank;
            _linkRating = linkRating;
            _effectCategories = effectCategories;
            _minAttack = minAttack;
            _maxAttack = maxAttack;
            _minDefense = minDefense;
            _maxDefense = maxDefense;
            _pendulumScale = pendulumScale;
            _linkMarker = linkMarker;
        }

        public bool Matches(CardInfo card)
        {
            if (card == null || (card.type & YgoConstants.TYPE_TOKEN) != 0 || !MatchesKind(card))
                return false;
            if ((card.type & _requiredTypes) != _requiredTypes || (card.type & _excludedTypes) != 0)
                return false;
            if (_race != 0 && (!card.IsMonster || (card.race & _race) == 0) ||
                _attribute != 0 && (!card.IsMonster || (card.attr & _attribute) == 0))
                return false;
            if (_level.HasValue && (!card.IsMonster || card.IsXyz || card.IsLink || card.level != _level.Value) ||
                _rank.HasValue && (!card.IsXyz || card.level != _rank.Value) ||
                _linkRating.HasValue && (!card.IsLink || card.level != _linkRating.Value))
                return false;
            if (_pendulumScale.HasValue && (!card.IsPendulum ||
                (card.lscale != _pendulumScale.Value && card.rscale != _pendulumScale.Value)) ||
                _linkMarker != 0 && (!card.IsLink || (card.link & _linkMarker) != _linkMarker))
                return false;
            if (!MatchesStat(card.IsMonster, card.atk, _minAttack, _maxAttack) ||
                !MatchesStat(card.IsMonster && !card.IsLink, card.def, _minDefense, _maxDefense))
                return false;
            if ((card.DeckEffectCategories & _effectCategories) != _effectCategories ||
                _effectText.Length != 0 && !Contains(card.desc, _effectText))
                return false;

            return _query.Length == 0 || Contains(card.desc, _query) || MatchesNameOrPasscode(card.code, card.name);
        }

        internal bool MatchesNameOrPasscode(uint code, string name) => _query.Length == 0 ||
            _passcode != 0 && code == _passcode || Contains(name, _query) || ContainsCompactName(name);

        private bool MatchesKind(CardInfo card) => _kind switch
        {
            DeckCardKind.MainMonster => card.IsMonster && !card.IsExtra,
            DeckCardKind.Spell => card.IsSpell,
            DeckCardKind.Trap => card.IsTrap,
            DeckCardKind.ExtraDeck => card.IsExtra,
            _ => true,
        };

        private static bool MatchesStat(bool hasStat, int stat, int? min, int? max) =>
            (!min.HasValue && !max.HasValue) ||
            (hasStat && stat >= 0 && (!min.HasValue || stat >= min.Value) && (!max.HasValue || stat <= max.Value));

        private static bool Contains(string text, string query) =>
            text?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsNameSeparator(char c) =>
            char.IsWhiteSpace(c) || c == '−' || char.GetUnicodeCategory(c) == UnicodeCategory.DashPunctuation;

        private bool ContainsCompactName(string name)
        {
            if (string.IsNullOrEmpty(name) || _compactNameQuery.Length == 0) return false;
            for (int start = 0; start < name.Length; start++)
            {
                if (char.ToUpperInvariant(name[start]) != _compactNameQuery[0]) continue;
                int matched = 1;
                for (int index = start + 1; index < name.Length && matched < _compactNameQuery.Length; index++)
                {
                    char letter = name[index];
                    if (IsNameSeparator(letter)) continue;
                    if (char.ToUpperInvariant(letter) != _compactNameQuery[matched]) break;
                    matched++;
                }
                if (matched == _compactNameQuery.Length) return true;
            }
            return false;
        }
    }
}
