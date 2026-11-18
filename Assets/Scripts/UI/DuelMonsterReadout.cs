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
        internal uint CounterCount => Live?.counterCount ?? 0;
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
            _attack = new StatCell(root, "Attack", "공격력", AttackColor, compact);
            _defense = new StatCell(root, "Defense", "수비력", DefenseColor, compact);
            Hide();
        }

        internal void Hide() => _root.gameObject.SetActive(false);

        internal void Bind(DuelCardReadout card, bool opponent = false, bool showChanges = false, uint? position = null)
        {
            _root.gameObject.SetActive(card.IsMonster);
            if (!card.IsMonster) return;

            Layout(card.IsLink, card.LinkMarkers, opponent);
            uint shownPosition = position ?? card.Live?.position ?? 0;
            _attack.Bind(card.Attack, card.Live?.baseAttack ?? card.Attack, (shownPosition & YgoConstants.POS_ATTACK) != 0, showChanges);
            if (!card.IsLink)
                _defense.Bind(card.Defense, card.Live?.baseDefense ?? card.Defense, (shownPosition & YgoConstants.POS_DEFENSE) != 0, showChanges);
        }

        internal void BindBattle(BattleCardStats card, bool link)
        {
            _root.gameObject.SetActive(card.IsFaceUp);
            if (!card.IsFaceUp) return;

            Layout(link, 0, false);
            _attack.Bind(card.Attack, card.Attack, !card.IsDefense);
            if (!link) _defense.Bind(card.Defense, card.Defense, card.IsDefense);
        }

        private void Layout(bool link, uint directions, bool opponent)
        {
            bool hasDirections = directions != 0;
            if (hasDirections) (_compass ??= new DuelLinkCompass(_root)).Bind(directions, opponent);
            else _compass?.Hide();
            _attack.Rect.anchorMin = link && _compact && !hasDirections ? new Vector2(.21f, 0f) : Vector2.zero;
            float attackWidth = !link ? .493f : hasDirections ? .62f : _compact ? .79f : 1f;
            _attack.Rect.anchorMax = new Vector2(attackWidth, 1f);
            _defense.Rect.anchorMin = new Vector2(.507f, 0f);
            _defense.Rect.anchorMax = Vector2.one;
            _defense.Rect.gameObject.SetActive(!link);
        }

        private sealed class StatCell
        {
            internal readonly RectTransform Rect;
            private readonly Image _plate;
            private readonly Image _rail;
            private readonly Text _value;
            private readonly Text _change;
            private readonly RectTransform _symbol;
            private readonly Color _accent;
            private int _lastValue = -1;

            internal StatCell(Transform parent, string key, string label, Color accent, bool compact)
            {
                _accent = accent;
                Rect = LotdUiKit.FillRect(key, parent);
                _plate = DuelReadoutPlate.Add(Rect, Backing);
                var rail = LotdUiKit.CreateRect("Rail", Rect, new Vector2(.07f, 0f), new Vector2(.93f, 0f),
                    Vector2.zero, new Vector2(0f, 1.8f));
                _rail = LotdUiKit.AddImage(rail.gameObject, LotdUiKit.WhiteSprite, accent, false, false);
                if (compact)
                {
                    _symbol = LotdUiKit.CreateRect("Symbol", Rect, new Vector2(.055f, .17f), new Vector2(.245f, .91f),
                        Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(_symbol.gameObject, DuelStatSymbols.Get(key == "Defense"), accent, true, false);
                }
                else
                {
                    var heading = LotdUiKit.CreateRect("Caption", Rect, new Vector2(.09f, .60f), new Vector2(.76f, .99f),
                        Vector2.zero, Vector2.zero);
                    var title = LotdUiKit.AddFittedText(heading, "Label", label, 14, 8, TextAnchor.MiddleLeft, accent, FontStyle.Bold);
                    title.verticalOverflow = VerticalWrapMode.Overflow;
                }
                var badge = LotdUiKit.CreateRect("Change", Rect, compact ? new Vector2(.055f, .04f) : new Vector2(.76f, .60f),
                    compact ? new Vector2(.245f, .43f) : new Vector2(.94f, .99f), Vector2.zero, Vector2.zero);
                _change = LotdUiKit.AddFittedText(badge, "Arrow", "", compact ? 10 : 12, compact ? 6 : 8,
                    TextAnchor.MiddleCenter, accent, FontStyle.Bold);
                _change.verticalOverflow = VerticalWrapMode.Overflow;
                var digits = LotdUiKit.CreateRect("Digits", Rect, compact ? new Vector2(.265f, .085f) : new Vector2(.055f, .035f),
                    compact ? new Vector2(.96f, .96f) : new Vector2(.945f, .68f), Vector2.zero, Vector2.zero);
                _value = LotdUiKit.AddFittedText(digits, "Value", "", compact ? 23 : 28, compact ? 7 : 9,
                    TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                _value.horizontalOverflow = HorizontalWrapMode.Overflow;
                _value.gameObject.AddComponent<DuelStatDigitFit>();
            }

            internal void Bind(int value, int original, bool active, bool showChanges = false)
            {
                int change = value >= 0 && original >= 0 ? value.CompareTo(original) : 0;
                string number = value >= 0 ? value.ToString() : "?";
                if (!showChanges) DuelReadoutPulse.Cancel(Rect);
                else if (_value.text.Length != 0 && _value.text != number)
                {
                    int delta = _lastValue >= 0 && value >= 0 ? value.CompareTo(_lastValue) : 0;
                    DuelReadoutPulse.Play(Rect, delta > 0 ? Increased : delta < 0 ? Decreased : _accent, delta < 0);
                }
                _lastValue = value;
                _value.text = number;
                _value.color = change > 0 ? Increased : change < 0 ? Decreased : active ? Color.white : DuelStyle.Muted;
                _change.text = change > 0 ? "▲" : change < 0 ? "▼" : "";
                _change.color = _value.color;
                if (_symbol != null) _symbol.anchorMin = new Vector2(.055f, change == 0 ? .17f : .44f);
                _plate.color = active ? Color.Lerp(Backing, _accent, .15f) : Backing;
                _rail.color = new Color(_accent.r, _accent.g, _accent.b, active ? .95f : .28f);
            }
        }
    }

    internal sealed class DuelStatDigitFit : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount == 0) return;
            Rect rect = graphic.GetPixelAdjustedRect();
            if (rect.width <= 0f) return;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            var vertex = default(UIVertex);
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                min = Mathf.Min(min, vertex.position.x);
                max = Mathf.Max(max, vertex.position.x);
            }
            float width = max - min;
            if (width <= 0f || min >= rect.xMin && max <= rect.xMax) return;
            float center = (min + max) * .5f;
            float scale = Mathf.Min(1f, rect.width / width);
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                vertex.position.x = rect.center.x + (vertex.position.x - center) * scale;
                mesh.SetUIVertex(vertex, i);
            }
        }
    }

    internal static class DuelStatSymbols
    {
        private static Sprite _blade;
        private static Sprite _shield;

        internal static Sprite Get(bool defense)
        {
            if (defense) return _shield != null ? _shield : (_shield = Build(defense: true));
            return _blade != null ? _blade : (_blade = Build(defense: false));
        }

        private static Sprite Build(bool defense)
        {
            const int size = 24, samples = 3;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int covered = 0;
                for (int row = 0; row < samples; row++)
                for (int col = 0; col < samples; col++)
                {
                    float u = (x + (col + .5f) / samples) / size - .5f;
                    float v = (y + (row + .5f) / samples) / size - .5f;
                    if (defense ? InsideShield(u, v) : InsideBlade(u, v)) covered++;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * covered / (samples * samples)));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = defense ? "Duel Defense Symbol" : "Duel Attack Symbol",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f);
        }

        private static bool InsideShield(float x, float y) =>
            y >= -.40f && y <= .37f && Mathf.Abs(x) < .34f * Mathf.InverseLerp(-.40f, -.14f, y);

        private static bool InsideBlade(float x, float y)
        {
            float along = (x + y) * .7071f;
            float across = Mathf.Abs((y - x) * .7071f);
            return along >= -.51f && along <= -.19f && across < .066f ||
                along >= -.24f && along <= -.12f && across < .24f ||
                along >= -.13f && along < .52f && across < .127f * Mathf.Clamp01((.52f - along) / .20f);
        }
    }

    internal sealed class DuelLinkCompass
    {
        internal static readonly (uint mask, string name, Vector2Int direction)[] Marks =
        {
            (0x040, "TopLeft", new Vector2Int(-1, 1)), (0x080, "Top", Vector2Int.up),
            (0x100, "TopRight", new Vector2Int(1, 1)), (0x008, "Left", Vector2Int.left),
            (0x020, "Right", Vector2Int.right), (0x001, "BottomLeft", new Vector2Int(-1, -1)),
            (0x002, "Bottom", Vector2Int.down), (0x004, "BottomRight", new Vector2Int(1, -1)),
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
                var at = Vector2.one * .5f + (Vector2)item.direction * .328f;
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

    internal sealed class DuelCounterBadge
    {
        private static readonly Color Ink = new Color(.91f, .82f, 1f);
        private static readonly Color Backing = new Color(.155f, .105f, .22f, .97f);
        private readonly Text _value;
        private readonly bool _compact;
        internal RectTransform Root { get; }

        internal DuelCounterBadge(RectTransform root, bool compact)
        {
            Root = root;
            _compact = compact;
            DuelReadoutPlate.Add(root, Backing);
            var digits = LotdUiKit.CreateRect("Number", root, new Vector2(.06f, 0f), new Vector2(.94f, 1f),
                Vector2.zero, Vector2.zero);
            _value = LotdUiKit.AddFittedText(digits, "Value", "", compact ? 14 : 16, 8,
                TextAnchor.MiddleCenter, Ink, FontStyle.Bold);
            _value.horizontalOverflow = HorizontalWrapMode.Overflow;
            _value.gameObject.AddComponent<DuelStatDigitFit>();
            Bind(0);
        }

        internal void Bind(uint count, bool showChanges = false)
        {
            bool visible = count != 0;
            string next = !visible ? "" : _compact ? $"◆ {count}" : $"카운터 {count}개";
            Root.gameObject.SetActive(visible);
            if (!showChanges || !visible) DuelReadoutPulse.Cancel(Root);
            else if (_value.text != next) DuelReadoutPulse.Play(Root, Ink);
            _value.text = next;
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
        private DuelCounterBadge _counters;
        private (uint Code, bool Opponent) _lastCard;

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

        internal void Bind(DuelCardReadout card, bool opponent, uint? scale, uint? position = null)
        {
            var visibleCard = (Code: card.Live?.code ?? 0u, Opponent: opponent);
            bool showChanges = !_inPreview && _root.gameObject.activeSelf && visibleCard.Code != 0 && visibleCard == _lastCard;
            _lastCard = visibleCard;
            _root.gameObject.SetActive(true);
            if (!_inPreview)
            {
                _combat.anchorMin = new Vector2(.03f, opponent ? .715f : .015f);
                _combat.anchorMax = new Vector2(.97f, opponent ? .985f : .285f);
                _labels.anchorMin = new Vector2(0f, opponent ? .025f : .765f);
                _labels.anchorMax = new Vector2(1f, opponent ? .235f : .975f);
            }
            _stats.Bind(card, opponent, showChanges, position);
            SetLabel(_rating, scale.HasValue ? $"스케일 {scale.Value}" : card.Rating,
                card.IsXyz ? RankColor : card.IsLink || scale.HasValue ? DuelStyle.Player : DuelStyle.Gold, showChanges);
            SetLabel(_status, card.Status,
                card.Disabled ? new Color(1f, .65f, .58f) : DuelStyle.Muted, showChanges);
            BindCounters(card.CounterCount, opponent, showChanges);
        }

        private void BindCounters(uint count, bool opponent, bool showChanges)
        {
            if (_counters == null)
            {
                if (count == 0) return;
                var chip = LotdUiKit.CreateRect("Counters", _root,
                    new Vector2(_inPreview ? .38f : .50f, 1f), new Vector2(_inPreview ? .79f : .965f, 1f),
                    new Vector2(0f, _inPreview ? -44f : 0f), new Vector2(0f, _inPreview ? -25f : 0f));
                _counters = new DuelCounterBadge(chip, compact: true);
            }
            if (!_inPreview)
            {
                _counters.Root.anchorMin = new Vector2(.50f, opponent ? .245f : .545f);
                _counters.Root.anchorMax = new Vector2(.965f, opponent ? .455f : .755f);
            }
            _counters.Bind(count, showChanges);
        }

        private static void SetLabel(Text label, string content, Color color, bool showChanges)
        {
            var chip = (RectTransform)label.transform.parent;
            bool visible = content.Length != 0;
            chip.gameObject.SetActive(visible);
            if (!showChanges || !visible) DuelReadoutPulse.Cancel(chip);
            else if (label.text != content) DuelReadoutPulse.Play(chip, color);
            label.text = content;
            label.color = color;
        }
    }

    internal sealed class DuelMovingReadout
    {
        internal RectTransform Root { get; }
        private readonly DuelFieldReadout _readout;
        private readonly DuelCardReadout _card;
        private readonly (Vector2 StartSize, Vector2 EndSize, uint StartPosition, bool StartOpponent, bool EndOpponent) _movement;
        private readonly CanvasGroup _opacity;
        private bool _atDestination;

        internal DuelMovingReadout(RectTransform root, CardMoveReadoutSnapshot snapshot, DuelCardReadout card)
        {
            Root = root;
            _movement = snapshot.Movement.Value;
            _card = card;
            _readout = new DuelFieldReadout(root);
            _readout.Bind(card, _movement.StartOpponent, null, _movement.StartPosition);
            if (_movement.StartOpponent != _movement.EndOpponent)
            {
                _opacity = root.gameObject.AddComponent<CanvasGroup>();
                _opacity.blocksRaycasts = _opacity.interactable = false;
            }
        }

        internal void Follow(RectTransform card, float progress)
        {
            Root.anchoredPosition = card.anchoredPosition;
            Root.sizeDelta = Vector2.Lerp(_movement.StartSize, _movement.EndSize, Mathf.SmoothStep(0f, 1f, progress));
            bool destination = progress >= .5f;
            if (destination != _atDestination)
            {
                _atDestination = destination;
                _readout.Bind(_card, destination ? _movement.EndOpponent : _movement.StartOpponent,
                    null, destination ? null : _movement.StartPosition);
            }
            if (_opacity != null)
                _opacity.alpha = Mathf.Lerp(.60f, 1f, Mathf.Abs(progress - .5f) * 5f);
        }
    }

    internal sealed class DuelReadoutPulse : MonoBehaviour
    {
        private const float Duration = .44f;
        private Image _wash;
        private Image _sweep;
        private Color _color;
        private bool _reverse;
        private float _elapsed;

        internal static void Play(RectTransform surface, Color color, bool reverse = false)
        {
            if (!surface.TryGetComponent(out DuelReadoutPulse pulse))
            {
                pulse = surface.gameObject.AddComponent<DuelReadoutPulse>();
                var wash = LotdUiKit.FillRect("ChangePulse", surface);
                wash.SetAsFirstSibling();
                pulse._wash = DuelReadoutPlate.Add(wash, Color.clear);
                var sweep = LotdUiKit.CreateRect("Sweep", wash, Vector2.up, Vector2.one,
                    new Vector2(0f, -4f), new Vector2(0f, -1f));
                pulse._sweep = LotdUiKit.AddImage(sweep.gameObject, LotdUiKit.WhiteSprite, Color.clear, false, false);
            }

            pulse._elapsed = 0f;
            pulse._color = color;
            pulse._reverse = reverse;
            pulse.enabled = true;
            pulse._wash.gameObject.SetActive(true);
            pulse.Paint(0f);
        }

        internal static void Cancel(Transform surface)
        {
            if (surface.TryGetComponent(out DuelReadoutPulse pulse)) pulse.Stop();
        }

        private void Update()
        {
            if (CardAnimationManager.Instance?.Paused != true)
                Advance(Time.unscaledDeltaTime * DuelFieldUI.PlaybackRate);
        }

        internal void Advance(float deltaTime)
        {
            if (!enabled) return;
            _elapsed += deltaTime;
            if (_elapsed >= Duration) Stop();
            else Paint(_elapsed / Duration);
        }

        private void Paint(float progress)
        {
            float strength = 1f - Mathf.SmoothStep(0f, 1f, progress);
            _wash.color = new Color(_color.r, _color.g, _color.b, .26f * strength);
            _sweep.color = new Color(_color.r, _color.g, _color.b, strength);
            float head = Mathf.Lerp(.14f, .95f, progress);
            float tail = Mathf.Max(.05f, head - .46f);
            _sweep.rectTransform.anchorMin = new Vector2(_reverse ? 1f - head : tail, 1f);
            _sweep.rectTransform.anchorMax = new Vector2(_reverse ? 1f - tail : head, 1f);
        }

        private void Stop()
        {
            _wash.gameObject.SetActive(false);
            enabled = false;
        }

        private void OnDisable()
        {
            if (_wash != null) _wash.gameObject.SetActive(false);
            _elapsed = Duration;
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
