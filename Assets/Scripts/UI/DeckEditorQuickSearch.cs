using System;
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

        [Serializable]
        private sealed class RegulationSnapshot
        {
            public string effective;
            public int[] forbidden;
            public int[] limited;
            public int[] semiLimited;
        }

        private static Dictionary<int, int> _referenceLimits;
        private static string _regulationDate;
        private int _quickFilter;
        private ScrollRect _quickFilterScroll;
        private RectTransform _quickFilterLegend;
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

            LoadReferenceLimits();
            var legend = _quickFilterLegend = LotdUiKit.CreateRect("RegulationLegend", panel, new Vector2(0f, .668f),
                new Vector2(1f, .699f), left, right);
            LotdUiKit.AddFittedText(legend, "Text", $"OCG 참고 {_regulationDate}  ·  0 금지  1 제한  2 준제한",
                14, 10, TextAnchor.MiddleLeft, LotdUiKit.TextMuted);
        }

        private void LayoutMobileQuickFilters()
        {
            if (!_mobileLayout || _trunkPanel.rect.height <= 0f) return;
            const float top = .757f;
            float minimum = MobileSafeArea.MinimumTouchSize(_trunkPanel) + 2f;
            float bottom = top - Mathf.Min(.24f, minimum / _trunkPanel.rect.height);
            SetVerticalAnchors((RectTransform)_quickFilterScroll.transform, bottom, top);
            SetVerticalAnchors(_quickFilterLegend, bottom - .037f, bottom - .004f);
            SetVerticalAnchors((RectTransform)_trunkScroll.transform, .101f, bottom - .043f);
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

        private static void LoadReferenceLimits()
        {
            if (_referenceLimits != null) return;
            _referenceLimits = new Dictionary<int, int>();
            var asset = Resources.Load<TextAsset>("DeckReferenceLimits");
            if (asset == null) return;
            var snapshot = JsonUtility.FromJson<RegulationSnapshot>(asset.text);
            _regulationDate = snapshot.effective.Replace('-', '.');
            foreach (int id in snapshot.forbidden) _referenceLimits[id] = 0;
            foreach (int id in snapshot.limited) _referenceLimits[id] = 1;
            foreach (int id in snapshot.semiLimited) _referenceLimits[id] = 2;
        }

        private static void BuildRegulationBadge(CompositeCardView card)
        {
            var badge = LotdUiKit.CreateRect("Regulation", card.transform.Find("AspectBox"),
                new Vector2(.68f, .79f), new Vector2(.981f, .985f), Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(badge.gameObject, LotdUiKit.WhiteSprite, Color.white, false, false);
            var label = LotdUiKit.AddFittedText(badge, "Label", "", 20, 8, TextAnchor.MiddleCenter,
                Color.white, FontStyle.Bold);
            label.raycastTarget = false;
            var outline = badge.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.04f, 0.05f, 0.09f);
            outline.effectDistance = new Vector2(1.25f, -1.25f);
            badge.gameObject.SetActive(false);
        }

        private static void RefreshRegulationBadge(CompositeCardView view, uint code)
        {
            var badge = view.transform.Find("AspectBox/Regulation");
            var card = CardDatabase.Get(DeckGroupKey(code)) ?? CardDatabase.Get(code);
            int id = card == null ? 0 : card.konamiId > 0 ? card.konamiId : card.lotdId;
            LoadReferenceLimits();
            bool visible = _referenceLimits.TryGetValue(id, out int limit);
            badge.gameObject.SetActive(visible);
            if (!visible) return;
            badge.GetComponent<Image>().color = limit switch
            {
                0 => new Color(.74f, .11f, .18f),
                1 => new Color(.69f, .30f, .06f),
                _ => new Color(.12f, .43f, .70f),
            };
            badge.GetChild(0).GetComponent<Text>().text = limit.ToString();
        }
    }
}
