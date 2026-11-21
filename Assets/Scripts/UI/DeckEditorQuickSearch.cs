using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DeckEditorUI
    {
        private const int HandTrapFilter = 1;
        private const int BoardBreakerFilter = 2;
        private static readonly string[] QuickFilterLabels =
            { "전체", "패트랩", "범용 돌파", "튜너", "융합", "싱크로", "엑시즈", "링크" };
        private static readonly uint[] QuickFilterTypes =
            { 0, 0, 0, YgoConstants.TYPE_TUNER, YgoConstants.TYPE_FUSION, YgoConstants.TYPE_SYNCHRO,
                YgoConstants.TYPE_XYZ, YgoConstants.TYPE_LINK };
        private static readonly HashSet<uint> HandTraps = new()
        {
            14558127, 23434538, 10045474, 97268402, 27204311, 94145021, 59438930, 38814750,
            42141493, 84192580, 87126721, 73642296, 52038441, 62015408, 24508238, 34267821, 67750322, 46502744, 91800273,
        };
        private static readonly HashSet<uint> BoardBreakers = new()
        {
            25311006, 15693423, 14532163, 18144506, 12580477, 24299458, 54693926, 53129443,
            99330325, 48130397, 10000080, 102380, 55063751, 8267140, 23002292, 35480699, 35269904, 19613556,
        };

        private static readonly (string label, uint type)[] MobileFrameChips =
        {
            ("몬스터", YgoConstants.TYPE_MONSTER), ("마법", YgoConstants.TYPE_SPELL),
            ("함정", YgoConstants.TYPE_TRAP), ("의식", YgoConstants.TYPE_RITUAL),
            ("펜듈럼", YgoConstants.TYPE_PENDULUM),
        };
        private enum QuickChipKind { Preset, Frame, Attribute, Race }
        private (string label, QuickChipKind kind, int index)[] _quickChips;
        private int _quickFilter;
        private int _mobileFrameFilter;
        private int _mobileAttributeFilter;
        private int _mobileRaceFilter;
        private ScrollRect _quickFilterScroll;
        private Button[] _quickFilterButtons;
        private Text[] _quickFilterCaptions;

        private void BuildQuickFilters(RectTransform panel, Vector2 left, Vector2 right)
        {
            var row = LotdUiKit.CreateRect("QuickFilters", panel, new Vector2(0f, .702f),
                new Vector2(1f, .757f), left, right);
            LotdUiKit.AddImage(row.gameObject, LotdUiKit.WhiteSprite, LotdUiKit.PanelNavy);
            var viewport = LotdUiKit.FillRect("Viewport", row);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = LotdUiKit.CreateRect("Chips", viewport, Vector2.zero, Vector2.up,
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0f, .5f);
            var scroll = _quickFilterScroll = row.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;

            var presets = QuickFilterLabels.Select((label, i) => (label, QuickChipKind.Preset, i));
            _quickChips = (!_mobileLayout ? presets : presets
                .Concat(MobileFrameChips.Select((chip, i) => (chip.label, QuickChipKind.Frame, i + 1)))
                .Concat(AttributeOptions.Skip(1).Select((chip, i) => ($"속성 {chip.label}", QuickChipKind.Attribute, i + 1)))
                .Concat(RaceOptions.Skip(1).Select((chip, i) => (chip.label, QuickChipKind.Race, i + 1)))).ToArray();
            _quickFilterButtons = new Button[_quickChips.Length];
            _quickFilterCaptions = new Text[_quickChips.Length];
            float x = 0f;
            for (int i = 0; i < _quickChips.Length; i++)
            {
                var filter = _quickChips[i];
                float width = filter.label.Length * (_mobileLayout ? 22f : 15f) + 34f;
                if (_mobileLayout) width = Mathf.Max(width, MobileSafeArea.MinimumTouchSize(row) + 6f);
                var chip = LotdUiKit.CreateRect($"Chip_{i}", content, Vector2.zero, Vector2.up,
                    new Vector2(x + 2f, 1f), new Vector2(x + width - 2f, -1f));
                var button = LotdUiKit.CreateLotdButton(chip, "Button", filter.label, () =>
                    ToggleQuickFilter(filter.kind, filter.index), _mobileLayout ? 22 : 15);
                _quickFilterButtons[i] = button;
                _quickFilterCaptions[i] = button.GetComponentInChildren<Text>();
                if (i > 0)
                {
                    var previous = _quickFilterButtons[i - 1];
                    var navigation = previous.navigation;
                    navigation.mode = Navigation.Mode.Explicit;
                    navigation.selectOnRight = button;
                    previous.navigation = navigation;
                    navigation = button.navigation;
                    navigation.mode = Navigation.Mode.Explicit;
                    navigation.selectOnLeft = previous;
                    button.navigation = navigation;
                }
                x += width;
            }
            content.sizeDelta = new Vector2(x, 0f);
            content.anchoredPosition = Vector2.zero;
            SetQuickFilter(0, refresh: false);
        }

        private void LayoutMobileQuickFilters()
        {
            if (!_mobileLayout || _mobileEditor == null || _trunkPanel.rect.height <= 0f) return;
            bool typing = _mobileKeyboardInset > 0f;
            var heading = (RectTransform)_trunkPanel.Find("TrunkHeading");
            var searchOptions = (RectTransform)_trunkPanel.Find("SearchOptions");
            heading.gameObject.SetActive(!typing);
            searchOptions.gameObject.SetActive(!typing);
            _quickFilterScroll.gameObject.SetActive(!typing);
            var searchRow = (RectTransform)_trunkPanel.Find("SearchRow");
            searchRow.anchorMax = new Vector2(typing ? 1f : .55f, 1f);
            searchOptions.anchorMin = new Vector2(.55f, 1f);
            heading.anchorMin = new Vector2(.78f, 1f);
            heading.anchorMax = Vector2.one;
            var chips = (RectTransform)_quickFilterScroll.transform;
            chips.anchorMax = new Vector2(.78f, 1f);

            const float searchTop = 9f;
            float touch = MobileSafeArea.MinimumTouchSize(_trunkPanel);
            float searchHeight = Mathf.Clamp(touch * .47f, 47f, 62f) * MobileHeightScale;
            float chipsHeight = Mathf.Clamp(touch * .51f, 52f, 69f) * MobileHeightScale;
            PositionFromTop(searchRow, searchTop, searchTop + searchHeight);
            PositionFromTop(searchOptions, searchTop, searchTop + searchHeight);
            float chipsTop = searchTop + searchHeight + 5f;
            PositionFromTop(chips, chipsTop, chipsTop + chipsHeight);
            PositionFromTop(heading, chipsTop, chipsTop + chipsHeight);
            var list = (RectTransform)_trunkScroll.transform;
            list.anchorMin = new Vector2(0f, 0f);
            list.anchorMax = new Vector2(1f, 1f);
            list.offsetMin = new Vector2(list.offsetMin.x, 10f);
            list.offsetMax = new Vector2(list.offsetMax.x, -(typing ? chipsTop : chipsTop + chipsHeight + 5f));
            list.ForceUpdateRectTransforms();
        }

        private int SelectedQuickChip(QuickChipKind kind) => kind switch
        {
            QuickChipKind.Frame => _mobileFrameFilter,
            QuickChipKind.Attribute => _mobileAttributeFilter,
            QuickChipKind.Race => _mobileRaceFilter,
            _ => _quickFilter,
        };

        private void ToggleQuickFilter(QuickChipKind kind, int index)
        {
            if (kind == QuickChipKind.Preset && index == 0)
            {
                SetQuickFilter(0);
                return;
            }

            int selected = SelectedQuickChip(kind) == index ? 0 : index;
            switch (kind)
            {
                case QuickChipKind.Frame: _mobileFrameFilter = selected; break;
                case QuickChipKind.Attribute: _mobileAttributeFilter = selected; break;
                case QuickChipKind.Race: _mobileRaceFilter = selected; break;
                default: _quickFilter = selected; break;
            }
            RefreshQuickFilterChips();
            RefreshTrunkFilter();
        }

        private void SetQuickFilter(int filter, bool refresh = true)
        {
            _quickFilter = filter;
            if (filter == 0) _mobileFrameFilter = _mobileAttributeFilter = _mobileRaceFilter = 0;
            RefreshQuickFilterChips();
            if (refresh) RefreshTrunkFilter();
        }

        private void RefreshQuickFilterChips()
        {
            if (_quickFilterCaptions == null) return;
            for (int i = 0; i < _quickFilterCaptions.Length; i++)
            {
                var (label, kind, index) = _quickChips[i];
                var caption = _quickFilterCaptions[i];
                bool active = SelectedQuickChip(kind) == index && (index != 0 ||
                    _mobileFrameFilter == 0 && _mobileAttributeFilter == 0 && _mobileRaceFilter == 0);
                caption.text = active ? "• " + label : label;
                caption.color = active ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
            }
        }

        private bool MatchesQuickFilter(CardInfo card)
        {
            if (_mobileFrameFilter != 0 && (card.type & MobileFrameChips[_mobileFrameFilter - 1].type) == 0 ||
                _mobileAttributeFilter != 0 && (card.attr & AttributeOptions[_mobileAttributeFilter].mask) == 0 ||
                _mobileRaceFilter != 0 && (card.race & RaceOptions[_mobileRaceFilter].mask) == 0)
                return false;
            return _quickFilter switch
            {
                0 => true,
                HandTrapFilter => HandTraps.Contains(DeckGroupKey(card.code)),
                BoardBreakerFilter => BoardBreakers.Contains(DeckGroupKey(card.code)),
                _ => (card.type & QuickFilterTypes[_quickFilter]) != 0,
            };
        }
    }
}
