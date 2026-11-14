using System;
using System.Collections.Generic;
using System.IO;
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
            int cleanLv = Mathf.Clamp((int)level, 0, 13);
            string stars = (!IsLink && cleanLv > 0) ? new string('★', cleanLv) + " " : "";
            string lvStr = IsLink ? $"링크 {level}" : IsXyz ? $"{stars}(랭크 {level})" : $"{stars}(레벨 {level})";
            string tag = YgoConstants.GetTypeTagKR(type);
            string atkDef = IsLink ? $"공격력 {AtkString} / LINK-{level}" : $"공격력 {AtkString} / 수비력 {DefString}";
            string pend = IsPendulum ? $" / 펜듈럼 스케일 {lscale}" : "";
            return $"[{raceName} / {attrName} / {lvStr}{pend} / {tag}]\n{atkDef}";
        }
    }

    [Serializable]
    internal class CardKrWrapper
    {
        public CardInfo[] cards;
    }

    public static class CardDatabase
    {
        private static Dictionary<uint, CardInfo> _byCode;
        private static Dictionary<int, CardInfo> _byLotdId;
        private static List<CardInfo> _allCards;

        public static bool IsLoaded => _allCards != null;
        public static IReadOnlyList<CardInfo> AllCards => _allCards ?? (IReadOnlyList<CardInfo>)Array.Empty<CardInfo>();

        private static readonly Dictionary<uint, string> SystemHintsKR = new Dictionary<uint, string>
        {
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
            { 575, "무효화할 카드를 선택하십시오." },
        };

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

            _relatedCache.Clear();
            Debug.Log($"[CardDatabase] Loaded {_allCards.Count} KR cards.");
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
            return (c != null && !string.IsNullOrEmpty(c.name)) ? c.name : $"카드 #{code}";
        }

        public static string ResolveDescription(ulong desc, uint cardCode = 0)
        {
            if (desc == 0) return "효과 발동";
            if (desc <= 10000)
            {
                if (SystemHintsKR.TryGetValue((uint)desc, out var sh)) return sh;
                if (desc == 1) return "발동 / 선택";
                return $"효과 / 안내 (#{desc})";
            }
            uint code = (uint)(desc >> 4);
            int idx = (int)(desc & 0xF);
            var c = Get(code);
            if (c != null && c.strs != null && idx < c.strs.Length && !string.IsNullOrEmpty(c.strs[idx]))
            {
                return c.strs[idx];
            }
            if (cardCode != 0 && cardCode != code)
            {
                c = Get(cardCode);
                if (c != null && c.strs != null && idx < c.strs.Length && !string.IsNullOrEmpty(c.strs[idx]))
                    return c.strs[idx];
            }
            return c != null ? $"{c.name} 효과 {idx + 1}" : $"효과 ({code}:{idx})";
        }
    }
}
