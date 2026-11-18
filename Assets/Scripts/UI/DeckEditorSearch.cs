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
            Level, Rank, LinkRating, PendulumScale, Attack, Defense, EffectText, Count,
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

        private readonly Button[] _facetButtons = new Button[(int)Facet.Count];
        private readonly Text[] _facetLabels = new Text[(int)Facet.Count];
        private readonly Text[] _facetValues = new Text[(int)Facet.Count];
        private readonly IReadOnlyList<string>[] _facetOptions = new IReadOnlyList<string>[(int)Facet.Count];
        private readonly int[] _draftFacets = new int[(int)Facet.Count];
        private readonly int[] _savedFacets = new int[(int)Facet.Count];
        private readonly InputField[] _statInputs = new InputField[4];
        private readonly int?[] _draftStats = new int?[4];
        private readonly int?[] _savedStats = new int?[4];
        private readonly List<Button> _optionButtons = new List<Button>();
        private string _savedEffectText = "";
        private string _queryBeforeFilters = "";
        private GameObject _cardFilterDialog;
        private InputField _detailedQueryInput;
        private InputField _effectTextInput;
        private Text _filterError;
        private Text _detailButtonText;
        private Text _optionHeading;
        private RectTransform _optionRows;
        private ScrollRect _optionScroll;
        private RectTransform _attackEditor;
        private RectTransform _defenseEditor;
        private RectTransform _effectTextEditor;
        private Facet _activeFacet;

        private void BuildDetailedSearch(Transform parent)
        {
            var panel = LotdUiKit.CreateRect("TrunkFilterPanel", parent, _deckPanel.anchorMin, _deckPanel.anchorMax,
                Vector2.zero, Vector2.zero);
            _cardFilterDialog = panel.gameObject;
            var conditions = LotdUiKit.CreateMenuPanel(panel, "Conditions", Vector2.zero, new Vector2(.526f, 1f));
            var options = LotdUiKit.CreateMenuPanel(panel, "Options", new Vector2(.536f, 0f), Vector2.one);
            AddFilterHeading(conditions, "보관함 필터");
            _optionHeading = AddFilterHeading(options, "");

            var queryRow = LotdUiKit.CreateRect("TextSearch", conditions, new Vector2(.087f, .803f),
                new Vector2(.638f, .868f), Vector2.zero, Vector2.zero);
            _detailedQueryInput = LotdUiKit.CreateInputField(queryRow, "Query", "카드명·효과·카드 번호", 16);
            _detailedQueryInput.onValueChanged.AddListener(_ => RefreshDetailedDraft());
            var archetype = LotdUiKit.CreateRect("ArchetypeSearch", conditions, new Vector2(.656f, .803f),
                new Vector2(.913f, .868f), Vector2.zero, Vector2.zero);
            LotdUiKit.CreateLotdButton(archetype, "Button", "카드군 검색", OpenArchetypeSearch, 14);

            var conditionRows = BuildFilterRows(conditions, "ConditionsList", .105f, .783f, out _);
            _optionRows = BuildFilterRows(options, "OptionList", .105f, .868f, out _optionScroll);

            void AddFacet(Facet facet, string label, string icon, IReadOnlyList<string> values = null)
            {
                int index = (int)facet;
                _facetOptions[index] = values;
                var row = AddFilterRow(conditionRows, facet.ToString());
                var button = LotdUiKit.CreateLotdButton(row, "Button", label, () => ShowDetailedFacet(facet), 15);
                _facetButtons[index] = button;
                var caption = button.GetComponentInChildren<Text>();
                _facetLabels[index] = caption;
                caption.alignment = TextAnchor.MiddleLeft;
                caption.rectTransform.anchorMin = new Vector2(.137f, .06f);
                caption.rectTransform.anchorMax = new Vector2(.58f, .94f);
                caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
                var sprite = LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_FILTER_CARD_" + icon);
                if (sprite != null)
                {
                    var image = LotdUiKit.CreateRect("Icon", button.transform, new Vector2(.039f, .10f),
                        new Vector2(.121f, .90f), Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(image.gameObject, sprite, Color.white, true, false);
                }
                var value = LotdUiKit.CreateRect("Value", button.transform, new Vector2(.595f, .06f),
                    new Vector2(.96f, .94f), Vector2.zero, Vector2.zero);
                _facetValues[index] = LotdUiKit.AddFittedText(value, "Text", "", 14, 10, TextAnchor.MiddleRight,
                    LotdUiKit.TextPrimary);
            }

            AddFacet(Facet.Kind, "카드 종류", "FRAME", new[] { "전체", "메인 몬스터", "마법", "함정", "엑스트라 몬스터" });
            AddFacet(Facet.Frame, "카드 프레임", "FRAME", FrameOptions.Select(item => item.label).ToArray());
            AddFacet(Facet.MonsterType, "몬스터 특성", "TYPE", MonsterTypes.Select(item => item.label).ToArray());
            AddFacet(Facet.SpellTrap, "마법·함정 종류", "ICON", SpellTrapTypes.Select(item => item.label).ToArray());
            AddFacet(Facet.Race, "종족", "TYPE", RaceOptions.Select(item => item.label).ToArray());
            AddFacet(Facet.Attribute, "속성", "ATTR", AttributeOptions.Select(item => item.label).ToArray());
            AddFacet(Facet.Level, "레벨", "LEVEL", NumberChoices(1, 13));
            AddFacet(Facet.Rank, "랭크", "RANK", NumberChoices(0, 13));
            AddFacet(Facet.LinkRating, "링크", "LINKNUM", NumberChoices(1, 8));
            AddFacet(Facet.LinkMarker, "링크 마커", "LINKDIR", LinkMarkerOptions.Select(item => item.label).ToArray());
            AddFacet(Facet.PendulumScale, "펜듈럼 스케일", "PEND", NumberChoices(0, 13));
            AddFacet(Facet.Effect, "효과 분류", "GENRE", DeckCardFilter.EffectOptions.Select(item => item.label).ToArray());
            AddFacet(Facet.Attack, "공격력", "ATK");
            AddFacet(Facet.Defense, "수비력", "DEF");
            AddFacet(Facet.EffectText, "효과 문구", "GENRE");

            _attackEditor = BuildStatInputs(options, "Attack", 0);
            _defenseEditor = BuildStatInputs(options, "Defense", 2);
            _effectTextEditor = LotdUiKit.CreateRect("EffectText", options, new Vector2(.087f, .803f),
                new Vector2(.913f, .868f), Vector2.zero, Vector2.zero);
            _effectTextInput = LotdUiKit.CreateInputField(_effectTextEditor, "Query", "예: 특수 소환", 16);
            _effectTextInput.onValueChanged.AddListener(_ => RefreshDetailedDraft());
            var status = LotdUiKit.CreateRect("Status", options, new Vector2(.087f, .033f), new Vector2(.913f, .099f),
                Vector2.zero, Vector2.zero);
            _filterError = LotdUiKit.AddFittedText(status, "Text", "", 15, 11, TextAnchor.MiddleCenter, LotdUiKit.RedAccent);

            var footer = LotdUiKit.CreateRect("Actions", conditions, new Vector2(.084f, .039f), new Vector2(.916f, .094f),
                Vector2.zero, Vector2.zero);
            void ActionButton(string name, string label, float left, float right, Action action, bool gold = false)
            {
                var slot = LotdUiKit.CreateRect(name, footer, new Vector2(left, 0f), new Vector2(right, 1f),
                    Vector2.zero, Vector2.zero);
                LotdUiKit.CreateLotdButton(slot, "Button", label, action, 15, gold);
            }
            ActionButton("Reset", "초기화", 0f, .31f, ResetTrunkFilters);
            ActionButton("Cancel", "취소", .345f, .655f, CloseDetailedSearch);
            ActionButton("Apply", "적용", .69f, 1f, ApplyDetailedSearch, true);
            _cardFilterDialog.SetActive(false);
        }

        private static Text AddFilterHeading(RectTransform panel, string title)
        {
            var header = LotdUiKit.CreateRect("Heading", panel, new Vector2(.087f, .884f), new Vector2(.913f, .946f),
                Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(header.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelHeader, false, false);
            return LotdUiKit.AddFittedText(header, "Title", title, 21, 14, TextAnchor.MiddleCenter,
                Color.white, FontStyle.Bold);
        }

        private static RectTransform BuildFilterRows(RectTransform panel, string name, float bottom, float top, out ScrollRect scroll)
        {
            var holder = LotdUiKit.CreateRect(name, panel, new Vector2(.087f, bottom), new Vector2(.913f, top),
                Vector2.zero, Vector2.zero);
            var viewport = LotdUiKit.CreateRect("Viewport", holder, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-15f, 0f));
            LotdUiKit.AddImage(viewport.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            var rows = LotdUiKit.CreateRect("Rows", viewport, new Vector2(0f, 1f), Vector2.one,
                Vector2.zero, Vector2.zero);
            rows.pivot = new Vector2(.5f, 1f);
            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childControlWidth = layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            rows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = holder.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = rows;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            scroll.verticalScrollbar = AddDeckScrollbar(holder);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return rows;
        }

        private static RectTransform AddFilterRow(RectTransform rows, string name)
        {
            var row = LotdUiKit.FillRect(name, rows);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 31f;
            return row;
        }

        private RectTransform BuildStatInputs(RectTransform parent, string name, int start)
        {
            var group = LotdUiKit.CreateRect(name, parent, new Vector2(.087f, .66f), new Vector2(.913f, .868f),
                Vector2.zero, Vector2.zero);
            for (int i = 0; i < 2; i++)
            {
                string label = i == 0 ? "최소" : "최대";
                var row = LotdUiKit.CreateRect(label, group, new Vector2(0f, i == 0 ? .54f : .04f),
                    new Vector2(1f, i == 0 ? 1f : .5f), Vector2.zero, Vector2.zero);
                var input = LotdUiKit.CreateInputField(row, "Number", label, 16);
                input.contentType = InputField.ContentType.IntegerNumber;
                input.characterLimit = 6;
                input.onValueChanged.AddListener(_ => RefreshDetailedDraft());
                _statInputs[start + i] = input;
            }
            return group;
        }

        private static string[] NumberChoices(int first, int last) =>
            new[] { "전체" }.Concat(Enumerable.Range(first, last - first + 1).Select(number => number.ToString())).ToArray();

        private void OnDetailedChoiceChanged(Facet facet, int index)
        {
            _draftFacets[(int)facet] = index;
            if (facet == Facet.Kind)
            {
                SyncDetailedKind();
                return;
            }
            if (index != 0 && (facet == Facet.Frame || facet == Facet.SpellTrap))
            {
                uint mask = facet == Facet.Frame ? FrameOptions[index].mask : SpellTrapTypes[index].required;
                var kind = (DeckCardKind)_draftFacets[(int)Facet.Kind];
                var implied = KindForMask(mask);
                if (facet == Facet.Frame && (mask & TYPE_MONSTER) != 0)
                    _draftFacets[(int)Facet.SpellTrap] = 0;
                if (!KindAllowsMask(kind, mask) || kind == DeckCardKind.Any && implied != DeckCardKind.Any)
                {
                    _draftFacets[(int)Facet.Kind] = (int)implied;
                    SyncDetailedKind();
                }
            }
            if (index == 0 || (facet != Facet.Level && facet != Facet.Rank && facet != Facet.LinkRating)) return;
            foreach (var metric in new[] { Facet.Level, Facet.Rank, Facet.LinkRating })
                if (metric != facet) _draftFacets[(int)metric] = 0;
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
            var kind = (DeckCardKind)_draftFacets[(int)Facet.Kind];
            ClearIncompatibleFacets(kind, facet => _draftFacets[(int)facet], facet => _draftFacets[(int)facet] = 0);
            bool nonMonster = kind == DeckCardKind.Spell || kind == DeckCardKind.Trap;
            foreach (var facet in MonsterFacets)
                _facetButtons[(int)facet].interactable = !nonMonster;
            if (kind == DeckCardKind.MainMonster)
            {
                _facetButtons[(int)Facet.Rank].interactable = false;
                _facetButtons[(int)Facet.LinkRating].interactable = false;
                _facetButtons[(int)Facet.LinkMarker].interactable = false;
            }
            _facetButtons[(int)Facet.Attack].interactable = !nonMonster;
            _facetButtons[(int)Facet.Defense].interactable = !nonMonster;
            if (nonMonster)
            {
                foreach (var input in _statInputs) input.SetTextWithoutNotify("");
            }
        }

        private void ShowDetailedFacet(Facet facet)
        {
            _activeFacet = facet;
            _optionHeading.text = _facetLabels[(int)facet].text;
            var choices = _facetOptions[(int)facet];
            _attackEditor.gameObject.SetActive(facet == Facet.Attack);
            _defenseEditor.gameObject.SetActive(facet == Facet.Defense);
            _effectTextEditor.gameObject.SetActive(facet == Facet.EffectText);
            _optionScroll.gameObject.SetActive(choices != null);

            if (choices != null)
            {
                while (_optionButtons.Count < choices.Count)
                {
                    int choice = _optionButtons.Count;
                    var row = AddFilterRow(_optionRows, $"Option_{choice}");
                    _optionButtons.Add(LotdUiKit.CreateLotdButton(row, "Button", "", () =>
                    {
                        OnDetailedChoiceChanged(_activeFacet, choice);
                        RefreshDetailedDraft();
                    }, 15));
                }
                for (int i = 0; i < _optionButtons.Count; i++)
                    _optionButtons[i].transform.parent.gameObject.SetActive(i < choices.Count);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_optionRows);
                _optionScroll.verticalNormalizedPosition = 1f;
                UpdateDeckScrollbar(_optionScroll);
            }

            UpdateDetailedSelectionLabels();
        }

        private void UpdateDetailedSelectionLabels()
        {
            for (int i = 0; i < _facetValues.Length; i++)
            {
                string value = (Facet)i switch
                {
                    Facet.Attack => FormatStatRange(_draftStats[0], _draftStats[1]),
                    Facet.Defense => FormatStatRange(_draftStats[2], _draftStats[3]),
                    Facet.EffectText => _effectTextInput.text.Trim(),
                    _ => _facetOptions[i][_draftFacets[i]],
                };
                bool enabled = _facetButtons[i].interactable;
                bool selected = (Facet)i == _activeFacet;
                _facetLabels[i].color = !enabled ? LotdUiKit.TextMuted : selected ? LotdUiKit.GoldAccent : Color.white;
                _facetValues[i].text = value.Length > 0 ? value : "전체";
                _facetValues[i].color = enabled && _facetValues[i].text != "전체"
                    ? LotdUiKit.GoldAccent : LotdUiKit.TextMuted;
            }

            var choices = _facetOptions[(int)_activeFacet];
            if (choices == null) return;
            for (int i = 0; i < choices.Count; i++)
            {
                var label = _optionButtons[i].GetComponentInChildren<Text>();
                bool selected = i == _draftFacets[(int)_activeFacet];
                label.text = selected ? $"●  {choices[i]}" : choices[i];
                label.color = selected ? LotdUiKit.GoldAccent : Color.white;
            }
        }

        private static string FormatStatRange(int? min, int? max)
        {
            if (min.HasValue && max.HasValue) return $"{min}–{max}";
            if (min.HasValue) return $"{min} 이상";
            return max.HasValue ? $"{max} 이하" : "전체";
        }

        private void ToggleDetailedSearch()
        {
            if (_cardFilterDialog.activeSelf) CloseDetailedSearch();
            else OpenDetailedSearch();
        }

        private void OpenDetailedSearch()
        {
            if (_cardFilterDialog.activeSelf) return;
            _queryBeforeFilters = _searchInp.text;
            _savedFacets[(int)Facet.Kind] = _kindFilter.value;
            Array.Copy(_savedFacets, _draftFacets, _savedFacets.Length);
            Array.Copy(_savedStats, _draftStats, _savedStats.Length);
            _detailedQueryInput.SetTextWithoutNotify(_queryBeforeFilters);
            for (int i = 0; i < _statInputs.Length; i++)
                _statInputs[i].SetTextWithoutNotify(_savedStats[i]?.ToString() ?? "");
            _effectTextInput.SetTextWithoutNotify(_savedEffectText);
            SyncDetailedKind();
            EventSystem.current?.SetSelectedGameObject(null);
            _searchInp.interactable = false;
            _kindFilter.interactable = false;
            _deckPanel.gameObject.SetActive(false);
            _cardFilterDialog.SetActive(true);
            UiEntranceMotion.Play(_cardFilterDialog);
            ShowDetailedFacet(Facet.Kind);
            RefreshDetailedDraft();
        }

        private void CloseDetailedSearch()
        {
            if (!_cardFilterDialog.activeSelf) return;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(_cardFilterDialog.transform))
                EventSystem.current.SetSelectedGameObject(null);
            _cardFilterDialog.SetActive(false);
            _deckPanel.gameObject.SetActive(true);
            _searchInp.SetTextWithoutNotify(_queryBeforeFilters);
            _kindFilter.SetValueWithoutNotify(_savedFacets[(int)Facet.Kind]);
            _searchInp.interactable = true;
            _kindFilter.interactable = true;
            RefreshTrunkFilter();
        }

        private void ResetDetailedDraft()
        {
            Array.Clear(_draftFacets, 0, _draftFacets.Length);
            foreach (var input in _statInputs) input.SetTextWithoutNotify("");
            _detailedQueryInput.SetTextWithoutNotify("");
            _effectTextInput.SetTextWithoutNotify("");
            SyncDetailedKind();
            RefreshDetailedDraft();
        }

        private bool ReadDetailedStats()
        {
            var bounds = new int?[_statInputs.Length];
            for (int i = 0; i < bounds.Length; i++)
            {
                string raw = _statInputs[i].text.Trim();
                if (raw.Length == 0) continue;
                if (!int.TryParse(raw, out int stat) || stat < 0)
                {
                    _filterError.text = "공격력과 수비력은\n0 이상의 정수로 입력하세요.";
                    return false;
                }
                bounds[i] = stat;
            }
            if (bounds[0] > bounds[1] || bounds[2] > bounds[3])
            {
                _filterError.text = "최솟값은 최댓값보다\n클 수 없습니다.";
                return false;
            }

            Array.Copy(bounds, _draftStats, bounds.Length);
            _filterError.text = "";
            return true;
        }

        private void RefreshDetailedDraft()
        {
            if (!_cardFilterDialog.activeSelf) return;
            ReadDetailedStats();
            UpdateDetailedSelectionLabels();
            _searchInp.SetTextWithoutNotify(_detailedQueryInput.text);
            _kindFilter.SetValueWithoutNotify(_draftFacets[(int)Facet.Kind]);
            RefreshTrunkFilter();
        }

        private void ApplyDetailedSearch()
        {
            if (!ReadDetailedStats()) return;
            Array.Copy(_draftFacets, _savedFacets, _draftFacets.Length);
            Array.Copy(_draftStats, _savedStats, _draftStats.Length);
            _savedEffectText = _effectTextInput.text.Trim();
            _queryBeforeFilters = _detailedQueryInput.text.Trim();
            CloseDetailedSearch();
        }

        private void ClearDetailedSearch()
        {
            Array.Clear(_savedFacets, 0, _savedFacets.Length);
            Array.Clear(_savedStats, 0, _savedStats.Length);
            _savedEffectText = "";
        }

        private void UpdateDetailedSearchButton()
        {
            if (_cardFilterDialog.activeSelf)
            {
                _detailButtonText.text = "필터 닫기";
                return;
            }
            int active = _savedFacets.Skip(1).Count(index => index != 0);
            if (_kindFilter.value != 0) active++;
            if (_searchInp.text.Trim().Length > 0) active++;
            if (_savedStats[0].HasValue || _savedStats[1].HasValue) active++;
            if (_savedStats[2].HasValue || _savedStats[3].HasValue) active++;
            if (_savedEffectText.Length > 0) active++;
            _detailButtonText.text = active == 0 ? "필터" : $"필터 ({active})";
        }

        private DeckCardFilter CreateTrunkFilter(string query, DeckCardKind kind)
        {
            bool editing = _cardFilterDialog.activeSelf;
            var facets = editing ? _draftFacets : _savedFacets;
            var stats = editing ? _draftStats : _savedStats;
            int Selected(Facet facet) => facets[(int)facet];
            int? Rating(Facet facet, int first) => Selected(facet) == 0 ? null : Selected(facet) + first - 1;
            var frame = FrameOptions[Selected(Facet.Frame)];
            var monster = MonsterTypes[Selected(Facet.MonsterType)];
            var spellTrap = SpellTrapTypes[Selected(Facet.SpellTrap)];
            return new DeckCardFilter(query, editing ? _effectTextInput.text.Trim() : _savedEffectText, kind,
                requiredTypes: frame.mask | monster.required | spellTrap.required,
                excludedTypes: spellTrap.excluded,
                race: RaceOptions[Selected(Facet.Race)].mask, attribute: AttributeOptions[Selected(Facet.Attribute)].mask,
                level: Rating(Facet.Level, 1), rank: Rating(Facet.Rank, 0), linkRating: Rating(Facet.LinkRating, 1),
                effectCategories: DeckCardFilter.EffectOptions[Selected(Facet.Effect)].mask,
                minAttack: stats[0], maxAttack: stats[1], minDefense: stats[2], maxDefense: stats[3],
                pendulumScale: Rating(Facet.PendulumScale, 0), linkMarker: LinkMarkerOptions[Selected(Facet.LinkMarker)].mask);
        }
    }
}
