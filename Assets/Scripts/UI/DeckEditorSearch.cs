using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;
using static YGOPortable.Core.YgoConstants;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private enum Facet
        {
            Kind, Frame, MonsterType, SpellTrap, Race, Attribute, Effect, LinkMarker,
            Level, Rank, LinkRating, PendulumScale, Count,
        }

        private static readonly (uint mask, string label)[] FrameOptions =
        {
            (0, "전체"),
            (TYPE_MONSTER | TYPE_NORMAL, "일반 몬스터"),
            (TYPE_MONSTER | TYPE_EFFECT, "효과 몬스터"),
            (TYPE_MONSTER | TYPE_RITUAL, "의식"),
            (TYPE_MONSTER | TYPE_FUSION, "융합"),
            (TYPE_MONSTER | TYPE_SYNCHRO, "싱크로"),
            (TYPE_MONSTER | TYPE_XYZ, "엑시즈"),
            (TYPE_MONSTER | TYPE_LINK, "링크"),
            (TYPE_MONSTER | TYPE_PENDULUM, "펜듈럼"),
            (TYPE_MONSTER | TYPE_FUSION | TYPE_PENDULUM, "융합 / 펜듈럼"),
            (TYPE_MONSTER | TYPE_SYNCHRO | TYPE_PENDULUM, "싱크로 / 펜듈럼"),
            (TYPE_MONSTER | TYPE_XYZ | TYPE_PENDULUM, "엑시즈 / 펜듈럼"),
            (TYPE_SPELL, "마법"),
            (TYPE_TRAP, "함정"),
        };

        private static readonly (uint required, string label)[] MonsterTypes =
        {
            (0, "전체"),
            (TYPE_MONSTER | TYPE_TUNER, "튜너"),
            (TYPE_MONSTER | TYPE_FLIP, "리버스"),
            (TYPE_MONSTER | TYPE_SPIRIT, "스피릿"),
            (TYPE_MONSTER | TYPE_UNION, "유니온"),
            (TYPE_MONSTER | TYPE_GEMINI, "듀얼"),
            (TYPE_MONSTER | TYPE_TOON, "툰"),
            (TYPE_MONSTER | TYPE_SPSUMMON, "특수 소환 몬스터"),
        };

        private static readonly (uint required, uint excluded, string label)[] SpellTrapTypes =
        {
            (0, 0, "전체"),
            (TYPE_SPELL, TYPE_QUICKPLAY | TYPE_CONTINUOUS | TYPE_EQUIP | TYPE_FIELD | TYPE_RITUAL, "일반 마법"),
            (TYPE_SPELL | TYPE_QUICKPLAY, 0, "속공 마법"),
            (TYPE_SPELL | TYPE_CONTINUOUS, 0, "지속 마법"),
            (TYPE_SPELL | TYPE_EQUIP, 0, "장착 마법"),
            (TYPE_SPELL | TYPE_FIELD, 0, "필드 마법"),
            (TYPE_SPELL | TYPE_RITUAL, 0, "의식 마법"),
            (TYPE_TRAP, TYPE_CONTINUOUS | TYPE_COUNTER, "일반 함정"),
            (TYPE_TRAP | TYPE_CONTINUOUS, 0, "지속 함정"),
            (TYPE_TRAP | TYPE_COUNTER, 0, "카운터 함정"),
        };

        private static readonly (ulong mask, string label)[] RaceOptions =
            new[] { (mask: 0UL, label: "전체") }.Concat(RaceNamesKR.OrderBy(pair => pair.Value, StringComparer.Ordinal)
                .Select(pair => (pair.Key, pair.Value))).ToArray();

        private static readonly (uint mask, string label)[] AttributeOptions =
            new[] { (mask: 0u, label: "전체") }.Concat(AttributeNamesKR.Select(pair => (pair.Key, pair.Value))).ToArray();

        private static readonly (uint mask, string label)[] LinkMarkerOptions =
        {
            (0, "전체"), (0x040, "↖ 왼쪽 위"), (0x080, "↑ 위"), (0x100, "↗ 오른쪽 위"),
            (0x008, "← 왼쪽"), (0x020, "→ 오른쪽"),
            (0x001, "↙ 왼쪽 아래"), (0x002, "↓ 아래"), (0x004, "↘ 오른쪽 아래"),
        };

        private static readonly Facet[] MonsterFacets =
        {
            Facet.MonsterType, Facet.Race, Facet.Attribute, Facet.Level, Facet.Rank,
            Facet.LinkRating, Facet.PendulumScale, Facet.LinkMarker,
        };

        private readonly Dropdown[] _facetControls = new Dropdown[(int)Facet.Count];
        private readonly int[] _savedFacets = new int[(int)Facet.Count];
        private readonly InputField[] _statInputs = new InputField[4];
        private readonly int?[] _savedStats = new int?[4];
        private string _savedEffectText = "";
        private GameObject _cardFilterDialog;
        private InputField _effectTextInput;
        private Text _filterError;
        private Text _detailButtonText;

        private void BuildDetailedSearch(Transform parent)
        {
            var overlay = LotdUiKit.FillRect("Overlay_TrunkSearch", parent);
            _cardFilterDialog = overlay.gameObject;
            LotdUiKit.AddImage(overlay.gameObject, LotdUiKit.WhiteSprite, new Color(.011f, .024f, .066f, .79f));
            var panel = LotdUiKit.CreateMenuPanel(overlay, "SearchWindow", new Vector2(.151f, .105f), new Vector2(.849f, .895f));

            var header = LotdUiKit.CreateRect("Heading", panel, new Vector2(.044f, .88f), new Vector2(.956f, .952f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(header.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader, false, false);
            LotdUiKit.AddFittedText(header, "Title", "보관함 상세 검색", 27, 18,
                TextAnchor.MiddleCenter, LotdUiKit.TextPrimary, FontStyle.Bold);

            RectTransform Field(string name, string title, string icon, int row, int column, int columns = 1)
            {
                float left = .045f + column * .230f;
                float top = .862f - row * .138f;
                var field = LotdUiKit.CreateRect(name, panel, new Vector2(left, top - .114f),
                    new Vector2(left + .230f * columns - .015f, top), Vector2.zero, Vector2.zero);
                var caption = LotdUiKit.CreateRect("Label", field, new Vector2(0f, .56f), Vector2.one,
                    Vector2.zero, Vector2.zero);
                var sprite = LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_FILTER_CARD_" + icon);
                if (sprite != null)
                {
                    var image = LotdUiKit.CreateRect("Icon", caption, Vector2.zero, Vector2.up,
                        Vector2.zero, new Vector2(23f, 0f));
                    LotdUiKit.AddImage(image.gameObject, sprite, Color.white, true, false);
                }
                var label = LotdUiKit.AddFittedText(caption, "Text", title, 17, 11,
                    TextAnchor.MiddleLeft, LotdUiKit.TextPrimary, FontStyle.Bold);
                if (sprite != null) label.rectTransform.offsetMin = new Vector2(27f, 0f);
                return LotdUiKit.CreateRect("Value", field, Vector2.zero, new Vector2(1f, .55f), Vector2.zero, Vector2.zero);
            }

            void Choice(Facet facet, string label, string icon, int row, int col, IReadOnlyList<string> values)
            {
                var holder = Field(facet.ToString(), label, icon, row, col);
                var dropdown = LotdUiKit.CreateLotdDropdown(holder, "Dropdown", values,
                    index => OnDetailedChoiceChanged(facet, index));
                var caption = dropdown.captionText;
                caption.alignment = TextAnchor.MiddleCenter;
                caption.resizeTextMaxSize = 16;
                caption.rectTransform.offsetMin = new Vector2(20f, 0f);
                caption.rectTransform.offsetMax = new Vector2(-20f, 0f);
                _facetControls[(int)facet] = dropdown;
            }

            Choice(Facet.Kind, "카드 종류", "FRAME", 0, 0, new[] { "전체", "메인 몬스터", "마법", "함정", "엑스트라 몬스터" });
            Choice(Facet.Frame, "카드 프레임", "FRAME", 0, 1, FrameOptions.Select(item => item.label).ToArray());
            Choice(Facet.MonsterType, "몬스터 특성", "TYPE", 0, 2, MonsterTypes.Select(item => item.label).ToArray());
            Choice(Facet.SpellTrap, "마법·함정 종류", "ICON", 0, 3, SpellTrapTypes.Select(item => item.label).ToArray());
            Choice(Facet.Race, "종족", "TYPE", 1, 0, RaceOptions.Select(item => item.label).ToArray());
            Choice(Facet.Attribute, "속성", "ATTR", 1, 1, AttributeOptions.Select(item => item.label).ToArray());
            Choice(Facet.Effect, "효과 분류", "GENRE", 1, 2, DeckCardFilter.EffectOptions.Select(item => item.label).ToArray());
            Choice(Facet.LinkMarker, "링크 마커", "LINKDIR", 1, 3, LinkMarkerOptions.Select(item => item.label).ToArray());
            Choice(Facet.Level, "레벨", "LEVEL", 2, 0, NumberChoices(1, 13));
            Choice(Facet.Rank, "랭크", "RANK", 2, 1, NumberChoices(0, 13));
            Choice(Facet.LinkRating, "링크", "LINKNUM", 2, 2, NumberChoices(1, 8));
            Choice(Facet.PendulumScale, "펜듈럼 스케일", "PEND", 2, 3, NumberChoices(0, 13));

            string[] statLabels = { "최소 공격력", "최대 공격력", "최소 수비력", "최대 수비력" };
            for (int i = 0; i < statLabels.Length; i++)
            {
                var holder = Field("Stat" + i, statLabels[i], i < 2 ? "ATK" : "DEF", 3, i);
                _statInputs[i] = LotdUiKit.CreateInputField(holder, "Number", "전체", 16);
                _statInputs[i].contentType = InputField.ContentType.IntegerNumber;
                _statInputs[i].characterLimit = 6;
            }

            var effect = Field("EffectText", "효과 문구", "GENRE", 4, 0, 4);
            _effectTextInput = LotdUiKit.CreateInputField(effect, "Query", "예: 특수 소환, 패에 넣는다", 17);
            var status = LotdUiKit.CreateRect("Status", panel, new Vector2(.044f, .122f), new Vector2(.956f, .172f),
                Vector2.zero, Vector2.zero);
            _filterError = LotdUiKit.AddFittedText(status, "Text", "", 15, 10, TextAnchor.MiddleLeft, LotdUiKit.RedAccent);

            var footer = LotdUiKit.CreateRect("Actions", panel, new Vector2(.044f, .04f), new Vector2(.956f, .11f),
                Vector2.zero, Vector2.zero);
            Button ActionButton(string name, string label, float left, float right, Action action, bool gold = false)
            {
                var slot = LotdUiKit.CreateRect(name, footer, new Vector2(left, 0f), new Vector2(right, 1f),
                    Vector2.zero, Vector2.zero);
                return LotdUiKit.CreateLotdButton(slot, "Button", label, action, 17, gold);
            }
            ActionButton("Reset", "초기화", 0f, .19f, ResetDetailedDraft);
            ActionButton("Cancel", "취소", .59f, .785f, () => _cardFilterDialog.SetActive(false));
            ActionButton("Apply", "검색 적용", .805f, 1f, ApplyDetailedSearch, true);
            _cardFilterDialog.SetActive(false);
        }

        private static string[] NumberChoices(int first, int last) =>
            new[] { "전체" }.Concat(Enumerable.Range(first, last - first + 1).Select(number => number.ToString())).ToArray();

        private void OnDetailedChoiceChanged(Facet facet, int index)
        {
            if (facet == Facet.Kind)
            {
                SyncDetailedKind();
                return;
            }
            if (index != 0 && (facet == Facet.Frame || facet == Facet.SpellTrap))
            {
                uint mask = facet == Facet.Frame ? FrameOptions[index].mask : SpellTrapTypes[index].required;
                var kind = (DeckCardKind)_facetControls[(int)Facet.Kind].value;
                var implied = KindForMask(mask);
                if (facet == Facet.Frame && (mask & TYPE_MONSTER) != 0)
                    _facetControls[(int)Facet.SpellTrap].SetValueWithoutNotify(0);
                if (!KindAllowsMask(kind, mask) || kind == DeckCardKind.Any && implied != DeckCardKind.Any)
                {
                    _facetControls[(int)Facet.Kind].SetValueWithoutNotify((int)implied);
                    SyncDetailedKind();
                }
            }
            if (index == 0 || (facet != Facet.Level && facet != Facet.Rank && facet != Facet.LinkRating)) return;
            foreach (var metric in new[] { Facet.Level, Facet.Rank, Facet.LinkRating })
                if (metric != facet) _facetControls[(int)metric].SetValueWithoutNotify(0);
        }

        private static DeckCardKind KindForMask(uint mask)
        {
            if ((mask & TYPE_SPELL) != 0) return DeckCardKind.Spell;
            if ((mask & TYPE_TRAP) != 0) return DeckCardKind.Trap;
            if ((mask & TYPE_EXTRA) != 0) return DeckCardKind.ExtraDeck;
            if ((mask & TYPE_RITUAL) != 0) return DeckCardKind.MainMonster;
            return DeckCardKind.Any;
        }

        private static bool KindAllowsMask(DeckCardKind kind, uint mask)
        {
            if (mask == 0 || kind == DeckCardKind.Any) return true;
            if (kind == DeckCardKind.Spell) return (mask & TYPE_SPELL) != 0;
            if (kind == DeckCardKind.Trap) return (mask & TYPE_TRAP) != 0;
            return (mask & TYPE_MONSTER) != 0 &&
                (kind == DeckCardKind.MainMonster ? (mask & TYPE_EXTRA) == 0 : (mask & TYPE_RITUAL) == 0);
        }

        private static void ClearIncompatibleFacets(DeckCardKind kind, Func<Facet, int> get, Action<Facet> clear)
        {
            if (!KindAllowsMask(kind, FrameOptions[get(Facet.Frame)].mask)) clear(Facet.Frame);
            if (!KindAllowsMask(kind, SpellTrapTypes[get(Facet.SpellTrap)].required)) clear(Facet.SpellTrap);
            if (kind == DeckCardKind.Spell || kind == DeckCardKind.Trap)
            {
                foreach (var facet in MonsterFacets) clear(facet);
            }
            else if (kind == DeckCardKind.MainMonster)
            {
                clear(Facet.Rank);
                clear(Facet.LinkRating);
                clear(Facet.LinkMarker);
            }
        }

        private void ChangeTrunkKind(int choice)
        {
            var kind = (DeckCardKind)choice;
            ClearIncompatibleFacets(kind, facet => _savedFacets[(int)facet], facet => _savedFacets[(int)facet] = 0);
            if (kind == DeckCardKind.Spell || kind == DeckCardKind.Trap)
                Array.Clear(_savedStats, 0, _savedStats.Length);
            RefreshTrunkFilter();
        }

        private void SyncDetailedKind()
        {
            var kind = (DeckCardKind)_facetControls[(int)Facet.Kind].value;
            ClearIncompatibleFacets(kind, facet => _facetControls[(int)facet].value,
                facet => _facetControls[(int)facet].SetValueWithoutNotify(0));
            bool nonMonster = kind == DeckCardKind.Spell || kind == DeckCardKind.Trap;
            foreach (var facet in MonsterFacets)
                _facetControls[(int)facet].interactable = !nonMonster;
            if (kind == DeckCardKind.MainMonster)
            {
                _facetControls[(int)Facet.Rank].interactable = false;
                _facetControls[(int)Facet.LinkRating].interactable = false;
                _facetControls[(int)Facet.LinkMarker].interactable = false;
            }
            foreach (var input in _statInputs)
            {
                input.interactable = !nonMonster;
                if (nonMonster) input.SetTextWithoutNotify("");
            }
        }

        private void OpenDetailedSearch()
        {
            for (int i = 0; i < _facetControls.Length; i++)
                _facetControls[i].SetValueWithoutNotify(_savedFacets[i]);
            _facetControls[(int)Facet.Kind].SetValueWithoutNotify(_kindFilter.value);
            for (int i = 0; i < _statInputs.Length; i++)
                _statInputs[i].SetTextWithoutNotify(_savedStats[i]?.ToString() ?? "");
            _effectTextInput.SetTextWithoutNotify(_savedEffectText);
            SyncDetailedKind();
            _filterError.text = "";
            EventSystem.current?.SetSelectedGameObject(null);
            _cardFilterDialog.transform.SetAsLastSibling();
            _cardFilterDialog.SetActive(true);
        }

        private void ResetDetailedDraft()
        {
            foreach (var choice in _facetControls) choice.SetValueWithoutNotify(0);
            foreach (var input in _statInputs) input.SetTextWithoutNotify("");
            _effectTextInput.SetTextWithoutNotify("");
            SyncDetailedKind();
            _filterError.text = "";
        }

        private void ApplyDetailedSearch()
        {
            var bounds = new int?[_statInputs.Length];
            for (int i = 0; i < bounds.Length; i++)
            {
                string raw = _statInputs[i].text.Trim();
                if (raw.Length == 0) continue;
                if (!int.TryParse(raw, out int stat) || stat < 0)
                {
                    _filterError.text = "공격력과 수비력은 0 이상의 정수로 입력하세요.";
                    return;
                }
                bounds[i] = stat;
            }
            if (bounds[0] > bounds[1] || bounds[2] > bounds[3])
            {
                _filterError.text = "최솟값은 최댓값보다 클 수 없습니다.";
                return;
            }

            for (int i = 0; i < _facetControls.Length; i++)
                _savedFacets[i] = _facetControls[i].value;
            Array.Copy(bounds, _savedStats, bounds.Length);
            _savedEffectText = _effectTextInput.text.Trim();
            _kindFilter.SetValueWithoutNotify(_savedFacets[(int)Facet.Kind]);
            _cardFilterDialog.SetActive(false);
            RefreshTrunkFilter();
        }

        private void ClearDetailedSearch()
        {
            Array.Clear(_savedFacets, 0, _savedFacets.Length);
            Array.Clear(_savedStats, 0, _savedStats.Length);
            _savedEffectText = "";
        }

        private void UpdateDetailedSearchButton()
        {
            int active = _savedFacets.Skip(1).Count(index => index != 0) + _savedStats.Count(value => value.HasValue);
            if (_savedEffectText.Length > 0) active++;
            _detailButtonText.text = active == 0 ? "상세 검색" : $"상세 검색 ({active})";
        }

        private DeckCardFilter CreateTrunkFilter(string query, DeckCardKind kind)
        {
            int Selected(Facet facet) => _savedFacets[(int)facet];
            int? Rating(Facet facet, int first) => Selected(facet) == 0 ? null : Selected(facet) + first - 1;
            var frame = FrameOptions[Selected(Facet.Frame)];
            var monster = MonsterTypes[Selected(Facet.MonsterType)];
            var spellTrap = SpellTrapTypes[Selected(Facet.SpellTrap)];
            return new DeckCardFilter(query, _savedEffectText, kind,
                requiredTypes: frame.mask | monster.required | spellTrap.required,
                excludedTypes: spellTrap.excluded,
                race: RaceOptions[Selected(Facet.Race)].mask, attribute: AttributeOptions[Selected(Facet.Attribute)].mask,
                level: Rating(Facet.Level, 1), rank: Rating(Facet.Rank, 0), linkRating: Rating(Facet.LinkRating, 1),
                effectCategories: DeckCardFilter.EffectOptions[Selected(Facet.Effect)].mask,
                minAttack: _savedStats[0], maxAttack: _savedStats[1], minDefense: _savedStats[2], maxDefense: _savedStats[3],
                pendulumScale: Rating(Facet.PendulumScale, 0), linkMarker: LinkMarkerOptions[Selected(Facet.LinkMarker)].mask);
        }
    }
}
