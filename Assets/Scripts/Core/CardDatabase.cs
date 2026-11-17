using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    [Serializable]
    public class CardInfo
    {
        public uint code;
        public uint alias;
        public int lotdId;
        public int konamiId;
        public string name;
        public string desc;
        public uint type;
        public int atk;
        public int def;
        public uint level;
        public ulong race;
        public uint attr;
        public uint lscale;
        public uint rscale;
        public uint link;
        public ulong setcode;
        public string[] strs;
        public uint DeckEffectCategories { get; internal set; }

        public bool IsMonster => (type & YgoConstants.TYPE_MONSTER) != 0;
        public bool IsSpell => (type & YgoConstants.TYPE_SPELL) != 0;
        public bool IsTrap => (type & YgoConstants.TYPE_TRAP) != 0;
        public bool IsExtra => (type & YgoConstants.TYPE_EXTRA) != 0;
        public bool IsLink => (type & YgoConstants.TYPE_LINK) != 0;
        public bool IsXyz => (type & YgoConstants.TYPE_XYZ) != 0;
        public bool IsPendulum => (type & YgoConstants.TYPE_PENDULUM) != 0;

        public string FrameSpriteName => YgoConstants.GetCardFrameName(type);

        public string AtkString => IsMonster ? (atk < 0 ? "?" : atk.ToString()) : "";
        public string DefString
        {
            get
            {
                if (!IsMonster) return "";
                if (IsLink) return $"LINK-{level}";
                return def < 0 ? "?" : def.ToString();
            }
        }

        public string FormatStatsLineKR()
        {
            if (!IsMonster)
                return YgoConstants.GetTypeTagKR(type);

            string attrName = YgoConstants.AttributeNamesKR.TryGetValue(attr, out var an) ? an : "";
            string raceName = YgoConstants.RaceNamesKR.TryGetValue(race, out var rn) ? rn : "";
            string tag = YgoConstants.GetTypeTagKR(type);
            string pend = IsPendulum ? $" / 펜듈럼 스케일 {lscale}" : "";
            if (IsLink)
                return $"[{raceName} / {attrName}{pend} / {tag}]\n공격력 {AtkString} / LINK-{level}";

            int cleanLv = Mathf.Clamp((int)level, 0, 13);
            string stars = cleanLv > 0 ? new string('★', cleanLv) + " " : "";
            string lvStr = IsXyz ? $"{stars}(랭크 {level})" : $"{stars}(레벨 {level})";
            return $"[{raceName} / {attrName} / {lvStr}{pend} / {tag}]\n공격력 {AtkString} / 수비력 {DefString}";
        }
    }

    [Serializable]
    internal class CardKrWrapper
    {
        public CardInfo[] cards;
    }

    public class ArchetypeInfo
    {
        public string name;
        public string baseName;
        public uint representativeCode;
        public readonly HashSet<ushort> setcodes = new HashSet<ushort>();
        public readonly List<CardInfo> coreCards = new List<CardInfo>();
        public readonly List<CardInfo> supportCards = new List<CardInfo>();
        public int MainPoolCount;
        public int ExtraPoolCount;
        public int TotalUniqueCount => coreCards.Count + supportCards.Count;
    }

    public static class CardDatabase
    {
        private const int DescriptionIndexBits = 20;
        private const ulong DescriptionIndexMask = (1UL << DescriptionIndexBits) - 1;
        private static Dictionary<uint, CardInfo> _byCode;
        private static Dictionary<int, CardInfo> _byLotdId;
        private static List<CardInfo> _allCards;

        public static bool IsLoaded => _allCards != null;
        public static IReadOnlyList<CardInfo> AllCards => _allCards ?? (IReadOnlyList<CardInfo>)Array.Empty<CardInfo>();

        private static readonly Dictionary<uint, string> SystemHintsKR = new Dictionary<uint, string>
        {
            { 70, "몬스터 카드" },
            { 71, "마법 카드" },
            { 72, "함정 카드" },
            { 500, "릴리스할 카드를 선택하십시오." },
            { 501, "버릴 카드를 선택하십시오." },
            { 502, "파괴할 카드를 선택하십시오." },
            { 503, "제외할 카드를 선택하십시오." },
            { 504, "묘지로 보낼 카드를 선택하십시오." },
            { 505, "패로 되돌릴 카드를 선택하십시오." },
            { 506, "패에 넣을 카드를 선택하십시오." },
            { 507, "덱으로 되돌릴 카드를 선택하십시오." },
            { 508, "일반 소환할 몬스터를 선택하십시오." },
            { 509, "특수 소환할 몬스터를 선택하십시오." },
            { 510, "세트할 카드를 선택하십시오." },
            { 511, "융합 소재로 할 몬스터를 선택하십시오." },
            { 512, "싱크로 소재로 할 몬스터를 선택하십시오." },
            { 513, "엑시즈 소재로 할 몬스터를 선택하십시오." },
            { 514, "앞면 표시로 할 카드를 선택하십시오." },
            { 515, "뒷면 표시로 할 카드를 선택하십시오." },
            { 516, "공격 표시로 할 몬스터를 선택하십시오." },
            { 517, "수비 표시로 할 몬스터를 선택하십시오." },
            { 518, "장착할 카드를 선택하십시오." },
            { 519, "엑시즈 소재를 제거할 카드를 선택하십시오." },
            { 520, "컨트롤을 얻을 몬스터를 선택하십시오." },
            { 521, "대신 파괴할 카드를 선택하십시오." },
            { 526, "확인할 카드를 선택하십시오." },
            { 527, "필드에 놓을 카드를 선택하십시오." },
            { 528, "표시 형식을 변경할 카드를 선택하십시오." },
            { 531, "릴리스할 몬스터를 선택하십시오." },
            { 532, "엑시즈 소재를 떼어낼 카드를 선택하십시오." },
            { 533, "링크 소재로 할 몬스터를 선택하십시오." },
            { 549, "공격 대상으로 삼을 몬스터를 선택하십시오." },
            { 550, "효과를 발동할 카드를 선택하십시오." },
            { 551, "대상을 선택하십시오." },
            { 554, "카드의 종류를 선언하십시오." },
            { 555, "효과를 선택하십시오." },
            { 556, "적용할 효과를 선택하십시오." },
            { 560, "카드를 선택하십시오." },
            { 561, "존/표시 형식을 선택하십시오." },
            { 562, "속성을 선언하십시오." },
            { 563, "종족을 선언하십시오." },
            { 564, "카드명을 선언하십시오." },
            { 565, "숫자를 선언하십시오." },
            { 568, "처리할 카드를 선택하십시오." },
            { 569, "존을 선택하십시오." },
            { 572, "카운터를 선택하십시오." },
            { 573, "패에 넣는다" },
            { 575, "무효화할 카드를 선택하십시오." },
            { 1056, "융합" },
            { 1057, "의식" },
            { 1063, "싱크로" },
            { 1073, "엑시즈" },
            { 1074, "펜듈럼" },
            { 1152, "특수 소환" },
            { 1153, "세트" },
            { 1160, "펜듈럼 마법 카드로 발동" },
            { 1213, "예" },
            { 1214, "아니오" },
        };

        private static readonly Dictionary<string, string> EffectTextKR = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Special Summon", "특수 소환한다" },
            { "Activate 1 of these effects", "발동한 뒤 적용할 효과를 선택한다" },
            { "Special Summon this card", "이 카드를 특수 소환한다" },
            { "Special Summon this card from your hand", "패에서 이 카드를 특수 소환한다" },
            { "Special Summon this card from your hand in Defense Position", "패에서 이 카드를 수비 표시로 특수 소환한다" },
            { "Special Summon 1 Fiend monster from your hand", "패에서 악마족 몬스터 1장을 특수 소환한다" },
            { "Special Summon 1 Fiend monster, or Set 1 Spell/Trap, from your hand?",
                "패에서 악마족 몬스터 1장을 특수 소환하거나 마법·함정 카드 1장을 세트하시겠습니까?" },
            { "Set 1 Spell/Trap from your hand", "패에서 마법·함정 카드 1장을 세트한다" },
            { "Set 1 Normal Trap with a different name from your Deck", "덱에서 이름이 다른 일반 함정 카드 1장을 세트한다" },
            { "Special Summon 1 \"D/D\" from Extra Deck or GY in Defense Position", "엑스트라 덱이나 묘지에서 DD 몬스터 1장을 수비 표시로 특수 소환한다" },
            { "Change the Pendulum Scale of up to 2 cards to 0", "펜듈럼 존의 카드 최대 2장의 스케일을 0으로 한다" },
            { "Make this card's Level become 4", "이 카드의 레벨을 4로 한다" },
            { "Return 1 \"D/D\" Pendulum Monster Card you control to the hand", "자신 필드의 DD 펜듈럼 몬스터 카드 1장을 패로 되돌린다" },
            { "Return 1 other \"D/D\" card you control to the hand", "자신 필드의 다른 DD 카드 1장을 패로 되돌린다" },
            { "Search 1 \"Dark Contract\" card", "덱에서 계약서 카드 1장을 패에 넣는다" },
            { "Special Summon then return to the hand", "특수 소환한 후 패로 되돌린다" },
            { "Destroy", "파괴한다" },
            { "Destroy 1 card your opponent controls", "상대 필드의 카드 1장을 파괴한다" },
            { "Destroy 1 random card in your opponent's hand", "상대의 패를 무작위로 1장 파괴한다" },
            { "Draw", "드로우한다" },
            { "Draw 1 card", "카드를 1장 드로우한다" },
            { "Draw 2 cards", "카드를 2장 드로우한다" },
            { "Take control of 1 monster your opponent controls", "상대 필드의 몬스터 1장의 컨트롤을 얻는다" },
            { "Look at your opponent's hand", "상대의 패를 확인한다" },
            { "Draw 1 card for eatch Special Summoned card on opponent's Field", "상대가 몬스터를 특수 소환할 때마다 1장 드로우한다" },
            { "Add to hand", "패에 넣는다" },
            { "Return to hand", "패로 되돌린다" },
            { "Return to the hand", "패로 되돌린다" },
            { "Return to deck", "덱으로 되돌린다" },
            { "Banish", "제외한다" },
            { "Set", "세트한다" },
            { "Equip", "장착한다" },
            { "Send to Graveyard", "묘지로 보낸다" },
            { "Send to GY", "묘지로 보낸다" },
            { "Search or send to GY", "덱에서 카드를 패에 넣거나 묘지로 보낸다" },
            { "Mill", "덱 위에서 카드를 묘지로 보낸다" },
            { "Negate that effect", "그 효과를 무효로 한다" },
            { "Negate the activation of that effect", "그 효과의 발동을 무효로 한다" },
            { "Fusion Summon", "융합 소환한다" },
            { "Spell Card", "마법 카드" },
            { "Trap Card", "함정 카드" },
        };

        private static readonly Dictionary<ulong, string> CardEffectTextKR = new Dictionary<ulong, string>
        {
            { 9024198UL << DescriptionIndexBits, "덱에서 이름이 다른 DD 펜듈럼 몬스터 2장을 펜듈럼 존에 놓고 1000 데미지를 받는다. 턴 종료까지 DD 몬스터만 특수 소환할 수 있다." },
            { 11609969UL << DescriptionIndexBits, "이 카드의 펜듈럼 스케일을 2 내린다(최소 1). 새 스케일 이상의 레벨을 가진 자신의 DD 이외 몬스터를 파괴한다." },
            { (11609969UL << DescriptionIndexBits) | 1, "자신 필드의 다른 DD 카드 1장을 패로 되돌리거나, 덱에서 계약서 카드 1장을 패에 넣는다." },
        };

        private static readonly Dictionary<string, string> TokenNameMapKR = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Swordsoul Token", "상검 토큰" },
            { "Primal Being Token", "원시생명체 토큰" },
            { "Adventurer Token", "용사 토큰" },
            { "Sheep Token", "양 토큰" },
            { "Hippo Token", "하마 토큰" },
            { "Mecha Phantom Beast Token", "환상수기 토큰" },
            { "Photon Token", "포톤 토큰" },
            { "Gadget Token", "가젯 토큰" },
            { "Gadget Box Token", "가젯 박스 토큰" },
            { "Phantom Token", "팬텀 토큰" },
            { "Steam Token", "스팀 토큰" },
            { "Koa'ki Meiru Token", "코아키메일 토큰" },
            { "Phantasm Spiral Token", "환황룡 토큰" },
            { "Familiar of the Evil Eye Token", "주안의 권속 토큰" },
            { "Rose Token", "로즈 토큰" },
            { "Rose Tendril Token", "로즈 텐드릴 토큰" },
            { "Cyberse Token", "사이버스 토큰" },
            { "Cyberse Gadget Token", "사이버스 가젯 토큰" },
            { "Trickstar Token", "트릭스터 토큰" },
            { "Sunvine Token", "생아발론 토큰" },
            { "G-Golem Token", "G골렘 토큰" },
            { "Kuriboh Token", "크리보 토큰" },
            { "Dragonic Token", "드래고닉 토큰" },
            { "Crystal Beast Token", "보옥수 토큰" },
            { "Sin Token", "Sin 토큰" },
            { "Doppel Token", "도플 토큰" },
            { "Lamia Token", "라미아 토큰" },
            { "Slime Token", "슬라임 토큰" },
            { "Grinder Token", "토치 토큰" },
            { "Ojama Token", "방해꾼 토큰" },
            { "Emissary of Darkness Token", "명왕의 사자 토큰" },
            { "Dark Sanctuary Token", "다크 생츄어리 토큰" },
            { "Vague Shadow Token", "어스름의 그림자 토큰" },
            { "Blackwing - Black Crest Token", "BF－흑염 토큰" },
            { "Icebarrie Token", "빙결계 토큰" },
            { "Generaider Token", "제너레이드 토큰" },
            { "Plunder Patroll Token", "플런드롤 토큰" },
            { "Tindangle Token", "틴당글 토큰" },
            { "Gouki Token", "강귀 토큰" },
            { "World Chalice Token", "성잔 토큰" },
            { "Vylon Token", "바이론 토큰" },
            { "Raidraptor - Eagle Token", "RR－이글 토큰" },
            { "Jurrac Token", "쥬락 토큰" },
            { "Naturia Token", "나츄르 토큰" },
            { "Worm Token", "웜 토큰" },
            { "Megalith Token", "메가리스 토큰" },
            { "Alien Token", "에이리언 토큰" },
            { "Fluffal Token", "퍼니멀 토큰" },
            { "Kahyoki Token", "화빙천화 토큰" },
            { "Double Dude Token", "더블 가이 토큰" },
            { "Dark Soul Token", "다크 소울 토큰" },
            { "Battle Eagle Token", "배틀 이글 토큰" },
            { "Chewbone Jr. Token", "본체 Jr. 토큰" },
            { "Spool Token", "스풀 토큰" },
            { "Delta Token", "델타 토큰" },
            { "Moult Token", "탈피 토큰" },
            { "Fluff Token", "플러프 토큰" }
        };

        public static string LocalizeTokenName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (TokenNameMapKR.TryGetValue(name, out var kr)) return kr;
            if (name.EndsWith(" Token", StringComparison.OrdinalIgnoreCase))
            {
                string prefix = name.Substring(0, name.Length - 6);
                return prefix + " 토큰";
            }
            return name;
        }

        public static void Load(string streamingAssetsPath)
        {
            if (_allCards != null) return;
            string path = Path.Combine(streamingAssetsPath, "cards_kr.json");
            if (!File.Exists(path))
            {
                Debug.LogError($"[CardDatabase] Missing {path}");
                _allCards = new List<CardInfo>();
                _byCode = new Dictionary<uint, CardInfo>();
                _byLotdId = new Dictionary<int, CardInfo>();
                return;
            }
            string json = File.ReadAllText(path);
            var wrapper = JsonUtility.FromJson<CardKrWrapper>(json);
            _allCards = new List<CardInfo>(wrapper?.cards ?? Array.Empty<CardInfo>());
            _byCode = new Dictionary<uint, CardInfo>(_allCards.Count);
            _byLotdId = new Dictionary<int, CardInfo>(_allCards.Count);
            foreach (var c in _allCards)
            {
                if ((c.type & YgoConstants.TYPE_TOKEN) != 0 && !string.IsNullOrEmpty(c.name))
                {
                    c.name = LocalizeTokenName(c.name);
                }
                if (c.code != 0) _byCode[c.code] = c;
                if (c.lotdId > 0 && !_byLotdId.ContainsKey(c.lotdId)) _byLotdId[c.lotdId] = c;
            }

            string propPath = Path.Combine(streamingAssetsPath, "cards_prop.bin");
            if (File.Exists(propPath))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(propPath);
                    if (bytes.Length >= 12)
                    {
                        uint count = BitConverter.ToUInt32(bytes, 8);
                        const int sz = 64;
                        for (int i = 0; i < count && (12 + (i + 1) * sz) <= bytes.Length; i++)
                        {
                            int offset = 12 + i * sz;
                            uint code = BitConverter.ToUInt32(bytes, offset);
                            ulong sc = BitConverter.ToUInt64(bytes, offset + 48);
                            if (code != 0 && _byCode.TryGetValue(code, out var ci))
                            {
                                ci.setcode = sc;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CardDatabase] Failed loading setcodes from prop: {ex.Message}");
                }
            }

            LoadDeckEffectCategories(streamingAssetsPath);
            _relatedCache.Clear();
            _archetypes = null;
            Debug.Log($"[CardDatabase] Loaded {_allCards.Count} KR cards.");
        }

        private static void LoadDeckEffectCategories(string streamingAssetsPath)
        {
            string path = Path.Combine(streamingAssetsPath, "deck_effect_categories.bin");
            if (!File.Exists(path)) return;

            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt32() != 0x43454759u || reader.ReadUInt32() != 1)
                    throw new InvalidDataException("Unrecognized deck effect index format.");
                uint count = reader.ReadUInt32();
                if (stream.Length != 12L + count * 8L)
                    throw new InvalidDataException("Unexpected deck effect index size.");

                for (uint i = 0; i < count; i++)
                {
                    uint code = reader.ReadUInt32();
                    uint flags = reader.ReadUInt32();
                    if (_byCode.TryGetValue(code, out var card)) card.DeckEffectCategories = flags;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CardDatabase] Failed loading deck effect categories: {error.Message}");
            }
        }

        private static readonly Dictionary<uint, List<CardInfo>> _relatedCache = new Dictionary<uint, List<CardInfo>>();

        public static HashSet<ushort> ExtractSetcodes(ulong setcode)
        {
            var res = new HashSet<ushort>();
            if (setcode == 0) return res;
            for (int shift = 0; shift < 64; shift += 16)
            {
                ushort v = (ushort)((setcode >> shift) & 0xFFFF);
                if (v != 0)
                {
                    res.Add(v);
                    ushort baseSc = (ushort)(v & 0x0FFF);
                    if (baseSc != 0) res.Add(baseSc);
                }
            }
            return res;
        }

        public static List<CardInfo> GetRelatedCards(uint code)
        {
            if (code == 0) return new List<CardInfo>();
            if (_relatedCache.TryGetValue(code, out var cached)) return cached;

            var results = new List<CardInfo>();
            var target = Get(code);
            if (target == null || _allCards == null) return results;

            string tname = (target.name ?? "").Trim();
            string tdesc = target.desc ?? "";
            var targetSetcodes = ExtractSetcodes(target.setcode);

            // Extract quoted archetype keywords: "..." or 「...」
            var descKeywords = new List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(tdesc, "[\"「]([^\"」]+)[\"」]");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Groups.Count > 1)
                {
                    string term = m.Groups[1].Value.Trim();
                    if (term.Length >= 2 && !term.EndsWith("효과") && !term.EndsWith("턴") && !term.EndsWith("페이즈"))
                    {
                        descKeywords.Add(term);
                    }
                }
            }

            foreach (var c in _allCards)
            {
                if (c.code == code || (c.type & YgoConstants.TYPE_TOKEN) != 0) continue;

                // 1. Same archetype / setcode match
                if (targetSetcodes.Count > 0 && c.setcode != 0)
                {
                    var otherSc = ExtractSetcodes(c.setcode);
                    if (targetSetcodes.Overlaps(otherSc))
                    {
                        results.Add(c);
                        continue;
                    }
                }

                string otherDesc = c.desc ?? "";
                string otherName = c.name ?? "";

                // 2. Other card mentions target card's exact name
                if (tname.Length >= 2 && otherDesc.Contains(tname))
                {
                    results.Add(c);
                    continue;
                }

                // 3. Target card mentions other card's exact name (if meaningful length)
                if (otherName.Length >= 3 && tdesc.Contains(otherName))
                {
                    results.Add(c);
                    continue;
                }

                // 4. Target's quoted archetype keyword appears in other card's name
                bool kwMatch = false;
                for (int k = 0; k < descKeywords.Count; k++)
                {
                    if (otherName.Contains(descKeywords[k]))
                    {
                        kwMatch = true;
                        break;
                    }
                }
                if (kwMatch)
                {
                    results.Add(c);
                    continue;
                }
            }

            _relatedCache[code] = results;
            return results;
        }

        private static List<ArchetypeInfo> _archetypes;
        private static readonly System.Text.RegularExpressions.Regex _archQuoteRegex =
            new System.Text.RegularExpressions.Regex("\"([^\"]{1,24})\"\\s*(?:몬스터|카드|마법|함정|튜너|일반|효과|융합|싱크로|엑시즈|링크|의식|펜듈럼)",
                System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _anyQuoteRegex =
            new System.Text.RegularExpressions.Regex("\"([^\"]{2,18})\"",
                System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _rubyStripRegex =
            new System.Text.RegularExpressions.Regex("\\([^\\)]+\\)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string StripRuby(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int idx = s.IndexOf('(');
            if (idx < 0) return s.Trim();
            return _rubyStripRegex.Replace(s, "").Trim();
        }

        private static bool IsPureAltArtDuplicate(CardInfo c)
        {
            if (c == null || c.alias == 0 || _byCode == null) return false;
            if (_byCode.TryGetValue(c.alias, out var b) && b != null)
            {
                if (c.type == b.type && c.atk == b.atk && c.def == b.def && c.level == b.level)
                    return true;
            }
            return false;
        }

        public static IReadOnlyList<ArchetypeInfo> GetAllArchetypes()
        {
            if (_archetypes != null) return _archetypes;
            if (_allCards == null) return Array.Empty<ArchetypeInfo>();

            var scMap = new Dictionary<ushort, List<CardInfo>>();
            var quoteIndex = new Dictionary<string, List<CardInfo>>(StringComparer.Ordinal);

            foreach (var c in _allCards)
            {
                if ((c.type & YgoConstants.TYPE_TOKEN) != 0 || IsPureAltArtDuplicate(c)) continue;

                if (c.setcode != 0)
                {
                    var scs = ExtractSetcodes(c.setcode);
                    foreach (ushort sc in scs)
                    {
                        if (!scMap.TryGetValue(sc, out var list))
                        {
                            list = new List<CardInfo>();
                            scMap[sc] = list;
                        }
                        list.Add(c);
                    }
                }

                if (!string.IsNullOrEmpty(c.desc) && c.desc.IndexOf('"') >= 0)
                {
                    var seenInCard = new HashSet<string>(StringComparer.Ordinal);
                    foreach (System.Text.RegularExpressions.Match m in _anyQuoteRegex.Matches(c.desc))
                    {
                        string kw = m.Groups[1].Value.Trim();
                        string baseKw = StripRuby(kw);
                        if (baseKw.Length >= 2 && seenInCard.Add(baseKw))
                        {
                            if (!quoteIndex.TryGetValue(baseKw, out var qList))
                            {
                                qList = new List<CardInfo>();
                                quoteIndex[baseKw] = qList;
                            }
                            qList.Add(c);
                        }
                    }
                }
            }

            var merged = new Dictionary<string, ArchetypeInfo>(StringComparer.Ordinal);

            foreach (var kv in scMap)
            {
                ushort sc = kv.Key;
                var cl = kv.Value;
                if (cl.Count < 4) continue;

                var scores = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var c in cl)
                {
                    if (string.IsNullOrEmpty(c.desc) || c.desc.IndexOf('"') < 0) continue;

                    foreach (System.Text.RegularExpressions.Match m in _archQuoteRegex.Matches(c.desc))
                    {
                        string term = m.Groups[1].Value.Trim();
                        if (term.EndsWith("토큰")) continue;
                        string baseTerm = StripRuby(term);
                        int inName = 0;
                        for (int i = 0; i < cl.Count; i++)
                        {
                            string nm = cl[i].name ?? "";
                            if (nm.Contains(term) || (baseTerm.Length >= 2 && nm.Contains(baseTerm))) inName++;
                        }
                        int bonus = term.IndexOf('(') >= 0 ? 10 : 0;
                        scores.TryGetValue(term, out int cur);
                        scores[term] = cur + 3 + inName * 8 + bonus;
                    }

                    foreach (System.Text.RegularExpressions.Match m in _anyQuoteRegex.Matches(c.desc))
                    {
                        string term = m.Groups[1].Value.Trim();
                        if (term.EndsWith("토큰")) continue;
                        string baseTerm = StripRuby(term);
                        int inName = 0;
                        bool exactSingleCard = false;
                        for (int i = 0; i < cl.Count; i++)
                        {
                            string nm = cl[i].name ?? "";
                            if (nm == term) exactSingleCard = true;
                            if (nm.Contains(term) || (baseTerm.Length >= 2 && nm.Contains(baseTerm))) inName++;
                        }
                        if (inName >= 3 && (!exactSingleCard || term == "블랙 매지션" || term == "푸른 눈의 백룡" || term == "붉은 눈의 흑룡" || term == "유벨" || term == "사이버 드래곤"))
                        {
                            scores.TryGetValue(term, out int cur);
                            scores[term] = cur + inName * 5;
                        }
                    }
                }

                if (scores.Count == 0) continue;

                string bestName = null;
                int bestScore = -1;
                foreach (var skv in scores)
                {
                    if (skv.Value > bestScore)
                    {
                        bestScore = skv.Value;
                        bestName = skv.Key;
                    }
                }
                if (string.IsNullOrEmpty(bestName)) continue;

                if (!merged.TryGetValue(bestName, out var info))
                {
                    info = new ArchetypeInfo
                    {
                        name = bestName,
                        baseName = StripRuby(bestName),
                    };
                    merged[bestName] = info;
                }
                info.setcodes.Add(sc);
                foreach (var c in cl)
                {
                    if (!info.coreCards.Contains(c))
                        info.coreCards.Add(c);
                }
            }

            // Enrich each archetype with direct support cards & secondary paired keywords (e.g. 섬도희->섬도, 천배룡->찬환, 황금향->엘드리치)
            var resultList = new List<ArchetypeInfo>(merged.Values.Count);
            foreach (var info in merged.Values)
            {
                var coreCodeSet = new HashSet<uint>();
                foreach (var c in info.coreCards) coreCodeSet.Add(c.code);

                string bName = info.baseName;
                if (bName.Length >= 2)
                {
                    foreach (var c in _allCards)
                    {
                        if ((c.type & YgoConstants.TYPE_TOKEN) != 0 || IsPureAltArtDuplicate(c) || coreCodeSet.Contains(c.code)) continue;
                        if ((c.name ?? "").Contains(bName))
                        {
                            info.coreCards.Add(c);
                            coreCodeSet.Add(c.code);
                        }
                    }
                }

                var suppCodeSet = new HashSet<uint>();
                if (bName.Length >= 2 && quoteIndex.TryGetValue(bName, out var directSupps))
                {
                    foreach (var sc in directSupps)
                    {
                        if (!coreCodeSet.Contains(sc.code) && suppCodeSet.Add(sc.code))
                            info.supportCards.Add(sc);
                    }
                }

                // Secondary archetype keywords mentioned across core + initial support cards
                var secKwCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                void ScanSecKeywords(List<CardInfo> list)
                {
                    foreach (var c in list)
                    {
                        if (string.IsNullOrEmpty(c.desc) || c.desc.IndexOf('"') < 0) continue;
                        foreach (System.Text.RegularExpressions.Match m in _archQuoteRegex.Matches(c.desc))
                        {
                            string bm = StripRuby(m.Groups[1].Value.Trim());
                            if (bm.Length >= 2 && bm != bName && !bm.EndsWith("토큰"))
                            {
                                secKwCounts.TryGetValue(bm, out int cnt);
                                secKwCounts[bm] = cnt + 1;
                            }
                        }
                    }
                }
                ScanSecKeywords(info.coreCards);
                ScanSecKeywords(info.supportCards);

                int secThreshold = info.coreCards.Count <= 6 ? 1 : 2;
                foreach (var skv in secKwCounts)
                {
                    if (skv.Value < secThreshold) continue;
                    string secKw = skv.Key;
                    foreach (var c in _allCards)
                    {
                        if ((c.type & YgoConstants.TYPE_TOKEN) != 0 || IsPureAltArtDuplicate(c)) continue;
                        if (coreCodeSet.Contains(c.code) || suppCodeSet.Contains(c.code)) continue;
                        if ((c.name ?? "").Contains(secKw))
                        {
                            info.supportCards.Add(c);
                            suppCodeSet.Add(c.code);
                        }
                    }
                }

                // Count Main & Extra pool sizes and choose representative Boss/Ace card
                int mainCnt = 0, extraCnt = 0;
                CardInfo bestRep = null;
                int bestRepScore = int.MinValue;
                foreach (var c in info.coreCards)
                {
                    if (c.IsExtra) extraCnt++; else mainCnt++;
                    int repScore = (c.IsMonster ? c.atk : 0) + (c.IsExtra ? 800 : 0) + ((c.name ?? "").Contains(bName) ? 1000 : 0);
                    if (repScore > bestRepScore)
                    {
                        bestRepScore = repScore;
                        bestRep = c;
                    }
                }
                foreach (var c in info.supportCards)
                {
                    if (c.IsExtra) extraCnt++; else mainCnt++;
                }

                info.MainPoolCount = mainCnt;
                info.ExtraPoolCount = extraCnt;
                info.representativeCode = bestRep != null ? bestRep.code : (info.coreCards.Count > 0 ? info.coreCards[0].code : 0);

                if (info.TotalUniqueCount >= 5)
                    resultList.Add(info);
            }

            resultList.Sort((a, b) =>
            {
                int cmp = b.coreCards.Count.CompareTo(a.coreCards.Count);
                if (cmp != 0) return cmp;
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            _archetypes = resultList;
            return _archetypes;
        }

        public static ArchetypeInfo FindArchetypeForCard(uint code)
        {
            var card = Get(code);
            if (card == null) return null;
            var all = GetAllArchetypes();
            var cardScs = ExtractSetcodes(card.setcode);
            foreach (var arch in all)
            {
                if (cardScs.Count > 0 && arch.setcodes.Overlaps(cardScs)) return arch;
                if (arch.coreCards.Any(c => c.code == code)) return arch;
            }
            foreach (var arch in all)
            {
                if (arch.supportCards.Any(c => c.code == code)) return arch;
            }
            // Synthesize ad-hoc archetype from GetRelatedCards if not in named catalog
            var rel = GetRelatedCards(code);
            if (rel.Count > 0)
            {
                var adhoc = new ArchetypeInfo
                {
                    name = card.name,
                    baseName = card.name,
                    representativeCode = code,
                };
                adhoc.coreCards.Add(card);
                foreach (var r in rel)
                {
                    if (!IsPureAltArtDuplicate(r))
                        adhoc.coreCards.Add(r);
                }
                adhoc.MainPoolCount = adhoc.coreCards.Count(c => !c.IsExtra);
                adhoc.ExtraPoolCount = adhoc.coreCards.Count(c => c.IsExtra);
                return adhoc;
            }
            return null;
        }

        public static (List<uint> main, List<uint> extra) BuildPureArchetypeDeck(ArchetypeInfo arch)
        {
            var main = new List<uint>(40);
            var extra = new List<uint>(15);
            if (arch == null) return (main, extra);

            var copyCounts = new Dictionary<uint, int>();
            bool TryAddCard(List<uint> targetList, int maxListSize, CardInfo c, int maxCopies = 3)
            {
                if (c == null || (c.type & YgoConstants.TYPE_TOKEN) != 0 || targetList.Count >= maxListSize) return false;
                uint key = c.alias != 0 ? c.alias : c.code;
                copyCounts.TryGetValue(key, out int cur);
                if (cur >= maxCopies || cur >= 3) return false;
                targetList.Add(c.code);
                copyCounts[key] = cur + 1;
                return true;
            }

            int ScoreCardUtility(CardInfo c, bool isCore)
            {
                int s = isCore ? 1000 : 300;
                string d = c.desc ?? "";
                if (d.Contains("패에 넣") || d.Contains("서치")) s += 350;
                if (d.Contains("특수 소환")) s += 300;
                if (d.Contains("덱에서")) s += 250;
                if (d.Contains("묘지")) s += 150;
                if (d.Contains("무효")) s += 200;
                if (c.IsMonster)
                {
                    if (!c.IsExtra && c.level >= 1 && c.level <= 4) s += 250;
                    s += Mathf.Clamp(c.atk / 10, 0, 300);
                }
                else if (c.IsSpell)
                {
                    s += 220;
                }
                else if (c.IsTrap)
                {
                    s += 120;
                }
                return s;
            }

            // 1. Build Extra Deck (up to 15 cards) from Core + Support Extra Deck monsters
            var extraPool = new List<CardInfo>();
            foreach (var c in arch.coreCards) if (c.IsExtra) extraPool.Add(c);
            foreach (var c in arch.supportCards) if (c.IsExtra && !extraPool.Contains(c)) extraPool.Add(c);
            extraPool.Sort((a, b) => ScoreCardUtility(b, arch.coreCards.Contains(b)).CompareTo(ScoreCardUtility(a, arch.coreCards.Contains(a))));

            if (extraPool.Count > 0)
            {
                int copiesPerExtra = extraPool.Count <= 4 ? 3 : (extraPool.Count <= 7 ? 2 : 1);
                for (int pass = 0; pass < copiesPerExtra && extra.Count < 15; pass++)
                {
                    foreach (var ec in extraPool)
                    {
                        if (extra.Count >= 15) break;
                        TryAddCard(extra, 15, ec, copiesPerExtra);
                    }
                }
                // Top up to 15 if still room and we only added 1 copy
                for (int pass = 0; pass < 2 && extra.Count < 15; pass++)
                {
                    foreach (var ec in extraPool)
                    {
                        if (extra.Count >= 15) break;
                        TryAddCard(extra, 15, ec, 3);
                    }
                }
            }

            // 2. Build Main Deck (40 cards) prioritizing Pure Archetype Core first, then Direct Support
            var coreLowMonsters = arch.coreCards.Where(c => !c.IsExtra && c.IsMonster && c.level <= 4)
                .OrderByDescending(c => ScoreCardUtility(c, true)).ToList();
            var coreSpellsTraps = arch.coreCards.Where(c => !c.IsExtra && (c.IsSpell || c.IsTrap))
                .OrderByDescending(c => ScoreCardUtility(c, true)).ToList();
            var coreHighMonsters = arch.coreCards.Where(c => !c.IsExtra && c.IsMonster && c.level >= 5)
                .OrderByDescending(c => ScoreCardUtility(c, true)).ToList();
            var suppMain = arch.supportCards.Where(c => !c.IsExtra)
                .OrderByDescending(c => ScoreCardUtility(c, false)).ToList();

            // Pass 1: Core Engine (3x Low-level Monsters, 3x Core Spells/Traps, 2x High-level Monsters)
            foreach (var c in coreLowMonsters)
                for (int k = 0; k < 3 && main.Count < 40; k++) TryAddCard(main, 40, c, 3);

            foreach (var c in coreSpellsTraps)
                for (int k = 0; k < 3 && main.Count < 40; k++) TryAddCard(main, 40, c, 3);

            int highCopies = (coreLowMonsters.Count <= 3) ? 3 : 2;
            foreach (var c in coreHighMonsters)
                for (int k = 0; k < highCopies && main.Count < 40; k++) TryAddCard(main, 40, c, highCopies);

            // Pass 2: Remaining copies of Core High Monsters up to 3
            foreach (var c in coreHighMonsters)
                for (int k = 0; k < 3 && main.Count < 40; k++) TryAddCard(main, 40, c, 3);

            // Pass 3: Direct Archetype Support Cards (Spells/Traps/Monsters mentioning the archetype)
            foreach (var c in suppMain)
                for (int k = 0; k < 3 && main.Count < 40; k++) TryAddCard(main, 40, c, 3);

            // Pass 4: If Extra Deck has Fusion Monsters and Main Deck lacks a Fusion effect, include Polymerization (24094653)
            bool hasFusionExtra = extra.Any(code => { var ci = Get(code); return ci != null && (ci.type & YgoConstants.TYPE_FUSION) != 0; });
            bool hasFusionSpell = main.Any(code => { var ci = Get(code); return ci != null && (ci.desc ?? "").Contains("융합 소환"); });
            if (hasFusionExtra && !hasFusionSpell && main.Count < 40)
            {
                var poly = Get(24094653); // 융합
                for (int k = 0; k < 2 && main.Count < 40; k++) TryAddCard(main, 40, poly, 2);
            }

            // Pass 5: If archetype + support pool had < 40 total card copies, fill remaining slots to 40 with staple cards
            uint[] staples =
            {
                14558127, // 하루 우라라
                23434538, // 증식의 G
                10045474, // 무한포영
                24224830, // 무덤의 지명자
                35261759, // 욕망과 탐욕의 항아리
                81439173, // 어리석은 매장
                83764718, // 죽은 자의 소생
                18144506, // 해피의 깃털
                14532163, // 라이트닝 스톰
                25311006, // 삼전의 재
                12580477, // 번개
                14087893, // 달의 서
                8267140,  // 코즈믹 싸이크론
                41420027, // 신의 심판
            };
            foreach (uint stCode in staples)
            {
                if (main.Count >= 40) break;
                var stCard = Get(stCode);
                if (stCard != null)
                {
                    for (int k = 0; k < 3 && main.Count < 40; k++)
                        TryAddCard(main, 40, stCard, 3);
                }
            }

            return (main, extra);
        }

        public static CardInfo Get(uint code)
        {
            if (_byCode != null && _byCode.TryGetValue(code, out var c)) return c;
            return null;
        }

        public static CardInfo GetByLotdId(int lotdId)
        {
            if (_byLotdId != null && _byLotdId.TryGetValue(lotdId, out var c)) return c;
            return null;
        }

        public static string GetName(uint code)
        {
            var c = Get(code);
            if (c != null && !string.IsNullOrEmpty(c.name))
            {
                if ((c.type & YgoConstants.TYPE_TOKEN) != 0) return LocalizeTokenName(c.name);
                return c.name;
            }
            return $"카드 #{code}";
        }

        public static uint GetDescriptionCardCode(ulong desc)
        {
            // aux.Stringid stores the card code above the low 20 bits of the description index.
            ulong code = desc >> DescriptionIndexBits;
            return code <= uint.MaxValue ? (uint)code : 0;
        }

        public static string ResolveDescription(ulong desc)
        {
            if (desc <= DescriptionIndexMask)
                return SystemHintsKR.TryGetValue((uint)desc, out var hint) ? hint : null;
            if (CardEffectTextKR.TryGetValue(desc, out var specificText)) return specificText;

            int index = (int)(desc & DescriptionIndexMask);
            var card = Get(GetDescriptionCardCode(desc));
            string text = ReadDescription(card, index);
            if (text == null && card != null && card.alias != 0)
            {
                var original = Get(card.alias);
                if (original != null && original.name == card.name)
                    text = ReadDescription(original, index);
            }
            return text != null && EffectTextKR.TryGetValue(text, out var localized) ? localized : text;
        }

        public static string ResolveEffectDescription(ulong desc, uint cardCode)
        {
            string text = ResolveDescription(desc);
            if (text != null) return text;
            var source = Get(GetDescriptionCardCode(desc)) ?? Get(cardCode);
            return string.IsNullOrWhiteSpace(source?.desc) ? null : source.desc.Trim();
        }

        private static string ReadDescription(CardInfo card, int index)
        {
            var strings = card?.strs;
            return strings != null && index < strings.Length && !string.IsNullOrWhiteSpace(strings[index])
                ? strings[index].Trim() : null;
        }
    }
}
