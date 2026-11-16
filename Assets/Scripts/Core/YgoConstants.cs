using System;
using System.Collections.Generic;

namespace YGOPortable.Core
{
    public static class YgoConstants
    {
        // Master Rule 5 (2020 Revision): PZONE | EMZONE | FSX_MMZONE | TRAP_MONSTERS_NOT_USE_ZONE | TRIGGER_ONLY_IN_LOCATION
        public const ulong DUEL_MODE_MR5 = 0x2E800UL;

        public const int OCG_DUEL_STATUS_END = 0;
        public const int OCG_DUEL_STATUS_AWAITING = 1;
        public const int OCG_DUEL_STATUS_CONTINUE = 2;

        // Locations
        public const uint LOCATION_DECK = 0x01;
        public const uint LOCATION_HAND = 0x02;
        public const uint LOCATION_MZONE = 0x04;
        public const uint LOCATION_SZONE = 0x08;
        public const uint LOCATION_GRAVE = 0x10;
        public const uint LOCATION_REMOVED = 0x20;
        public const uint LOCATION_EXTRA = 0x40;
        public const uint LOCATION_OVERLAY = 0x80;
        public const uint LOCATION_ONFIELD = LOCATION_MZONE | LOCATION_SZONE;
        public const uint LOCATION_FZONE = 0x100;
        public const uint LOCATION_PZONE = 0x200;

        // Positions
        public const uint POS_FACEUP_ATTACK = 0x1;
        public const uint POS_FACEDOWN_ATTACK = 0x2;
        public const uint POS_FACEUP_DEFENSE = 0x4;
        public const uint POS_FACEDOWN_DEFENSE = 0x8;
        public const uint POS_FACEUP = POS_FACEUP_ATTACK | POS_FACEUP_DEFENSE;
        public const uint POS_FACEDOWN = POS_FACEDOWN_ATTACK | POS_FACEDOWN_DEFENSE;
        public const uint POS_ATTACK = POS_FACEUP_ATTACK | POS_FACEDOWN_ATTACK;
        public const uint POS_DEFENSE = POS_FACEUP_DEFENSE | POS_FACEDOWN_DEFENSE;

        // Card Types
        public const uint TYPE_MONSTER = 0x1;
        public const uint TYPE_SPELL = 0x2;
        public const uint TYPE_TRAP = 0x4;
        public const uint TYPE_NORMAL = 0x10;
        public const uint TYPE_EFFECT = 0x20;
        public const uint TYPE_FUSION = 0x40;
        public const uint TYPE_RITUAL = 0x80;
        public const uint TYPE_TRAPMONSTER = 0x100;
        public const uint TYPE_SPIRIT = 0x200;
        public const uint TYPE_UNION = 0x400;
        public const uint TYPE_GEMINI = 0x800;
        public const uint TYPE_TUNER = 0x1000;
        public const uint TYPE_SYNCHRO = 0x2000;
        public const uint TYPE_TOKEN = 0x4000;
        public const uint TYPE_QUICKPLAY = 0x10000;
        public const uint TYPE_CONTINUOUS = 0x20000;
        public const uint TYPE_EQUIP = 0x40000;
        public const uint TYPE_FIELD = 0x80000;
        public const uint TYPE_COUNTER = 0x100000;
        public const uint TYPE_FLIP = 0x200000;
        public const uint TYPE_TOON = 0x400000;
        public const uint TYPE_XYZ = 0x800000;
        public const uint TYPE_PENDULUM = 0x1000000;
        public const uint TYPE_SPSUMMON = 0x2000000;
        public const uint TYPE_LINK = 0x4000000;
        public const uint TYPE_EXTRA = TYPE_FUSION | TYPE_SYNCHRO | TYPE_XYZ | TYPE_LINK;

        // Attributes
        public const uint ATTRIBUTE_EARTH = 0x01;
        public const uint ATTRIBUTE_WATER = 0x02;
        public const uint ATTRIBUTE_FIRE = 0x04;
        public const uint ATTRIBUTE_WIND = 0x08;
        public const uint ATTRIBUTE_LIGHT = 0x10;
        public const uint ATTRIBUTE_DARK = 0x20;
        public const uint ATTRIBUTE_DIVINE = 0x40;

        // Races (including 0x2000000 Illusion)
        public const ulong RACE_WARRIOR = 0x1;
        public const ulong RACE_SPELLCASTER = 0x2;
        public const ulong RACE_FAIRY = 0x4;
        public const ulong RACE_FIEND = 0x8;
        public const ulong RACE_ZOMBIE = 0x10;
        public const ulong RACE_MACHINE = 0x20;
        public const ulong RACE_AQUA = 0x40;
        public const ulong RACE_PYRO = 0x80;
        public const ulong RACE_ROCK = 0x100;
        public const ulong RACE_WINGEDBEAST = 0x200;
        public const ulong RACE_PLANT = 0x400;
        public const ulong RACE_INSECT = 0x800;
        public const ulong RACE_THUNDER = 0x1000;
        public const ulong RACE_DRAGON = 0x2000;
        public const ulong RACE_BEAST = 0x4000;
        public const ulong RACE_BEASTWARRIOR = 0x8000;
        public const ulong RACE_DINOSAUR = 0x10000;
        public const ulong RACE_FISH = 0x20000;
        public const ulong RACE_SEASERPENT = 0x40000;
        public const ulong RACE_REPTILE = 0x80000;
        public const ulong RACE_PSYCHIC = 0x100000;
        public const ulong RACE_DIVINE = 0x200000;
        public const ulong RACE_CREATORGOD = 0x400000;
        public const ulong RACE_WYRM = 0x800000;
        public const ulong RACE_CYBERSE = 0x1000000;
        public const ulong RACE_ILLUSION = 0x2000000;

        // Query flags
        public const uint QUERY_CODE = 0x1;
        public const uint QUERY_POSITION = 0x2;
        public const uint QUERY_ALIAS = 0x4;
        public const uint QUERY_TYPE = 0x8;
        public const uint QUERY_LEVEL = 0x10;
        public const uint QUERY_RANK = 0x20;
        public const uint QUERY_ATTRIBUTE = 0x40;
        public const uint QUERY_RACE = 0x80;
        public const uint QUERY_ATTACK = 0x100;
        public const uint QUERY_DEFENSE = 0x200;
        public const uint QUERY_BASE_ATTACK = 0x400;
        public const uint QUERY_BASE_DEFENSE = 0x800;
        public const uint QUERY_REASON = 0x1000;
        public const uint QUERY_OVERLAY_CARD = 0x10000;
        public const uint QUERY_COUNTERS = 0x20000;
        public const uint QUERY_OWNER = 0x40000;
        public const uint QUERY_STATUS = 0x80000;
        public const uint QUERY_IS_PUBLIC = 0x100000;
        public const uint QUERY_LSCALE = 0x200000;
        public const uint QUERY_RSCALE = 0x400000;
        public const uint QUERY_LINK = 0x800000;
        public const uint QUERY_END = 0x80000000;
        public const uint QUERY_DEFAULT = QUERY_CODE | QUERY_POSITION | QUERY_ALIAS | QUERY_TYPE |
            QUERY_LEVEL | QUERY_RANK | QUERY_ATTRIBUTE | QUERY_RACE | QUERY_ATTACK | QUERY_DEFENSE |
            QUERY_BASE_ATTACK | QUERY_BASE_DEFENSE | QUERY_OVERLAY_CARD | QUERY_COUNTERS |
            QUERY_OWNER | QUERY_IS_PUBLIC | QUERY_LSCALE | QUERY_RSCALE | QUERY_LINK;

        // Phases
        public const int PHASE_DRAW = 0x01;
        public const int PHASE_STANDBY = 0x02;
        public const int PHASE_MAIN1 = 0x04;
        public const int PHASE_BATTLE_START = 0x08;
        public const int PHASE_BATTLE_STEP = 0x10;
        public const int PHASE_DAMAGE = 0x20;
        public const int PHASE_DAMAGE_CAL = 0x40;
        public const int PHASE_BATTLE = 0x80;
        public const int PHASE_MAIN2 = 0x100;
        public const int PHASE_END = 0x200;

        // Hints
        public const int HINT_EVENT = 1;
        public const int HINT_MESSAGE = 2;
        public const int HINT_SELECTMSG = 3;
        public const int HINT_OPSELECTED = 4;
        public const int HINT_EFFECT = 5;
        public const int HINT_RACE = 6;
        public const int HINT_ATTRIB = 7;
        public const int HINT_CODE = 8;
        public const int HINT_NUMBER = 9;
        public const int HINT_CARD = 10;
        public const int HINT_ZONE = 11;

        // Messages
        public const byte MSG_RETRY = 1;
        public const byte MSG_HINT = 2;
        public const byte MSG_WIN = 5;
        public const byte MSG_SELECT_BATTLECMD = 10;
        public const byte MSG_SELECT_IDLECMD = 11;
        public const byte MSG_SELECT_EFFECTYN = 12;
        public const byte MSG_SELECT_YESNO = 13;
        public const byte MSG_SELECT_OPTION = 14;
        public const byte MSG_SELECT_CARD = 15;
        public const byte MSG_SELECT_CHAIN = 16;
        public const byte MSG_SELECT_PLACE = 18;
        public const byte MSG_SELECT_POSITION = 19;
        public const byte MSG_SELECT_TRIBUTE = 20;
        public const byte MSG_SORT_CHAIN = 21;
        public const byte MSG_SELECT_COUNTER = 22;
        public const byte MSG_SELECT_SUM = 23;
        public const byte MSG_SELECT_DISFIELD = 24;
        public const byte MSG_SORT_CARD = 25;
        public const byte MSG_SELECT_UNSELECT_CARD = 26;
        public const byte MSG_CONFIRM_DECKTOP = 30;
        public const byte MSG_CONFIRM_CARDS = 31;
        public const byte MSG_SHUFFLE_DECK = 32;
        public const byte MSG_SHUFFLE_HAND = 33;
        public const byte MSG_SWAP_GRAVE_DECK = 35;
        public const byte MSG_SHUFFLE_SET_CARD = 36;
        public const byte MSG_DECK_TOP = 38;
        public const byte MSG_SHUFFLE_EXTRA = 39;
        public const byte MSG_NEW_TURN = 40;
        public const byte MSG_NEW_PHASE = 41;
        public const byte MSG_MOVE = 50;
        public const byte MSG_POS_CHANGE = 53;
        public const byte MSG_SET = 54;
        public const byte MSG_SWAP = 55;
        public const byte MSG_FIELD_DISABLED = 56;
        public const byte MSG_SUMMONING = 60;
        public const byte MSG_SUMMONED = 61;
        public const byte MSG_SPSUMMONING = 62;
        public const byte MSG_SPSUMMONED = 63;
        public const byte MSG_FLIPSUMMONING = 64;
        public const byte MSG_FLIPSUMMONED = 65;
        public const byte MSG_CHAINING = 70;
        public const byte MSG_CHAINED = 71;
        public const byte MSG_CHAIN_SOLVING = 72;
        public const byte MSG_CHAIN_SOLVED = 73;
        public const byte MSG_CHAIN_END = 74;
        public const byte MSG_CHAIN_NEGATED = 75;
        public const byte MSG_CHAIN_DISABLED = 76;
        public const byte MSG_CARD_SELECTED = 80;
        public const byte MSG_RANDOM_SELECTED = 81;
        public const byte MSG_BECOME_TARGET = 83;
        public const byte MSG_DRAW = 90;
        public const byte MSG_DAMAGE = 91;
        public const byte MSG_RECOVER = 92;
        public const byte MSG_EQUIP = 93;
        public const byte MSG_LPUPDATE = 94;
        public const byte MSG_PAY_LPCOST = 100;
        public const byte MSG_ATTACK = 110;
        public const byte MSG_BATTLE = 111;
        public const byte MSG_ATTACK_DISABLED = 112;
        public const byte MSG_DAMAGE_STEP_START = 113;
        public const byte MSG_DAMAGE_STEP_END = 114;
        public const byte MSG_TOSS_COIN = 130;
        public const byte MSG_TOSS_DICE = 131;
        public const byte MSG_ROCK_PAPER_SCISSORS = 132;
        public const byte MSG_HAND_RES = 133;
        public const byte MSG_ANNOUNCE_RACE = 140;
        public const byte MSG_ANNOUNCE_ATTRIB = 141;
        public const byte MSG_ANNOUNCE_CARD = 142;
        public const byte MSG_ANNOUNCE_NUMBER = 143;
        public const byte MSG_CARD_HINT = 160;
        public const byte MSG_TAG_SWAP = 161;
        public const byte MSG_MATCH_KILL = 170;

        public static readonly Dictionary<uint, string> AttributeNamesKR = new Dictionary<uint, string>
        {
            { ATTRIBUTE_EARTH, "땅" },
            { ATTRIBUTE_WATER, "물" },
            { ATTRIBUTE_FIRE, "화염" },
            { ATTRIBUTE_WIND, "바람" },
            { ATTRIBUTE_LIGHT, "빛" },
            { ATTRIBUTE_DARK, "어둠" },
            { ATTRIBUTE_DIVINE, "신" },
        };

        public static readonly Dictionary<uint, string> AttributeSpriteNames = new Dictionary<uint, string>
        {
            { ATTRIBUTE_EARTH, "ICON_ID_ATTR_EARTH" },
            { ATTRIBUTE_WATER, "ICON_ID_ATTR_WATER" },
            { ATTRIBUTE_FIRE, "ICON_ID_ATTR_FIRE" },
            { ATTRIBUTE_WIND, "ICON_ID_ATTR_WIND" },
            { ATTRIBUTE_LIGHT, "ICON_ID_ATTR_LIGHT" },
            { ATTRIBUTE_DARK, "ICON_ID_ATTR_DARK" },
            { ATTRIBUTE_DIVINE, "ICON_ID_ATTR_GOD" },
        };

        public static readonly Dictionary<ulong, string> RaceNamesKR = new Dictionary<ulong, string>
        {
            { RACE_WARRIOR, "전사족" },
            { RACE_SPELLCASTER, "마법사족" },
            { RACE_FAIRY, "천사족" },
            { RACE_FIEND, "악마족" },
            { RACE_ZOMBIE, "언데드족" },
            { RACE_MACHINE, "기계족" },
            { RACE_AQUA, "물족" },
            { RACE_PYRO, "화염족" },
            { RACE_ROCK, "암석족" },
            { RACE_WINGEDBEAST, "비행야수족" },
            { RACE_PLANT, "식물족" },
            { RACE_INSECT, "곤충족" },
            { RACE_THUNDER, "번개족" },
            { RACE_DRAGON, "드래곤족" },
            { RACE_BEAST, "야수족" },
            { RACE_BEASTWARRIOR, "야수전사족" },
            { RACE_DINOSAUR, "공룡족" },
            { RACE_FISH, "어류족" },
            { RACE_SEASERPENT, "해룡족" },
            { RACE_REPTILE, "파충류족" },
            { RACE_PSYCHIC, "사이킥족" },
            { RACE_DIVINE, "환신야수족" },
            { RACE_CREATORGOD, "창조신족" },
            { RACE_WYRM, "환룡족" },
            { RACE_CYBERSE, "사이버스족" },
            { RACE_ILLUSION, "환상마족" },
        };

        public static readonly Dictionary<ulong, string> RaceSpriteNames = new Dictionary<ulong, string>
        {
            { RACE_WARRIOR, "ICON_ID_TYPE_SOLDIER" },
            { RACE_SPELLCASTER, "ICON_ID_TYPE_WIZARD" },
            { RACE_FAIRY, "ICON_ID_TYPE_ANGEL" },
            { RACE_FIEND, "ICON_ID_TYPE_DEVIL" },
            { RACE_ZOMBIE, "ICON_ID_TYPE_UNDEAD" },
            { RACE_MACHINE, "ICON_ID_TYPE_MACHINE" },
            { RACE_AQUA, "ICON_ID_TYPE_AQUARIUS" },
            { RACE_PYRO, "ICON_ID_TYPE_FLAME" },
            { RACE_ROCK, "ICON_ID_TYPE_SANDROCK" },
            { RACE_WINGEDBEAST, "ICON_ID_TYPE_BIRD" },
            { RACE_PLANT, "ICON_ID_TYPE_BOTANICAL" },
            { RACE_INSECT, "ICON_ID_TYPE_INSECT" },
            { RACE_THUNDER, "ICON_ID_TYPE_THUNDER" },
            { RACE_DRAGON, "ICON_ID_TYPE_DRAGON" },
            { RACE_BEAST, "ICON_ID_TYPE_BEAST" },
            { RACE_BEASTWARRIOR, "ICON_ID_TYPE_BEASTBTL" },
            { RACE_DINOSAUR, "ICON_ID_TYPE_DINOSAURS" },
            { RACE_FISH, "ICON_ID_TYPE_FISH" },
            { RACE_SEASERPENT, "ICON_ID_TYPE_POSEIDON" },
            { RACE_REPTILE, "ICON_ID_TYPE_REPTILES" },
            { RACE_PSYCHIC, "ICON_ID_TYPE_PSYCHIC" },
            { RACE_DIVINE, "ICON_ID_TYPE_GOD" },
            { RACE_CREATORGOD, "ICON_ID_TYPE_CREATOR" },
            { RACE_WYRM, "ICON_ID_TYPE_MYSTDRAGON" },
            { RACE_CYBERSE, "ICON_ID_TYPE_CYVERSE" },
            { RACE_ILLUSION, "ICON_ID_TYPE_ILLUSION" },
        };

        public static string GetCardFrameName(uint type)
        {
            if ((type & TYPE_TOKEN) != 0) return "card_token";
            if ((type & TYPE_SPELL) != 0) return "card_mahou";
            if ((type & TYPE_TRAP) != 0) return "card_wana";
            bool isPend = (type & TYPE_PENDULUM) != 0;
            if ((type & TYPE_LINK) != 0) return "card_link";
            if ((type & TYPE_XYZ) != 0) return isPend ? "card_xyz_pendulum" : "card_xyz";
            if ((type & TYPE_SYNCHRO) != 0) return isPend ? "card_sync_pendulum" : "card_sync";
            if ((type & TYPE_FUSION) != 0) return isPend ? "card_fusion_pendulum" : "card_yugo";
            if ((type & TYPE_RITUAL) != 0) return "card_gisiki";
            if (isPend) return ((type & TYPE_EFFECT) != 0) ? "card_pendulum" : "card_pendulum_n";
            if ((type & TYPE_EFFECT) != 0) return "card_kouka";
            return "card_nomal";
        }

        public static string GetTypeTagKR(uint type)
        {
            if ((type & TYPE_SPELL) != 0)
            {
                if ((type & TYPE_QUICKPLAY) != 0) return "속공 마법";
                if ((type & TYPE_CONTINUOUS) != 0) return "지속 마법";
                if ((type & TYPE_EQUIP) != 0) return "장착 마법";
                if ((type & TYPE_FIELD) != 0) return "필드 마법";
                if ((type & TYPE_RITUAL) != 0) return "의식 마법";
                return "일반 마법";
            }
            if ((type & TYPE_TRAP) != 0)
            {
                if ((type & TYPE_CONTINUOUS) != 0) return "지속 함정";
                if ((type & TYPE_COUNTER) != 0) return "카운터 함정";
                return "일반 함정";
            }
            var parts = new List<string>(5);
            if ((type & TYPE_FUSION) != 0) parts.Add("융합");
            if ((type & TYPE_RITUAL) != 0) parts.Add("의식");
            if ((type & TYPE_SYNCHRO) != 0) parts.Add("싱크로");
            if ((type & TYPE_XYZ) != 0) parts.Add("엑시즈");
            if ((type & TYPE_PENDULUM) != 0) parts.Add("펜듈럼");
            if ((type & TYPE_LINK) != 0) parts.Add("링크");
            if ((type & TYPE_TUNER) != 0) parts.Add("튜너");
            if ((type & TYPE_FLIP) != 0) parts.Add("리버스");
            if ((type & TYPE_UNION) != 0) parts.Add("유니온");
            if ((type & TYPE_SPIRIT) != 0) parts.Add("스피릿");
            if ((type & TYPE_GEMINI) != 0) parts.Add("듀얼");
            if ((type & TYPE_TOON) != 0) parts.Add("툰");
            if ((type & TYPE_TOKEN) != 0)
            {
                if ((type & TYPE_EFFECT) != 0) parts.Add("효과");
                parts.Add("토큰");
            }
            else if ((type & TYPE_EFFECT) != 0) parts.Add("효과");
            else if ((type & TYPE_NORMAL) != 0) parts.Add("일반");
            if (parts.Count == 0) parts.Add("몬스터");
            return string.Join(" / ", parts);
        }

        public static string GetSpellTrapSubSpriteName(uint type)
        {
            if ((type & TYPE_SPELL) != 0)
            {
                if ((type & TYPE_QUICKPLAY) != 0) return "ICON_ID_ICON_SORT_MAGIC_QUICK_PLAY";
                if ((type & TYPE_CONTINUOUS) != 0) return "ICON_ID_ICON_SORT_MAGIC_CONTINUOUS";
                if ((type & TYPE_EQUIP) != 0) return "ICON_ID_ICON_SORT_MAGIC_EQUIP";
                if ((type & TYPE_FIELD) != 0) return "ICON_ID_ICON_SORT_MAGIC_FIELD";
                if ((type & TYPE_RITUAL) != 0) return "ICON_ID_ICON_SORT_MAGIC_RITUAL";
                return "ICON_ID_ICON_SORT_MAGIC_NORMAL";
            }
            if ((type & TYPE_TRAP) != 0)
            {
                if ((type & TYPE_CONTINUOUS) != 0) return "ICON_ID_ICON_SORT_TRAP_CONTINUOUS";
                if ((type & TYPE_COUNTER) != 0) return "ICON_ID_ICON_SORT_TRAP_COUNTER";
                return "ICON_ID_ICON_SORT_TRAP_NORMAL";
            }
            return "";
        }

        public static string GetPhaseNameKR(int phase)
        {
            switch (phase)
            {
                case PHASE_DRAW: return "드로우 페이즈";
                case PHASE_STANDBY: return "스탠바이 페이즈";
                case PHASE_MAIN1: return "메인 페이즈 1";
                case PHASE_BATTLE_START:
                case PHASE_BATTLE_STEP:
                case PHASE_DAMAGE:
                case PHASE_DAMAGE_CAL:
                case PHASE_BATTLE: return "배틀 페이즈";
                case PHASE_MAIN2: return "메인 페이즈 2";
                case PHASE_END: return "엔드 페이즈";
                default: return "";
            }
        }

        public static string GetLocationNameKR(uint loc)
        {
            switch (loc)
            {
                case LOCATION_DECK: return "덱";
                case LOCATION_HAND: return "패";
                case LOCATION_MZONE: return "몬스터 존";
                case LOCATION_SZONE: return "마법/함정 존";
                case LOCATION_GRAVE: return "묘지";
                case LOCATION_REMOVED: return "제외 존";
                case LOCATION_EXTRA: return "엑스트라 덱";
                case LOCATION_OVERLAY: return "엑시즈 소재";
                default: return "필드";
            }
        }
    }
}
