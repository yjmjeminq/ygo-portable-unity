using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public static class LotdUiKit
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
        private static readonly Dictionary<uint, Sprite> _opponentCardPortraits = new Dictionary<uint, Sprite>();
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

        public static Sprite LoadOpponentPortrait(OpponentCharacter opponent)
        {
            if (opponent == null) return null;
            if (!string.IsNullOrEmpty(opponent.portrait))
            {
                var bust = LoadSprite($"LOTD/Busts/{opponent.portrait}");
                if (bust != null) return bust;
            }
            if (opponent.portraitCard != 0)
            {
                // These images also live on the duel HUD after the opponent screen closes.
                if (!_opponentCardPortraits.TryGetValue(opponent.portraitCard, out var cardArt) || cardArt == null)
                    _opponentCardPortraits[opponent.portraitCard] = cardArt = CardArtLoader.RetainArtwork(opponent.portraitCard);
                if (cardArt != null) return cardArt;
            }
            return LoadSprite("LOTD/Busts/atem_neutral");
        }

        public static Sprite GetRaceSprite(ulong race)
        {
            if (YgoConstants.RaceSpriteNames.TryGetValue(race, out var name))
            {
                return LoadSprite($"LOTD/TypeIcons/{name}") ?? LoadSprite($"LOTD/Icons/{name}");
            }
            return null;
        }

        public static Sprite GetAttributeSprite(uint attr)
        {
            if (YgoConstants.AttributeSpriteNames.TryGetValue(attr, out var name))
            {
                return LoadSprite($"LOTD/Icons/{name}");
            }
            return null;
        }

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
            AddImage(panel.gameObject, LoadSprite("LOTD/Shared/opponentframe_fill"), Color.white);
            var frame = AddImage(FillRect("Frame", panel).gameObject, LoadSprite("LOTD/Shared/menuframe"),
                Color.white, false, false);
            if (frame.sprite.border.sqrMagnitude > 0f)
                frame.type = Image.Type.Sliced;
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
            AddImage(header.gameObject, LoadSprite("LOTD/Shared/menuheader"), Color.white, false, false);
            AddText(header, "Heading", title, 32, TextAnchor.MiddleCenter,
                new Color(0.025f, 0.075f, 0.14f), FontStyle.Bold);
        }

        public static Scrollbar AddVerticalScrollbar(Transform parent)
        {
            var track = CreateRect("Scrollbar", parent, new Vector2(1f, 0f), Vector2.one,
                new Vector2(-14f, 1f), new Vector2(-2f, -1f));
            AddImage(track.gameObject, LoadSprite("LOTD/Shared/scrollbar_blue_track"), Color.white);
            var slide = FillRect("SlidingArea", track, 1f);
            var thumb = FillRect("Handle", slide);
            var image = AddImage(thumb.gameObject, LoadSprite("LOTD/Shared/scrollbar_blue_thumb"), Color.white);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = thumb;
            scrollbar.targetGraphic = image;
            return scrollbar;
        }

        public static Button CreateLotdButton(Transform parent, string name, string label,
            Action onClick, int fontSize = 18, bool gold = false)
        {
            var rt = CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            string activePath = gold ? "LOTD/Buttons_Yellow/ds_btn_active01" : "LOTD/Buttons/ds_btn_active01";
            string inactivePath = gold ? "LOTD/Buttons_Yellow/ds_btn_inactive01" : "LOTD/Buttons/ds_btn_inactive01";
            var spInact = LoadSprite(inactivePath);
            var spAct = LoadSprite(activePath);

            var img = AddImage(rt.gameObject, spInact ?? WhiteSprite,
                spInact != null ? Color.white : (gold ? new Color(0.65f, 0.45f, 0.1f, 1f) : new Color(0.10f, 0.25f, 0.50f, 1f)));
            img.type = Image.Type.Simple;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var ss = new SpriteState();
            if (spAct != null)
            {
                ss.highlightedSprite = spAct;
                ss.pressedSprite = spAct;
                ss.selectedSprite = spAct;
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = ss;
            }
            var txt = AddText(rt, "Label", label, fontSize, TextAnchor.MiddleCenter, TextPrimary, FontStyle.Bold);
            var sh = txt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sh.effectDistance = new Vector2(1f, -1f);

            btn.onClick.AddListener(() =>
            {
                LotdAudio.PlayClick();
                onClick?.Invoke();
            });
            return btn;
        }

        public static Button CreateWrappedButton(Transform parent, string name, string label, Action onClick,
            int fontSize, bool gold, float minHeight, Sprite icon = null, int sidePadding = 24)
        {
            var button = CreateLotdButton(parent, name, label, onClick, fontSize, gold);
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

        public static Dropdown CreateLotdDropdown(Transform parent, string name, IReadOnlyList<string> choices,
            Action<int> onChanged, bool dynamicChoices = false)
        {
            const float rowHeight = 28f;
            const int visibleRows = 7;
            var root = FillRect(name, parent);
            var background = AddImage(root.gameObject, LoadSprite("LOTD/Buttons/ds_btn_inactive01"), Color.white);
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
            dropdown.template = template;
            dropdown.captionText = caption;
            dropdown.itemText = label;
            foreach (string choice in choices)
                dropdown.options.Add(new Dropdown.OptionData(choice));
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(value =>
            {
                LotdAudio.PlayClick();
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
        private GameObject _missingArtwork;
        private CardFinishGraphic _finish;
        private Image _frameImg;
        private Image _attrImg;
        private Image _limitImg;
        private Text _titleText;
        private Text _levelText;
        private Text _leftScaleText;
        private Text _rightScaleText;
        private Text _atkDefText;
        private DuelRatingBadge _duelFaceRating;
        private DuelStatStrip _duelFaceCombat;
        private Text _rulesText;
        private RectTransform _rulesArea;
        private GameObject _backObj;
        private bool _detailed;
        private uint _retainedArtwork;
        private int _retainedVariant;
        private (uint Code, int Variant) _artworkSource;
        private DeckCardAppearance? _deckAppearance;
        private GameObject _deckEditionBadge;
        private GameObject _destinyBadge;
        private bool _overframeEnabled;
        private bool _cinematicOverframe;
        private bool _overframeBound;
        private Sprite _retainedCutIn;
        private Image _overframePortrait;
        private CanvasGroup _overframe;
        private RectTransform _overframeTilt;
        private Image _overframeShade;
        private Image _overframeBackground;
        private AspectRatioFitter _overframeBackgroundFit;
        private CardFinishGraphic _overframeFinish;
        private CardOverframeGraphic _overframeTrim;

        internal bool HasOverframe => _overframe != null && _overframe.gameObject.activeSelf;

        public uint CurrentCode { get; private set; }
        internal bool UseDuelArtwork { get; set; }
        internal Sprite ArtworkSprite => _artImg.sprite;

        public static CompositeCardView Create(Transform parent, string name, bool detailedOverlay = false, bool duelArtwork = false)
            => Create(parent, name, detailedOverlay, duelArtwork, cinematicOverframe: false);

        internal static CompositeCardView Create(Transform parent, string name, bool detailedOverlay, bool duelArtwork,
            bool cinematicOverframe)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var view = go.AddComponent<CompositeCardView>();
            view.UseDuelArtwork = duelArtwork;
            view._cinematicOverframe = cinematicOverframe;
            view.BuildHierarchy(detailedOverlay);
            return view;
        }

        private void BuildHierarchy(bool detailed)
        {
            _detailed = detailed;

            var innerRt = LotdUiKit.FillRect("AspectBox", transform);
            var arf = innerRt.gameObject.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.aspectRatio = LotdUiKit.CardAspectRatio;

            var artRt = LotdUiKit.CreateRect("Artwork", innerRt, CardFaceFrame.Artwork.min, CardFaceFrame.Artwork.max,
                Vector2.zero, Vector2.zero);
            _artImg = LotdUiKit.AddImage(artRt.gameObject, null, Color.white, false, false);

            var frameRt = LotdUiKit.FillRect("Frame", innerRt);
            _frameImg = LotdUiKit.AddImage(frameRt.gameObject, null, Color.white, false, false);

            var titleRt = LotdUiKit.CreateRect("CardTitle", innerRt,
                new Vector2(.09f, .89f), new Vector2(detailed ? .795f : .91f, .953f), Vector2.zero, Vector2.zero);
            _titleText = LotdUiKit.AddFittedText(titleRt, "T", "", detailed ? 22 : 13, detailed ? 8 : 4,
                TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            var ts = _titleText.gameObject.AddComponent<Shadow>();
            ts.effectColor = new Color(0, 0, 0, 0.85f);
            ts.effectDistance = new Vector2(1, -1);

            if (detailed)
            {
                var attrRt = LotdUiKit.CreateRect("AttrIcon", innerRt,
                    new Vector2(0.822f, 0.89f), new Vector2(0.914f, 0.946f), Vector2.zero, Vector2.zero);
                _attrImg = LotdUiKit.AddImage(attrRt.gameObject, null, Color.white, true, false);

                var lvRt = LotdUiKit.CreateRect("LevelInfo", innerRt,
                    new Vector2(0.095f, 0.825f), new Vector2(0.90f, 0.883f), Vector2.zero, Vector2.zero);
                var levelLabel = UseDuelArtwork ? LotdUiKit.FillRect("Type", lvRt) : lvRt;
                _levelText = LotdUiKit.AddFittedText(levelLabel, "Lv", "", 12, 7, TextAnchor.MiddleRight, LotdUiKit.GoldAccent, FontStyle.Bold);
                var ls = _levelText.gameObject.AddComponent<Shadow>();
                ls.effectColor = Color.black;
                if (UseDuelArtwork)
                {
                    var rating = LotdUiKit.CreateRect("Rating", lvRt, new Vector2(.55f, .02f), new Vector2(.985f, .98f),
                        Vector2.zero, Vector2.zero);
                    _duelFaceRating = new DuelRatingBadge(rating, compact: true);
                }

                var leftScale = LotdUiKit.CreateRect("LeftScale", innerRt,
                    new Vector2(0.077f, 0.263f), new Vector2(0.130f, 0.319f), Vector2.zero, Vector2.zero);
                var rightScale = LotdUiKit.CreateRect("RightScale", innerRt,
                    new Vector2(0.87f, 0.263f), new Vector2(0.923f, 0.319f), Vector2.zero, Vector2.zero);
                _leftScaleText = AddScaleValue(leftScale, new Color(.07f, .21f, .43f));
                _rightScaleText = AddScaleValue(rightScale, new Color(.44f, .08f, .07f));

                Text AddScaleValue(RectTransform parent, Color color) => UseDuelArtwork
                    ? DuelTypography.AddNumber(parent, "Value", "", 14, TextAnchor.MiddleCenter, color)
                    : LotdUiKit.AddFittedText(parent, "Value", "", 11, 7, TextAnchor.MiddleCenter, color, FontStyle.Bold);

                _rulesArea = LotdUiKit.CreateRect("Rules", innerRt,
                    new Vector2(0.09f, 0.120f), new Vector2(0.91f, 0.244f), Vector2.zero, Vector2.zero);
                _rulesArea.gameObject.AddComponent<RectMask2D>();
                _rulesText = LotdUiKit.AddText(_rulesArea, "Text", "", 9, TextAnchor.UpperLeft, new Color(0.09f, 0.07f, 0.05f));
                _rulesText.supportRichText = false;

                var statRt = LotdUiKit.CreateRect("AtkDef", innerRt,
                    new Vector2(0.090f, UseDuelArtwork ? .057f : .064f),
                    new Vector2(0.91f, UseDuelArtwork ? .128f : .115f), Vector2.zero, Vector2.zero);
                if (UseDuelArtwork) _duelFaceCombat = new DuelStatStrip(statRt, compact: true);
                else _atkDefText = LotdUiKit.AddFittedText(statRt, "Stat", "", 11, 7, TextAnchor.MiddleRight,
                        new Color(0.12f, 0.1f, 0.1f, 1f), FontStyle.Bold);
            }

            var limRt = LotdUiKit.CreateRect("LimitBadge", innerRt,
                new Vector2(0.03f, 0.78f), new Vector2(0.28f, 0.95f), Vector2.zero, Vector2.zero);
            _limitImg = LotdUiKit.AddImage(limRt.gameObject, null, Color.white, true, false);
            _limitImg.gameObject.SetActive(false);

            var backRt = LotdUiKit.FillRect("CardBack", innerRt);
            LotdUiKit.AddImage(backRt.gameObject, CardArtLoader.GetCardBackSprite(), Color.white, false, false);
            _backObj = backRt.gameObject;
            _backObj.SetActive(false);
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
                if (_duelFaceCombat != null)
                {
                    _duelFaceCombat.Hide();
                    _duelFaceRating.Bind(default);
                    _levelText.text = "";
                    _leftScaleText.gameObject.SetActive(false);
                    _rightScaleText.gameObject.SetActive(false);
                }
                _backObj.SetActive(true);
                return;
            }
            _backObj.SetActive(false);

            var info = CardDatabase.Get(code);
            _titleText.text = info != null ? info.name : $"카드 #{code}";
            uint type = live != null && live.type != 0 ? live.type : (info?.type ?? YgoConstants.TYPE_EFFECT);
            bool isPend = (type & YgoConstants.TYPE_PENDULUM) != 0;

            var artRt = _artImg.rectTransform;
            Rect artwork = isPend ? CardFaceFrame.PendulumArtwork : CardFaceFrame.Artwork;
            artRt.anchorMin = artwork.min;
            artRt.anchorMax = artwork.max;

            BindArtwork(code);
            BindRarityFinish(info);
            RefreshOverframe();
            SetDeckEditionBadge(!_detailed && !_cinematicOverframe && appearance?.overframe == true);
            _frameImg.sprite = CardFaceFrame.Get(type);

            if (_detailed)
            {
                var readout = presentation ?? new DuelCardReadout(info, live);
                bool monster = _duelFaceCombat != null ? readout.IsMonster : (type & YgoConstants.TYPE_MONSTER) != 0;
                float rulesBottom = _duelFaceCombat != null ? .134f : .120f;
                _rulesArea.anchorMin = new Vector2(0.09f, monster ? rulesBottom : .074f);
                _rulesText.text = info?.desc ?? "";
                ApplyOverframeInk();

                uint attr = live != null && live.attribute != 0 ? live.attribute : (info?.attr ?? 0);
                Sprite attrSp = null;
                if ((type & YgoConstants.TYPE_SPELL) != 0) attrSp = LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_ATTR_MAGIC");
                else if ((type & YgoConstants.TYPE_TRAP) != 0) attrSp = LotdUiKit.LoadSprite("LOTD/Icons/ICON_ID_ATTR_TRAP");
                else attrSp = LotdUiKit.GetAttributeSprite(attr);

                _attrImg.gameObject.SetActive(attrSp != null);
                _attrImg.sprite = attrSp;

                _leftScaleText.gameObject.SetActive(isPend && monster);
                _rightScaleText.gameObject.SetActive(isPend && monster);
                if (isPend && monster)
                {
                    _leftScaleText.text = readout.LeftScale.ToString();
                    _rightScaleText.text = readout.RightScale.ToString();
                }

                if (_duelFaceCombat != null)
                {
                    var rating = readout.InspectionRating;
                    _duelFaceRating.Bind(rating);
                    _duelFaceCombat.Bind(readout, position: 0, showModifiers: false);
                    _levelText.text = !monster && rating.Kind == DuelRatingKind.None ? YgoConstants.GetTypeTagKR(type) : "";
                    _levelText.color = Color.white;
                }
                else if (monster)
                {
                    uint lv = live != null ? ((type & YgoConstants.TYPE_XYZ) != 0 ? live.rank : ((type & YgoConstants.TYPE_LINK) != 0 ? live.link : live.level)) : (info?.level ?? 0);
                    if ((type & YgoConstants.TYPE_LINK) != 0)
                    {
                        _levelText.text = $"LINK-{lv}";
                        _levelText.color = LotdUiKit.CyanAccent;
                    }
                    else if ((type & YgoConstants.TYPE_XYZ) != 0)
                    {
                        _levelText.text = new string('★', (int)Mathf.Min(13, lv)) + $" (R{lv})";
                        _levelText.color = new Color(0.95f, 0.55f, 0.2f, 1f);
                    }
                    else
                    {
                        _levelText.text = new string('★', (int)Mathf.Min(13, lv));
                        _levelText.color = LotdUiKit.GoldAccent;
                    }

                    int atk = live?.attack ?? info?.atk ?? 0;
                    int def = live?.defense ?? info?.def ?? 0;
                    string aStr = atk < 0 ? "?" : atk.ToString();
                    string defense = (type & YgoConstants.TYPE_LINK) != 0
                        ? $"LINK-{lv}" : $"DEF/{(def < 0 ? "?" : def.ToString())}";
                    _atkDefText.text = $"ATK/{aStr}  {defense}";
                }
                else
                {
                    _levelText.text = YgoConstants.GetTypeTagKR(type);
                    _levelText.color = Color.white;
                    _atkDefText.text = "";
                }
            }
        }

        internal void SetRarityHighlight(float amount)
        {
            if (_finish != null && _finish.gameObject.activeSelf) _finish.SetHighlight(amount);
            if (_overframeFinish != null && HasOverframe) _overframeFinish.SetHighlight(Mathf.Lerp(.18f, .72f, amount));
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
                var foil = LotdUiKit.FillRect("RarityFinish", _artImg.transform);
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
            if (_backObj != null && !_backObj.activeSelf)
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
            int variant = _deckAppearance?.ArtworkVariant ?? (UseDuelArtwork ? CardArtLoader.GetDuelArtworkVariant(code) : -1);
            if (code == _retainedArtwork && variant == _retainedVariant && _artImg.sprite != null) return;
            ReleaseArtwork();
            _artImg.sprite = CardArtLoader.RetainArtwork(code, variant, out _artworkSource);
            bool missing = _artImg.sprite == CardArtLoader.GetFallbackArt();
            if (missing && _missingArtwork == null)
            {
                var label = LotdUiKit.FillRect("MissingArtwork", _artImg.rectTransform, 2f);
                LotdUiKit.AddFittedText(label, "Label", "그림 없음", 14, 7, TextAnchor.MiddleCenter, LotdUiKit.TextMuted);
                _missingArtwork = label.gameObject;
            }
            if (_missingArtwork != null) _missingArtwork.SetActive(missing);
            _retainedArtwork = code;
            _retainedVariant = variant;
        }

        private void SetDeckEditionBadge(bool shown)
        {
            if (shown && _deckEditionBadge == null)
            {
                var aspect = _artImg.transform.parent;
                var badge = LotdUiKit.CreateRect("DeckEdition", aspect, new Vector2(.058f, .048f),
                    new Vector2(.255f, .166f), Vector2.zero, Vector2.zero);
                var rim = LotdUiKit.AddFittedText(badge, "Diamond", "◆", 24, 6, TextAnchor.MiddleCenter,
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
                var badge = LotdUiKit.CreateRect("DestinyDraw", _artImg.transform.parent,
                    new Vector2(.686f, .767f), new Vector2(.97f, .968f), Vector2.zero, Vector2.zero);
                LotdUiKit.AddImage(badge.gameObject, LotdUiKit.WhiteSprite, new Color(.29f, .135f, .019f, .96f), false, false);
                LotdUiKit.AddFittedText(badge, "Star", "★", 24, 8, TextAnchor.MiddleCenter, LotdUiKit.GoldAccent, FontStyle.Bold);
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
            if (!(_deckAppearance?.overframe ?? _overframeEnabled) || _overframeBound || _retainedArtwork == 0) return;
            _overframeBound = true;
            var (code, variant) = _artworkSource.Code == 0 ? (_retainedArtwork, _retainedVariant) : _artworkSource;
            _retainedCutIn = DuelVisualAssets.RetainCutIn(code, variant);
            if (_retainedCutIn == null && _deckAppearance?.overframe != true) return;
            if (_overframe == null)
            {
                var layer = LotdUiKit.FillRect("Overframe", _cinematicOverframe ? transform : _frameImg.transform.parent);
                if (_cinematicOverframe)
                {
                    var aspect = layer.gameObject.AddComponent<AspectRatioFitter>();
                    aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    aspect.aspectRatio = LotdUiKit.CardAspectRatio;
                }
                else
                {
                    layer.SetSiblingIndex(_frameImg.transform.GetSiblingIndex() + 1);
                    layer.gameObject.AddComponent<RectMask2D>();
                }
                _overframe = layer.gameObject.AddComponent<CanvasGroup>();
                _overframe.blocksRaycasts = _overframe.interactable = false;
                if (!_cinematicOverframe)
                {
                    var background = LotdUiKit.FillRect("ExpandedArtwork", layer);
                    _overframeBackground = LotdUiKit.AddImage(background.gameObject, null, new Color(.55f, .63f, .76f), false, false);
                    _overframeBackgroundFit = background.gameObject.AddComponent<AspectRatioFitter>();
                    _overframeBackgroundFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                }
                var shade = LotdUiKit.FillRect("OverframeShade", _cinematicOverframe ? _artImg.rectTransform : layer);
                _overframeShade = LotdUiKit.AddImage(shade.gameObject,
                    _cinematicOverframe ? LotdUiKit.WhiteSprite : DuelStyle.OverframeBackdrop,
                    _cinematicOverframe ? new Color(.021f, .033f, .048f) : new Color(1f, 1f, 1f, .51f), false, false);
                if (_cinematicOverframe) CreateOverframeTrim(layer);
                else
                {
                    _overframeFinish = LotdUiKit.FillRect("ExpandedFinish", layer).gameObject.AddComponent<CardFinishGraphic>();
                    _overframeFinish.raycastTarget = false;
                }
                _overframeTilt = LotdUiKit.FillRect("Depth", layer);
                _overframeTilt.pivot = new Vector2(.5f, .78f);
                var portrait = LotdUiKit.CreateRect("LocalCutIn", _overframeTilt,
                    _cinematicOverframe ? new Vector2(-.13f, .405f) : new Vector2(-.088f, .055f),
                    _cinematicOverframe ? new Vector2(1.13f, 1.166f) : new Vector2(1.088f, 1.07f),
                    Vector2.zero, Vector2.zero);
                portrait.pivot = new Vector2(.5f, 1f);
                _overframePortrait = LotdUiKit.AddImage(portrait.gameObject, null, Color.white, true, false);
                _overframePortrait.maskable = !_cinematicOverframe;
                if (!_cinematicOverframe) CreateOverframeTrim(layer);
            }
            if (_overframeBackground != null)
            {
                _overframeBackground.sprite = _artImg.sprite;
                Rect art = _artImg.sprite.rect;
                _overframeBackgroundFit.aspectRatio = art.width / Mathf.Max(1f, art.height);
                _overframeFinish.Bind(_retainedArtwork, CardRarity.UltraRare);
                _overframeFinish.SetHighlight(.18f);
            }
            _overframePortrait.sprite = _retainedCutIn;
            _overframePortrait.enabled = _retainedCutIn != null;
            _overframeShade.gameObject.SetActive(true);
            _overframe.gameObject.SetActive(true);
            ApplyOverframeInk();
            PoseOverframe(1f, 1f, Vector2.zero, 0f);
        }

        private void CreateOverframeTrim(Transform layer)
        {
            _overframeTrim = LotdUiKit.FillRect("OverframeTrim", layer).gameObject.AddComponent<CardOverframeGraphic>();
            _overframeTrim.Configure(_cinematicOverframe, _overframeFinish?.canvasRenderer);
        }

        private void ApplyOverframeInk()
        {
            if (!_detailed || _cinematicOverframe) return;
            bool fullArt = HasOverframe;
            _rulesText.color = fullArt ? new Color(.92f, .94f, 1f) : new Color(.09f, .07f, .05f);
            if (_atkDefText != null)
                _atkDefText.color = fullArt ? Color.white : new Color(.12f, .10f, .10f);
            _leftScaleText.color = fullArt ? new Color(.54f, .80f, 1f) : new Color(.07f, .21f, .43f);
            _rightScaleText.color = fullArt ? new Color(1f, .66f, .62f) : new Color(.44f, .08f, .07f);
        }

        internal void PoseOverframe(float opacity, float scale, Vector2 parallax, float shimmer)
        {
            if (!HasOverframe) return;
            _overframe.alpha = Mathf.Clamp01(opacity);
            if (_cinematicOverframe)
            {
                _overframeShade.canvasRenderer.SetAlpha(.72f * _overframe.alpha);
                float spread = Mathf.Sin(Mathf.Clamp01(shimmer) * Mathf.PI);
                _overframeTrim.transform.localScale = Vector3.one * (1.03f + spread * .26f);
                _overframeTrim.canvasRenderer.SetAlpha(_deckAppearance?.overframe == true ? .42f + spread * .52f : 0f);
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
            _overframe.gameObject.SetActive(false);
        }

        private void ReleaseArtwork()
        {
            HideOverframe();
            if (_retainedArtwork == 0) return;
            _artImg.sprite = null;
            if (_missingArtwork != null) _missingArtwork.SetActive(false);
            CardArtLoader.ReleaseArtwork(_retainedArtwork, _retainedVariant);
            _retainedArtwork = 0;
            _artworkSource = default;
        }
    }

    public static class LotdAudio
    {
        private static AudioSource _sfxSource;
        private static AudioSource _bgmSource;
        private static AudioClip _clipClick, _clipDraw, _clipSummon, _clipAttack, _clipDamage, _clipChain, _clipWin, _clipPhase, _clipDirectAttack, _clipTick, _clipCoin, _clipDice;
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
            _clipClick = MakeTone(0.06f, 880f, 1180f, 0.25f);
            _clipDraw = DuelVisualAssets.LoadSound("Draw") ?? MakeTone(0.09f, 520f, 760f, 0.25f);
            _clipSummon = DuelVisualAssets.LoadSound("Summon") ?? MakeTone(0.24f, 240f, 540f, 0.35f);
            _clipAttack = DuelVisualAssets.LoadSound("Attack") ?? MakeTone(0.18f, 440f, 110f, 0.45f);
            _clipDamage = DuelVisualAssets.LoadSound("Damage") ?? MakeTone(0.25f, 180f, 65f, 0.48f);
            _clipChain = DuelVisualAssets.LoadSound("Chain") ?? MakeTone(0.16f, 660f, 990f, 0.30f);
            _clipWin = MakeTone(0.45f, 523f, 1046f, 0.35f);
            _clipPhase = MakeTone(0.24f, 440f, 880f, 0.32f);
            _clipDirectAttack = DuelVisualAssets.LoadSound("DirectAttack") ?? MakeTone(0.32f, 280f, 60f, 0.50f);
            _clipTick = MakeTone(0.04f, 1320f, 1600f, 0.15f);
            _clipCoin = MakeTone(0.22f, 980f, 1320f, 0.35f);
            _clipDice = MakeTone(0.24f, 420f, 780f, 0.35f);

            _clipMenuBgm = MakeMenuBgm(sr);
        }

        public static void PlayClick() => Play(_clipClick);
        public static void PlayDraw() => Play(_clipDraw);
        public static void PlaySummon() => Play(_clipSummon);
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
            if (_bgmSource == null || BgmVolume < 0.01f) return;
            _clipDuelBgm ??= DuelVisualAssets.LoadSound("DuelMusic") ?? MakeDuelBgm(22050);
            if (_bgmSource.clip == _clipDuelBgm && _bgmSource.isPlaying) return;
            _bgmSource.clip = _clipDuelBgm;
            _bgmSource.loop = true;
            _bgmSource.volume = BgmVolume;
            _bgmSource.Play();
        }

        public static void PlayMenuBgm()
        {
            if (_bgmSource == null || _clipMenuBgm == null || BgmVolume < 0.01f) return;
            if (_bgmSource.clip == _clipMenuBgm && _bgmSource.isPlaying) return;
            _bgmSource.clip = _clipMenuBgm;
            _bgmSource.loop = true;
            _bgmSource.volume = BgmVolume;
            _bgmSource.Play();
        }

        public static void StopBgm()
        {
            if (_bgmSource != null) _bgmSource.Stop();
        }

        public static void UpdateBgmVolume()
        {
            if (_bgmSource != null)
            {
                _bgmSource.volume = BgmVolume;
                if (BgmVolume < 0.01f && _bgmSource.isPlaying) _bgmSource.Pause();
                else if (BgmVolume >= 0.01f && !_bgmSource.isPlaying && _bgmSource.clip != null) _bgmSource.Play();
            }
        }

        private static void Play(AudioClip clip)
        {
            if (_sfxSource != null && clip != null && SfxVolume > 0.01f)
                _sfxSource.PlayOneShot(clip, SfxVolume);
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
