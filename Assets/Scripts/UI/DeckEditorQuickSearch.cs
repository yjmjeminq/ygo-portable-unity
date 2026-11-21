using System.Collections.Generic;
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

        private int _quickFilter;
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

            _quickFilterButtons = new Button[QuickFilterLabels.Length];
            _quickFilterCaptions = new Text[QuickFilterLabels.Length];
            float x = 0f;
            for (int i = 0; i < QuickFilterLabels.Length; i++)
            {
                int filter = i;
                float width = QuickFilterLabels[i].Length * 15f + 34f;
                if (_mobileLayout) width = Mathf.Max(width, MobileSafeArea.MinimumTouchSize(row) + 6f);
                var chip = LotdUiKit.CreateRect($"Chip_{i}", content, Vector2.zero, Vector2.up,
                    new Vector2(x + 2f, 1f), new Vector2(x + width - 2f, -1f));
                var button = LotdUiKit.CreateLotdButton(chip, "Button", QuickFilterLabels[i], () =>
                    SetQuickFilter(_quickFilter == filter ? 0 : filter), 15);
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
            if (!_mobileLayout || _trunkPanel.rect.height <= 0f) return;
            const float top = .757f;
            float minimum = MobileSafeArea.MinimumTouchSize(_trunkPanel) + 2f;
            float bottom = top - Mathf.Min(.24f, minimum / _trunkPanel.rect.height);
            SetVerticalAnchors((RectTransform)_quickFilterScroll.transform, bottom, top);
            SetVerticalAnchors((RectTransform)_trunkScroll.transform, .101f, bottom - .008f);
        }

        private void SetQuickFilter(int filter, bool refresh = true)
        {
            _quickFilter = filter;
            if (_quickFilterCaptions != null)
            {
                for (int i = 0; i < _quickFilterCaptions.Length; i++)
                {
                    _quickFilterCaptions[i].text = i == filter ? "• " + QuickFilterLabels[i] : QuickFilterLabels[i];
                    _quickFilterCaptions[i].color = i == filter ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary;
                }
            }
            if (refresh) RefreshTrunkFilter();
        }

        private bool MatchesQuickFilter(CardInfo card) => _quickFilter switch
        {
            0 => true,
            HandTrapFilter => HandTraps.Contains(DeckGroupKey(card.code)),
            BoardBreakerFilter => BoardBreakers.Contains(DeckGroupKey(card.code)),
            _ => (card.type & QuickFilterTypes[_quickFilter]) != 0,
        };
    }
}
