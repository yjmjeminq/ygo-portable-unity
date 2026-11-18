using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal readonly struct DuelCardReadout
    {
        internal readonly CardInfo Printed;
        internal readonly LiveCardState Live;

        internal DuelCardReadout(CardInfo printed, LiveCardState live = null)
        {
            Printed = printed;
            Live = live;
        }

        internal uint Type => Live?.type > 0 ? Live.type : Printed?.type ?? 0;
        internal bool IsMonster => (Type & YgoConstants.TYPE_MONSTER) != 0;
        internal bool IsLink => (Type & YgoConstants.TYPE_LINK) != 0;
        internal bool IsXyz => (Type & YgoConstants.TYPE_XYZ) != 0;
        internal bool IsPendulum => (Type & YgoConstants.TYPE_PENDULUM) != 0;
        internal bool IsScale => Type == (YgoConstants.TYPE_PENDULUM | YgoConstants.TYPE_SPELL);
        internal int Attack => Live?.attack ?? Printed?.atk ?? -1;
        internal int Defense => Live?.defense ?? Printed?.def ?? -1;
        internal uint LeftScale => Live?.lscale ?? Printed?.lscale ?? 0;
        internal uint RightScale => Live?.rscale ?? Printed?.rscale ?? 0;
        internal uint LinkMarkers => IsLink ? Live?.linkMarker ?? Printed?.link ?? 0 : 0;
        internal int Materials => Live?.overlayCodes.Count ?? 0;
        internal bool Disabled => Live?.effectsDisabled == true;

        internal uint? ScaleIn(uint location, uint sequence) =>
            location == YgoConstants.LOCATION_SZONE && IsScale && sequence is 0 or 4
                ? sequence == 0 ? LeftScale : RightScale : null;

        internal string Rating
        {
            get
            {
                if (!IsMonster) return "";
                uint printedRating = Printed?.level ?? 0;
                if (IsLink) return $"링크 {Live?.link ?? printedRating}";
                if (IsXyz) return $"랭크 {Live?.rank ?? printedRating}";
                uint level = Live?.level ?? printedRating;
                return level > 0 ? $"레벨 {level}" : "";
            }
        }

        internal string Status
        {
            get
            {
                string badge = Live != null && (IsXyz || Materials > 0) ? $"소재 {Materials}" :
                    (Type & YgoConstants.TYPE_TOKEN) != 0 ? "토큰" : "";
                if (!Disabled) return badge;
                return badge.Length == 0 ? "효과 무효" : $"{badge} · 무효";
            }
        }
    }

    internal sealed class DuelStatStrip
    {
        private static readonly Color AttackColor = new Color(1f, .76f, .5f);
        private static readonly Color DefenseColor = new Color(.61f, .87f, .98f);
        private static readonly Color Increased = new Color(.58f, 1f, .82f);
        private static readonly Color Decreased = new Color(1f, .59f, .56f);
        private static readonly Color Backing = new Color(.045f, .068f, .085f, .98f);
        private readonly RectTransform _root;
        private readonly StatCell _attack;
        private readonly StatCell _defense;
        private readonly bool _compact;
        private DuelLinkCompass _compass;

        internal DuelStatStrip(RectTransform root, bool compact)
        {
            _root = root;
            _compact = compact;
            _attack = new StatCell(root, "Attack", compact ? "ATK" : "공격력", AttackColor, compact);
            _defense = new StatCell(root, "Defense", compact ? "DEF" : "수비력", DefenseColor, compact);
            Hide();
        }

        internal void Hide() => _root.gameObject.SetActive(false);

        internal void Bind(DuelCardReadout card, bool opponent = false)
        {
            _root.gameObject.SetActive(card.IsMonster);
            if (!card.IsMonster) return;

            bool hasDirections = card.LinkMarkers != 0;
            if (hasDirections) (_compass ??= new DuelLinkCompass(_root)).Bind(card.LinkMarkers, opponent);
            else _compass?.Hide();
            _attack.Rect.anchorMin = card.IsLink && _compact && !hasDirections ? new Vector2(.21f, 0f) : Vector2.zero;
            float attackWidth = !card.IsLink ? .493f : hasDirections ? .62f : _compact ? .79f : 1f;
            _attack.Rect.anchorMax = new Vector2(attackWidth, 1f);
            _defense.Rect.anchorMin = new Vector2(.507f, 0f);
            _defense.Rect.anchorMax = Vector2.one;
            _defense.Rect.gameObject.SetActive(!card.IsLink);
            _attack.Bind(card.Attack, card.Live?.baseAttack ?? card.Attack, card.Live?.IsAttack == true);
            if (!card.IsLink)
                _defense.Bind(card.Defense, card.Live?.baseDefense ?? card.Defense, card.Live?.IsDefense == true);
        }

        private sealed class StatCell
        {
            internal readonly RectTransform Rect;
            private readonly Image _plate;
            private readonly Image _rail;
            private readonly Text _value;
            private readonly Text _change;
            private readonly Color _accent;

            internal StatCell(Transform parent, string key, string label, Color accent, bool compact)
            {
                _accent = accent;
                Rect = LotdUiKit.FillRect(key, parent);
                _plate = DuelReadoutPlate.Add(Rect, Backing);
                var rail = LotdUiKit.CreateRect("Rail", Rect, new Vector2(.07f, 0f), new Vector2(.93f, 0f),
                    Vector2.zero, new Vector2(0f, 1.8f));
                _rail = LotdUiKit.AddImage(rail.gameObject, LotdUiKit.WhiteSprite, accent, false, false);
                var heading = LotdUiKit.CreateRect("Caption", Rect, new Vector2(.09f, .60f), new Vector2(.76f, .99f),
                    Vector2.zero, Vector2.zero);
                var title = LotdUiKit.AddFittedText(heading, "Label", label, compact ? 11 : 14, 8,
                    TextAnchor.MiddleLeft, accent, FontStyle.Bold);
                title.verticalOverflow = VerticalWrapMode.Overflow;
                var badge = LotdUiKit.CreateRect("Change", Rect, new Vector2(.76f, .60f), new Vector2(.94f, .99f),
                    Vector2.zero, Vector2.zero);
                _change = LotdUiKit.AddFittedText(badge, "Arrow", "", 12, 8, TextAnchor.MiddleCenter, accent, FontStyle.Bold);
                _change.verticalOverflow = VerticalWrapMode.Overflow;
                var digits = LotdUiKit.CreateRect("Digits", Rect, new Vector2(.055f, .035f), new Vector2(.945f, .68f),
                    Vector2.zero, Vector2.zero);
                _value = LotdUiKit.AddFittedText(digits, "Value", "", compact ? 22 : 28, 9,
                    TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            }

            internal void Bind(int value, int original, bool active)
            {
                int change = value >= 0 && original >= 0 ? value.CompareTo(original) : 0;
                _value.text = value >= 0 ? value.ToString() : "?";
                _value.color = change > 0 ? Increased : change < 0 ? Decreased : active ? Color.white : DuelStyle.Muted;
                _change.text = change > 0 ? "▲" : change < 0 ? "▼" : "";
                _change.color = _value.color;
                _plate.color = active ? Color.Lerp(Backing, _accent, .15f) : Backing;
                _rail.color = new Color(_accent.r, _accent.g, _accent.b, active ? .95f : .28f);
            }
        }
    }

    internal sealed class DuelLinkCompass
    {
        private static readonly (uint mask, string name, Vector2 direction)[] Marks =
        {
            (0x040, "TopLeft", new Vector2(-1, 1)), (0x080, "Top", Vector2.up),
            (0x100, "TopRight", new Vector2(1, 1)), (0x008, "Left", Vector2.left),
            (0x020, "Right", Vector2.right), (0x001, "BottomLeft", new Vector2(-1, -1)),
            (0x002, "Bottom", Vector2.down), (0x004, "BottomRight", new Vector2(1, -1)),
        };
        private static Sprite _arrow;
        private readonly RectTransform _root;
        private readonly GameObject[] _marks = new GameObject[Marks.Length];

        internal DuelLinkCompass(Transform parent)
        {
            _root = LotdUiKit.CreateRect("LinkDirections", parent, new Vector2(.635f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(_root, new Color(.043f, .090f, .105f, .98f));
            var map = LotdUiKit.FillRect("Compass", _root, 2.5f);
            var aspect = map.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1f;
            var center = LotdUiKit.CreateRect("Center", map, Vector2.one * .415f, Vector2.one * .585f, Vector2.zero, Vector2.zero);
            LotdUiKit.AddImage(center.gameObject, DuelStyle.Disc, new Color(.60f, .91f, .89f), true, false);
            if (_arrow == null) _arrow = BuildArrow();
            for (int i = 0; i < Marks.Length; i++)
            {
                var item = Marks[i];
                var at = Vector2.one * .5f + item.direction * .328f;
                var marker = LotdUiKit.CreateRect(item.name, map, at - Vector2.one * .137f, at + Vector2.one * .137f,
                    Vector2.zero, Vector2.zero);
                marker.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, item.direction));
                LotdUiKit.AddImage(marker.gameObject, _arrow, new Color(.65f, 1f, .91f), true, false);
                _marks[i] = marker.gameObject;
            }
        }

        internal void Bind(uint markers, bool opponent)
        {
            _root.gameObject.SetActive(true);
            for (int i = 0; i < Marks.Length; i++)
                _marks[i].SetActive((markers & Marks[opponent ? Marks.Length - 1 - i : i].mask) != 0);
        }

        internal void Hide() => _root.gameObject.SetActive(false);

        private static Sprite BuildArrow()
        {
            const int size = 24;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float halfWidth = 11f - y * .49f;
                float opacity = Mathf.Clamp01(halfWidth - Mathf.Abs(x + .5f - size * .5f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(opacity * 255f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Link Direction Marker",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f);
        }
    }

    internal sealed class DuelFieldReadout
    {
        private static readonly Color Backing = new Color(.045f, .068f, .085f, .96f);
        private static readonly Color RankColor = new Color(.91f, .75f, 1f);
        private readonly RectTransform _root;
        private readonly RectTransform _combat;
        private readonly RectTransform _labels;
        private readonly DuelStatStrip _stats;
        private readonly Text _rating;
        private readonly Text _status;
        private readonly bool _inPreview;

        internal DuelFieldReadout(Transform slot, bool inPreview = false)
        {
            _inPreview = inPreview;
            _root = LotdUiKit.FillRect("CardReadout", slot);
            _combat = LotdUiKit.FillRect("Combat", _root);
            _stats = new DuelStatStrip(_combat, compact: true);
            _labels = LotdUiKit.FillRect("Labels", _root);
            // Leave room for the selection check on choice cards.
            _rating = CreateLabel("Rating", .035f, inPreview ? .365f : .485f, 15);
            _status = CreateLabel("Status", inPreview ? .38f : .50f, inPreview ? .79f : .965f, 13);
            if (inPreview)
            {
                _combat.anchorMin = new Vector2(.03f, 0f);
                _combat.anchorMax = new Vector2(.97f, 0f);
                _combat.offsetMin = new Vector2(0f, 3f);
                _combat.offsetMax = new Vector2(0f, 35f);
                _labels.anchorMin = Vector2.up;
                _labels.anchorMax = Vector2.one;
                _labels.offsetMin = new Vector2(0f, -24f);
                _labels.offsetMax = new Vector2(0f, -4f);
            }
        }

        private Text CreateLabel(string name, float left, float right, int size)
        {
            var chip = LotdUiKit.CreateRect(name, _labels, new Vector2(left, 0f), new Vector2(right, 1f),
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(chip, Backing);
            return LotdUiKit.AddFittedText(chip, "Value", "", size, 8, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        }

        internal void Hide() => _root.gameObject.SetActive(false);

        internal void Bind(DuelCardReadout card, bool opponent, uint? scale)
        {
            _root.gameObject.SetActive(true);
            if (!_inPreview)
            {
                _combat.anchorMin = new Vector2(.03f, opponent ? .715f : .015f);
                _combat.anchorMax = new Vector2(.97f, opponent ? .985f : .285f);
                _labels.anchorMin = new Vector2(0f, opponent ? .025f : .765f);
                _labels.anchorMax = new Vector2(1f, opponent ? .235f : .975f);
            }
            _stats.Bind(card, opponent);
            SetLabel(_rating, scale.HasValue ? $"스케일 {scale.Value}" : card.Rating,
                card.IsXyz ? RankColor : card.IsLink || scale.HasValue ? DuelStyle.Player : DuelStyle.Gold);
            SetLabel(_status, card.Status,
                card.Disabled ? new Color(1f, .65f, .58f) : DuelStyle.Muted);
        }

        private static void SetLabel(Text label, string content, Color color)
        {
            label.transform.parent.gameObject.SetActive(content.Length != 0);
            label.text = content;
            label.color = color;
        }
    }

    internal static class DuelReadoutPlate
    {
        private static Sprite _corners;

        internal static Image Add(RectTransform root, Color tint)
        {
            if (_corners == null) _corners = BuildCorners();
            var plate = LotdUiKit.AddImage(root.gameObject, _corners, tint, false, false);
            plate.type = Image.Type.Sliced;
            return plate;
        }

        private static Sprite BuildCorners()
        {
            const int size = 24, cut = 5;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int distance = Mathf.Min(x + y + 1, size * 2 - x - y - 1);
                byte alpha = distance < cut ? (byte)0 : distance == cut ? (byte)128 : (byte)255;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Duel Readout Plate",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, 100f, 0,
                SpriteMeshType.FullRect, Vector4.one * cut);
        }
    }
}
