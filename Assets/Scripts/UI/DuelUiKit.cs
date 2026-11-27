using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public static class DuelUiKit
    {
        public const float CardAspectRatio = 400f / 580f;
        public static readonly Color BgDeep = new Color(0.03f, 0.065f, 0.135f, 1f);
        public static readonly Color PanelNavy = new Color(0.045f, 0.095f, 0.195f, 0.94f);
        public static readonly Color PanelHeader = new Color(0.07f, 0.16f, 0.32f, 0.96f);
        public static readonly Color CyanAccent = new Color(0.18f, 0.68f, 1f, 1f);
        public static readonly Color GoldAccent = new Color(0.97f, 0.77f, 0.25f, 1f);
        public static readonly Color RedAccent = new Color(0.92f, 0.28f, 0.28f, 1f);
        public static readonly Color GreenAccent = new Color(0.24f, 0.85f, 0.45f, 1f);
        public static readonly Color TextPrimary = new Color(0.95f, 0.97f, 1f, 1f);
        public static readonly Color TextMuted = new Color(0.64f, 0.76f, 0.90f, 1f);

        private static Font _fontKR;
        private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private static Sprite _whiteSprite;

        public static Font FontKR
        {
            get
            {
                if (_fontKR != null) return _fontKR;
                _fontKR = Resources.Load<Font>("Fonts/NotoSansKR");
                if (_fontKR == null) _fontKR = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 18);
                if (_fontKR == null) _fontKR = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _fontKR;
            }
        }

        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite != null) return _whiteSprite;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var arr = new Color32[16];
                for (int i = 0; i < 16; i++) arr[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(arr);
                tex.Apply();
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                return _whiteSprite;
            }
        }

        public static Sprite LoadSprite(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath)) return null;
            if (_spriteCache.TryGetValue(resourcesPath, out var sp) && sp != null) return sp;
            sp = Resources.Load<Sprite>(resourcesPath);
            if (sp == null)
            {
                var tex = Resources.Load<Texture2D>(resourcesPath);
                if (tex != null)
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            if (sp != null) _spriteCache[resourcesPath] = sp;
            return sp;
        }

        public static Sprite LoadOpponentPortrait(OpponentCharacter opponent) => DuelProfileArt.Opponent(opponent);

        public static Sprite GetRaceSprite(ulong race)
        {
            return YgoConstants.RaceSpriteNames.TryGetValue(race, out var key) ? DuelUiIcons.FindCardIdentity(key) : null;
        }

        public static Sprite GetAttributeSprite(uint attr)
        {
            return YgoConstants.AttributeSpriteNames.TryGetValue(attr, out var key) ? DuelUiIcons.FindCardIdentity(key) : null;
        }

        internal static Sprite GetSpellTrapSprite(uint type) => DuelUiIcons.FindSpellTrap(type);

        public static RectTransform CreateRect(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        public static RectTransform FillRect(string name, Transform parent, float pad = 0f)
        {
            return CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(pad, pad), new Vector2(-pad, -pad));
        }

        public static Image AddImage(GameObject go, Sprite sprite, Color color, bool preserveAspect = false, bool raycast = true)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : WhiteSprite;
            img.color = color;
            img.preserveAspect = preserveAspect;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text AddText(Transform parent, string name, string content, int fontSize,
            TextAnchor align, Color color, FontStyle style = FontStyle.Normal, bool raycast = false)
        {
            var rt = FillRect(name, parent);
            var txt = rt.gameObject.AddComponent<Text>();
            txt.font = FontKR;
            txt.text = content ?? "";
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = color;
            txt.fontStyle = style;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.raycastTarget = raycast;
            return txt;
        }

        public static Text AddFittedText(Transform parent, string name, string content, int fontSize, int minSize,
            TextAnchor align, Color color, FontStyle style = FontStyle.Normal)
        {
            var text = AddText(parent, name, content, fontSize, align, color, style);
            text.rectTransform.anchorMin = new Vector2(0.018f, 0.035f);
            text.rectTransform.anchorMax = new Vector2(0.982f, 0.965f);
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minSize;
            text.resizeTextMaxSize = fontSize;
            text.alignByGeometry = true;
            text.supportRichText = false;
            if (!parent.TryGetComponent<RectMask2D>(out _))
                parent.gameObject.AddComponent<RectMask2D>();
            return text;
        }

        public static RectTransform CreateBorderedPanel(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color? fill = null, Color? border = null)
        {
            var rt = CreateRect(name, parent, aMin, aMax, oMin, oMax);
            AddImage(rt.gameObject, WhiteSprite, fill ?? PanelNavy);
            var ol = rt.gameObject.AddComponent<Outline>();
            ol.effectColor = border ?? new Color(0.18f, 0.56f, 0.94f, 0.85f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            return rt;
        }

        public static RectTransform CreateMenuPanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, bool highlighted = false)
        {
            var panel = CreateRect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            AddImage(panel.gameObject, DuelUiChrome.Panel, Color.white).type = Image.Type.Sliced;
            if (highlighted)
            {
                var outline = panel.gameObject.AddComponent<Outline>();
                outline.effectColor = GoldAccent;
                outline.effectDistance = new Vector2(2f, -2f);
            }
            return panel;
        }

        public static void AddMenuHeader(Transform parent, string title)
        {
            var header = CreateRect("MenuHeader", parent, new Vector2(0.25f, 0.90f), new Vector2(0.75f, 0.995f),
                Vector2.zero, Vector2.zero);
            AddImage(header.gameObject, DuelUiChrome.Header, Color.white, false, false).type = Image.Type.Sliced;
            AddFittedText(header, "Heading", title, 32, 18, TextAnchor.MiddleCenter, TextPrimary, FontStyle.Bold);
        }

        public static Scrollbar AddVerticalScrollbar(Transform parent)
        {
            var track = CreateRect("Scrollbar", parent, new Vector2(1f, 0f), Vector2.one,
                new Vector2(-14f, 1f), new Vector2(-2f, -1f));
            AddImage(track.gameObject, WhiteSprite, new Color32(23, 33, 43, 255));
            var slide = FillRect("SlidingArea", track, 1f);
            var thumb = FillRect("Handle", slide);
            var image = AddImage(thumb.gameObject, WhiteSprite, new Color32(104, 142, 156, 255));
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = thumb;
            scrollbar.targetGraphic = image;
            return scrollbar;
        }

        public static Button CreateButton(Transform parent, string name, string label,
            Action onClick, int fontSize = 18, bool gold = false)
        {
            var rt = CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var img = AddImage(rt.gameObject, WhiteSprite, Color.white);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            DuelUiChrome.ApplyControl(btn, img, gold);
            var txt = AddText(rt, "Label", label, fontSize, TextAnchor.MiddleCenter, TextPrimary, FontStyle.Bold);
            var sh = txt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sh.effectDistance = new Vector2(1f, -1f);

            btn.onClick.AddListener(() =>
            {
                DuelAudio.PlayClick();
                onClick?.Invoke();
            });
            return btn;
        }

        internal static Button CreateRpgButton(Transform parent, string name, string label, Action onClick,
            int fontSize = 20, bool primary = false, bool prominent = false)
        {
            var button = CreateButton(parent, name, label, onClick, fontSize);
            RpgUiStyle.Control(button, primary, prominent);
            return button;
        }

        public static Button CreateWrappedButton(Transform parent, string name, string label, Action onClick,
            int fontSize, bool gold, float minHeight, Sprite icon = null, int sidePadding = 24)
        {
            var button = CreateButton(parent, name, label, onClick, fontSize, gold);
            button.gameObject.AddComponent<LayoutElement>().minHeight = minHeight;
            var layout = button.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(sidePadding, sidePadding, 8, 8);
            layout.spacing = 9f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var caption = button.GetComponentInChildren<Text>();
            caption.alignment = TextAnchor.MiddleLeft;
            caption.verticalOverflow = VerticalWrapMode.Overflow;
            caption.supportRichText = false;
            var textLayout = caption.gameObject.AddComponent<LayoutElement>();
            textLayout.minWidth = 0f;
            textLayout.preferredWidth = 0f;
            textLayout.flexibleWidth = 1f;
            if (icon != null)
            {
                var holder = FillRect("CommandIcon", button.transform);
                holder.SetAsFirstSibling();
                var iconLayout = holder.gameObject.AddComponent<LayoutElement>();
                iconLayout.preferredWidth = 27f;
                iconLayout.preferredHeight = 27f;
                AddImage(holder.gameObject, icon, Color.white, true, false);
            }
            return button;
        }

        public static Dropdown CreateDropdown(Transform parent, string name, IReadOnlyList<string> choices,
            Action<int> onChanged, bool dynamicChoices = false)
        {
            const float rowHeight = 28f;
            const int visibleRows = 7;
            var root = FillRect(name, parent);
            var background = AddImage(root.gameObject, WhiteSprite, Color.white);
            var caption = AddText(root, "Caption", "", 13, TextAnchor.MiddleLeft, TextPrimary, FontStyle.Bold);
            caption.rectTransform.offsetMin = new Vector2(7f, 0f);
            caption.rectTransform.offsetMax = new Vector2(-17f, 0f);
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 9;
            caption.resizeTextMaxSize = 13;
            caption.supportRichText = false;
            var arrow = AddText(root, "Arrow", "▼", 10, TextAnchor.MiddleRight, GoldAccent);
            arrow.rectTransform.offsetMax = new Vector2(-5f, 0f);

            float height = (dynamicChoices ? visibleRows : Mathf.Min(visibleRows, choices.Count)) * rowHeight;
            var template = CreateBorderedPanel(root, "Options", Vector2.zero, Vector2.right,
                new Vector2(0f, -height - 4f), new Vector2(0f, -4f), PanelNavy, CyanAccent);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -4f);
            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = rowHeight;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            bool needsScrollbar = dynamicChoices || choices.Count > visibleRows;
            var viewport = CreateRect("Viewport", template, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(needsScrollbar ? -15f : 0f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = CreateRect("Content", viewport, Vector2.up, Vector2.one,
                new Vector2(0f, -rowHeight), Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;
            if (needsScrollbar) scroll.verticalScrollbar = AddVerticalScrollbar(template);
            if (dynamicChoices) scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            var item = FillRect("Item", content);
            var itemBackground = AddImage(item.gameObject, WhiteSprite, new Color(0.08f, 0.20f, 0.39f));
            var selected = FillRect("Selected", item, 1f);
            var selectedImage = AddImage(selected.gameObject, WhiteSprite,
                new Color(GoldAccent.r, GoldAccent.g, GoldAccent.b, 0.30f), false, false);
            var label = AddText(item, "Label", "", 13, TextAnchor.MiddleLeft, TextPrimary);
            label.rectTransform.offsetMin = new Vector2(7f, 0f);
            label.rectTransform.offsetMax = new Vector2(-2f, 0f);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 9;
            label.resizeTextMaxSize = 13;
            label.supportRichText = false;
            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = itemBackground;
            toggle.graphic = selectedImage;

            var dropdown = root.gameObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = background;
            DuelUiChrome.ApplyControl(dropdown, background);
            dropdown.template = template;
            dropdown.captionText = caption;
            dropdown.itemText = label;
            foreach (string choice in choices)
                dropdown.options.Add(new Dropdown.OptionData(choice));
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(value =>
            {
                DuelAudio.PlayClick();
                onChanged(value);
            });
            template.gameObject.SetActive(false);
            return dropdown;
        }

        public static InputField CreateInputField(Transform parent, string name, string placeholderText, int fontSize = 16)
        {
            var rt = FillRect(name, parent);
            AddImage(rt.gameObject, WhiteSprite, new Color(0.05f, 0.10f, 0.20f, 0.96f));
            var ol = rt.gameObject.AddComponent<Outline>();
            ol.effectColor = CyanAccent;
            ol.effectDistance = new Vector2(1f, -1f);

            var ph = AddText(rt, "Placeholder", placeholderText, fontSize, TextAnchor.MiddleLeft, TextMuted, FontStyle.Italic);
            ph.rectTransform.offsetMin = new Vector2(10, 2);
            ph.rectTransform.offsetMax = new Vector2(-10, -2);

            var tx = AddText(rt, "Text", "", fontSize, TextAnchor.MiddleLeft, TextPrimary);
            tx.rectTransform.offsetMin = new Vector2(10, 2);
            tx.rectTransform.offsetMax = new Vector2(-10, -2);
            tx.supportRichText = false;

            var inp = rt.gameObject.AddComponent<InputField>();
            inp.textComponent = tx;
            inp.placeholder = ph;
            return inp;
        }
    }

    public class CompositeCardView : MonoBehaviour
    {
        private Image _artImg;
        private Image _sourceFace;
        private GameObject _missingArtwork;
        private CardFinishGraphic _finish;
        private Image _frameImg;
        private Image _attrImg;
        private Image _limitImg;
        private Text _titleText;
        private Text _levelText;
        private Text _leftScaleText;
        private Text _rightScaleText;
        private DuelRatingBadge _faceRating;
        private DuelStatStrip _faceStats;
        private Text _rulesText;
        private RectTransform _rulesArea;
        private Image _backImg;
        private bool _detailed;
        private uint _retainedArtwork;
        private int _retainedVariant;
        private (uint Code, int Variant) _artworkSource;
        private DeckCardAppearance? _deckAppearance;
        private GameObject _deckEditionBadge;
        private GameObject _destinyBadge;
        private bool _overframeEnabled;
        private bool _printedFaceOnly;
        private bool _showDeckEditionBadge = true;
        private bool _cinematicOverframe;
        private bool _overframeBound;
        private Sprite _retainedCutIn;
        private OverframePortraitImage _overframePortrait;
        private CanvasGroup _overframe;
        private RectTransform _overframeTilt;
        private Image _overframeShade;
        private Image _overframeBackground;
        private Image _overframeHeader;
        private AspectRatioFitter _overframeBackgroundFit;
        private CardFinishGraphic _overframeFinish;
        private CardOverframeGraphic _overframeTrim;

        internal bool HasOverframe => _overframe != null && _overframe.gameObject.activeSelf;
        internal bool HasOverframePortrait => HasOverframe && _retainedCutIn != null;

        public uint CurrentCode { get; private set; }
        internal Sprite ArtworkSprite => _retainedVariant == CardArtLoader.SourceOverframeFace ? _sourceFace.sprite : _artImg.sprite;
        internal DeckCardAppearance? Appearance => _deckAppearance;
        internal bool RegularEditionOnly { get; private set; }

        internal static bool UsesRegularEditionInDuelZone(uint? location) =>
            location is YgoConstants.LOCATION_GRAVE or YgoConstants.LOCATION_EXTRA;

        internal void UsePrintedFaceOnly(bool enabled = true, bool showEditionBadge = true, bool regularEditionOnly = false)
        {
            RegularEditionOnly = regularEditionOnly;
            _showDeckEditionBadge = showEditionBadge && !regularEditionOnly;
            if (!_showDeckEditionBadge) SetDeckEditionBadge(false);
            enabled |= regularEditionOnly;
            if (_printedFaceOnly == enabled) return;
            _printedFaceOnly = enabled;
            _artImg.preserveAspect = enabled;
            if (enabled) HideOverframe();
        }

        public static CompositeCardView Create(Transform parent, string name, bool detailedOverlay = false)
            => Create(parent, name, detailedOverlay, cinematicOverframe: false);

        internal static CompositeCardView Create(Transform parent, string name, bool detailedOverlay, bool cinematicOverframe)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var view = go.AddComponent<CompositeCardView>();
            view._cinematicOverframe = cinematicOverframe;
            view.BuildHierarchy(detailedOverlay);
            return view;
        }

        private void BuildHierarchy(bool detailed)
        {
            _detailed = detailed;

            var innerRt = DuelUiKit.FillRect("AspectBox", transform);
            var arf = innerRt.gameObject.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.aspectRatio = DuelUiKit.CardAspectRatio;

            var artRt = DuelUiKit.CreateRect("Artwork", innerRt, CardFaceFrame.Artwork.min, CardFaceFrame.Artwork.max,
                Vector2.zero, Vector2.zero);
            _artImg = DuelUiKit.AddImage(artRt.gameObject, null, Color.white, false, false);

            var frameRt = DuelUiKit.FillRect("Frame", innerRt);
            _frameImg = DuelUiKit.AddImage(frameRt.gameObject, null, Color.white, false, false);

            var titleRt = DuelUiKit.CreateRect("CardTitle", innerRt,
                new Vector2(.080f, .894f), new Vector2(detailed ? .81f : .921f, .967f), Vector2.zero, Vector2.zero);
            _titleText = DuelUiKit.AddFittedText(titleRt, "T", "", detailed ? 22 : 13, detailed ? 6 : 4,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var ts = _titleText.gameObject.AddComponent<Shadow>();
            ts.effectColor = new Color(0, 0, 0, 0.85f);
            ts.effectDistance = new Vector2(1, -1);

            if (detailed)
            {
                var attrRt = DuelUiKit.CreateRect("AttrIcon", innerRt,
                    new Vector2(.828f, .901f), new Vector2(.923f, .953f), Vector2.zero, Vector2.zero);
                _attrImg = DuelUiKit.AddImage(attrRt.gameObject, null, Color.white, true, false);

                var lvRt = DuelUiKit.CreateRect("LevelInfo", innerRt,
                    new Vector2(.079f, .844f), new Vector2(.921f, .886f), Vector2.zero, Vector2.zero);
                var levelLabel = DuelUiKit.CreateRect("Type", lvRt, Vector2.zero, new Vector2(.70f, 1f), Vector2.zero, Vector2.zero);
                _levelText = DuelUiKit.AddFittedText(levelLabel, "Lv", "", 15, 7,
                    TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
                var rating = DuelUiKit.CreateRect("Rating", lvRt, new Vector2(.74f, .045f), new Vector2(1f, .955f),
                    Vector2.zero, Vector2.zero);
                _faceRating = new DuelRatingBadge(rating, compact: true);

                var leftScale = DuelUiKit.CreateRect("LeftScale", innerRt,
                    new Vector2(.073f, .240f), new Vector2(.143f, .298f), Vector2.zero, Vector2.zero);
                var rightScale = DuelUiKit.CreateRect("RightScale", innerRt,
                    new Vector2(.857f, .240f), new Vector2(.927f, .298f), Vector2.zero, Vector2.zero);
                _leftScaleText = AddScaleValue(leftScale, new Color(.56f, .82f, 1f));
                _rightScaleText = AddScaleValue(rightScale, new Color(1f, .68f, .64f));

                Text AddScaleValue(RectTransform parent, Color color)
                {
                    DuelReadoutPlate.Add(parent, new Color(.041f, .063f, .097f, .97f));
                    return DuelTypography.AddNumber(parent, "Value", "", 16, TextAnchor.MiddleCenter, color);
                }

                _rulesArea = DuelUiKit.CreateRect("Rules", innerRt,
                    new Vector2(.080f, .124f), new Vector2(.920f, .218f), Vector2.zero, Vector2.zero);
                _rulesArea.gameObject.AddComponent<RectMask2D>();
                _rulesText = DuelUiKit.AddText(_rulesArea, "Text", "", 11, TextAnchor.UpperLeft, new Color(.075f, .09f, .12f));
                _rulesText.supportRichText = false;

                var statRt = DuelUiKit.CreateRect("AtkDef", innerRt,
                    new Vector2(.080f, .054f), new Vector2(.920f, .118f), Vector2.zero, Vector2.zero);
                _faceStats = new DuelStatStrip(statRt, compact: true, useHudSkin: false);
            }

            var limRt = DuelUiKit.CreateRect("LimitBadge", innerRt,
                new Vector2(0.03f, 0.78f), new Vector2(0.28f, 0.95f), Vector2.zero, Vector2.zero);
            _limitImg = DuelUiKit.AddImage(limRt.gameObject, null, Color.white, true, false);
            _limitImg.gameObject.SetActive(false);

            var backRt = DuelUiKit.FillRect("CardBack", innerRt);
            _backImg = DuelUiKit.AddImage(backRt.gameObject, null, Color.white, false, false);
            _backImg.gameObject.SetActive(false);
        }

        public void SetCard(uint code, bool faceDown = false, LiveCardState live = null)
            => SetCard(code, faceDown, live, null);

        internal void SetCard(uint code, DuelCardReadout readout, DeckCardAppearance? appearance = null) =>
            SetCard(code, false, readout.Live, readout, appearance);

        internal void SetDeckCard(uint code, DeckCardAppearance? appearance, bool faceDown = false, LiveCardState live = null)
            => SetCard(code, faceDown, live, null, appearance);

        private void SetCard(uint code, bool faceDown, LiveCardState live, DuelCardReadout? presentation,
            DeckCardAppearance? appearance = null)
        {
            appearance ??= live?.Appearance;
            if (!Nullable.Equals(_deckAppearance, appearance)) HideOverframe();
            _deckAppearance = appearance;
            CurrentCode = code;
            if (faceDown || code == 0)
            {
                ReleaseArtwork();
                SetDeckEditionBadge(false);
                HideRarityFinish();
                if (_detailed)
                {
                    _faceStats.Hide();
                    _faceRating.Bind(default);
                    _levelText.text = "";
                    SetScalesVisible(false);
                }
                _backImg.sprite = CardArtLoader.GetCardBackSprite();
                _backImg.gameObject.SetActive(true);
                return;
            }
            _backImg.gameObject.SetActive(false);

            var info = CardDatabase.Get(code);
            _titleText.text = info != null ? info.name : $"카드 #{code}";
            uint type = live != null && live.type != 0 ? live.type : (info?.type ?? YgoConstants.TYPE_EFFECT);
            uint faceType = info?.type ?? type;
            bool isPend = (type & YgoConstants.TYPE_PENDULUM) != 0;

            var artRt = _artImg.rectTransform;
            Rect artwork = (faceType & YgoConstants.TYPE_PENDULUM) != 0 ? CardFaceFrame.PendulumArtwork : CardFaceFrame.Artwork;
            artRt.anchorMin = artwork.min;
            artRt.anchorMax = artwork.max;

            BindArtwork(code);
            BindRarityFinish(info);
            RefreshOverframe();
            SetDeckEditionBadge((!_detailed || _printedFaceOnly) && !_cinematicOverframe && appearance?.overframe == true);
            _frameImg.sprite = CardFaceFrame.Get(faceType);

            if (_detailed)
            {
                var readout = presentation ?? new DuelCardReadout(info, live);
                bool monster = readout.IsMonster;
                _rulesArea.anchorMin = new Vector2(.080f, monster ? .124f : .057f);
                _rulesText.text = info?.desc ?? "";

                uint attr = live != null && live.attribute != 0 ? live.attribute : (info?.attr ?? 0);
                Sprite attrSp = null;
                if ((type & YgoConstants.TYPE_SPELL) != 0) attrSp = DuelUiIcons.FindCardIdentity("attribute/spell");
                else if ((type & YgoConstants.TYPE_TRAP) != 0) attrSp = DuelUiIcons.FindCardIdentity("attribute/trap");
                else attrSp = DuelUiKit.GetAttributeSprite(attr);

                _attrImg.gameObject.SetActive(attrSp != null);
                _attrImg.sprite = attrSp;

                SetScalesVisible(isPend && monster);
                if (isPend && monster)
                {
                    _leftScaleText.text = readout.LeftScale.ToString();
                    _rightScaleText.text = readout.RightScale.ToString();
                }

                _faceRating.Bind(readout.InspectionRating);
                _faceStats.Bind(readout, position: 0, showModifiers: false);
                _levelText.text = YgoConstants.GetTypeTagKR(type);
            }
        }

        private void SetScalesVisible(bool visible)
        {
            _leftScaleText.transform.parent.gameObject.SetActive(visible);
            _rightScaleText.transform.parent.gameObject.SetActive(visible);
        }

        internal void SetRarityHighlight(float amount)
        {
            if (_finish != null && _finish.gameObject.activeSelf) _finish.SetHighlight(amount);
            if (!_cinematicOverframe && HasOverframe) _overframeFinish.SetHighlight(Mathf.Lerp(.18f, .72f, amount));
        }

        internal void SetReadoutFocus(float amount)
        {
            SetRarityHighlight(amount);
            if (!_cinematicOverframe && HasOverframe)
                PoseOverframe(1f, 1f + .04f * amount, Vector2.up * amount, amount);
        }

        private void BindRarityFinish(CardInfo info)
        {
            var rarity = info == null || (info.type & YgoConstants.TYPE_TOKEN) != 0
                ? CardRarity.Normal : CardRarityCatalog.GetRarity(info.code);
            if (rarity < CardRarity.SuperRare)
            {
                HideRarityFinish();
                return;
            }
            if (_finish == null)
            {
                var foil = DuelUiKit.FillRect("RarityFinish", _artImg.transform);
                _finish = foil.gameObject.AddComponent<CardFinishGraphic>();
                _finish.raycastTarget = false;
            }
            _finish.Bind(info.code, rarity);
            _finish.gameObject.SetActive(true);
        }

        private void HideRarityFinish()
        {
            if (_finish == null || !_finish.gameObject.activeSelf) return;
            _finish.SetHighlight(0f);
            _finish.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (_backImg == null) return;
            if (_backImg.gameObject.activeSelf)
                _backImg.sprite = CardArtLoader.GetCardBackSprite();
            else
            {
                BindArtwork(CurrentCode);
                RefreshOverframe();
            }
        }

        private void OnDisable()
        {
            ReleaseArtwork();
        }

        private void BindArtwork(uint code)
        {
            if (code == 0 || !isActiveAndEnabled) return;
            bool fullFace = !RegularEditionOnly && _deckAppearance?.overframe == true && CardArtLoader.HasSourceOverframe(code);
            var (artCode, variant) = fullFace
                ? (code, CardArtLoader.SourceOverframeFace)
                : CardArtLoader.GetPreferredArtwork(code, _deckAppearance?.ArtworkVariant);
            if (artCode == _retainedArtwork && variant == _retainedVariant && ArtworkSprite != null) return;
            ReleaseArtwork();
            var artwork = CardArtLoader.RetainArtwork(artCode, variant, out _artworkSource);
            if (fullFace && _artworkSource.Code != 0)
            {
                SetSourceFace(artwork);
                _retainedArtwork = artCode;
                _retainedVariant = variant;
                return;
            }
            if (fullFace)
            {
                (artCode, variant) = CardArtLoader.GetPreferredArtwork(code, _deckAppearance?.ArtworkVariant);
                artwork = CardArtLoader.RetainArtwork(artCode, variant, out _artworkSource);
            }
            _artImg.sprite = artwork;
            bool missing = _artImg.sprite == CardArtLoader.GetFallbackArt();
            if (missing && _missingArtwork == null)
            {
                var label = DuelUiKit.FillRect("MissingArtwork", _artImg.rectTransform, 2f);
                DuelUiKit.AddFittedText(label, "Label", "그림 없음", 14, 7, TextAnchor.MiddleCenter, DuelUiKit.TextMuted);
                _missingArtwork = label.gameObject;
            }
            if (_missingArtwork != null) _missingArtwork.SetActive(missing);
            _retainedArtwork = artCode;
            _retainedVariant = variant;
        }

        private void SetSourceFace(Sprite sprite)
        {
            if (sprite != null && _sourceFace == null)
            {
                var face = DuelUiKit.FillRect("OverframeCardFace", _artImg.transform.parent);
                // The supplied face includes its own rules and title. Only badges and the sleeve go above it.
                face.SetSiblingIndex(_limitImg.transform.GetSiblingIndex());
                _sourceFace = DuelUiKit.AddImage(face.gameObject, null, Color.white, false, false);
            }
            if (_sourceFace == null) return;
            _sourceFace.sprite = sprite;
            _sourceFace.enabled = sprite != null;
            _artImg.enabled = _frameImg.enabled = sprite == null;
        }

        private void SetDeckEditionBadge(bool shown)
        {
            shown &= _showDeckEditionBadge;
            if (shown && _deckEditionBadge == null)
            {
                var aspect = _artImg.transform.parent;
                var badge = DuelUiKit.CreateRect("DeckEdition", aspect, new Vector2(.058f, .048f),
                    new Vector2(.255f, .166f), Vector2.zero, Vector2.zero);
                var rim = DuelUiKit.AddFittedText(badge, "Diamond", "◆", 24, 6, TextAnchor.MiddleCenter,
                    new Color(.85f, .67f, 1f), FontStyle.Bold);
                var stroke = rim.gameObject.AddComponent<Outline>();
                stroke.effectColor = new Color(.16f, .075f, .26f);
                stroke.effectDistance = new Vector2(1f, -1f);
                _deckEditionBadge = badge.gameObject;
            }
            if (_deckEditionBadge != null) _deckEditionBadge.SetActive(shown);
        }

        internal void SetDestinyBadge(bool shown)
        {
            if (shown && _destinyBadge == null)
            {
                var badge = DuelUiKit.CreateRect("DestinyDraw", _artImg.transform.parent,
                    new Vector2(.686f, .767f), new Vector2(.97f, .968f), Vector2.zero, Vector2.zero);
                DuelUiKit.AddImage(badge.gameObject, DuelUiKit.WhiteSprite, new Color(.29f, .135f, .019f, .96f), false, false);
                DuelUiKit.AddFittedText(badge, "Star", "★", 24, 8, TextAnchor.MiddleCenter, DuelUiKit.GoldAccent, FontStyle.Bold);
                _destinyBadge = badge.gameObject;
            }
            if (_destinyBadge != null) _destinyBadge.SetActive(shown);
        }

        internal void EnableOverframe()
        {
            _overframeEnabled = true;
            RefreshOverframe();
        }

        private void RefreshOverframe()
        {
            if (_printedFaceOnly || _retainedVariant == CardArtLoader.SourceOverframeFace ||
                !(_deckAppearance?.overframe ?? _overframeEnabled) || _overframeBound || _retainedArtwork == 0) return;
            _overframeBound = true;
            var (code, variant) = _artworkSource.Code == 0 ? (_retainedArtwork, _retainedVariant) : _artworkSource;
            _retainedCutIn = DuelVisualAssets.RetainCutIn(code, variant);
            bool hasPortrait = _retainedCutIn != null;
            if (!hasPortrait && _deckAppearance?.overframe != true) return;
            if (_overframe == null)
            {
                var layer = DuelUiKit.FillRect("Overframe", _cinematicOverframe ? transform : _frameImg.transform.parent);
                if (_cinematicOverframe)
                {
                    var aspect = layer.gameObject.AddComponent<AspectRatioFitter>();
                    aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    aspect.aspectRatio = DuelUiKit.CardAspectRatio;
                }
                else
                {
                    layer.SetSiblingIndex(_frameImg.transform.GetSiblingIndex() + 1);
                }
                _overframe = layer.gameObject.AddComponent<CanvasGroup>();
                _overframe.blocksRaycasts = _overframe.interactable = false;
                RectTransform portraitBounds = layer;
                RectTransform finishBounds = _artImg.rectTransform;
                if (!_cinematicOverframe)
                {
                    var face = DuelUiKit.CreateRect("FaceArtwork", layer, new Vector2(.032f, .026f),
                        new Vector2(.968f, .974f), Vector2.zero, Vector2.zero);
                    face.gameObject.AddComponent<RectMask2D>();
                    finishBounds = face;
                    var background = DuelUiKit.FillRect("ExpandedArtwork", face);
                    _overframeBackground = DuelUiKit.AddImage(background.gameObject, null, new Color(.86f, .91f, .98f), false, false);
                    _overframeBackgroundFit = background.gameObject.AddComponent<AspectRatioFitter>();
                    _overframeBackgroundFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    // Leave clipping to the enclosing viewport so tilt can carry the subject past the card.
                    portraitBounds = DuelUiKit.CreateRect("PortraitBounds", layer, new Vector2(-.105f, .28f),
                        new Vector2(1.105f, 1.115f), Vector2.zero, Vector2.zero);
                }
                var shade = DuelUiKit.FillRect("OverframeShade", finishBounds);
                _overframeShade = DuelUiKit.AddImage(shade.gameObject, DuelStyle.OverframeBackdrop,
                    _cinematicOverframe ? Color.white : new Color(1f, 1f, 1f, .23f), false, false);
                var finish = DuelUiKit.FillRect(_cinematicOverframe ? "CinematicFinish" : "ExpandedFinish", finishBounds);
                _overframeFinish = finish.gameObject.AddComponent<CardFinishGraphic>();
                _overframeFinish.raycastTarget = false;
                CreateOverframeTrim(layer);
                if (!_cinematicOverframe)
                    _overframeTrim.transform.SetSiblingIndex(portraitBounds.GetSiblingIndex());
                _overframeTilt = DuelUiKit.FillRect("Depth", portraitBounds);
                _overframeTilt.pivot = new Vector2(.5f, .78f);
                var portrait = DuelUiKit.CreateRect("LocalCutIn", _overframeTilt,
                    _cinematicOverframe ? new Vector2(-.13f, .405f) : Vector2.zero,
                    _cinematicOverframe ? new Vector2(1.13f, 1.166f) : Vector2.one,
                    Vector2.zero, Vector2.zero);
                portrait.pivot = new Vector2(.5f, 1f);
                _overframePortrait = portrait.gameObject.AddComponent<OverframePortraitImage>();
                _overframePortrait.preserveAspect = true;
                _overframePortrait.raycastTarget = false;
                _overframePortrait.maskable = !_cinematicOverframe;
            }
            if (_overframeBackground != null)
            {
                // The base illustration contains the same subject. Keep it as the fallback for editions without a cut-in.
                _overframeBackground.sprite = hasPortrait ? DuelStyle.OverframeBackdrop : _artImg.sprite;
                _overframeBackground.color = hasPortrait ? Color.white : new Color(.86f, .91f, .98f);
                Rect art = _overframeBackground.sprite.rect;
                _overframeBackgroundFit.aspectRatio = art.width / Mathf.Max(1f, art.height);
            }
            bool showFinish = !_cinematicOverframe || _deckAppearance?.overframe == true;
            _overframeFinish.gameObject.SetActive(showFinish);
            if (showFinish)
            {
                _overframeFinish.Bind(CurrentCode, CardRarity.UltraRare);
                _overframeFinish.SetHighlight(_cinematicOverframe ? .54f : .18f);
            }
            _overframePortrait.Bind(_retainedCutIn, code);
            _overframePortrait.enabled = hasPortrait;
            _overframeShade.gameObject.SetActive(_cinematicOverframe || !hasPortrait);
            _overframe.gameObject.SetActive(true);
            ApplyOverframeInk();
            PoseOverframe(1f, 1f, Vector2.zero, 0f);
        }

        private void CreateOverframeTrim(Transform layer)
        {
            _overframeTrim = DuelUiKit.FillRect("OverframeTrim", layer).gameObject.AddComponent<CardOverframeGraphic>();
            _overframeTrim.Configure(_cinematicOverframe, _cinematicOverframe ? null : _overframeFinish.canvasRenderer);
        }

        private void ApplyOverframeInk()
        {
            if (_cinematicOverframe) return;
            if (_detailed)
                _rulesText.color = HasOverframe ? new Color(.92f, .94f, 1f) : new Color(.075f, .09f, .12f);
            bool shown = HasOverframePortrait;
            if (shown && _overframeHeader == null)
            {
                var title = _titleText.transform.parent;
                var header = DuelUiKit.CreateRect("OverframeHeader", title.parent,
                    new Vector2(.069f, .890f), new Vector2(.936f, .972f), Vector2.zero, Vector2.zero);
                header.SetSiblingIndex(title.GetSiblingIndex());
                _overframeHeader = DuelReadoutPlate.Add(header, new Color(.023f, .034f, .062f, .88f));
            }
            if (_overframeHeader != null) _overframeHeader.enabled = shown;
        }

        internal void PoseOverframe(float opacity, float scale, Vector2 parallax, float shimmer)
        {
            if (!HasOverframe) return;
            _overframe.alpha = Mathf.Clamp01(opacity);
            if (_cinematicOverframe)
            {
                float shade = HasOverframePortrait ? Mathf.Clamp01(_overframe.alpha * 1.5f) : .27f * _overframe.alpha;
                _overframeShade.canvasRenderer.SetAlpha(shade);
                float spread = Mathf.Sin(Mathf.Clamp01(shimmer) * Mathf.PI);
                _overframeTrim.transform.localScale = Vector3.one * (1.03f + spread * .26f);
                _overframeTrim.canvasRenderer.SetAlpha(_deckAppearance?.overframe == true ? .42f + spread * .52f : 0f);
                if (_overframeFinish.gameObject.activeSelf)
                    _overframeFinish.canvasRenderer.SetAlpha(_overframe.alpha * Mathf.Lerp(.28f, .96f, spread));
            }
            else
            {
                var artwork = _overframeBackground.rectTransform;
                artwork.anchoredPosition = Vector2.Scale(artwork.rect.size, parallax) * -.012f;
            }
            _overframeTilt.localScale = Vector3.one * scale;
            _overframeTilt.anchoredPosition = Vector2.Scale(_overframeTilt.rect.size, parallax) * .024f;
            _overframeTilt.localRotation = Quaternion.Euler(parallax.y * 3f, -parallax.x * 6f, 0f);
            float shine = .96f + .04f * Mathf.Cos(shimmer * Mathf.PI * 2f);
            _overframePortrait.canvasRenderer.SetColor(new Color(1f, shine, shine, 1f));
        }

        private void HideOverframe()
        {
            _overframeBound = false;
            if (!HasOverframe) return;
            _overframePortrait.sprite = null;
            if (_overframeBackground != null) _overframeBackground.sprite = null;
            DuelVisualAssets.ReleaseCutIn(_retainedCutIn);
            _retainedCutIn = null;
            _overframeShade.gameObject.SetActive(false);
            if (_cinematicOverframe) _overframeFinish.gameObject.SetActive(false);
            _overframe.gameObject.SetActive(false);
            ApplyOverframeInk();
        }

        private void ReleaseArtwork()
        {
            HideOverframe();
            if (_retainedArtwork == 0) return;
            SetSourceFace(null);
            _artImg.sprite = null;
            if (_missingArtwork != null) _missingArtwork.SetActive(false);
            CardArtLoader.ReleaseArtwork(_retainedArtwork, _retainedVariant);
            _retainedArtwork = 0;
            _retainedVariant = 0;
            _artworkSource = default;
        }
    }

    internal sealed class OverframePortraitImage : Image
    {
        private Vector2 _sourceOffset;

        internal void Bind(Sprite portrait, uint code)
        {
            sprite = portrait;
            var (offset, scale) = portrait == null ? (Vector2.zero, 1f) : OverframeOffsetCatalog.GetOffset(code);
            _sourceOffset = offset;
            rectTransform.localScale = Vector3.one * scale;
            ApplyOffset();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (_sourceOffset != Vector2.zero) ApplyOffset();
        }

        private void ApplyOffset()
        {
            if (_sourceOffset == Vector2.zero || sprite == null)
            {
                rectTransform.anchoredPosition = Vector2.zero;
                return;
            }

            // Match Image.preserveAspect so one source pixel scales with the portrait in every card size.
            Vector2 box = GetPixelAdjustedRect().size;
            Vector2 artwork = sprite.rect.size;
            float fit = Mathf.Min(box.x / artwork.x, box.y / artwork.y);
            rectTransform.anchoredPosition = _sourceOffset * fit;
        }
    }

    public static partial class DuelAudio
    {
        private static AudioSource _sfxSource;
        private static AudioSource _bgmSource;
        private static bool _bgmPaused;
        private static AudioClip _clipClick, _clipDraw, _clipSummon, _clipAttack, _clipDamage, _clipChain, _clipWin, _clipPhase, _clipDirectAttack, _clipTick, _clipCoin, _clipDice;
        private static AudioClip _clipSpecialSummon, _clipActivate, _clipSet, _clipShuffle;
        private static AudioClip _clipDuelBgm, _clipMenuBgm;

        public static float SfxVolume { get; set; } = 0.65f;
        public static float BgmVolume { get; set; } = 0.35f;

        public static void EnsureInit(GameObject host)
        {
            if (_sfxSource != null) return;
            _sfxSource = host.GetComponent<AudioSource>();
            if (_sfxSource == null) _sfxSource = host.AddComponent<AudioSource>();
            if (_sfxSource == null) return;
            _sfxSource.playOnAwake = false;

            _bgmSource = host.AddComponent<AudioSource>();
            if (_bgmSource != null)
            {
                _bgmSource.playOnAwake = false;
                _bgmSource.loop = true;
            }

            int sr = 22050;
            _clipClick = DuelVisualAssets.LoadSound("Click") ?? MakeTone(0.06f, 880f, 1180f, 0.25f);
            _clipDraw = DuelVisualAssets.LoadSound("Draw") ?? MakeTone(0.09f, 520f, 760f, 0.25f);
            _clipSummon = DuelVisualAssets.LoadSound("Summon") ?? MakeTone(0.24f, 240f, 540f, 0.35f);
            _clipAttack = DuelVisualAssets.LoadSound("Attack") ?? MakeTone(0.18f, 440f, 110f, 0.45f);
            _clipDamage = DuelVisualAssets.LoadSound("Damage") ?? MakeTone(0.25f, 180f, 65f, 0.48f);
            _clipChain = DuelVisualAssets.LoadSound("Chain") ?? MakeTone(0.16f, 660f, 990f, 0.30f);
            _clipWin = DuelVisualAssets.LoadSound("Win") ?? MakeTone(0.45f, 523f, 1046f, 0.35f);
            _clipPhase = DuelVisualAssets.LoadSound("Phase") ?? MakeTone(0.24f, 440f, 880f, 0.32f);
            _clipDirectAttack = DuelVisualAssets.LoadSound("DirectAttack") ?? MakeTone(0.32f, 280f, 60f, 0.50f);
            _clipTick = DuelVisualAssets.LoadSound("DialogueBlip") ?? MakeTone(0.04f, 1320f, 1600f, 0.15f);
            _clipCoin = DuelVisualAssets.LoadSound("Coin") ?? MakeTone(0.22f, 980f, 1320f, 0.35f);
            _clipDice = DuelVisualAssets.LoadSound("Dice") ?? MakeTone(0.24f, 420f, 780f, 0.35f);
            _clipSpecialSummon = DuelVisualAssets.LoadSound("SpecialSummon") ?? _clipSummon;
            _clipActivate = DuelVisualAssets.LoadSound("Activate") ?? _clipChain;
            _clipSet = DuelVisualAssets.LoadSound("Set") ?? _clipClick;
            _clipShuffle = DuelVisualAssets.LoadSound("Shuffle") ?? _clipDraw;

            _clipMenuBgm = MakeMenuBgm(sr);
        }

        public static void PlayClick() => Play(_clipClick);
        public static void PlayDraw() => Play(_clipDraw);
        public static void PlaySummon() => Play(_clipSummon);
        internal static void PlaySummon(CardMotion motion) =>
            Play(motion is CardMotion.Summon or CardMotion.TributeSummon ? _clipSummon : _clipSpecialSummon);
        internal static void PlaySet() => Play(_clipSet);
        internal static void PlayShuffle() => Play(_clipShuffle);
        internal static void PlayActivation() => Play(_clipActivate);
        public static void PlayAttack() => Play(_clipAttack);
        public static void PlayDamage() => Play(_clipDamage);
        public static void PlayChain() => Play(_clipChain);
        public static void PlayWin() => Play(_clipWin);
        public static void PlayPhaseTransition() => Play(_clipPhase);
        public static void PlayDirectAttack() => Play(_clipDirectAttack);
        public static void PlayTick() => Play(_clipTick);
        public static void PlayCoin() => Play(_clipCoin);
        public static void PlayDice() => Play(_clipDice);

        public static void PlayDuelBgm()
        {
            if (_bgmSource == null) return;
            _clipDuelBgm ??= DuelVisualAssets.LoadSound("DuelMusic") ?? MakeDuelBgm(22050);
            PlayBgmClip(_clipDuelBgm);
        }

        public static void PlayMenuBgm() => PlayBgmClip(_clipMenuBgm);

        private static void PlayBgmClip(AudioClip clip)
        {
            if (_bgmSource == null || clip == null) return;
            if (_bgmSource.clip != clip)
            {
                StopBgm();
                _bgmSource.clip = clip;
            }
            _bgmSource.loop = true;
            UpdateBgmVolume();
        }

        public static void StopBgm()
        {
            _bgmPaused = false;
            if (_bgmSource != null) _bgmSource.Stop();
        }

        public static void UpdateBgmVolume()
        {
            if (_bgmSource == null) return;
            _bgmSource.volume = BgmVolume;
            if (BgmVolume < 0.01f && _bgmSource.isPlaying)
            {
                _bgmSource.Pause();
                _bgmPaused = true;
            }
            else if (BgmVolume >= 0.01f && !_bgmSource.isPlaying && _bgmSource.clip != null)
            {
                if (_bgmPaused) _bgmSource.UnPause();
                else _bgmSource.Play();
                _bgmPaused = false;
            }
        }

        private static void Play(AudioClip clip, float volumeMultiplier = 1f)
        {
            float volume = Mathf.Clamp01(SfxVolume * volumeMultiplier);
            if (_sfxSource != null && clip != null && SfxVolume > 0.01f && volume > 0f)
                _sfxSource.PlayOneShot(clip, volume);
        }

        private static AudioClip MakeTone(float sec, float fStart, float fEnd, float amp)
        {
            int sr = 22050;
            int n = Mathf.Max(64, (int)(sr * sec));
            float[] samples = new float[n];
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float freq = Mathf.Lerp(fStart, fEnd, t);
                phase += (2.0 * Math.PI * freq) / sr;
                float env = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.45f);
                samples[i] = (float)Math.Sin(phase) * env * amp;
            }
            var clip = AudioClip.Create("sfx", n, 1, sr, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip MakeDuelBgm(int sr)
        {
            float duration = 8.0f;
            int n = (int)(sr * duration);
            float[] samples = new float[n];
            float[] roots = { 73.416f, 58.270f, 65.406f, 55.000f };
            float[] fifths = { 110.00f, 87.307f, 97.999f, 82.407f };
            float[] thirds = { 174.61f, 146.83f, 164.81f, 130.81f };

            double pBass = 0.0, pArp = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                int bar = Mathf.Clamp((int)(t / 2.0f), 0, 3);
                float barTime = t % 2.0f;
                float beatTime = t % 0.5f;

                float kick = 0f;
                if (beatTime < 0.18f)
                {
                    float kt = beatTime / 0.18f;
                    float kFreq = Mathf.Lerp(130f, 45f, kt * kt);
                    kick = (float)Math.Sin(kFreq * 2.0 * Math.PI * beatTime) * Mathf.Exp(-kt * 5f) * 0.42f;
                }

                float hat = 0f;
                float hatTime = (beatTime > 0.25f) ? (beatTime - 0.25f) : 1f;
                if (hatTime < 0.08f)
                {
                    float ht = hatTime / 0.08f;
                    float noise = ((i * 1103515245 + 12345) & 0x7FFFFFFF) / (float)0x7FFFFFFF * 2f - 1f;
                    hat = noise * Mathf.Exp(-ht * 8f) * 0.12f;
                }

                float bassFreq = (barTime < 1.0f) ? roots[bar] : fifths[bar];
                if ((int)(t * 8f) % 2 == 1) bassFreq *= 1.5f;
                pBass += 2.0 * Math.PI * bassFreq / sr;
                float bassEnv = Mathf.Repeat(t * 4f, 1f);
                float bass = (float)(Math.Sin(pBass) + 0.35 * Math.Sin(pBass * 2.0)) * (1f - bassEnv * 0.6f) * 0.30f;

                int arpStep = (int)(t * 8f) % 4;
                float arpFreq = (arpStep == 0) ? roots[bar] * 4f : (arpStep == 1) ? thirds[bar] * 2f : (arpStep == 2) ? fifths[bar] * 4f : thirds[bar] * 4f;
                pArp += 2.0 * Math.PI * arpFreq / sr;
                float arpEnv = Mathf.Repeat(t * 8f, 1f);
                float arp = (float)Math.Sin(pArp) * Mathf.Exp(-arpEnv * 4f) * 0.16f;

                float loopFade = 1f;
                if (i < 1000) loopFade = i / 1000f;
                else if (i > n - 1000) loopFade = (n - i) / 1000f;

                samples[i] = Mathf.Clamp((kick + hat + bass + arp) * loopFade, -1f, 1f);
            }
            var clip = AudioClip.Create("duel_bgm", n, 1, sr, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip MakeMenuBgm(int sr)
        {
            float duration = 8.0f;
            int n = (int)(sr * duration);
            float[] samples = new float[n];
            float[] chords = { 130.81f, 116.54f, 146.83f, 110.00f };

            double pChord = 0.0, pPluck = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                int bar = Mathf.Clamp((int)(t / 2.0f), 0, 3);
                float f = chords[bar];

                pChord += 2.0 * Math.PI * f / sr;
                float pad = (float)(Math.Sin(pChord) * 0.20 + Math.Sin(pChord * 1.5) * 0.12 + Math.Sin(pChord * 2.0) * 0.08);

                int pStep = (int)(t * 2f) % 2;
                float pf = f * (pStep == 0 ? 2f : 3f);
                pPluck += 2.0 * Math.PI * pf / sr;
                float pluckEnv = Mathf.Repeat(t * 2f, 1f);
                float pluck = (float)Math.Sin(pPluck) * Mathf.Exp(-pluckEnv * 3.5f) * 0.12f;

                float loopFade = 1f;
                if (i < 1000) loopFade = i / 1000f;
                else if (i > n - 1000) loopFade = (n - i) / 1000f;

                samples[i] = Mathf.Clamp((pad + pluck) * loopFade, -1f, 1f);
            }
            var clip = AudioClip.Create("menu_bgm", n, 1, sr, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
