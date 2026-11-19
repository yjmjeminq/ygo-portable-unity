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
        private static readonly Dictionary<uint, CardInfo[]> DeckPrintingsByCode = new();
        private static Dictionary<int, CardInfo> _byLotdId;
        private static List<CardInfo> _allCards;

        public static bool IsLoaded => _allCards != null;
        public static IReadOnlyList<CardInfo> AllCards => _allCards ?? (IReadOnlyList<CardInfo>)Array.Empty<CardInfo>();

        private static readonly Dictionary<uint, string> SystemHintsKR = new Dictionary<uint, string>
        {
            { 1, "기본 소환·세트 방법을 사용한다" },
            { 10, "기본 방법으로 카운터를 제거한다" },
            { 11, "기본 방법으로 LP를 지불한다" },
            { 12, "기본 방법으로 엑시즈 소재를 제거한다" },
            { 30, "공격을 계속하시겠습니까?" },
            { 31, "직접 공격을 선택하시겠습니까?" },
            { 32, "배틀 페이즈를 한 번 더 진행하시겠습니까?" },
            { 60, "코인 앞면" },
            { 61, "코인 뒷면" },
            { 62, "코인 앞면 효과" },
            { 63, "코인 뒷면 효과" },
            { 70, "몬스터 카드" },
            { 71, "마법 카드" },
            { 72, "함정 카드" },
            { 80, "배틀 페이즈로 이동한다" },
            { 81, "엔드 페이즈로 이동한다" },
            { 90, "몬스터를 릴리스하시겠습니까?" },
            { 94, "이 카드의 추가 효과를 적용하시겠습니까?" },
            { 95, "이 카드의 선택 효과를 적용하시겠습니까?" },
            { 96, "이 카드의 대체 효과를 적용하시겠습니까?" },
            { 97, "이 카드를 마법·함정 존에 놓으시겠습니까?" },
            { 99, "추가 처리가 없는 방법으로 발동한다" },
            { 210, "카드를 1장 더 선택하시겠습니까?" },
            { 218, "이 카드의 효과로 LP 지불을 대신하시겠습니까?" },
            { 219, "이 카드의 효과로 엑시즈 소재 제거를 대신하시겠습니까?" },
            { 220, "이 카드의 효과로 카운터 제거를 대신하시겠습니까?" },
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
            { 522, "앞면 공격 표시로 할 몬스터를 선택하십시오." },
            { 523, "앞면 수비 표시로 할 몬스터를 선택하십시오." },
            { 524, "뒷면 공격 표시로 할 몬스터를 선택하십시오." },
            { 525, "뒷면 수비 표시로 할 몬스터를 선택하십시오." },
            { 526, "확인할 카드를 선택하십시오." },
            { 527, "필드에 놓을 카드를 선택하십시오." },
            { 528, "표시 형식을 변경할 카드를 선택하십시오." },
            { 529, "자신" },
            { 530, "상대" },
            { 531, "릴리스할 몬스터를 선택하십시오." },
            { 532, "엑시즈 소재를 떼어낼 카드를 선택하십시오." },
            { 533, "링크 소재로 할 몬스터를 선택하십시오." },
            { 549, "공격 대상으로 삼을 몬스터를 선택하십시오." },
            { 550, "효과를 발동할 카드를 선택하십시오." },
            { 551, "대상을 선택하십시오." },
            { 552, "코인의 앞면 또는 뒷면을 선언하십시오." },
            { 553, "주사위의 눈을 선언하십시오." },
            { 554, "카드의 종류를 선언하십시오." },
            { 555, "효과를 선택하십시오." },
            { 556, "적용할 효과를 선택하십시오." },
            { 560, "카드를 선택하십시오." },
            { 561, "존/표시 형식을 선택하십시오." },
            { 562, "속성을 선언하십시오." },
            { 563, "종족을 선언하십시오." },
            { 564, "카드명을 선언하십시오." },
            { 565, "숫자를 선언하십시오." },
            { 566, "발동할 효과를 선택하십시오." },
            { 567, "레벨 또는 랭크를 선택하십시오." },
            { 568, "처리할 카드를 선택하십시오." },
            { 569, "존을 선택하십시오." },
            { 570, "사용할 수 없게 할 존을 선택하십시오." },
            { 571, "이동할 존을 선택하십시오." },
            { 572, "카운터를 선택하십시오." },
            { 573, "패에 넣는다" },
            { 574, "묘지로 보낸다" },
            { 575, "무효화할 카드를 선택하십시오." },
            { 576, "공격력 또는 수비력을 변경할 몬스터를 선택하십시오." },
            { 577, "효과를 적용할 카드를 선택하십시오." },
            { 578, "엑시즈 소재로 할 카드를 선택하십시오." },
            { 579, "묘지로 되돌릴 카드를 선택하십시오." },
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
            { "Add this card from your GY to your hand", "이 카드를 자신의 묘지에서 패에 넣는다" },
            { "Add to your hand 1 Ritual Spell from your Graveyard?", "자신의 묘지에서 의식 마법 카드 1장을 패에 넣으시겠습니까?" },
            { "Attach as Xyz Material", "엑시즈 소재로 한다" },
            { "Attach that destroyed monster to this card as material", "파괴한 몬스터를 이 카드의 엑시즈 소재로 한다" },
            { "ATK becomes 0", "공격력을 0으로 한다" },
            { "ATK to 0", "공격력을 0으로 한다" },
            { "Banish 1 card", "카드 1장을 제외한다" },
            { "Banish 2 monsters", "몬스터 2장을 제외한다" },
            { "Change attack target", "공격 대상을 변경한다" },
            { "Change this card's Level", "이 카드의 레벨을 변경한다" },
            { "Decrease Level", "레벨을 내린다" },
            { "Destroy 1 face-up card on the field", "필드의 앞면 표시 카드 1장을 파괴한다" },
            { "Destroy 1 Spell/Trap", "마법·함정 카드 1장을 파괴한다" },
            { "Destroy and damage", "파괴하고 데미지를 준다" },
            { "Destroy and Set", "파괴하고 세트한다" },
            { "Destroy monster", "몬스터를 파괴한다" },
            { "Destroy monsters your opponent controls", "상대 필드의 몬스터를 파괴한다" },
            { "Draw 2", "카드를 2장 드로우한다" },
            { "End the Battle Phase", "배틀 페이즈를 종료한다" },
            { "Equip Synchro monster", "싱크로 몬스터를 장착한다" },
            { "Fusion Summon 1 Fusion Monster", "융합 몬스터 1장을 융합 소환한다" },
            { "Halve ATK and DEF", "공격력과 수비력을 절반으로 한다" },
            { "Increase this card's ATK", "이 카드의 공격력을 올린다" },
            { "Increase this card's Level by 1", "이 카드의 레벨을 1 올린다" },
            { "Loses ATK and DEF", "공격력과 수비력을 내린다" },
            { "Make this card gain ATK", "이 카드의 공격력을 올린다" },
            { "Move", "이동한다" },
            { "Negate the effects of 1 opponent's Effect Monster", "상대의 효과 몬스터 1장의 효과를 무효로 한다" },
            { "Negate the effects of an opponent's monster", "상대 몬스터의 효과를 무효로 한다" },
            { "Normal Summon 1 Winged Beast monster?", "비행야수족 몬스터 1장을 일반 소환하시겠습니까?" },
            { "Place 1 card on the field on top of the Deck", "필드의 카드 1장을 덱 맨 위에 놓는다" },
            { "Place in Pend Zone", "펜듈럼 존에 놓는다" },
            { "Place in Spell/Trap zone instead of destruction", "파괴되는 대신 마법·함정 존에 놓는다" },
            { "Place this card in your Spell & Trap Zone as a Continuous Spell", "이 카드를 지속 마법 카드로 취급하여 자신의 마법·함정 존에 놓는다" },
            { "Recover LP", "LP를 회복한다" },
            { "Reduce ATK to 0", "공격력을 0으로 한다" },
            { "Return this card to hand", "이 카드를 패로 되돌린다" },
            { "Return to Graveyard", "묘지로 되돌린다" },
            { "Return to hand and Special Summon", "패로 되돌리고 특수 소환한다" },
            { "Reveal", "공개한다" },
            { "Search from Deck", "덱에서 카드를 패에 넣는다" },
            { "Set 1 Card?", "카드 1장을 세트하시겠습니까?" },
            { "Set the card?", "그 카드를 세트하시겠습니까?" },
            { "Special Summon another monster?", "다른 몬스터를 특수 소환하시겠습니까?" },
            { "Special Summon a target", "대상 몬스터를 특수 소환한다" },
            { "Special Summon it, ignoring its Summoning conditions?", "그 몬스터를 소환 조건을 무시하고 특수 소환하시겠습니까?" },
            { "Special Summon the drawn monster?", "드로우한 몬스터를 특수 소환하시겠습니까?" },
            { "Special Summon the target?", "대상 몬스터를 특수 소환하시겠습니까?" },
            { "Special Summon this card to the opponent's field", "이 카드를 상대 필드에 특수 소환한다" },
            { "Summon", "소환한다" },
            { "Synchro Summon using this card you control", "자신 필드의 이 카드를 소재로 싱크로 소환한다" },
            { "Take 1000 damage", "1000 데미지를 받는다" },
            { "Target to destroy", "파괴할 카드를 대상으로 한다" },
            { "Tribute Summon by Tributing 1 Tribute Summoned monster", "어드밴스 소환한 몬스터 1장을 릴리스하여 어드밴스 소환한다" },
            { "Negate that effect", "그 효과를 무효로 한다" },
            { "Negate the activation of that effect", "그 효과의 발동을 무효로 한다" },
            { "Fusion Summon", "융합 소환한다" },
            { "Search", "덱에서 카드를 패에 넣는다" },
            { "Add card", "카드를 패에 넣는다" },
            { "Add this card to your hand", "이 카드를 패에 넣는다" },
            { "Return this card to the hand", "이 카드를 패로 되돌린다" },
            { "Return to the Deck", "덱으로 되돌린다" },
            { "Shuffle to Deck", "덱으로 되돌린다" },
            { "Shuffle to the Deck", "덱으로 되돌린다" },
            { "Shuffle into the Deck", "덱으로 되돌린다" },
            { "Return to the Extra Deck", "엑스트라 덱으로 되돌린다" },
            { "Send to the GY", "묘지로 보낸다" },
            { "Send to the Graveyard", "묘지로 보낸다" },
            { "Send it to the GY", "그 카드를 묘지로 보낸다" },
            { "Send the top 2 cards of your Deck to the GY", "자신의 덱 위에서 카드 2장을 묘지로 보낸다" },
            { "Send the top 3 cards of your Deck to the GY", "자신의 덱 위에서 카드 3장을 묘지로 보낸다" },
            { "Send the top 3 cards of either Deck to the GY", "자신 또는 상대 덱 위에서 카드 3장을 묘지로 보낸다" },
            { "Send the top 3 cards of your Deck", "내 덱 위에서 카드 3장을 묘지로 보낸다" },
            { "Send the top 3 cards of your opponent's Deck", "상대 덱 위에서 카드 3장을 묘지로 보낸다" },
            { "Send the top 3 cards of your Deck…", "자신의 덱 위에서 카드 3장을 묘지로 보낸다" },
            { "Top of the Deck", "덱 맨 위" },
            { "Bottom of Deck", "덱 맨 아래" },
            { "Bottom of the Deck", "덱 맨 아래" },
            { "Place it on the top of the Deck", "덱 맨 위에 놓는다" },
            { "Place it on the bottom of the Deck", "덱 맨 아래에 놓는다" },
            { "Place on bottom", "덱 맨 아래에 놓는다" },
            { "Apply both effects, in sequence", "두 효과를 순서대로 적용한다" },
            { "Activate both effects", "두 효과를 모두 발동한다" },
            { "Look at your Deck", "내 덱을 확인한다" },
            { "Look at your opponent's Deck", "상대 덱을 확인한다" },
            { "Look at your Extra Deck", "내 엑스트라 덱을 확인한다" },
            { "Look at your opponent's Extra Deck", "상대 엑스트라 덱을 확인한다" },
            { "Look at your opponent's Set Spells/Traps", "상대가 세트한 마법·함정 카드를 확인한다" },
            { "Look at the top 2 cards of either player's Deck", "내 덱 또는 상대 덱의 맨 위 카드 2장을 확인한다" },
            { "Send \"Iron Core of Koa'ki Meiru\" to the Graveyard", "코아키메일의 강철핵을 묘지로 보낸다" },
            { "Reveal 1 Warrior-Type monster in your hand", "패에서 전사족 몬스터 1장을 공개한다" },
            { "Reveal 1 Rock-Type monster in your hand", "패에서 암석족 몬스터 1장을 공개한다" },
            { "Reveal 1 Beast-Warrior-Type monster in your hand", "패에서 야수전사족 몬스터 1장을 공개한다" },
            { "This card gains 400 ATK", "이 카드의 공격력을 400 올린다" },
            { "This card gains 1000 ATK", "이 카드의 공격력을 1000 올린다" },
            { "Special Summon it face-down on your opponent's field", "상대 필드에 뒷면 수비 표시로 특수 소환한다" },
            { "Special Summon it face-up on your field", "내 필드에 앞면 표시로 특수 소환한다" },
            { "Special Summon to your field", "내 필드에 특수 소환한다" },
            { "Search 1 \"Mystical Space Typhoon\"", "덱에서 싸이크론 1장을 패에 넣는다" },
            { "Pay 2000 LP", "LP 2000을 지불한다" },
            { "Reveal 1 \"Millennium Ankh\" in your hand", "패에서 천년의 십자 1장을 공개한다" },
            { "Return it to the hand", "그 카드를 패로 되돌린다" },
            { "Mill the top 5 cards", "덱 위에서 카드 5장을 묘지로 보낸다" },
            { "Face-down and send to GY", "뒷면 수비 표시로 하고 덱에서 카드를 묘지로 보낸다" },
            { "Draw card", "카드를 드로우한다" },
            { "Draw 1 card?", "카드를 1장 드로우하시겠습니까?" },
            { "Draw 3 cards", "카드를 3장 드로우한다" },
            { "Special Summon it", "그 몬스터를 특수 소환한다" },
            { "Special Summon from hand", "패에서 특수 소환한다" },
            { "Special Summon from the Deck", "덱에서 특수 소환한다" },
            { "Special Summon from the GY", "묘지에서 특수 소환한다" },
            { "Special Summon this card from your GY", "자신의 묘지에서 이 카드를 특수 소환한다" },
            { "Special Summon this card and banish", "이 카드를 특수 소환하고 카드를 제외한다" },
            { "Banish and Special Summon", "카드를 제외하고 특수 소환한다" },
            { "Banish an opponent's card face-down", "상대의 카드를 뒷면으로 제외한다" },
            { "Banish 1 monster", "몬스터 1장을 제외한다" },
            { "Select the card to add to your hand or Special Summon", "패에 넣거나 특수 소환할 카드를 선택하세요." },
            { "Normal Summon without Tribute", "릴리스 없이 일반 소환한다" },
            { "Ritual Summon", "의식 소환한다" },
            { "Synchro Summon", "싱크로 소환한다" },
            { "Xyz Summon", "엑시즈 소환한다" },
            { "Pendulum Summon", "펜듈럼 소환한다" },
            { "Link Summon", "링크 소환한다" },
            { "Destroy 1 card on the field", "필드의 카드 1장을 파괴한다" },
            { "Destroy 1 monster your opponent controls", "상대 필드의 몬스터 1장을 파괴한다" },
            { "Inflict damage", "데미지를 준다" },
            { "Damage", "데미지를 준다" },
            { "Inflict damage to your opponent", "상대에게 데미지를 준다" },
            { "Gain LP", "LP를 회복한다" },
            { "Negate", "무효로 한다" },
            { "Negate effect", "효과를 무효로 한다" },
            { "Negate attack", "공격을 무효로 한다" },
            { "Negate activation", "발동을 무효로 한다" },
            { "Negate the activation", "발동을 무효로 한다" },
            { "Gain attack", "공격력을 올린다" },
            { "Gain ATK", "공격력을 올린다" },
            { "Increase ATK", "공격력을 올린다" },
            { "Change ATK", "공격력을 변경한다" },
            { "Change Level", "레벨을 변경한다" },
            { "Increase Level", "레벨을 올린다" },
            { "Change position", "표시 형식을 변경한다" },
            { "Change battle position", "표시 형식을 변경한다" },
            { "Change to Defense Position", "수비 표시로 변경한다" },
            { "Change to face-down Defense Position", "뒷면 수비 표시로 변경한다" },
            { "Take control", "컨트롤을 얻는다" },
            { "Activate", "발동한다" },
            { "Place counter", "카운터를 놓는다" },
            { "Set this card", "이 카드를 세트한다" },
            { "Set 1 Trap from your GY?", "자신의 묘지에서 함정 카드 1장을 세트하시겠습니까?" },
            { "Place this card in your Pendulum Zone", "이 카드를 자신의 펜듈럼 존에 놓는다" },
            { "Excavate", "덱 위에서 카드를 넘긴다" },
            { "Attach", "엑시즈 소재로 한다" },
            { "Coin toss", "코인을 던진다" },
            { "Roll dice", "주사위를 굴린다" },
            { "Search 1 \"Polymerization\"", "덱에서 융합 1장을 패에 넣는다" },
            { "Activate using \"Infinite Impermanence\"", "무한포영을 발동한다" },
            { "Normal Summon", "일반 소환한다" },
            { "Normal Summon without Tributing", "릴리스 없이 일반 소환한다" },
            { "Special Summon?", "특수 소환하시겠습니까?" },
            { "Special Summon itself", "이 카드를 특수 소환한다" },
            { "Special Summon this card?", "이 카드를 특수 소환하시겠습니까?" },
            { "Special Summon this card from your hand or GY", "패 또는 묘지에서 이 카드를 특수 소환한다" },
            { "Special Summon this card in Defense Position", "이 카드를 수비 표시로 특수 소환한다" },
            { "Special Summon this card as an Effect Monster", "이 카드를 효과 몬스터로 특수 소환한다" },
            { "Special Summon 1 monster from your hand", "패에서 몬스터 1장을 특수 소환한다" },
            { "Special Summon 1 monster from your GY", "자신의 묘지에서 몬스터 1장을 특수 소환한다" },
            { "Special Summon Token", "토큰을 특수 소환한다" },
            { "Special Summon it to your field", "자신 필드에 그 몬스터를 특수 소환한다" },
            { "Special Summon it to your opponent's field", "상대 필드에 그 몬스터를 특수 소환한다" },
            { "Fusion Summon 1 Fusion Monster from your Extra Deck", "엑스트라 덱에서 융합 몬스터 1장을 융합 소환한다" },
            { "Destroy this card", "이 카드를 파괴한다" },
            { "Destroy 1 card on the field?", "필드의 카드 1장을 파괴하시겠습니까?" },
            { "Destroy 1 monster on the field", "필드의 몬스터 1장을 파괴한다" },
            { "Destroy 1 Spell/Trap on the field", "필드의 마법·함정 카드 1장을 파괴한다" },
            { "Destroy all monsters your opponent controls", "상대 필드의 몬스터를 전부 파괴한다" },
            { "Destroy all cards your opponent controls", "상대 필드의 카드를 전부 파괴한다" },
            { "Destroy all cards on the field", "필드의 카드를 전부 파괴한다" },
            { "Destroy and inflict damage", "파괴하고 데미지를 준다" },
            { "Destroy and Special Summon", "파괴하고 특수 소환한다" },
            { "Target 1 card to destroy", "파괴할 카드 1장을 대상으로 한다" },
            { "Negate effects", "효과를 무효로 한다" },
            { "Negate and destroy", "무효로 하고 파괴한다" },
            { "Negate the activation and destroy that card", "발동을 무효로 하고 그 카드를 파괴한다" },
            { "Negate the activation, and if you do, destroy that card", "발동을 무효로 하고 그 카드를 파괴한다" },
            { "Select effect", "적용할 효과를 선택하세요." },
            { "Apply 1 of these effects", "다음 효과 중 1개를 적용한다" },
            { "Apply these effects in sequence", "이 효과들을 순서대로 적용한다" },
            { "Search card", "덱에서 카드를 패에 넣는다" },
            { "Search or Special Summon", "덱에서 카드를 패에 넣거나 특수 소환한다" },
            { "Select the card to add to your hand or send to the GY", "패에 넣거나 묘지로 보낼 카드를 선택하세요." },
            { "Back to hand", "패로 되돌린다" },
            { "Shuffle it into the Deck", "그 카드를 덱으로 되돌린다" },
            { "Return this card to the Extra Deck", "이 카드를 엑스트라 덱으로 되돌린다" },
            { "Send this card to the GY", "이 카드를 묘지로 보낸다" },
            { "Banish 1 card on the field", "필드의 카드 1장을 제외한다" },
            { "Banish 1 card your opponent controls", "상대 필드의 카드 1장을 제외한다" },
            { "Discard", "패에서 카드를 버린다" },
            { "Discard 1 random card from your opponent's hand", "상대의 패를 무작위로 1장 버린다" },
            { "Reduce ATK", "공격력을 내린다" },
            { "Decrease ATK", "공격력을 내린다" },
            { "Lower attack", "공격력을 내린다" },
            { "Halve ATK", "공격력을 절반으로 한다" },
            { "Double ATK", "공격력을 2배로 한다" },
            { "Increase ATK and DEF", "공격력과 수비력을 올린다" },
            { "Reduce damage to 0", "데미지를 0으로 한다" },
            { "Reduce Level", "레벨을 내린다" },
            { "Increase Level by 1", "레벨을 1개 올린다" },
            { "Increase Level by 2", "레벨을 2 올린다" },
            { "Decrease Level by 1", "레벨을 1 내린다" },
            { "Increase its Level by 1", "그 몬스터의 레벨을 1개 올린다" },
            { "Decrease its Level by 1", "그 몬스터의 레벨을 1개 내린다" },
            { "Reduce its Level by 1", "그 몬스터의 레벨을 1 내린다" },
            { "Flip into face-down Defense Position", "뒷면 수비 표시로 한다" },
            { "Place in Pendulum Zone", "펜듈럼 존에 놓는다" },
            { "Equip this card", "이 카드를 장착한다" },
            { "Place spell counter", "마력 카운터를 놓는다" },
            { "Copy effect", "효과를 복사한다" },
            { "Attack again", "한 번 더 공격한다" },
            { "Direct attack", "직접 공격한다" },
            { "Heads", "앞면" },
            { "Tails", "뒷면" },
            { "Token", "토큰" },
            { "Spell Card", "마법 카드" },
            { "Trap Card", "함정 카드" },
            { "Add it to your hand", "그 카드를 패에 넣는다" },
            { "Apply 1 of the \"●\" effects?", "●로 표시된 효과 중 1개를 적용하시겠습니까?" },
            { "Apply an effect", "효과를 적용한다" },
            { "Apply effects", "효과를 적용한다" },
            { "Activate 1 effect according to Attribute(s)", "속성 조건을 충족하는 효과 1개를 발동한다" },
            { "Activate both of these effects", "두 효과를 모두 발동한다" },
            { "Activate both effects, in sequence", "두 효과를 순서대로 발동한다" },
            { "Activate effect now?", "지금 효과를 발동하시겠습니까?" },
            { "Activate only", "추가 효과 없이 발동한다" },
            { "Attack twice", "2회 공격할 수 있다" },
            { "Can be activated this turn", "이 턴에 발동할 수 있다" },
            { "Cannot be destroyed by battle", "전투로는 파괴되지 않는다" },
            { "Change ATK/DEF", "공격력·수비력을 변경한다" },
            { "Change name", "카드명을 변경한다" },
            { "Change its battle position", "그 몬스터의 표시 형식을 변경한다" },
            { "Change the battle position of 1 monster on the field", "필드의 몬스터 1장의 표시 형식을 변경한다" },
            { "Detach 1 material from this card", "이 카드의 엑시즈 소재를 1개 제거한다" },
            { "Destroy 1 card", "카드 1장을 파괴한다" },
            { "Destroy 1 Spell/Trap your opponent controls", "상대 필드의 마법·함정 카드 1장을 파괴한다" },
            { "Destroy Spell/Trap", "마법·함정 카드를 파괴한다" },
            { "Destroy the targeted card", "대상 카드를 파괴한다" },
            { "Discard 1 card", "패를 1장 버린다" },
            { "Discard hand", "패를 전부 버린다" },
            { "Draw 1", "카드를 1장 드로우한다" },
            { "Draw 2 cards, then discard 1 card", "2장 드로우한 뒤 패를 1장 버린다" },
            { "Effect already used", "이미 효과를 사용했다" },
            { "Equip it to this card", "그 카드를 이 카드에 장착한다" },
            { "Flip face-down", "뒷면 표시로 한다" },
            { "Increase ATK/DEF", "공격력과 수비력을 올린다" },
            { "Increase its Level", "그 몬스터의 레벨을 올린다" },
            { "Inflict 500 damage", "500 데미지를 준다" },
            { "LP Recovery", "LP를 회복한다" },
            { "Monster destruction", "몬스터를 파괴한다" },
            { "Multiple attacks", "여러 번 공격할 수 있다" },
            { "Negate and destroy Spell Card", "마법 카드의 발동을 무효로 하고 파괴한다" },
            { "Negate the attack", "공격을 무효로 한다" },
            { "Negate the effect", "효과를 무효로 한다" },
            { "Pay 500 LP", "500 LP를 지불한다" },
            { "Place A-Counters", "A 카운터를 놓는다" },
            { "Place counters", "카운터를 놓는다" },
            { "Place on the bottom of the Deck", "덱 맨 아래에 놓는다" },
            { "Prevent destruction", "파괴를 막는다" },
            { "Recover", "회복한다" },
            { "Return 1 card on the field to the hand", "필드의 카드 1장을 패로 되돌린다" },
            { "Return 1 card your opponent controls to the hand", "상대 필드의 카드 1장을 패로 되돌린다" },
            { "Return the targeted card to the hand", "대상 카드를 패로 되돌린다" },
            { "Return to Extra Deck", "엑스트라 덱으로 되돌린다" },
            { "S/T Destruction", "마법·함정 카드를 파괴한다" },
            { "Search and Set", "덱에서 카드를 골라 세트한다" },
            { "Select the card to place on top of your Deck", "덱 맨 위에 놓을 카드를 고른다" },
            { "Select the monster to add to your hand or Special Summon", "패에 넣거나 특수 소환할 몬스터를 고른다" },
            { "Self-destruct", "이 카드를 파괴한다" },
            { "Self Destruct", "이 카드를 파괴한다" },
            { "Send from deck to the Graveyard", "덱에서 묘지로 보낸다" },
            { "Send the top 2 cards of your opponent's Deck to the GY", "상대 덱 위에서 카드 2장을 묘지로 보낸다" },
            { "Set this card from your GY", "자신의 묘지에서 이 카드를 세트한다" },
            { "Shuffle", "셔플한다" },
            { "Special Summon from Graveyard", "묘지에서 특수 소환한다" },
            { "Special Summon it in Defense Position", "그 몬스터를 수비 표시로 특수 소환한다" },
            { "Special Summon this card as a Normal Monster", "이 카드를 일반 몬스터로 취급하여 특수 소환한다" },
            { "Special summon itself from hand", "패에서 이 카드를 특수 소환한다" },
            { "Special Summon the monster", "그 몬스터를 특수 소환한다" },
            { "Special Summon this card to your field", "이 카드를 자신 필드에 특수 소환한다" },
            { "Special Summon this card to your opponent's field", "이 카드를 상대 필드에 특수 소환한다" },
            { "Special Summon 1 \"Runick\" from your Extra Deck", "엑스트라 덱에서 루닉 몬스터 1장을 엑스트라 몬스터 존에 특수 소환한다" },
            { "Switch control", "컨트롤을 맞바꾼다" },
            { "Take damage", "데미지를 받는다" },
            { "Toss a coin", "코인 토스를 한다" },
            { "Tribute 1 other monster", "다른 몬스터 1장을 릴리스한다" },
            { "Add 1 monster from your GY to your hand", "자신 묘지에서 몬스터 1장을 패에 넣는다" },
            { "Add this card to hand", "이 카드를 패에 넣는다" },
            { "Attach itself to 1 \"Springans\" Xyz monster", "이 카드를 스프리건즈 엑시즈 몬스터 1장의 소재로 겹친다" },
            { "Attach the \"Purrely\" Quick-Play Spell to this card", "그 퓨어리 속공 마법을 이 카드의 엑시즈 소재로 겹친다" },
            { "Banish a target", "대상 카드를 제외한다" },
            { "Banish it", "그 카드를 제외한다" },
            { "Cannot be used as material for a Fusion, Synchro, Xyz, or Link Summon", "융합·싱크로·엑시즈·링크 소환 소재로 사용할 수 없다" },
            { "Change Attribute", "속성을 변경한다" },
            { "Change Scale", "펜듈럼 스케일을 변경한다" },
            { "Change levels", "레벨을 변경한다" },
            { "Change this card to face-down Defense Position", "이 카드를 뒷면 수비 표시로 한다" },
            { "Change to face-up Defense Position", "앞면 수비 표시로 한다" },
            { "Destroy 1 monster", "몬스터 1장을 파괴한다" },
            { "Destroy all monsters on the field", "필드의 몬스터를 전부 파괴한다" },
            { "Destroy all other cards on the field", "필드의 다른 카드를 전부 파괴한다" },
            { "Destroy it", "그 카드를 파괴한다" },
            { "Destroy that card", "그 카드를 파괴한다" },
            { "Destroy that monster", "그 몬스터를 파괴한다" },
            { "Detach 1 material and Special Summon this card", "엑시즈 소재를 1개 제거하고 이 카드를 특수 소환한다" },
            { "Equip this card to 1 \"Utopia\" monster", "이 카드를 희망황 호프 몬스터 1장에 장착한다" },
            { "Negate special summon", "특수 소환을 무효로 한다" },
            { "Negate the Summon", "소환을 무효로 한다" },
            { "Place as a Continuous Trap", "지속 함정으로 놓는다" },
            { "Return this card from the field to the hand", "필드의 이 카드를 패로 되돌린다" },
            { "Send 1 card on the field to the GY", "필드의 카드 1장을 묘지로 보낸다" },
            { "Send 1 monster your opponent controls to the GY", "상대 필드의 몬스터 1장을 묘지로 보낸다" },
            { "Send the top 2 cards of your Deck to the Graveyard", "자신의 덱 위에서 2장을 묘지로 보낸다" },
            { "Set 1 \"Dracotail\" Spell/Trap from your Deck", "덱에서 드래곤테일 마법·함정 카드 1장을 세트한다" },
            { "Shuffle 1 Normal Monster from your GY into the Deck", "자신 묘지의 일반 몬스터 1장을 덱으로 되돌린다" },
            { "Special Summon 1 FIRE monster from your GY", "자신 묘지에서 화염 속성 몬스터 1장을 특수 소환한다" },
            { "Special Summon 1 monster from your Deck", "자신의 덱에서 몬스터 1장을 특수 소환한다" },
            { "Special Summon 2 \"Krawler\" with different names", "이름이 다른 크롤러 몬스터 2장을 특수 소환한다" },
            { "Special Summon a token", "토큰 1장을 특수 소환한다" },
            { "Special Summon from deck", "덱에서 특수 소환한다" },
            { "Special Summon self", "이 카드를 특수 소환한다" },
            { "Special Summon this banished card", "제외 상태인 이 카드를 특수 소환한다" },
            { "Special Summon Tokens", "토큰을 특수 소환한다" },
            { "Switch ATK and DEF", "공격력과 수비력을 맞바꾼다" },
            { "Target 1 monster to destroy", "파괴할 몬스터 1장을 대상으로 한다" },
            { "Treat as Tuner", "튜너로 취급한다" },
            { "Tribute Summon with 1 Tribute", "몬스터 1장을 릴리스해 어드밴스 소환한다" },
            { "Xyz Summon by using another \"Zoodiac\" monster", "다른 십이수 몬스터를 소재로 겹쳐 엑시즈 소환한다" },
            { "Send from Deck to the GY", "덱에서 묘지로 보낸다" },
            { "Special Summon this card from the GY", "묘지의 이 카드를 특수 소환한다" },
            { "Special Summon this card from the Pendulum Zone", "펜듈럼 존의 이 카드를 특수 소환한다" },
            { "Special Summon this card and treat it as a Tuner", "이 카드를 튜너로 취급하여 특수 소환한다" },
            { "Special Summon this card in face-down Defense Position", "이 카드를 뒷면 수비 표시로 특수 소환한다" },
            { "Special Summon 1 monster from your opponent's GY", "상대의 묘지에서 몬스터 1장을 특수 소환한다" },
            { "Special Summon 1 monster from either GY", "양쪽 묘지 중 한 곳에서 몬스터 1장을 특수 소환한다" },
            { "Special Summon 1 Level 4 or lower monster from your GY", "자신의 묘지에서 레벨 4 이하의 몬스터 1장을 특수 소환한다" },
            { "Special Summon 1 excavated monster", "넘긴 카드 중 몬스터 1장을 특수 소환한다" },
            { "Special Summon from the Extra Deck", "엑스트라 덱에서 특수 소환한다" },
            { "Special Summon from the hand", "패에서 특수 소환한다" },
            { "Add this card to the hand", "이 카드를 패에 넣는다" },
            { "Add from GY to hand", "묘지에서 패에 넣는다" },
            { "Add from Deck to hand", "덱에서 패에 넣는다" },
            { "Add 1 Spell from your GY to your hand", "자신의 묘지에서 마법 카드 1장을 패에 넣는다" },
            { "Add 1 Ritual Spell from your Deck to your hand", "자신의 덱에서 의식 마법 카드 1장을 패에 넣는다" },
            { "Add 1 Ritual Monster from your Deck to your hand", "자신의 덱에서 의식 몬스터 1장을 패에 넣는다" },
            { "Return a target to the Extra Deck", "대상 카드를 엑스트라 덱으로 되돌린다" },
            { "Return 1 Spell/Trap your opponent controls to the hand", "상대 필드의 마법·함정 카드 1장을 패로 되돌린다" },
            { "Banish 1 monster from your opponent's GY", "상대의 묘지에서 몬스터 1장을 제외한다" },
            { "Banish 1 monster your opponent controls", "상대 필드의 몬스터 1장을 제외한다" },
            { "Destroy 1 other card on the field", "필드의 다른 카드 1장을 파괴한다" },
            { "Destroy 1 face-up card your opponent controls", "상대 필드의 앞면 표시 카드 1장을 파괴한다" },
            { "Destroy 1 monster your opponent controls with less ATK than this card", "상대 필드에서 이 카드보다 공격력이 낮은 몬스터 1장을 파괴한다" },
            { "Negate the Summon and destroy that monster(s)", "그 소환을 무효로 하고 그 몬스터를 파괴한다" },
            { "Negate the activation, and if you do, destroy it", "그 발동을 무효로 하고 그 카드를 파괴한다" },
            { "Negate the effects of 1 opponent's monster", "상대 몬스터 1장의 효과를 무효로 한다" },
            { "Negate the effects of 1 opponent's face-up card", "상대 필드의 앞면 표시 카드 1장의 효과를 무효로 한다" },
            { "Change 1 face-up monster on the field to face-down Defense Position", "필드의 앞면 표시 몬스터 1장을 뒷면 수비 표시로 변경한다" },
            { "Change that opponent's monster's ATK to 0", "그 상대 몬스터의 공격력을 0으로 한다" },
            { "Increase or decrease this card's Level by 1", "이 카드의 레벨을 1개 올리거나 내린다" },
            { "Inflict 1000 damage to your opponent", "상대에게 1000 데미지를 준다" },
            { "Inflict 500 damage to your opponent", "상대에게 500 데미지를 준다" },
            { "Take 2000 damage", "자신이 2000 데미지를 받는다" },
            { "Normal Summon with 1 Tribute", "몬스터 1장을 릴리스하고 일반 소환한다" },
            { "Select number of cards to excavate", "덱에서 넘길 카드의 수를 선택하세요." },
        };

        // These scripts reuse the same description ID for an alternate summon and for an activated effect.
        private static readonly Dictionary<ulong, string> CardActivationTextKR = new()
        {
            { 26096328UL << DescriptionIndexBits, "엑시즈 소재를 1개 제거해 공격력과 수비력을 2000 올린다" },
            { 90448279UL << DescriptionIndexBits, "엑시즈 소재를 2개 제거해 필드의 다른 카드를 전부 묘지로 보낸다" },
        };

        private static readonly Dictionary<ulong, string> CardEffectTextKR = new Dictionary<ulong, string>
        {
            // Use the option IDs from the bundled Lua scripts when the database strings are missing or out of date.
            { (101303084UL << DescriptionIndexBits) | 2, "이 카드의 엑시즈 소재를 1개 제거한다" },
            { (101303084UL << DescriptionIndexBits) | 3, "내가 3000 데미지를 받는다" },
            { (1710647UL << DescriptionIndexBits) | 3, "대상 몬스터의 레벨을 1 올린다" },
            { (1710647UL << DescriptionIndexBits) | 4, "대상 몬스터의 레벨을 1 내린다" },
            { (3441553UL << DescriptionIndexBits) | 1, "내 삼환마 몬스터에 체인한 상대 효과를 무효로 한다" },
            { (3441553UL << DescriptionIndexBits) | 2, "패의 레벨 10 삼환마를 공개하고, 패 1장을 버려 환마의 문을 패에 넣는다" },
            { (3441553UL << DescriptionIndexBits) | 3, "묘지·제외 상태의 삼환마 함정 3장을 덱으로 되돌려 상대 카드 1장을 파괴한다" },
            { (37839434UL << DescriptionIndexBits) | 1, "이 카드를 레벨 1 튜너로 특수 소환한 뒤 싱크로 소환할 수 있다" },
            { (37839434UL << DescriptionIndexBits) | 2, "상대가 발동한 마법·함정 효과를 무효로 한다" },
            { (37839434UL << DescriptionIndexBits) | 3, "싱크로 소환을 실행하시겠습니까?" },
            { (40235813UL << DescriptionIndexBits) | 1, "시간의 흑마술사가 쓰인 카드 1장을 덱에서 패에 넣고, 엔드 페이즈에 묘지의 시간의 흑마술사를 회수한다" },
            { (40235813UL << DescriptionIndexBits) | 2, "코인 토스에 성공하면 상대 몬스터를 전부 파괴하고 데미지를 준다. 실패하면 내 몬스터를 전부 파괴한다" },
            { (55742055UL << DescriptionIndexBits) | 1, "덱에서 성기사 카드 1장을 묘지로 보낸다" },
            { (55742055UL << DescriptionIndexBits) | 2, "패에서 성기사 몬스터 1장을 특수 소환한 뒤 패의 성검을 장착할 수 있다" },
            { (55742055UL << DescriptionIndexBits) | 3, "내 묘지의 성기사 몬스터 1장을 패에 넣는다" },
            { (55742055UL << DescriptionIndexBits) | 4, "1장 드로우한다" },
            { (63111591UL << DescriptionIndexBits) | 1, "묘지로 보낸 카드로 특수 소환 또는 세트를 하시겠습니까?" },
            { (63111591UL << DescriptionIndexBits) | 2, "묘지로 보낸 몬스터 중 1장을 특수 소환한다" },
            { (63111591UL << DescriptionIndexBits) | 3, "묘지로 보낸 마법·함정 중 1장을 세트한다. 속공·함정은 이번 턴에 발동할 수 있다" },
            { (64455720UL << DescriptionIndexBits) | 1, "이 카드를 레벨 1 튜너로 특수 소환한 뒤 싱크로 소환할 수 있다" },
            { (64455720UL << DescriptionIndexBits) | 2, "내 묘지에서 아슈트라 몬스터 1장을 특수 소환한다" },
            { (64455720UL << DescriptionIndexBits) | 3, "싱크로 소환을 실행하시겠습니까?" },
            { (76334960UL << DescriptionIndexBits) | 1, "내 패·필드의 몬스터 1장과 내 덱의 몬스터 1장을 제외해 소환수를 융합 소환한다" },
            { (76334960UL << DescriptionIndexBits) | 2, "내 필드의 몬스터 1장과 상대의 앞면 몬스터 1장을 제외해 소환수를 융합 소환한다" },
            { (77456448UL << DescriptionIndexBits) | 2, "상대의 앞면 마법 카드를 전부 파괴한다" },
            { (77456448UL << DescriptionIndexBits) | 3, "빛과 어둠의 의식 1장을 공개하고, 상대 몬스터 1장의 공격력을 0으로 하며 효과를 무효로 한다" },
            { (77456448UL << DescriptionIndexBits) | 4, "상대 패의 카드 1장을 무작위로 파괴한다" },
            { (77456448UL << DescriptionIndexBits) | 5, "상대 필드의 카드 1장을 골라 파괴한다" },
            { (77482666UL << DescriptionIndexBits) | 1, "시간의 흑마술사가 쓰인 몬스터 1장을 패·덱에서 특수 소환한다" },
            { (77482666UL << DescriptionIndexBits) | 2, "시간의 흑마술사가 쓰인 마법·함정 1장을 덱에서 묘지로 보낸다" },
            { (80635735UL << DescriptionIndexBits) | 2, "엑스트라 덱에서 특수 소환된 상대 몬스터를 전부 파괴한다" },
            { (80635735UL << DescriptionIndexBits) | 3, "상대 필드의 마법·함정을 전부 파괴한다" },
            { (90875418UL << DescriptionIndexBits) | 3, "덱에서 아르스＝마그나 카드 1장을 패에 넣는다" },
            { (90875418UL << DescriptionIndexBits) | 4, "이 카드를 제외하고 엑스트라 덱에서 디아크트로스 몬스터 1장을 특수 소환한다" },
            { (91444835UL << DescriptionIndexBits) | 1, "이 카드를 레벨 1 튜너로 특수 소환한 뒤 싱크로 소환할 수 있다" },
            { (91444835UL << DescriptionIndexBits) | 2, "상대 필드의 몬스터 1장을 파괴한다" },
            { (91444835UL << DescriptionIndexBits) | 3, "싱크로 소환을 실행하시겠습니까?" },
            { (99505609UL << DescriptionIndexBits) | 2, "내 메인 몬스터 존 또는 마법·함정 존에서 5장을 파괴하고 2장 드로우한다" },
            { (99505609UL << DescriptionIndexBits) | 3, "상대 메인 몬스터 존 또는 마법·함정 존에서 5장을 파괴하고 상대가 2장 드로우한다" },
            { (99505609UL << DescriptionIndexBits) | 4, "메인 몬스터 존의 몬스터 5장을 파괴한다" },
            { (99505609UL << DescriptionIndexBits) | 5, "마법·함정 존의 카드 5장을 파괴한다" },

            { 4928565UL << DescriptionIndexBits, "패에서 이 카드를 특수 소환한 뒤, 자신의 패·묘지에서 크샤트리라 또는 티아라멘츠 카드 1장을 제외한다." },
            { 28226490UL << DescriptionIndexBits, "상대 필드의 카드 1장을 주인의 덱으로 되돌린다." },
            { (28226490UL << DescriptionIndexBits) | 1, "이 카드를 특수 소환하고, 덱에서 티아라멘츠 카드 1장을 묘지로 보낸다." },
            { (32731036UL << DescriptionIndexBits) | 1, "덱에서 낙인 지속 마법·함정 카드 1장을 자신 필드에 앞면으로 놓는다." },
            { (33854624UL << DescriptionIndexBits) | 1, "이 턴의 엔드 페이즈에 덱 또는 묘지에서 다른 드래곤족 몬스터 1장을 패에 넣는다." },
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
                if (!string.IsNullOrEmpty(c.desc))
                    c.desc = LocalizeEffectHeadings(c.desc);
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
            LoadDuelEffectLabels(streamingAssetsPath);
            IndexDeckPrintings();
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

        internal static uint GetArchetypeCollectionKey(CardInfo card)
        {
            var original = GetSameNameOriginal(card);
            return original != null && IsPureAltArtDuplicate(card) ? original.code : card.code;
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

        private static void IndexDeckPrintings()
        {
            DeckPrintingsByCode.Clear();
            var families = _byCode.Values.Where(card => (card.type & YgoConstants.TYPE_TOKEN) == 0)
                .GroupBy(card => card.alias != 0 ? card.alias : card.code);
            foreach (var family in families.Where(group => group.Count() > 1))
            {
                // A rules alias can also tie together separate cards such as Umi and A Legendary Ocean.
                var equivalent = family.GroupBy(card => (card.type, card.desc, Monster: card.IsMonster
                    ? (card.atk, card.def, card.level, card.race, card.attr, card.lscale, card.rscale, card.link)
                    : default));
                foreach (var group in equivalent.Where(group => group.Count() > 1))
                {
                    var printings = group.OrderBy(card => card.code != family.Key).ThenBy(card => card.code).ToArray();
                    foreach (var card in printings) DeckPrintingsByCode.Add(card.code, printings);
                }
            }
        }

        internal static bool TryGetDeckPrintings(uint code, out CardInfo[] printings) => DeckPrintingsByCode.TryGetValue(code, out printings);

        internal static uint GetDeckPrintingKey(uint code) => TryGetDeckPrintings(code, out var printings) ? printings[0].code : code;

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

        private static void LoadDuelEffectLabels(string streamingAssetsPath)
        {
            string path = Path.Combine(streamingAssetsPath, "duel_effect_labels_ko.tsv");
            if (!File.Exists(path)) return;
            try
            {
                foreach (string line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    var fields = line.Split('\t');
                    if (fields.Length != 3 || !uint.TryParse(fields[0], out uint code) || code == 0 ||
                        !uint.TryParse(fields[1], out uint index) || index > DescriptionIndexMask || !HasKoreanText(fields[2])) continue;
                    CardEffectTextKR.TryAdd(((ulong)code << DescriptionIndexBits) | index, fields[2].Trim());
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"[CardDatabase] Could not read the Korean duel effect labels. {error.Message}");
            }
        }

        public static string ResolveDescription(ulong desc) => ResolveDescription(desc, activation: false);

        private static string ResolveDescription(ulong desc, bool activation)
        {
            if (desc <= DescriptionIndexMask)
                return SystemHintsKR.TryGetValue((uint)desc, out var hint) ? hint : null;
            if (TryGetCardEffectText(desc, activation, out var specificText)) return specificText;

            int index = (int)(desc & DescriptionIndexMask);
            var card = Get(GetDescriptionCardCode(desc));
            var original = GetSameNameOriginal(card);
            if (original != null && TryGetCardEffectText(((ulong)original.code << DescriptionIndexBits) | (uint)index, activation, out specificText))
                return specificText;
            return ReadLocalizedDescription(card, index) ?? ReadLocalizedDescription(original, index);
        }

        private static bool TryGetCardEffectText(ulong desc, bool activation, out string text) =>
            (activation && CardActivationTextKR.TryGetValue(desc, out text)) || CardEffectTextKR.TryGetValue(desc, out text);

        public static string ResolveEffectDescription(ulong desc, uint cardCode)
        {
            string text = ResolveDescription(desc, activation: true);
            if (text != null) return text;
            return GetKoreanCardDescription(GetDescriptionCardCode(desc)) ?? GetKoreanCardDescription(cardCode);
        }

        internal static string ResolveOptionDescription(ulong desc) => ResolveDescription(desc);

        internal static string GetKoreanCardDescription(uint code)
        {
            string text = Get(code)?.desc;
            return HasKoreanText(text) ? LocalizeEffectHeadings(text.Trim()) : null;
        }

        private static string ReadLocalizedDescription(CardInfo card, int index)
        {
            string text = ReadDescriptionString(card, index);
            if (text == null) return null;
            if (HasKoreanText(text)) return LocalizeEffectHeadings(text);

            if (EffectTextKR.TryGetValue(text, out var localized)) return localized;

            // Identical labels can have a printed bullet or use question punctuation in a confirmation.
            string key = text.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"')
                .Replace('\u00A0', ' ').TrimStart('●', ' ');
            if (EffectTextKR.TryGetValue(key, out localized)) return localized;
            key = key.TrimEnd('.', '?', '!', ';', ' ');
            return EffectTextKR.TryGetValue(key, out localized) ? localized : null;
        }

        private static string ReadDescriptionString(CardInfo card, int index)
        {
            var strings = card?.strs;
            if (strings == null || index >= strings.Length || string.IsNullOrWhiteSpace(strings[index]))
                return null;
            return strings[index].Trim();
        }

        private static string LocalizeEffectHeadings(string text) =>
            text.Replace("[Pendulum Effect]", "[펜듈럼 효과]", StringComparison.OrdinalIgnoreCase)
                .Replace("[Monster Effect]", "[몬스터 효과]", StringComparison.OrdinalIgnoreCase);

        private static CardInfo GetSameNameOriginal(CardInfo card)
        {
            if (card == null || card.alias == 0) return null;
            var original = Get(card.alias);
            return original?.name == card.name ? original : null;
        }

        private static bool HasKoreanText(string text)
        {
            if (text == null) return false;
            foreach (char character in text)
                if (character >= '가' && character <= '힣') return true;
            return false;
        }
    }
}
