using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal enum DuelRatingKind { None, Level, Rank, Link, Scale }

    internal readonly struct DuelCardReadout
    {
        internal readonly CardInfo Printed;
        internal readonly LiveCardState Live;
        private readonly BattleCardStats? _battleStats;
        private readonly (int Count, int Attack, int Defense)? _materialReadout;
        internal readonly bool ShowsSummonPreview;

        internal DuelCardReadout(CardInfo printed, LiveCardState live = null, BattleCardStats? battleStats = null,
            (int Count, int Attack, int Defense)? materialReadout = null, bool showsSummonPreview = false)
        {
            Printed = printed;
            Live = live;
            _battleStats = battleStats;
            _materialReadout = materialReadout;
            ShowsSummonPreview = showsSummonPreview;
        }

        internal static DuelCardReadout ForSummonPosition(CardInfo printed, LiveCardState source)
        {
            var registered = new DuelCardReadout(printed, source);
            // The position prompt arrives while a Trap Monster is still in its Spell & Trap Zone.
            if (printed != null && (printed.type & YgoConstants.TYPE_TRAP) != 0 &&
                (source?.level ?? printed.level) > 0 && registered.Race != 0 && registered.Attribute != 0)
                return new DuelCardReadout(printed, source, showsSummonPreview: true);
            return new DuelCardReadout(printed);
        }

        internal uint Type => (Live?.type > 0 ? Live.type : Printed?.type ?? 0) |
            (ShowsSummonPreview ? YgoConstants.TYPE_MONSTER : 0u);
        internal bool IsMonster => (Type & YgoConstants.TYPE_MONSTER) != 0;
        internal bool IsLink => (Type & YgoConstants.TYPE_LINK) != 0;
        internal bool IsXyz => (Type & YgoConstants.TYPE_XYZ) != 0;
        internal bool IsPendulum => (Type & YgoConstants.TYPE_PENDULUM) != 0;
        internal bool IsScale => Type == (YgoConstants.TYPE_PENDULUM | YgoConstants.TYPE_SPELL);
        internal ulong Race => Live?.race ?? Printed?.race ?? 0;
        internal uint Attribute => Live?.attribute ?? Printed?.attr ?? 0;
        internal int Attack => _battleStats?.Attack ?? _materialReadout?.Attack ?? Live?.attack ?? Printed?.atk ?? -1;
        internal int Defense => _battleStats?.Defense ?? _materialReadout?.Defense ?? Live?.defense ?? Printed?.def ?? -1;
        internal uint Position => ShowsSummonPreview ? 0u : _battleStats?.Position ?? Live?.position ?? 0;
        internal BattleComparisonValue? ComparedValue => _battleStats?.ComparedValue;
        internal uint LeftScale => Live?.lscale ?? Printed?.lscale ?? 0;
        internal uint RightScale => Live?.rscale ?? Printed?.rscale ?? 0;
        internal uint LinkMarkers => IsLink ? Live?.linkMarker ?? Printed?.link ?? 0 : 0;
        internal int Materials => _materialReadout?.Count ?? Live?.overlayCodes.Count ?? 0;
        internal (ushort Type, ushort Count)[] Counters => Live?.Counters ?? Array.Empty<(ushort, ushort)>();
        internal uint CounterCount => Live?.counterCount ?? 0;
        internal bool Disabled => Live?.effectsDisabled == true;
        internal bool HasMaterials => Live != null && (IsXyz || Materials > 0);
        internal bool IsToken => (Type & YgoConstants.TYPE_TOKEN) != 0;

        internal string RulesName
        {
            get
            {
                if (Live?.IsFaceUp != true || Live.CurrentNameCode == Live.code) return null;
                // Art variants can share a rules code with their original printing.
                if (CardDatabase.GetDeckPrintingKey(Live.CurrentNameCode) == CardDatabase.GetDeckPrintingKey(Live.code))
                    return null;
                string name = CardDatabase.Get(Live.CurrentNameCode)?.name;
                return string.Equals(name, Printed?.name, StringComparison.Ordinal) ? null : name;
            }
        }

        internal uint? ScaleIn(uint location, uint sequence) =>
            location == YgoConstants.LOCATION_SZONE && IsScale && sequence is 0 or 4
                ? sequence == 0 ? LeftScale : RightScale : null;

        internal (DuelRatingKind Kind, uint Value) RatingNumber
        {
            get
            {
                if (!IsMonster) return default;
                uint printedRating = Printed?.level ?? 0;
                if (IsLink) return (DuelRatingKind.Link, Live?.link ?? printedRating);
                if (IsXyz) return (DuelRatingKind.Rank, Live?.rank ?? printedRating);
                uint level = Live?.level ?? printedRating;
                return level > 0 ? (DuelRatingKind.Level, level) : default;
            }
        }

        internal string Rating
        {
            get
            {
                var (kind, value) = RatingNumber;
                return kind switch
                {
                    DuelRatingKind.Level => $"레벨 {value}",
                    DuelRatingKind.Rank => $"랭크 {value}",
                    DuelRatingKind.Link => $"링크 {value}",
                    _ => "",
                };
            }
        }

        internal string Status
        {
            get
            {
                string badge = HasMaterials ? $"소재 {Materials}" : IsToken ? "토큰" : "";
                if (!Disabled) return badge;
                return badge.Length == 0 ? "효과 무효" : $"{badge} · 무효";
            }
        }
    }

    internal sealed class DuelStatStrip
    {
        private static readonly Color AttackColor = new Color(1f, .76f, .5f);
        private static readonly Color DefenseColor = new Color(.61f, .87f, .98f);
        private static readonly Color EffectColor = new Color(.87f, .74f, 1f);
        private static readonly Color Increased = new Color(.58f, 1f, .82f);
        private static readonly Color Decreased = new Color(1f, .59f, .56f);
        private static readonly Color Backing = new Color(.045f, .068f, .085f, .98f);
        private readonly RectTransform _root;
        private readonly StatCell _attack;
        private readonly StatCell _defense;
        private readonly bool _compact;
        private StatCell _effect;
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
            uint shownPosition = position ?? card.Position;
            var source = card.ComparedValue?.Source;
            bool activeAttack = source.HasValue ? source == BattleStatSource.Attack : (shownPosition & YgoConstants.POS_ATTACK) != 0;
            bool activeDefense = source.HasValue ? source == BattleStatSource.Defense : (shownPosition & YgoConstants.POS_DEFENSE) != 0;
            _attack.Bind(card.Attack, card.Live?.baseAttack ?? card.Attack, activeAttack, showChanges);
            if (!card.IsLink)
                _defense.Bind(card.Defense, card.Live?.baseDefense ?? card.Defense, activeDefense, showChanges);
        }

        internal void BindBattle(BattleCardStats card, bool link, bool attacking)
        {
            _root.gameObject.SetActive(card.IsFaceUp);
            if (!card.IsFaceUp) return;

            _compass?.Hide();
            var source = card.ComparedValue?.Source;
            long? value = card.ComparedValue?.Value;
            // Older builds emit both current stats without the source used for damage.
            bool showDefense = source.HasValue ? source == BattleStatSource.Defense : !link && card.IsDefense;
            bool showAttack = source.HasValue ? source == BattleStatSource.Attack : attacking || !showDefense;
            bool split = showAttack && showDefense;
            Place(_attack, showAttack, value ?? card.Attack, 0f, split ? .49f : 1f);
            Place(_defense, showDefense, value ?? card.Defense, split ? .51f : 0f, 1f);
            if (source == BattleStatSource.Effect)
            {
                _effect ??= new StatCell(_root, "Effect", "전투 수치", EffectColor, _compact, DuelStatSymbols.BattleEffect);
                Place(_effect, true, value.Value, 0f, 1f);
            }
            else _effect?.Rect.gameObject.SetActive(false);

            void Place(StatCell cell, bool visible, long number, float from, float to)
            {
                cell.Rect.gameObject.SetActive(visible);
                if (!visible) return;
                cell.Rect.anchorMin = new Vector2(from, 0f);
                cell.Rect.anchorMax = new Vector2(to, 1f);
                cell.BindComparison(number, split);
            }
        }

        private void Layout(bool link, uint directions, bool opponent)
        {
            bool hasDirections = directions != 0;
            if (hasDirections) (_compass ??= new DuelLinkCompass(_root)).Bind(directions, opponent);
            else _compass?.Hide();
            _effect?.Rect.gameObject.SetActive(false);
            _attack.Rect.gameObject.SetActive(true);
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
            private readonly RectTransform _digits;
            private readonly RectTransform _symbol;
            private readonly Color _accent;
            private long _lastValue = -1;

            internal StatCell(Transform parent, string key, string label, Color accent, bool compact, Sprite icon = null)
            {
                _accent = accent;
                Rect = LotdUiKit.FillRect(key, parent);
                _plate = DuelReadoutPlate.Add(Rect, Backing);
                var rail = LotdUiKit.CreateRect("Rail", Rect, new Vector2(.07f, 0f), new Vector2(.93f, 0f),
                    Vector2.zero, new Vector2(0f, 1.8f));
                _rail = LotdUiKit.AddImage(rail.gameObject, LotdUiKit.WhiteSprite, accent, false, false);
                if (compact)
                {
                    _symbol = LotdUiKit.CreateRect("Symbol", Rect, new Vector2(.045f, .22f), new Vector2(.217f, .83f),
                        Vector2.zero, Vector2.zero);
                    LotdUiKit.AddImage(_symbol.gameObject, icon ?? DuelStatSymbols.Get(key == "Defense"), accent, true, false);
                }
                else
                {
                    var heading = LotdUiKit.CreateRect("Caption", Rect, new Vector2(.09f, .60f), new Vector2(.76f, .99f),
                        Vector2.zero, Vector2.zero);
                    var title = LotdUiKit.AddFittedText(heading, "Label", label, 14, 8, TextAnchor.MiddleLeft, accent);
                    title.verticalOverflow = VerticalWrapMode.Overflow;
                }
                var badge = LotdUiKit.CreateRect("Change", Rect, compact ? new Vector2(.045f, .04f) : new Vector2(.76f, .60f),
                    compact ? new Vector2(.217f, .40f) : new Vector2(.94f, .99f), Vector2.zero, Vector2.zero);
                _change = LotdUiKit.AddFittedText(badge, "Arrow", "", compact ? 13 : 14, 7,
                    TextAnchor.MiddleCenter, accent);
                DuelStatDigitFit.Attach(_change, useGlyphBounds: true);
                _digits = LotdUiKit.CreateRect("Digits", Rect, compact ? new Vector2(.225f, .055f) : new Vector2(.055f, .035f),
                    compact ? new Vector2(.975f, .985f) : new Vector2(.945f, .59f), Vector2.zero, Vector2.zero);
                _value = DuelTypography.AddNumber(_digits, "Value", "", compact ? 23 : 31,
                    TextAnchor.MiddleCenter, Color.white);
            }

            internal void BindComparison(long value, bool split)
            {
                _digits.anchorMin = new Vector2(.225f, .035f);
                _digits.anchorMax = new Vector2(.97f, .99f);
                _value.fontSize = split ? 23 : 28;
                Bind(value, value, active: true);
            }

            internal void Bind(long value, long original, bool active, bool showChanges = false)
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
                if (_symbol != null) _symbol.anchorMin = new Vector2(.045f, change == 0 ? .22f : .43f);
                _plate.color = active ? Color.Lerp(Backing, _accent, .15f) : Backing;
                _rail.color = new Color(_accent.r, _accent.g, _accent.b, active ? .72f : .22f);
            }
        }
    }

    internal static class DuelTypography
    {
        private static Font _numberFont;
        internal static Font NumberFont => _numberFont != null ? _numberFont : _numberFont =
            Resources.Load<Font>("Fonts/BarlowSemiCondensedTabular-SemiBold") ?? LotdUiKit.FontKR;

        internal static Text AddNumber(Transform parent, string name, string content, int size, TextAnchor align, Color color)
        {
            var label = LotdUiKit.AddText(parent, name, content, size, align, color);
            label.font = NumberFont;
            DuelStatDigitFit.Attach(label, useGlyphBounds: true);
            return label;
        }
    }

    internal sealed class DuelStatDigitFit : BaseMeshEffect
    {
        internal static void Attach(Text label, bool useGlyphBounds)
        {
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (useGlyphBounds)
            {
                // Size the painted characters, excluding the font's line spacing.
                label.resizeTextForBestFit = false;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
            label.gameObject.AddComponent<DuelStatDigitFit>();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount == 0) return;
            Rect rect = graphic.GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;
            var min = Vector2.positiveInfinity;
            var max = Vector2.negativeInfinity;
            var vertex = default(UIVertex);
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                min = Vector2.Min(min, vertex.position);
                max = Vector2.Max(max, vertex.position);
            }
            Vector2 size = max - min;
            if (size.x <= 0f || size.y <= 0f) return;

            // Fit the entire run at one scale so small readouts keep the typeface's proportions.
            float scale = Mathf.Min(1f, Mathf.Min(rect.width / size.x, rect.height / size.y));
            float alignment = ((Text)graphic).alignment switch
            {
                TextAnchor.UpperLeft or TextAnchor.MiddleLeft or TextAnchor.LowerLeft => rect.xMin,
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => rect.xMax,
                _ => rect.center.x,
            };
            float x = alignment * (1f - scale);
            x += Mathf.Clamp(0f, rect.xMin - (min.x * scale + x), rect.xMax - (max.x * scale + x));
            float y = rect.center.y - (min.y + max.y) * .5f * scale;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                vertex.position.x = vertex.position.x * scale + x;
                vertex.position.y = vertex.position.y * scale + y;
                mesh.SetUIVertex(vertex, i);
            }
        }
    }

    internal static class DuelStatSymbols
    {
        private static Sprite _blade;
        private static Sprite _shield;
        private static Sprite _counter;
        private static Sprite _battleEffect;
        private static readonly Sprite[] RatingSprites = new Sprite[(int)DuelRatingKind.Scale + 1];

        internal static Sprite Counter => _counter != null ? _counter : (_counter = Build("Duel Counter Symbol", InsideCounter));
        internal static Sprite BattleEffect => _battleEffect != null ? _battleEffect :
            (_battleEffect = Build("Duel Battle Effect Symbol", InsideBattleEffect));

        internal static Sprite Get(bool defense)
        {
            if (defense) return _shield != null ? _shield : (_shield = Build("Duel Defense Symbol", InsideShield));
            return _blade != null ? _blade : (_blade = Build("Duel Attack Symbol", InsideBlade));
        }

        internal static Sprite Rating(DuelRatingKind kind)
        {
            int index = (int)kind;
            if (kind == DuelRatingKind.None) return null;
            if (RatingSprites[index] != null) return RatingSprites[index];
            return RatingSprites[index] = kind switch
            {
                DuelRatingKind.Level => Build("Duel Level Symbol", InsideStar),
                DuelRatingKind.Rank => Build("Duel Rank Symbol", InsideDiamond),
                DuelRatingKind.Link => Build("Duel Link Symbol", InsideChain),
                DuelRatingKind.Scale => Build("Duel Scale Symbol", InsideHourglass),
                _ => null,
            };
        }

        private static Sprite Build(string name, Func<float, float, bool> inside)
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
                    if (inside(u, v)) covered++;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * covered / (samples * samples)));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
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

        private static bool InsideStar(float x, float y)
        {
            const float halfPoint = Mathf.PI / 5f, outer = .47f, inner = .235f;
            float angle = Mathf.Abs(Mathf.Repeat(Mathf.Atan2(x, y) + halfPoint, 2f * halfPoint) - halfPoint);
            float distance = Mathf.Sqrt(x * x + y * y);
            float edge = inner * Mathf.Sin(halfPoint - angle) + outer * Mathf.Sin(angle);
            return distance * edge <= outer * inner * Mathf.Sin(halfPoint);
        }

        private static bool InsideDiamond(float x, float y)
        {
            float distance = Mathf.Abs(x) + Mathf.Abs(y);
            return distance <= .48f && (distance >= .27f || distance <= .13f);
        }

        private static bool InsideChain(float x, float y)
        {
            float along = (x + y) * .7071f;
            float across = (y - x) * .7071f;
            return InsideChainLink(along - .16f, across) || InsideChainLink(along + .16f, across);
        }

        private static bool InsideChainLink(float along, float across)
        {
            float distance = along * along / (.26f * .26f) + across * across / (.165f * .165f);
            return distance <= 1f && distance >= .34f;
        }

        private static bool InsideHourglass(float x, float y)
        {
            float height = Mathf.Abs(y);
            return height <= .41f && Mathf.Abs(x) <= .065f + height * .83f;
        }

        private static bool InsideCounter(float x, float y) =>
            x * x + (y - .21f) * (y - .21f) <= .027f ||
            (Mathf.Abs(x) - .20f) * (Mathf.Abs(x) - .20f) + (y + .17f) * (y + .17f) <= .027f;

        private static bool InsideBattleEffect(float x, float y) => y >= 0f
            ? y <= .43f && x >= -.30f + y * .55f && x <= .14f + y * .45f
            : y >= -.43f && x >= -.10f + y * .34f && x <= .34f + y * 1.34f;
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
        internal static readonly Color Ink = new Color(.91f, .82f, 1f);
        private static readonly Color Backing = new Color(.155f, .105f, .22f, .97f);
        private readonly Text _value;
        private readonly bool _compact;
        private (ushort Type, ushort Count)[] _previousCounters;
        internal RectTransform Root { get; }

        internal DuelCounterBadge(RectTransform root, bool compact)
        {
            Root = root;
            _compact = compact;
            DuelReadoutPlate.Add(root, Backing);
            if (compact)
            {
                var icon = LotdUiKit.CreateRect("Symbol", root, new Vector2(.04f, .08f), new Vector2(.41f, .92f),
                    Vector2.zero, Vector2.zero);
                LotdUiKit.AddImage(icon.gameObject, DuelStatSymbols.Counter, Ink, true, false);
            }
            var digits = LotdUiKit.CreateRect("Number", root, new Vector2(compact ? .42f : .06f, 0f), new Vector2(.95f, 1f),
                Vector2.zero, Vector2.zero);
            _value = compact
                ? DuelTypography.AddNumber(digits, "Value", "", 21, TextAnchor.MiddleCenter, Ink)
                : LotdUiKit.AddFittedText(digits, "Value", "", 16, 8, TextAnchor.MiddleCenter, Ink);
            Bind(default);
        }

        internal static string TypeName(ushort type)
        {
            // Counter IDs from the duel core's card scripts, named as in the Korean card text.
            string name = type switch
            {
                0x0001 => "마력",
                0x0003 => "무사도",
                0x0004 => "사이코",
                0x0005 => "샤인",
                0x0006 => "젬",
                0x0008 => "디포머",
                0x000A => "제넥스",
                0x000C => "번개",
                0x000D => "욕망",
                0x000F => "웜",
                0x0010 => "검은 날개",
                0x0011 => "하이퍼 베놈",
                0x0012 => "자동기계",
                0x0013 => "카오스",
                0x0016 => "마석",
                0x0017 => "도토리",
                0x0018 => "플라워",
                0x001A => "더블",
                0x001B => "시계",
                0x001C => "D",
                0x001D => "정크",
                0x001E => "게이트",
                0x0020 => "플랜트",
                0x0023 => "오션",
                0x0025 => "크로니클",
                0x002B => "데스티니",
                0x002E => "샤크",
                0x002F => "펌프킨",
                0x0031 => "호프검",
                0x0032 => "벌룬",
                0x0033 => "요선",
                0x0035 => "음향",
                0x0036 => "Em(엔터메이지)",
                0x0037 => "파괴수",
                0x0147 => "바렐",
                0x0148 => "서몬",
                0x0201 => "염성",
                0x0207 => "황의 열쇠",
                0x020A => "피스",
                0x020C => "GG(G골렘)",
                0x0211 => "향명",
                0x0217 => "규죄",
                0x04A0 => "애슬리트",
                0x1002 => "쐐기",
                0x1009 => "베놈",
                0x100E => "A",
                0x1015 => "아이스",
                0x1019 => "포그",
                0x1021 => "가드",
                0x1022 => "드래고닉",
                0x1024 => "스트링",
                0x1038 => "방계",
                0x1039 => "즈신",
                0x1041 => "포식",
                0x1045 => "인분",
                0x1049 => "경라",
                0x1101 => "환각",
                0x1148 => "시그널",
                0x1149 => "베너미",
                0x1207 => "연소",
                0x1208 => "우사미미",
                _ => null,
            };
            return name == null ? $"카운터 #{type:X4}" : $"{name} 카운터";
        }

        internal void Bind(DuelCardReadout card, bool showChanges = false)
        {
            var counters = card.Counters;
            bool visible = counters.Length != 0;
            string next = !visible ? "" : _compact ? card.CounterCount.ToString() :
                counters.Length == 1 ? $"{TypeName(counters[0].Type)} {counters[0].Count}개" : $"카운터 {counters.Length}종";
            Root.gameObject.SetActive(visible);
            if (!showChanges || !visible) DuelReadoutPulse.Cancel(Root);
            else if (!_previousCounters.AsSpan().SequenceEqual(counters)) DuelReadoutPulse.Play(Root, Ink);
            if (_value.text != next) _value.text = next;
            _previousCounters = counters;
        }
    }

    internal sealed class DuelFieldReadout
    {
        private static readonly Color Backing = new Color(.045f, .068f, .085f, .96f);
        private static readonly Color RankColor = new Color(.91f, .75f, 1f);
        private static readonly Color ScaleColor = new Color(.60f, 1f, .83f);
        private static readonly Color MaterialColor = new Color(.80f, .89f, 1f);
        private static readonly Color DisabledColor = new Color(1f, .65f, .58f);
        private readonly RectTransform _root;
        private readonly RectTransform _combat;
        private readonly RectTransform _labels;
        private readonly DuelStatStrip _stats;
        private readonly Text _rating;
        private readonly Image _ratingIcon;
        private readonly Text _status;
        private readonly bool _inPreview;
        private readonly List<(RectTransform Rect, float Width)> _fieldLabels;
        private Text _materials;
        private DuelCounterBadge _counters;
        private (uint Code, bool Opponent) _lastCard;

        internal RectTransform Rect => _root;

        internal DuelFieldReadout(Transform slot, bool inPreview = false)
        {
            _inPreview = inPreview;
            _fieldLabels = inPreview ? null : new(4);
            _root = LotdUiKit.FillRect("CardReadout", slot);
            _combat = LotdUiKit.FillRect("Combat", _root);
            _stats = new DuelStatStrip(_combat, compact: true);
            _labels = LotdUiKit.FillRect("Labels", _root);
            // Leave room for the selection check on choice cards.
            _rating = CreateLabel("Rating", .035f, .355f, 21, numeric: true);
            var icon = CreateLabelSymbol(_rating);
            _ratingIcon = LotdUiKit.AddImage(icon.gameObject, null, Color.white, true, false);
            _status = CreateLabel("Status", .372f, inPreview ? .79f : .965f, inPreview ? 13 : 16, numeric: false);
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

        private Text CreateLabel(string name, float left, float right, int size, bool numeric)
        {
            var chip = LotdUiKit.CreateRect(name, _labels, new Vector2(left, 0f), new Vector2(right, 1f),
                Vector2.zero, Vector2.zero);
            DuelReadoutPlate.Add(chip, Backing);
            if (numeric) return DuelTypography.AddNumber(chip, "Value", "", size, TextAnchor.MiddleCenter, Color.white);
            var status = LotdUiKit.AddFittedText(chip, "Value", "", size, 8, TextAnchor.MiddleCenter, Color.white);
            DuelStatDigitFit.Attach(status, useGlyphBounds: false);
            return status;
        }

        private static RectTransform CreateLabelSymbol(Text label)
        {
            label.rectTransform.anchorMin = new Vector2(.435f, .04f);
            label.rectTransform.anchorMax = new Vector2(.96f, .96f);
            return LotdUiKit.CreateRect("Symbol", label.transform.parent, new Vector2(.055f, .10f),
                new Vector2(.435f, .90f), Vector2.zero, Vector2.zero);
        }

        internal void Hide() => _root.gameObject.SetActive(false);

        internal void FollowFocus(Vector2 lift, float scale)
        {
            if (_root.anchoredPosition != lift) _root.anchoredPosition = lift;
            var size = Vector3.one * scale;
            if (_root.localScale != size) _root.localScale = size;

            if (_inPreview) return;
            KeepLaneInSlot(_combat);
            KeepLaneInSlot(_labels);

            void KeepLaneInSlot(RectTransform lane)
            {
                // Follow the raised card up to the slot edge so an adjacent card's badges stay visible.
                float height = _root.rect.height;
                float edge = height * .492f;
                float bottom = (lane.anchorMin.y - .5f) * height * scale + lift.y;
                float top = (lane.anchorMax.y - .5f) * height * scale + lift.y;
                var offset = Vector2.up * (Mathf.Clamp(0f, -edge - bottom, edge - top) / scale);
                if (lane.offsetMin != offset) lane.offsetMin = offset;
                if (lane.offsetMax != offset) lane.offsetMax = offset;
            }
        }

        internal void Bind(DuelCardReadout card, bool opponent, uint? scale, uint? position = null, bool showCounters = true)
        {
            var visibleCard = (Code: card.Live?.code ?? 0u, Opponent: opponent);
            bool showChanges = !_inPreview && _root.gameObject.activeSelf && visibleCard.Code != 0 && visibleCard == _lastCard;
            _lastCard = visibleCard;
            _root.gameObject.SetActive(true);
            if (!_inPreview)
            {
                _combat.anchorMin = new Vector2(.03f, opponent ? .765f : .015f);
                _combat.anchorMax = new Vector2(.97f, opponent ? .985f : .235f);
            }
            _stats.Bind(card, opponent, showChanges, position);
            var rating = scale.HasValue ? (DuelRatingKind.Scale, scale.Value) : card.RatingNumber;
            BindRating(rating, showChanges);
            var counterReadout = showCounters ? card : default;
            BindCounters(counterReadout, showChanges);
            if (_inPreview)
            {
                SetLabel(_status, card.Status, card.Disabled ? DisabledColor : DuelStyle.Muted, showChanges);
                return;
            }

            if (card.HasMaterials && _materials == null)
            {
                _materials = CreateLabel("Materials", 0f, 1f, 21, numeric: true);
                var symbol = CreateLabelSymbol(_materials);
                DuelPromptLocationIcon.Add(symbol, YgoConstants.LOCATION_OVERLAY, false, glyphOnly: true).color = MaterialColor;
            }
            if (_materials != null)
                SetLabel(_materials, card.HasMaterials ? card.Materials.ToString() : "", MaterialColor, showChanges);
            string status = card.IsToken ? "토큰" : "";
            if (card.Disabled) status = card.IsToken ? "토큰 · 무효" : "무효";
            SetLabel(_status, status, card.Disabled ? DisabledColor : DuelStyle.Muted, showChanges);
            LayoutFieldLabels(rating.Value, card.Materials, counterReadout.CounterCount, status.Length > 2, opponent);
        }

        private void BindRating((DuelRatingKind Kind, uint Value) rating, bool showChanges)
        {
            bool visible = rating.Kind != DuelRatingKind.None;
            var color = rating.Kind switch
            {
                DuelRatingKind.Rank => RankColor,
                DuelRatingKind.Link => DuelStyle.Player,
                DuelRatingKind.Scale => ScaleColor,
                _ => DuelStyle.Gold,
            };
            if (_inPreview)
            {
                float right = .315f + (rating.Value >= 10 ? .065f : 0f);
                ((RectTransform)_rating.transform.parent).anchorMax = new Vector2(right, 1f);
                ((RectTransform)_status.transform.parent).anchorMin = new Vector2(visible ? right + .017f : .035f, 0f);
            }
            _ratingIcon.sprite = DuelStatSymbols.Rating(rating.Kind);
            _ratingIcon.color = color;
            SetLabel(_rating, visible ? rating.Value.ToString() : "", color, showChanges);
        }

        private void BindCounters(DuelCardReadout card, bool showChanges)
        {
            if (_counters == null && card.Counters.Length != 0)
            {
                var chip = _inPreview
                    ? LotdUiKit.CreateRect("Counters", _root, new Vector2(.38f, 1f), new Vector2(.79f, 1f),
                        new Vector2(0f, -44f), new Vector2(0f, -25f))
                    : LotdUiKit.FillRect("Counters", _labels);
                _counters = new DuelCounterBadge(chip, compact: true);
            }
            _counters?.Bind(card, showChanges);
        }

        private void LayoutFieldLabels(uint rating, int materials, uint counters, bool longStatus, bool opponent)
        {
            _fieldLabels.Clear();
            Add((RectTransform)_rating.transform.parent, rating >= 10 ? .38f : .295f);
            Add(_materials?.transform.parent as RectTransform, materials >= 10 ? .37f : .28f);
            Add(_counters?.Root, counters >= 10 ? .37f : .28f);
            Add((RectTransform)_status.transform.parent, longStatus ? .50f : .31f);

            bool twoRows = _fieldLabels.Count == 4;
            _labels.anchorMin = new Vector2(0f, opponent ? .025f : twoRows ? .548f : .765f);
            _labels.anchorMax = new Vector2(1f, opponent ? twoRows ? .452f : .235f : .975f);
            if (twoRows)
            {
                PlaceRow(0, 2, opponent ? 0f : .515f, opponent ? .485f : 1f);
                PlaceRow(2, 2, opponent ? .515f : 0f, opponent ? 1f : .485f);
            }
            else PlaceRow(0, _fieldLabels.Count, 0f, 1f);

            void Add(RectTransform chip, float width)
            {
                if (chip != null && chip.gameObject.activeSelf) _fieldLabels.Add((chip, width));
            }

            void PlaceRow(int first, int count, float bottom, float top)
            {
                const float edge = .035f, gap = .017f;
                if (count == 0) return;
                float width = 0f;
                for (int i = first; i < first + count; i++) width += _fieldLabels[i].Width;
                float fit = Mathf.Min(1f, (1f - 2f * edge - gap * (count - 1)) / width);
                float x = edge;
                for (int i = first; i < first + count; i++)
                {
                    var badge = _fieldLabels[i];
                    badge.Rect.anchorMin = new Vector2(x, bottom);
                    x += badge.Width * fit;
                    badge.Rect.anchorMax = new Vector2(x, top);
                    x += gap;
                }
            }
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
        private readonly CardMoveReadoutSnapshot _snapshot;
        private readonly CanvasGroup _opacity;
        private readonly float _handoff;
        private bool? _atDestination;

        internal DuelMovingReadout(RectTransform root, CardMoveReadoutSnapshot snapshot, float handoff)
        {
            Root = root;
            _snapshot = snapshot;
            _handoff = handoff;
            _readout = new DuelFieldReadout(root);
            _readout.Hide();
            _opacity = root.gameObject.AddComponent<CanvasGroup>();
            _opacity.alpha = 0f;
            _opacity.blocksRaycasts = _opacity.interactable = false;
        }

        internal void Follow(CompositeCardView view, float progress)
        {
            var movement = _snapshot.Movement;
            Root.anchoredPosition = ((RectTransform)view.transform).anchoredPosition;
            Root.sizeDelta = Vector2.Lerp(movement.StartSize, movement.EndSize, Mathf.SmoothStep(0f, 1f, progress));
            bool destination = progress >= _handoff;
            if (destination != _atDestination)
            {
                _atDestination = destination;
                var live = destination ? _snapshot.DestinationCard : _snapshot.SourceCard;
                if (live?.IsFaceUp == true && live.code != 0 && view.CurrentCode == live.code &&
                    (live.type & YgoConstants.TYPE_MONSTER) != 0)
                {
                    view.SetCard(live.code, live: live);
                    _readout.Bind(new DuelCardReadout(CardDatabase.Get(live.code), live),
                        destination ? movement.EndOpponent : movement.StartOpponent, null);
                }
                else _readout.Hide();
            }
            _opacity.alpha = _snapshot.SourceCard == null ? Mathf.SmoothStep(0f, 1f, (progress - _handoff) / .24f)
                : _snapshot.DestinationCard == null ? Mathf.SmoothStep(0f, 1f, (_handoff - progress) / .24f)
                : movement.StartOpponent != movement.EndOpponent ? Mathf.Lerp(.60f, 1f, Mathf.Abs(progress - _handoff) * 5f)
                : 1f;
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
