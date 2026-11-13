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
            string lvStr = IsLink ? $"링크 {level}" : IsXyz ? $"랭크 {level}" : $"레벨 {level}";
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
            { 500, "제외할 카드를 선택하십시오." },
            { 501, "묘지로 보낼 카드를 선택하십시오." },
            { 502, "파괴할 카드를 선택하십시오." },
            { 503, "패에서 묘지로 버릴 카드를 선택하십시오." },
            { 504, "패로 되돌릴 카드를 선택하십시오." },
            { 505, "패에 넣을 카드를 선택하십시오." },
            { 506, "효과를 발동할 카드를 선택하십시오." },
            { 507, "릴리스할 몬스터를 선택하십시오." },
            { 508, "소환할 몬스터를 선택하십시오." },
            { 509, "특수 소환할 몬스터를 선택하십시오." },
            { 510, "덱 맨 위로 되돌릴 카드를 선택하십시오." },
            { 511, "엑스트라 덱으로 되돌릴 카드를 선택하십시오." },
            { 512, "싱크로 소환할 몬스터를 선택하십시오." },
            { 513, "엑시즈 소환할 몬스터를 선택하십시오." },
            { 514, "융합 소환할 몬스터를 선택하십시오." },
            { 515, "의식 소환할 몬스터를 선택하십시오." },
            { 516, "펜듈럼 소환할 몬스터를 선택하십시오." },
            { 530, "표시 형식을 변경할 몬스터를 선택하십시오." },
            { 532, "세트할 카드를 선택하십시오." },
            { 533, "링크 소환할 몬스터를 선택하십시오." },
            { 549, "공격 대상으로 삼을 몬스터를 선택하십시오." },
            { 550, "대상을 선택하십시오." },
            { 551, "장착 대상으로 할 몬스터를 선택하십시오." },
            { 559, "카드를 선택하십시오." },
            { 560, "존을 선택하십시오." },
            { 562, "종족을 선언하십시오." },
            { 563, "속성을 선언하십시오." },
            { 564, "카드명을 선언하십시오." },
            { 565, "숫자를 선언하십시오." },
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
            Debug.Log($"[CardDatabase] Loaded {_allCards.Count} KR cards.");
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
