#!/usr/bin/env python3
"""
Builds all Unity runtime & streaming assets for YGO Portable Unity:
  1. Assets/StreamingAssets/cards_prop.bin      (sorted 64B PackedCard structs for ygoengine.dll)
  2. Assets/StreamingAssets/cardscripts.pack    (all CardScripts Lua packed into 1 fast binary)
  3. Assets/StreamingAssets/cards_kr.json       (unified Korean card DB + hint strings + ID maps)
  4. Assets/StreamingAssets/opponents_db.json   (modern Free Duel decks for LOTD and additional characters)
  5. Assets/StreamingAssets/cardart.idx + cardart_*.pack (<40MB chunks of 224x224 JPEG artwork)
  6. Assets/Resources/LOTD/... + Fonts/NotoSansKR.ttf    (sliced LOTD UI sprites, frames, Busts, TypeIcons)
  7. Assets/StreamingAssets/tokenart.pack      (token artwork and its index)
  8. Assets/StreamingAssets/deck_effect_categories.bin (deck editor effect filters)
"""

import io
import json
import pathlib
import shutil
import sqlite3
import struct
import sys
from collections import Counter, defaultdict
from PIL import Image
from modernize_opponents import modernize_opponents

UNITY_ROOT = pathlib.Path(r"c:\jeminFolder\자체개발\ygo-portable-unity")
YGO_PC = pathlib.Path(r"c:\jeminFolder\자체개발\YGO-PC-Full")
OPENDUEL = pathlib.Path(r"C:\Users\y\AppData\Local\YGO-PC-Full\OpenDuelEngine")
STRAWHAT_FONT = pathlib.Path(r"c:\jeminFolder\자체개발\strawhat-survivors-unity\Assets\Resources\Strawhat\Fonts\NotoSansKR.ttf")

STREAMING = UNITY_ROOT / "Assets" / "StreamingAssets"
RES_LOTD = UNITY_ROOT / "Assets" / "Resources" / "LOTD"
RES_FONTS = UNITY_ROOT / "Assets" / "Resources" / "Fonts"
LOTD_UI = UNITY_ROOT / "Assets" / "Textures" / "LOTD_UI"
TYPE_ICONS_SRC = UNITY_ROOT / "Assets" / "Textures" / "TypeIcons"
BUSTS_SRC = UNITY_ROOT / "Assets" / "Textures" / "Characters" / "Busts"
ORIG_YDC_DIR = UNITY_ROOT / "Assets" / "Data" / "Decks" / "Original"
META_DECKS_JSON = UNITY_ROOT / "Assets" / "Data" / "Decks" / "decks.json"
META_CHARS_JSON = UNITY_ROOT / "Assets" / "Data" / "Decks" / "characters.json"
OPPONENT_THEMES_JSON = pathlib.Path(__file__).with_name("opponent_themes.json")

SERIES_NAMES = {
    0: "DM",
    1: "GX",
    2: "5D's",
    3: "ZEXAL",
    4: "ARC-V",
    5: "VRAINS",
    6: "2026 메타",
}

KIND_TO_TYPE = {
    "Normal": 0x11,
    "Effect": 0x21,
    "EffectSpecialSummonOnly": 0x2000021,
    "FlipEffect": 0x200021,
    "TunerNormal": 0x1011,
    "TunerEffect": 0x1021,
    "TunerEffectSpecialSummonOnly": 0x2001021,
    "TunerFlipEffect": 0x1200021,
    "Gemini": 0x821,
    "Spirit": 0x221,
    "SpiritSpecialSummonOnly": 0x2000221,
    "Toon": 0x400021,
    "ToonSpecialSummonOnly": 0x2400021,
    "Union": 0x421,
    "RitualEffect": 0xA1,
    "FusionEffect": 0x61,
    "FusionTuner": 0x1061,
    "FusionPendulumEffect": 0x1000061,
    "SynchroEffect": 0x2021,
    "SynchroTunerEffect": 0x3021,
    "SynchroPendulumEffect": 0x1002021,
    "XyzEffect": 0x800021,
    "XyzPendulumEffect": 0x1800021,
    "Pendulum": 0x1000011,
    "PendulumEffect": 0x1000021,
    "PendulumEffectSpecialSummonOnly": 0x3000021,
    "PendulumFlipEffect": 0x1200021,
    "PendulumSpiritEffect": 0x1000221,
    "PendulumTunerEffect": 0x1001021,
    "LinkEffect": 0x4000021,
    "Spell": 0x2,
    "Trap": 0x4,
}
TYPE_EXTRA = 0x40 | 0x2000 | 0x800000 | 0x4000000  # Fusion, Synchro, Xyz, Link
TYPE_TOKEN = 0x4000

# Use the appropriate source card art for these tokens that have no dedicated LOTD illustration.
TOKEN_ART_FALLBACKS = {
    20001444: 20001443,  # Swordsoul Token -> Swordsoul of Mo Ye
    3285552: 3285551,   # Adventurer Token -> Rite of Aramesir
    41458362: 41458361,  # 기석 토큰 -> 아다마시아 루미나스
    3129528: 3129527,    # 희생하는 양 토큰 -> 잠자는 양 희생양
}

# CARD_Pass.bin leaves these original Egyptian God card passwords at zero.
LOTD_MISSING_PASSCODES = {
    4998: 10000000,   # Obelisk the Tormentor
    4999: 10000020,   # Slifer the Sky Dragon
    5000: 10000010,   # The Winged Dragon of Ra
    11927: 10000080,  # The Winged Dragon of Ra - Sphere Mode
    12234: 10000090,  # The Winged Dragon of Ra - Immortal Phoenix
}

MERCURIUM_PRE_RELEASE = 101303084
MERCURIUM = 22984000

# Spell/Trap subtype bitmasks matched against YGO-Ex cards.json "icon" field
ICON_TO_SUBTYPE = {
    "Normal": 0,
    "QuickPlay": 0x10000,   # TYPE_QUICKPLAY
    "Continuous": 0x20000,  # TYPE_CONTINUOUS
    "Equip": 0x40000,       # TYPE_EQUIP
    "Field": 0x80000,       # TYPE_FIELD
    "Ritual": 0x80,         # TYPE_RITUAL
    "Counter": 0x100000,    # TYPE_COUNTER
}

# Official EDOPro/OpenDuel setcodes for cards present in cards.json but absent from OpenDuelEngine/cards.cdb.
# Includes Konami archetype tags and "This card is always treated as ..." rule-clause setcodes.
MISSING_CARD_SETCODES = {
    # Destiny HERO (0xc008; matches HERO 0x8 and Destiny HERO 0xc008)
    25158975: (0xC008,),  # 데스티니 히어로 데스도그마 가이
    54641720: (0xC008,),  # 데스티니 히어로 드레드노트 가이
    62542673: (0xC008,),  # 데스티니 히어로 데블로드 가이
    98931282: (0xC008,),  # 데스티니 히어로 드레드노트 서번트
    # Adamancipator (0x140)
    24925387: (0x0140,),  # 아다마시아 컨덕터
    30068809: (0x0140,),  # 아다마시아 메이트
    41458361: (0x0140,),  # 아다마시아 루미나스
    51420096: (0x0140,),  # 아다마시아 크리스타－티아마이트
    89913287: (0x0140,),  # 아다마시아 라이즈－티아마이트
    # Ashtra (0x1db)
    1710647:  (0x01DB,),  # 역신의 아슈트라 요아
    2906939:  (0x01DB,),  # 아슈트라셴 삼천세계
    17020474: (0x01DB,),  # 아슈트라쟈 스사노오
    37839434: (0x01DB,),  # 아슈트라의 신역
    38114652: (0x01DB,),  # 봉신의 아슈트라 미쿠마리
    38331244: (0x01DB,),  # 금신의 아슈트라 아카스나
    43419178: (0x01DB,),  # 아슈트라야 미쿠마리노미코토
    64455720: (0x01DB,),  # 아슈트라의 충독
    65325949: (0x01DB,),  # 고신의 아슈트라 쿠즈하
    91444835: (0x01DB,),  # 아슈트라의 주포
    # Ars Magna (0x1dd) / Power Patron (0x1c6) / Theorealize (0x1d7) / Medius (0x1df)
    36270527: (0x01DD,),                         # 무한과 유한의 아르스＝마그나
    62368221: (0x01DD,),                         # 정화와 부패의 아르스＝마그나
    90728287: (0x01DD,),                         # 아르스＝마그나－「필로소피룸」
    99753860: (0x01DD,),                         # 결합과 괴리의 아르스＝마그나
    37279096: (0x01DD, 0x01C7, 0x01CB, 0x01D0),  # 아르스＝마그나－「키트리니타스」 (룰상 아르토메기아/둠즈/엘펜노츠)
    4063756:  (0x01C6,),                         # 환감옥신 메디쿠리우스
    90875418: (0x01D7, 0x01DF, 0x01C6),          # 테오리얼라이즈 메디우스 (룰상 감옥신)
    # Daedalus (0x1da)
    41570943: (0x01DA,),  # 창해룡신－네오다이달로스 레이지
    64233143: (0x01DA,),  # 다이달로스 메일스트롬
    64603351: (0x01DA,),  # 아틀란티스의 룡신－다이달로스
    78579058: (0x01DA,),  # 진해룡기－다이달로스
    # Umbral Horror (0x87) / Number (0x48/0x1048) / Masquerade (0x1e0) / Chaos (0xcf) / CXyz (0x1073) / Barian (0x178/0x1178) / Seventh (0x177)
    2932235:  (0x0087,),                         # 사우전드 엄브럴
    3064783:  (0x0087,),                         # 샤이닝 엄브럴
    39053882: (0x0087,),                         # V엄브럴
    76548531: (0x0087,),                         # 고르고닉 엄브럴
    16908882: (0x0048, 0x01E0),                  # No.104 마스커레이드 매지션 샤이닝V
    42396591: (0x1048, 0x0087, 0x01E0, 0x00CF),  # CNo.104 마스커레이드 매지션 엄브럴 빅토리
    79791695: (0x1073, 0x00CF, 0x0178),          # CX 기망황룡 카오스 바리언 드래곤
    39513225: (0x0177, 0x1178),                  # 세븐스 바리언즈
    38001022: (0x1178,),                         # 바리언즈 씰
    75518330: (0x1178,),                         # 바리언즈 호프
    # Red-Eyes / Archfiend / Invoked / Blitzclique / Sacred Beast / Warrior / Jinzo / Magician
    17242022: (0x003B,),  # 붉은 눈의 초월흑룡
    97818130: (0x0045,),  # 결속의 악마룡 블랙 데몬즈 드래곤
    80635735: (0x00F4,),  # 소환수 데 아니마
    30945251: (0x01D6,),  # 블리츠크리크－오버볼티지
    3441553:  (0x1145,),  # 삼환마의 천괴
    77482666: (0x2066,),  # 질풍의 표범전사 팬서 워리어
    40275470: (0x00BC,),  # 인조인간 －사이코 에너지 쇼커－
    40235813: (0x0098,),  # 시간의 흑마술사
}

JSON_TYPE_TO_RACE = {
    "Warrior": 0x1, "Spellcaster": 0x2, "Fairy": 0x4, "Fiend": 0x8,
    "Zombie": 0x10, "Machine": 0x20, "Aqua": 0x40, "Pyro": 0x80,
    "Rock": 0x100, "WingedBeast": 0x200, "Plant": 0x400, "Insect": 0x800,
    "Thunder": 0x1000, "Dragon": 0x2000, "Beast": 0x4000, "BeastWarrior": 0x8000,
    "Dinosaur": 0x10000, "Fish": 0x20000, "SeaSerpent": 0x40000, "Reptile": 0x80000,
    "Psychic": 0x100000, "Divine": 0x200000, "CreatorGod": 0x400000,
    "Wyrm": 0x800000, "Cyberse": 0x1000000, "Illusion": 0x2000000,
}

JSON_ATTR_MAP = {
    "Earth": 0x1, "Water": 0x2, "Fire": 0x4, "Wind": 0x8,
    "Light": 0x10, "Dark": 0x20, "Divine": 0x40,
}

LINK_MARKER_MAP = {
    "Bottom-Left": 0x001, "Bottom": 0x002, "Bottom-Right": 0x004,
    "Left": 0x008, "Right": 0x020,
    "Top-Left": 0x040, "Top": 0x080, "Top-Right": 0x100,
}


def parse_stat(val) -> int:
    if val is None or val == "":
        return 0
    if isinstance(val, int):
        return val
    s = str(val).strip()
    if s == "?":
        return -2
    try:
        return int(s)
    except ValueError:
        return 0


def parse_link_markers(val) -> int:
    if isinstance(val, int):
        return val
    if isinstance(val, list):
        mask = 0
        for m in val:
            mask |= LINK_MARKER_MAP.get(str(m), 0)
        return mask
    return 0


def parse_toc(toc_path: pathlib.Path):
    entries = {}
    pos = 0
    for line in toc_path.read_text(encoding="utf-8", errors="ignore").splitlines()[1:]:
        parts = line.strip().split(None, 2)
        if len(parts) == 3:
            sz = int(parts[0], 16)
            entries[parts[2].replace("\\", "/")] = (pos, sz)
            pos += (sz + 3) & ~3
    return entries


def read_utf16(buf: bytes, off: int) -> str:
    if off <= 0 or off >= len(buf):
        return ""
    end = off
    while end + 1 < len(buf):
        if buf[end] == 0 and buf[end + 1] == 0:
            break
        end += 2
    return buf[off:end].decode("utf-16-le", errors="ignore")


def read_ascii(buf: bytes, off: int) -> str:
    if off <= 0 or off >= len(buf):
        return ""
    end = buf.find(b"\x00", off)
    if end == -1:
        end = len(buf)
    return buf[off:end].decode("ascii", errors="ignore")


def build_cards():
    print("[1/5] Building cards_prop.bin + cards_kr.json ...")
    STREAMING.mkdir(parents=True, exist_ok=True)

    # Read YGO_2020.dat CARD_Prop & CARD_Pass + English fallback texts
    main_toc = parse_toc(YGO_PC / "YGO_2020.toc")
    ex_dir = YGO_PC / "Mods" / "2026-11-KR-Cards"
    ex_toc = parse_toc(ex_dir / "YGO_2020-Ex.toc")

    with open(YGO_PC / "YGO_2020.dat", "rb") as f:
        def r_main(k):
            o, s = main_toc[k]
            f.seek(o)
            return f.read(s)
        card_prop = r_main("bin/CARD_Prop.bin")
        card_pass = r_main("bin/CARD_Pass.bin")
        en_indx = r_main("bin/CARD_Indx_E.bin")
        en_name = r_main("bin/CARD_Name_E.bin")
        en_desc = r_main("bin/CARD_Desc_E.bin")

    with open(ex_dir / "YGO_2020-Ex.dat", "rb") as f:
        def r_ex(k):
            o, s = ex_toc[k]
            f.seek(o)
            return f.read(s)
        kr_indx = r_ex("bin/CARD_Indx_E.bin")
        kr_name = r_ex("bin/CARD_Name_E.bin")
        kr_desc = r_ex("bin/CARD_Desc_E.bin")

    lotd_to_pass = {}
    pass_to_lotd = {}
    stock_kr_by_pass = {}
    n_stock = len(card_prop) // 8
    for i in range(1, n_stock):
        first, _second = struct.unpack_from("<II", card_prop, 8 * i)
        lotd_id = first & 0x3FFF
        passcode = struct.unpack_from("<I", card_pass, 4 * i)[0]
        passcode = passcode or LOTD_MISSING_PASSCODES.get(lotd_id, 0)
        if lotd_id:
            lotd_to_pass[lotd_id] = passcode
        if passcode:
            pass_to_lotd.setdefault(passcode, lotd_id)
            n_off, d_off = struct.unpack_from("<II", kr_indx, 8 * i)
            nm = read_utf16(kr_name, n_off).strip()
            ds = read_utf16(kr_desc, d_off).strip()
            if not nm:
                no2, do2 = struct.unpack_from("<II", en_indx, 8 * i)
                nm = read_utf16(en_name, no2).strip()
                ds = read_utf16(en_desc, do2).strip()
            if nm:
                stock_kr_by_pass[passcode] = (nm, ds)

    # Read Modern 2026 KR cards.json
    jcards_raw = json.loads((ex_dir / "Yu-Gi-Oh-Ex" / "cards.json").read_text(encoding="utf-8"))["cards"]
    mod_by_pass = {}
    lotd_to_konami = {}
    pass_to_konami = {}
    for c in jcards_raw:
        lid = int(c.get("id") or 0)
        kid = int(c.get("konamiId") or 0)
        pw = int(c.get("password") or 0)
        if lid and pw:
            lotd_to_pass[lid] = pw
            pass_to_lotd.setdefault(pw, lid)
        if lid and kid:
            lotd_to_konami[lid] = kid
        if pw and kid:
            pass_to_konami[pw] = kid
        if pw:
            mod_by_pass[pw] = c

    # Read OpenDuelEngine/cards.cdb
    db = sqlite3.connect(str(OPENDUEL / "cards.cdb"))
    cdb_datas = {}
    for row in db.execute("SELECT id, ot, alias, setcode, type, atk, def, level, race, attribute, category FROM datas"):
        code, _ot, alias, setcode, ctype, atk, df, lvl, race, attr, _cat = row
        link_marker = 0
        defense = df
        if ctype & 0x4000000:  # TYPE_LINK
            link_marker = df & 0xFFFFFFFF
            defense = 0
        lscale = (lvl >> 24) & 0xFF
        rscale = (lvl >> 16) & 0xFF
        level_val = lvl & 0xFF
        scs = []
        for s in range(4):
            sc = (setcode >> (16 * s)) & 0xFFFF
            if sc:
                scs.append(sc)
        while len(scs) < 8:
            scs.append(0)
        cdb_datas[code] = {
            "code": code,
            "alias": alias or 0,
            "type": ctype or 0,
            "level": level_val,
            "attribute": attr or 0,
            "link_marker": link_marker,
            "race": race or 0,
            "attack": atk if atk is not None else 0,
            "defense": defense if defense is not None else 0,
            "lscale": lscale,
            "rscale": rscale,
            "setcodes": scs[:8],
        }

    cdb_texts = {}
    for row in db.execute("SELECT id, name, desc, str1, str2, str3, str4, str5, str6, str7, str8, str9, str10, str11, str12, str13, str14, str15, str16 FROM texts"):
        cid = row[0]
        strs = [s or "" for s in row[3:19]]
        last_nz = -1
        for idx, s in enumerate(strs):
            if s:
                last_nz = idx
        strs = strs[:last_nz + 1] if last_nz >= 0 else []
        cdb_texts[cid] = (row[1] or "", row[2] or "", strs)

    # These effects create tokens missing from the installed card database.
    generated_tokens = (
        (41458361, 41458362, "기석 토큰", "아다마시아 루미나스", "Rock", "Light"),
        (3129527, 3129528, "희생하는 양 토큰", "잠자는 양 희생양", "Beast", "Earth"),
    )
    for source, code, name, generator, race, attribute in generated_tokens:
        if source not in cdb_datas and source not in mod_by_pass:
            continue
        cdb_datas.setdefault(code, {
            "code": code,
            "alias": 0,
            "type": 0x4011,  # Monster | Normal | Token
            "level": 1,
            "attribute": JSON_ATTR_MAP[attribute],
            "link_marker": 0,
            "race": JSON_TYPE_TO_RACE[race],
            "attack": 0,
            "defense": 0,
            "lscale": 0,
            "rscale": 0,
            "setcodes": [0] * 8,
        })
        stock_kr_by_pass.setdefault(code, (name, f'"{generator}"의 효과로 특수 소환되는 토큰.'))

    # Synthesize any cards in mod_by_pass missing from cards.cdb
    all_codes = sorted(set(cdb_datas.keys()) | set(mod_by_pass.keys()))
    packed_bytes = bytearray()
    packed_bytes += struct.pack("<4sII", b"YGOP", 1, len(all_codes))

    kr_cards_list = []
    for code in all_codes:
        if code in cdb_datas:
            d = cdb_datas[code]
        else:
            mc = mod_by_pass[code]
            kind = mc.get("kind", "Effect")
            ctype = KIND_TO_TYPE.get(kind, 0x21)
            if ctype in (0x2, 0x4):
                ctype |= ICON_TO_SUBTYPE.get(mc.get("icon") or "Normal", 0)
            atk = parse_stat(mc.get("atk"))
            df = parse_stat(mc.get("def"))
            lvl = parse_stat(mc.get("level"))
            sc = parse_stat(mc.get("scale"))
            lm = parse_link_markers(mc.get("linkmarkers")) if (ctype & 0x4000000) else 0
            if ctype & 0x4000000:
                df = 0
            race = JSON_TYPE_TO_RACE.get(mc.get("type", ""), 0)
            attr = JSON_ATTR_MAP.get(mc.get("attribute", ""), 0)
            sn = mc.get("sameName")
            alias = lotd_to_pass.get(int(sn["card"]), 0) if (isinstance(sn, dict) and sn.get("always") and sn.get("card")) else 0
            scs = list(MISSING_CARD_SETCODES.get(code, ()))
            while len(scs) < 8:
                scs.append(0)
            d = {
                "code": code,
                "alias": alias,
                "type": ctype,
                "level": lvl,
                "attribute": attr,
                "link_marker": lm,
                "race": race,
                "attack": atk,
                "defense": df,
                "lscale": sc,
                "rscale": sc,
                "setcodes": scs[:8],
            }

        if code == MERCURIUM_PRE_RELEASE and MERCURIUM in cdb_datas:
            d["alias"] = MERCURIUM

        packed_bytes += struct.pack(
            "<IIIIIIQiiII8H",
            d["code"], d["alias"], d["type"], d["level"], d["attribute"], d["link_marker"],
            d["race"], d["attack"], d["defense"], d["lscale"], d["rscale"], *d["setcodes"]
        )

        # Build KR text entry: priority mod_by_pass -> stock_kr_by_pass -> cdb_texts
        nm, ds = "", ""
        if code in mod_by_pass:
            nm = mod_by_pass[code].get("name", "") or ""
            ds = mod_by_pass[code].get("description", "") or ""
        if not nm and code in stock_kr_by_pass:
            nm, ds = stock_kr_by_pass[code]
        cdb_t = cdb_texts.get(code)
        if not nm and cdb_t:
            nm, ds = cdb_t[0], cdb_t[1]
        elif not ds and cdb_t:
            ds = cdb_t[1]
        strs = cdb_t[2] if cdb_t else []

        entry = {
            "code": d["code"],
            "alias": d["alias"],
            "lotdId": pass_to_lotd.get(code, 0),
            "konamiId": pass_to_konami.get(code, 0),
            "name": nm,
            "desc": ds.replace("\r\n", "\n"),
            "type": d["type"],
            "atk": d["attack"],
            "def": d["defense"],
            "level": d["level"],
            "race": d["race"],
            "attr": d["attribute"],
            "lscale": d["lscale"],
            "rscale": d["rscale"],
            "link": d["link_marker"],
        }
        if strs:
            entry["strs"] = strs
        kr_cards_list.append(entry)

    (STREAMING / "cards_prop.bin").write_bytes(packed_bytes)
    kr_json_text = json.dumps({"cards": kr_cards_list}, ensure_ascii=False, separators=(",", ":"))
    (STREAMING / "cards_kr.json").write_text(kr_json_text, encoding="utf-8")
    build_deck_effect_categories(all_codes)
    print(f"  cards_prop.bin: {len(all_codes)} cards ({len(packed_bytes)/1024:.1f} KB), cards_kr.json: {len(kr_json_text)/1024/1024:.2f} MB")
    return set(all_codes), lotd_to_pass, mod_by_pass, pass_to_konami, pass_to_lotd, kr_cards_list


def build_deck_effect_categories(all_codes):
    # Output bits follow DeckCardFilter.EffectOptions. Input bit numbers differ
    # between the original LOTD genre table and EDOPro's datas.category field.
    lotd_bits = (
        (10, 11), (14,), (37,), (15,), (2,), (5,), (3,), (30,), (4,),
        (1,), (0,), (8,), (7,), (9,), (6,), (13,), (12,), (31,),
        (29,), (16,), (17,), (18,),
    )
    edopro_bits = (
        (0, 1), (7,), (4,), (5, 6), (8,), (9,), (20,), (22,), (16, 17),
        (18,), (19,), (12,), (10,), (30,), (), (2,), (3,), (28,),
        (23,), (13,), (14,), (15,),
    )

    def remap(value, source_bits):
        return sum(1 << index for index, bits in enumerate(source_bits)
                   if any(value & (1 << bit) for bit in bits))

    toc = parse_toc(YGO_PC / "YGO_2020.toc")
    with (YGO_PC / "YGO_2020.dat").open("rb") as archive:
        def read(member):
            offset, length = toc[member]
            archive.seek(offset)
            return archive.read(length)

        passcodes = list(struct.iter_unpack("<I", read("bin/CARD_Pass.bin")))
        genres = list(struct.iter_unpack("<Q", read("bin/CARD_Genre.bin")))
    if len(passcodes) != len(genres):
        raise ValueError("LOTD genre table does not match its passcode table.")
    legacy = {code: remap(mask, lotd_bits) for (code,), (mask,) in zip(passcodes, genres) if code}

    db_path = (OPENDUEL / "cards.cdb").resolve().as_uri()
    with sqlite3.connect(f"{db_path}?mode=ro", uri=True) as db:
        modern = {code: remap(category or 0, edopro_bits)
                  for code, category in db.execute("SELECT id, category FROM datas")}

    records = []
    for code in sorted(all_codes):
        mask = legacy.get(code, modern.get(code, 0))
        if mask:
            records.append(struct.pack("<II", code, mask))
    path = STREAMING / "deck_effect_categories.bin"
    path.write_bytes(struct.pack("<4sII", b"YGEC", 1, len(records)) + b"".join(records))
    print(f"  deck_effect_categories.bin: {len(records)} cards with searchable effect tags")


def build_script_pack(all_codes: set):
    print("[2/5] Building cardscripts.pack ...")
    cs_root = OPENDUEL / "CardScripts"
    scripts = {}  # lower_name -> bytes

    def add_file(p: pathlib.Path):
        key = p.name.lower()
        if key not in scripts:
            scripts[key] = p.read_bytes()

    for p in sorted(cs_root.glob("*.lua")):
        add_file(p)
    for p in sorted((cs_root / "official").glob("*.lua")):
        add_file(p)
    for p in sorted((cs_root / "pre-release").glob("*.lua")):
        add_file(p)
    for p in sorted((cs_root / "unofficial").glob("*.lua")):
        stem = p.stem
        if stem.startswith("c") and stem[1:].isdigit() and int(stem[1:]) in all_codes:
            add_file(p)

    # Match the LOTD prize-card IDs to the corresponding Project Ignis scripts.
    fallback_scripts = {
        662853: "unofficial/c501000085.lua",  # Sanctity of Dragon
        662854: "unofficial/c501000086.lua",  # Noritoshi, in Darkest Rainment
        662855: "unofficial/c501000087.lua",  # Amatsu, Okami of the Divine Peaks
        662857: "unofficial/c501000084.lua",  # Iron Knight of Revolution
        88264978: "pre-errata/c88264978.lua",  # Red-Eyes Darkness Metal Dragon (OCG errata)
    }
    for code, source in fallback_scripts.items():
        name = f"c{code}.lua"
        if code in all_codes and name not in scripts:
            scripts[name] = (cs_root / source).read_bytes()

    # Saved decks can still contain Mercurium's pre-release ID. Keep both
    # IDs on the same once-per-turn counter and effect descriptions.
    if MERCURIUM_PRE_RELEASE in all_codes:
        name = f"c{MERCURIUM_PRE_RELEASE}.lua"
        if name not in scripts:
            script = scripts[f"c{MERCURIUM}.lua"]
            scripts[name] = script.replace(b"local s,id=GetID()", f"local s=GetID()\nlocal id={MERCURIUM}".encode(), 1)

    if 40975243 in all_codes:
        scripts["c40975243.lua"] = (UNITY_ROOT / "Tools" / "ScriptOverrides" / "c40975243.lua").read_bytes()

    # Master Duel automatically makes blind random selections. These effects reveal and
    # shuffle the candidates before choosing one, and their card text explicitly says random.
    random_opponent_groups = {
        14733538: "sg1",   # Draco Face-Off
        21888494: "sg1",   # Chosen One
        23270035: "g",     # Monster Assortment
        23898021: "sg",    # Lilith, Lady of Lament
        32360466: "sg",    # Beginning of Heaven and Earth
        40230018: "revg",  # Spellbook Library of the Crescent
        50756327: "sg1",   # Crowley, the First Propheseer
        58753372: "cg",    # Super Quantal Fairy Alphan
        64280356: "sg",    # Kozmo Tincan
        70508653: "g",     # Tool Box
        72813401: "sg",    # Hi-Speedroid Rubber Band Shooter
        93437091: "sg",    # Bingo Machine, Go!!!
    }
    for code, group in random_opponent_groups.items():
        if code not in all_codes:
            continue
        name = f"c{code}.lua"
        select = f"{group}:Select(1-tp,1,1,nil)".encode("ascii")
        random_select = f"{group}:RandomSelect(1-tp,1)".encode("ascii")
        script = scripts[name]
        if script.count(select) != 1:
            if random_select in script and select not in script:
                continue
            raise ValueError(f"Review the random selection override for {name}: expected one matching call")
        lines = script.splitlines(keepends=True)
        index = next(i for i, line in enumerate(lines) if select in line)
        lines[index] = lines[index].replace(select, random_select)
        if index > 0 and lines[index - 1].lstrip().startswith(b"Duel.Hint(HINT_SELECTMSG,"):
            del lines[index - 1]
        scripts[name] = b"".join(lines)

    # Possessed Destiny's summon check mistakenly uses Duel.SpecialSummon's
    # argument order. Its destination player defaults to the summoning player.
    if "c45508030.lua" in scripts:
        scripts["c45508030.lua"] = scripts["c45508030.lua"].replace(
            b"c:IsCanBeSpecialSummoned(e,0,tp,tp,false,false,",
            b"c:IsCanBeSpecialSummoned(e,0,tp,false,false,",
        )

    # Rainbow Dark Dragon cannot summon without seven names and room after banishing.
    # Check both requirements before searching for a valid group of materials.
    rainbow = scripts.get("c79407975.lua")
    if rainbow is not None:
        search = b"\treturn aux.SelectUnselectGroup(rg,e,tp,7,7,s.rescon,0)"
        guard = b"\tif rg:GetClassCount(Card.GetCode)<7 or Duel.GetMZoneCount(tp,rg)<1 then return false end"
        if guard not in rainbow:
            newline = b"\r\n" if b"\r\n" in rainbow else b"\n"
            scripts["c79407975.lua"] = rainbow.replace(search, guard + newline + search, 1)

    # Pack binary: YGOS + ver(1) + count + entries(16B each) + name_table + data_blob
    items = sorted(scripts.items(), key=lambda kv: kv[0])
    count = len(items)
    header_size = 12 + count * 16
    name_blob = bytearray()
    name_meta = []
    for key, _data in items:
        kb = key.encode("utf-8")
        off = header_size + len(name_blob)
        name_blob += kb
        name_meta.append((off, len(kb)))

    data_base = header_size + len(name_blob)
    data_blob = bytearray()
    dir_bytes = bytearray()
    for (key, data), (noff, nlen) in zip(items, name_meta):
        doff = data_base + len(data_blob)
        data_blob += data
        dir_bytes += struct.pack("<IIII", noff, nlen, doff, len(data))

    out = struct.pack("<4sII", b"YGOS", 1, count) + dir_bytes + name_blob + data_blob
    (STREAMING / "cardscripts.pack").write_bytes(out)
    print(f"  cardscripts.pack: {count} scripts ({len(out)/1024/1024:.2f} MB)")


def compress_card_art(raw_bytes: bytes) -> bytes:
    im = Image.open(io.BytesIO(raw_bytes)).convert("RGB")
    if im.size != (224, 224):
        im = im.resize((224, 224), Image.Resampling.LANCZOS)
    buf = io.BytesIO()
    im.save(buf, format="JPEG", quality=76, optimize=True)
    return buf.getvalue()


def iter_lotd_art_locations(archive, toc):
    for member in ("2020.full.illust_j.jpg.zib", "2020.full.illust_a.jpg.zib"):
        base, _ = toc[member]
        archive.seek(base)
        first_offset, _, _ = struct.unpack(">II56s", archive.read(64))
        count = ((first_offset & ~15) - 16) // 64
        archive.seek(base)
        header = archive.read(count * 64)
        for offset, size, name in struct.iter_unpack(">II56s", header):
            stem = name.rstrip(b"\x00").rsplit(b".", 1)[0]
            if stem.isdigit():
                yield int(stem), base + (offset & ~15), size


def build_card_art(all_codes: set, lotd_to_pass: dict, mod_by_pass: dict):
    print("[3/5] Building chunked cardart_N.pack + cardart.idx (224x224 JPEG Q=76) ...")
    # Remove old chunks if re-running
    for old in STREAMING.glob("cardart*"):
        old.unlink()

    art_dir = YGO_PC / "Mods" / "2026-11-KR-Cards" / "Yu-Gi-Oh-Ex" / "art"
    main_toc = parse_toc(YGO_PC / "YGO_2020.toc")

    # Map passcode -> compressed 224x224 JPEG bytes
    pass_to_jpg = {}

    # Priority 1: 2026 KR mod art (by konamiId -> password)
    for pw, mc in mod_by_pass.items():
        kid = int(mc.get("konamiId") or 0)
        p = art_dir / f"{kid}.jpg"
        if not p.exists():
            img_rel = mc.get("image", "")
            if img_rel:
                p = YGO_PC / "Mods" / "2026-11-KR-Cards" / "Yu-Gi-Oh-Ex" / img_rel
        if p.exists():
            pass_to_jpg[pw] = compress_card_art(p.read_bytes())

    # Priority 2: 2020.full.illust_j.jpg.zib then 2020.full.illust_a.jpg.zib
    with open(YGO_PC / "YGO_2020.dat", "rb") as f:
        for lid, offset, size in iter_lotd_art_locations(f, main_toc):
            pw = lotd_to_pass.get(lid, 0)
            if pw and pw not in pass_to_jpg:
                f.seek(offset)
                try:
                    pass_to_jpg[pw] = compress_card_art(f.read(size))
                except (OSError, ValueError):
                    pass

    MAX_CHUNK = 40 * 1024 * 1024  # 40 MB (<50MB GitHub limit!)
    chunk_id = 0
    cur_chunk = bytearray()
    entries = []
    # Append recovered artwork so existing chunks keep their byte offsets.
    recovered = set(LOTD_MISSING_PASSCODES.values())
    for pw in sorted(pass_to_jpg, key=lambda code: (code in recovered, code)):
        jpg = pass_to_jpg[pw]
        if len(cur_chunk) + len(jpg) > MAX_CHUNK and len(cur_chunk) > 0:
            (STREAMING / f"cardart_{chunk_id}.pack").write_bytes(cur_chunk)
            chunk_id += 1
            cur_chunk = bytearray()
        entries.append((pw, chunk_id, len(cur_chunk), len(jpg)))
        cur_chunk += jpg

    if len(cur_chunk) > 0:
        (STREAMING / f"cardart_{chunk_id}.pack").write_bytes(cur_chunk)

    # Index file: YGOI + ver(1) + count + [passcode(u32), packId(u32), offset(u32), length(u32)]
    idx_bytes = bytearray(struct.pack("<4sII", b"YGOI", 1, len(entries)))
    for pw, pid, off, ln in entries:
        idx_bytes += struct.pack("<IIII", pw, pid, off, ln)
    (STREAMING / "cardart.idx").write_bytes(idx_bytes)
    total_mb = sum(p.stat().st_size for p in STREAMING.glob("cardart*.pack")) / 1024 / 1024
    print(f"  cardart: {len(entries)} images in {chunk_id+1} chunks ({total_mb:.2f} MB total)")


def build_token_art(cards):
    token_codes = defaultdict(list)
    for card in cards:
        name = card["name"].strip().casefold()
        if card["type"] & TYPE_TOKEN and name.endswith(" token"):
            token_codes[name].append(card["code"])

    toc = parse_toc(YGO_PC / "YGO_2020.toc")
    token_art = {}
    with (YGO_PC / "YGO_2020.dat").open("rb") as archive:
        def read_entry(name):
            offset, size = toc[f"bin/{name}.bin"]
            archive.seek(offset)
            return archive.read(size)

        props = read_entry("CARD_Prop")
        passwords = read_entry("CARD_Pass")
        offsets = read_entry("CARD_Indx_E")
        english_names = read_entry("CARD_Name_E")
        lotd_ids_by_name = defaultdict(list)
        for i in range(1, len(props) // 8):
            if struct.unpack_from("<I", passwords, i * 4)[0] != 0:
                continue
            name_offset = struct.unpack_from("<I", offsets, i * 8)[0]
            name = read_utf16(english_names, name_offset).strip().casefold()
            if name in token_codes:
                lotd_id = struct.unpack_from("<I", props, i * 8)[0] & 0x3FFF
                lotd_ids_by_name[name].append(lotd_id)

        wanted = {lid for ids in lotd_ids_by_name.values() for lid in ids}
        art_by_lotd_id = {}
        for lid, offset, size in iter_lotd_art_locations(archive, toc):
            if lid in wanted:
                archive.seek(offset)
                art_by_lotd_id[lid] = compress_card_art(archive.read(size))
                wanted.remove(lid)

        for name, ids in lotd_ids_by_name.items():
            illustrations = [art_by_lotd_id[lid] for lid in ids if lid in art_by_lotd_id]
            codes = sorted(token_codes[name])
            if not illustrations:
                continue
            # Keep separate variants where LOTD and the engine provide the same number of copies.
            if len(illustrations) == len(codes):
                token_art.update(zip(codes, illustrations))
            else:
                token_art.update(dict.fromkeys(codes, illustrations[0]))

    original_count = len(token_art)
    ordinary_art = {
        code: (pack_id, offset, size)
        for code, pack_id, offset, size in struct.iter_unpack("<IIII", (STREAMING / "cardart.idx").read_bytes()[12:])
    }
    valid_tokens = {card["code"] for card in cards if card["type"] & TYPE_TOKEN}
    for code, source in TOKEN_ART_FALLBACKS.items():
        if code in valid_tokens and code not in token_art and source in ordinary_art:
            pack_id, offset, size = ordinary_art[source]
            with (STREAMING / f"cardart_{pack_id}.pack").open("rb") as art:
                art.seek(offset)
                token_art[code] = art.read(size)

    # The index shares the existing record layout. Pack ID -1 points to this file's embedded images.
    count = len(token_art)
    directory = bytearray(struct.pack("<4sII", b"YGOI", 1, count))
    images = bytearray()
    first_image = 12 + count * 16
    positions = {}
    for code, image in sorted(token_art.items()):
        if image not in positions:
            positions[image] = first_image + len(images)
            images += image
        directory += struct.pack("<IiII", code, -1, positions[image], len(image))
    (STREAMING / "tokenart.pack").write_bytes(directory + images)
    print(f"  tokenart: {original_count} token codes with LOTD artwork, {count-original_count} with source card artwork")


def resolve_portrait_name(key: str, bust_map: dict) -> str:
    if not key:
        return "atem_neutral"
    lk = key.lower().replace(".png", "")
    candidates = [
        lk,
        f"{lk}_neutral",
        lk.replace("_neutral", "") + "_neutral",
    ]
    for c in candidates:
        if c in bust_map:
            return bust_map[c]
    return "atem_neutral"


def load_lotd_passcodes():
    toc = parse_toc(YGO_PC / "YGO_2020.toc")
    with (YGO_PC / "YGO_2020.dat").open("rb") as archive:
        chunks = []
        for entry in ("bin/CARD_Prop.bin", "bin/CARD_Pass.bin"):
            offset, size = toc[entry]
            archive.seek(offset)
            chunks.append(archive.read(size))
    props, codes = chunks
    mapping = {}
    for index in range(1, len(props) // 8):
        lid = struct.unpack_from("<I", props, index * 8)[0] & 0x3FFF
        if lid:
            code = struct.unpack_from("<I", codes, index * 4)[0]
            mapping[lid] = code or LOTD_MISSING_PASSCODES.get(lid, 0)
    mod = YGO_PC / "Mods" / "2026-11-KR-Cards" / "Yu-Gi-Oh-Ex" / "cards.json"
    for card in json.loads(mod.read_text(encoding="utf-8"))["cards"]:
        lid = int(card.get("id") or 0)
        code = int(card.get("password") or 0)
        if lid and code:
            mapping[lid] = code
    return mapping


def deck_signature(deck):
    return tuple(tuple(sorted(deck[section])) for section in ("main", "extra"))


def playable_deck(deck, extra_deck_codes):
    return (40 <= len(deck["main"]) <= 60 and len(deck["extra"]) <= 15 and len(deck["side"]) <= 15
            and not (set(deck["main"]) & extra_deck_codes) and set(deck["extra"]) <= extra_deck_codes)


def apply_opponent_themes(characters, decks, cards, extra_deck_codes, bust_map):
    catalog = json.loads(OPPONENT_THEMES_JSON.read_text(encoding="utf-8"))
    char_by_id = {character["id"]: character for character in characters}
    for raw_id, name in catalog["names"].items():
        character = char_by_id[int(raw_id)]
        if character["name"] != name:
            character["aliases"] = [character["name"]]
            character["name"] = name
    for raw_id, bio in catalog.get("bios", {}).items():
        char_by_id[int(raw_id)]["bio"] = bio

    for profile in catalog["characters"]:
        cid = profile["id"]
        if cid in char_by_id:
            raise ValueError(f"Duplicate opponent character id {cid}")
        series = profile["seriesIdx"]
        portrait = profile.get("portrait", "")
        portrait_card = profile.get("portraitCard", 0)
        if series not in range(6) or (portrait and portrait.lower() not in bust_map):
            raise ValueError(f"Bad series or portrait for {profile['name']}")
        if not portrait and portrait_card not in cards:
            raise ValueError(f"Missing portrait for {profile['name']}")
        character = {**profile, "seriesName": SERIES_NAMES[series], "isMeta2026": False}
        characters.append(character)
        char_by_id[cid] = character

    by_id = {deck["id"]: deck for deck in decks}
    if len(by_id) != len(decks):
        raise ValueError("Duplicate original opponent deck IDs")
    for raw_id, label in catalog["deckLabels"].items():
        deck = by_id[int(raw_id)]
        deck.update(label)
        if deck["sigCard"] not in deck["main"] + deck["extra"]:
            raise ValueError(f"Missing ace for {deck['title']}")
    signatures = defaultdict(set)
    for deck in decks:
        signatures[deck["charId"]].add(deck_signature(deck))

    for recipe in catalog["decks"]:
        cid = recipe["charId"]
        character = char_by_id[cid]
        did = recipe["id"]
        if did in by_id:
            raise ValueError(f"Duplicate opponent deck id {did}")
        if "copy" in recipe:
            original = by_id[recipe["copy"]]
            main, extra, side = (original[section] for section in ("main", "extra", "side"))
            signature = recipe.get("sigCard", original["sigCard"])
        else:
            main, extra, side = ([code for code, count in recipe.get(section, []) for _ in range(count)]
                                 for section in ("main", "extra", "side"))
            signature = recipe["sigCard"]
        deck = {
            "id": did, "charId": cid, "seriesIdx": character["seriesIdx"],
            "title": recipe["title"], "subtitle": recipe.get("subtitle", "캐릭터 테마"),
            "sigCard": signature, "isMeta2026": character["isMeta2026"], "featured": True,
            "main": main, "extra": extra, "side": side,
        }
        in_deck = main + extra + side
        if not set(in_deck) <= cards.keys() or signature not in main + extra or not playable_deck(deck, extra_deck_codes):
            raise ValueError(f"Invalid opponent deck {did} ({deck['title']})")
        if "copy" not in recipe:
            copies = Counter(cards[code].get("alias") or code for code in in_deck)
            if any(count > 3 for count in copies.values()):
                raise ValueError(f"Too many copies of a card in {deck['title']}")
        key = deck_signature(deck)
        if key in signatures[cid]:
            raise ValueError(f"Duplicate deck for {character['name']}: {deck['title']}")
        signatures[cid].add(key)
        decks.append(deck)
        by_id[did] = deck

    if any(profile["id"] not in signatures for profile in catalog["characters"]):
        raise ValueError("An added opponent has no decks")
    active = [c for c in characters if c["id"] in signatures]
    incomplete = [(c["id"], c["name"]) for c in active if len(signatures[c["id"]]) < 2]
    if incomplete:
        raise ValueError(f"Opponents with fewer than two distinct decks: {incomplete}")
    priority = catalog["featuredCharacters"] + [profile["id"] for profile in catalog["characters"]]
    rank = {cid: index for index, cid in enumerate(priority)}
    return sorted(active, key=lambda c: (c["seriesIdx"], rank.get(c["id"], len(rank))))


def build_opponents_db(lotd_to_pass: dict, kr_cards):
    print("[4/5] Building opponents_db.json ...")
    cards = {card["code"]: card for card in kr_cards}
    all_codes = cards.keys()
    extra_deck_codes = {code for code, card in cards.items() if card["type"] & TYPE_EXTRA}
    bust_map = {}
    for p in sorted(BUSTS_SRC.glob("*.png")):
        bust_map[p.stem.lower()] = p.stem

    main_toc = parse_toc(YGO_PC / "YGO_2020.toc")
    with open(YGO_PC / "YGO_2020.dat", "rb") as f:
        o_c, s_c = main_toc["main/chardata_E.bin"]
        f.seek(o_c)
        cd = f.read(s_c)
        o_d, s_d = main_toc["main/deckdata_E.bin"]
        f.seek(o_d)
        dd = f.read(s_d)

    char_list = []
    char_by_id = {}
    char_by_key = {}
    c_cnt = struct.unpack_from("<Q", cd, 0)[0]
    for i in range(c_cnt):
        rec = struct.unpack_from("<8i3Q", cd, 8 + i * 56)
        cid, series, _bust_id, selectable = rec[0], rec[1], rec[2], rec[3]
        key = read_ascii(cd, rec[8]).strip()
        name = read_utf16(cd, rec[9]).strip() or key
        portrait = resolve_portrait_name(key, bust_map)
        cobj = {
            "id": cid,
            "seriesIdx": series,
            "seriesName": SERIES_NAMES.get(series, "DM"),
            "name": name,
            "bio": "",
            "portrait": portrait,
            "isMeta2026": False,
        }
        char_list.append(cobj)
        char_by_id[cid] = cobj
        char_by_key[key.lower()] = cobj

    # Parse deckdata_E.bin metadata by filename stem
    deck_meta_by_stem = {}
    d_cnt = struct.unpack_from("<Q", dd, 0)[0]
    for i in range(d_cnt):
        rec = struct.unpack_from("<6i4Q", dd, 8 + i * 56)
        did, _, series, sig_lid, char_id, _ = rec[:6]
        stem = read_ascii(dd, rec[6]).strip().lower()
        title = read_utf16(dd, rec[7]).strip()
        deck_meta_by_stem[stem] = (did, series, sig_lid, char_id, title)

    def convert_ids(id_list):
        out = []
        for lid in id_list:
            pw = lotd_to_pass.get(int(lid), 0)
            if not pw and int(lid) in all_codes:
                pw = int(lid)
            if pw and pw in all_codes:
                out.append(pw)
        return out

    # Add 20 [2026 Meta] characters & decks first!
    meta_chars = json.loads(META_CHARS_JSON.read_text(encoding="utf-8"))["characters"]
    meta_decks = json.loads(META_DECKS_JSON.read_text(encoding="utf-8"))["decks"]
    meta_char_map = {}
    all_chars = []
    next_meta_cid = 2000
    for mc in meta_chars:
        orig_id = mc["id"]
        cid = next_meta_cid
        next_meta_cid += 1
        portrait = resolve_portrait_name(mc.get("portrait", "") or mc.get("key", ""), bust_map)
        cobj = {
            "id": cid,
            "seriesIdx": 6,
            "seriesName": "2026 메타",
            "name": f"[2026] {mc['name']}",
            "bio": "",
            "portrait": portrait,
            "isMeta2026": True,
        }
        all_chars.append(cobj)
        meta_char_map[orig_id] = cobj

    all_decks = []
    for md in meta_decks:
        cobj = meta_char_map.get(md["character"])
        if not cobj:
            continue
        main_p = convert_ids(md["cards"]["main"])
        extra_p = convert_ids(md["cards"]["extra"])
        wrong_main = sorted(set(main_p) & extra_deck_codes)
        wrong_extra = sorted(set(extra_p) - extra_deck_codes)
        if wrong_main or wrong_extra:
            raise ValueError(f"{md['title']}: wrong Main Deck cards {wrong_main}, wrong Extra Deck cards {wrong_extra}")
        side_p = convert_ids(md["cards"]["side"])
        sig = lotd_to_pass.get(md.get("signatureCard", 0), main_p[0] if main_p else 89631139)
        all_decks.append({
            "id": 20000 + md["id"],
            "charId": cobj["id"],
            "seriesIdx": 6,
            "title": md["title"],
            "subtitle": md.get("text2", ""),
            "sigCard": sig,
            "isMeta2026": True,
            "main": main_p,
            "extra": extra_p,
            "side": side_p,
        })

    # Append original LOTD characters
    all_chars.extend(char_list)

    def read_ydc(path: pathlib.Path):
        data = path.read_bytes()
        off = 8
        n_m = struct.unpack_from("<H", data, off)[0]; off += 2
        m = list(struct.unpack_from(f"<{n_m}H", data, off)); off += 2 * n_m
        n_e = struct.unpack_from("<H", data, off)[0]; off += 2
        e = list(struct.unpack_from(f"<{n_e}H", data, off)); off += 2 * n_e
        n_s = struct.unpack_from("<H", data, off)[0]; off += 2
        s = list(struct.unpack_from(f"<{n_s}H", data, off)); off += 2 * n_s
        return m, e, s

    signatures = defaultdict(set)
    for idx, ydc in enumerate(sorted(ORIG_YDC_DIR.glob("*.ydc"))):
        stem = ydc.stem
        low = stem.lower()
        m_l, e_l, s_l = read_ydc(ydc)
        main_p = convert_ids(m_l)
        extra_p = convert_ids(e_l)
        side_p = convert_ids(s_l)
        meta = deck_meta_by_stem.get(low)
        if meta:
            did, series, sig_lid, char_id, title = meta
            owner = char_by_id.get(char_id)
        else:
            parts = low.split("_")
            owner = char_by_key.get(parts[-1])
            if low.startswith("arcv_") and parts[-1] in ("a", "b") and len(parts) >= 4:
                first, second = (char_by_key.get(key) for key in parts[-3:-1])
                if first and second and first["seriesIdx"] == second["seriesIdx"] == 4:
                    owner = first if parts[-1] == "a" else second
            if owner is None:
                continue
            did = 10000 + idx
            char_id = owner["id"]
            sig_lid = m_l[0] if m_l else 0
            title = stem.replace("_", " ").title()

        if owner is None or not main_p:
            continue
        series = owner["seriesIdx"]
        sig = lotd_to_pass.get(sig_lid, 0)
        if sig not in main_p + extra_p:
            sig = main_p[0]
        deck = {
            "id": did,
            "charId": char_id,
            "seriesIdx": series,
            "title": title,
            "subtitle": f"LOTD 오리지널 ({SERIES_NAMES.get(series, 'DM')})",
            "sigCard": sig,
            "isMeta2026": False,
            "main": main_p,
            "extra": extra_p,
            "side": side_p,
        }
        key = deck_signature(deck)
        if not playable_deck(deck, extra_deck_codes) or key in signatures[char_id]:
            continue
        signatures[char_id].add(key)
        all_decks.append(deck)

    final_chars = apply_opponent_themes(all_chars, all_decks, cards, extra_deck_codes, bust_map)
    final_decks = modernize_opponents(final_chars, all_decks, cards, STREAMING / "cards_prop.bin")
    out_obj = {"characters": final_chars, "decks": final_decks}
    (STREAMING / "opponents_db.json").write_text(
        json.dumps(out_obj, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    print(f"  opponents_db.json: {len(final_chars)} characters, {len(final_decks)} selectable decks")


def slice_dfymoo(dfymoo_path: pathlib.Path, png_path: pathlib.Path, out_dir: pathlib.Path, keep_prefix=None, apply_offset=False):
    out_dir.mkdir(parents=True, exist_ok=True)
    im = Image.open(png_path).convert("RGBA")
    text = dfymoo_path.read_text(encoding="utf-8", errors="ignore")
    blocks = text.split("~")
    saved = 0
    for blk in blocks:
        name = None
        s_rect = None
        o_rect = None
        for line in blk.splitlines():
            parts = line.strip().split()
            if not parts:
                continue
            if parts[0] == "n" and len(parts) >= 2:
                name = line.strip()[2:].strip()
            elif parts[0] == "s" and len(parts) >= 5:
                s_rect = tuple(int(x) for x in parts[1:5])
            elif parts[0] == "o" and len(parts) >= 5:
                o_rect = tuple(int(x) for x in parts[1:5])
        if not name or not s_rect:
            continue
        if keep_prefix is not None and not any(name.startswith(p) for p in keep_prefix):
            continue
        sx, sy, sw, sh = s_rect
        crop = im.crop((sx, sy, sx + sw, sy + sh))
        if apply_offset and o_rect:
            ox, oy, ow, oh = o_rect
            canvas = Image.new("RGBA", (ow, oh), (0, 0, 0, 0))
            canvas.paste(crop, (ox, oy))
            crop = canvas
        safe_name = name.replace("/", "_").replace("\\", "_")
        crop.save(out_dir / f"{safe_name}.png", format="PNG", optimize=True)
        saved += 1
    return saved


def build_resources_lotd():
    print("[5/5] Preparing Assets/Resources/LOTD + Fonts/NotoSansKR.ttf ...")
    RES_FONTS.mkdir(parents=True, exist_ok=True)
    if STRAWHAT_FONT.exists():
        shutil.copy2(STRAWHAT_FONT, RES_FONTS / "NotoSansKR.ttf")

    pdui = LOTD_UI / "pdui"
    sliced_total = 0
    sliced_total += slice_dfymoo(pdui / "duel" / "PhaseBar.dfymoo", pdui / "duel" / "PhaseBar.png", RES_LOTD / "PhaseBar", apply_offset=True)
    sliced_total += slice_dfymoo(pdui / "hud.dfymoo", pdui / "hud.png", RES_LOTD / "HUD", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "field.dfymoo", pdui / "field.png", RES_LOTD / "Field", apply_offset=True)
    sliced_total += slice_dfymoo(pdui / "doShared.dfymoo", pdui / "doShared.png", RES_LOTD / "Shared", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "panel_parts.dfymoo", pdui / "panel_parts.png", RES_LOTD / "Panel", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "MatchResult.dfymoo", pdui / "MatchResult.png", RES_LOTD / "MatchResult", apply_offset=True)
    sliced_total += slice_dfymoo(pdui / "TurnCounter.dfymoo", pdui / "TurnCounter.png", RES_LOTD / "TurnCounter", apply_offset=True)
    sliced_total += slice_dfymoo(pdui / "SeriesLogo.dfymoo", pdui / "SeriesLogo.png", RES_LOTD / "SeriesLogo", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "Janken.dfymoo", pdui / "Janken.png", RES_LOTD / "Janken", apply_offset=True)
    sliced_total += slice_dfymoo(pdui / "title_logo.dfymoo", pdui / "title_logo.png", RES_LOTD / "Title", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "pendulum.dfymoo", pdui / "pendulum.png", RES_LOTD / "Pendulum", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "segment-link.dfymoo", pdui / "segment-link.png", RES_LOTD / "Link", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "Settings.dfymoo", pdui / "Settings.png", RES_LOTD / "Settings", apply_offset=False)
    sliced_total += slice_dfymoo(pdui / "ds_btn_active.dfymoo", pdui / "ds_btn_active.png", RES_LOTD / "Buttons", keep_prefix=["ds_btn_active01"])
    sliced_total += slice_dfymoo(pdui / "ds_btn_inactive.dfymoo", pdui / "ds_btn_inactive.png", RES_LOTD / "Buttons", keep_prefix=["ds_btn_inactive01"])
    sliced_total += slice_dfymoo(pdui / "ds_btn_active_yellow.dfymoo", pdui / "ds_btn_active_yellow.png", RES_LOTD / "Buttons_Yellow", keep_prefix=["ds_btn_active01"])
    sliced_total += slice_dfymoo(pdui / "ds_btn_inactive_yellow.dfymoo", pdui / "ds_btn_inactive_yellow.png", RES_LOTD / "Buttons_Yellow", keep_prefix=["ds_btn_inactive01"])

    keep_steam = [
        "ICON_ID_ATTR_", "ICON_ID_TYPE_", "ICON_ID_LEVEL", "ICON_ID_L_LEVEL",
        "ICON_ID_RANK", "ICON_ID_L_RANK", "ICON_ID_ATK", "ICON_ID_DEF",
        "ICON_ID_LIMIT_", "ICON_ID_FLINK_", "ICON_ID_FRAME_", "ICON_ID_ICON_SORT_",
        "ICON_ID_FILTER_", "ICON_ID_SORT_", "ICON_ID_ORDER_", "ICON_ID_SEARCH",
        "ICON_ID_SCALE_", "ICON_ID_CMDID_48_", "ICON_ID_BANNED", "ICON_ID_CHECKMARK",
        "ICON_ID_MATCH_",
    ]
    sliced_total += slice_dfymoo(pdui / "STEAM_icons.dfymoo", pdui / "STEAM_icons.png", RES_LOTD / "Icons", keep_prefix=keep_steam)

    # Copy Illusion custom icon (48x48) + all TypeIcons cleanly
    t_dir = RES_LOTD / "TypeIcons"
    t_dir.mkdir(parents=True, exist_ok=True)
    for p in TYPE_ICONS_SRC.glob("*.png"):
        shutil.copy2(p, t_dir / p.name)
    ill_48 = pdui / "ICON_ID_TYPE_ILLUSION.png"
    if ill_48.exists():
        shutil.copy2(ill_48, t_dir / "ICON_ID_TYPE_ILLUSION.png")
        shutil.copy2(ill_48, RES_LOTD / "Icons" / "ICON_ID_TYPE_ILLUSION.png")

    # Copy 400x580 card frames + card back + LP fill + shared frames + background
    f_dir = RES_LOTD / "Frames"
    f_dir.mkdir(parents=True, exist_ok=True)
    for p in (LOTD_UI / "duel" / "frame").glob("card_*.png"):
        shutil.copy2(p, f_dir / p.name)
    cb = LOTD_UI / "card" / "0000.png"
    if cb.exists():
        shutil.copy2(cb, f_dir / "card_back.png")

    (RES_LOTD / "Link").mkdir(parents=True, exist_ok=True)
    shutil.copy2(LOTD_UI / "duel" / "hud-pd" / "LP_FILL.png", RES_LOTD / "HUD" / "LP_FILL.png")
    shutil.copy2(LOTD_UI / "duel" / "effect" / "chain.png", RES_LOTD / "HUD" / "chain.png")
    shutil.copy2(LOTD_UI / "duel" / "effect" / "mark_link.png", RES_LOTD / "Link" / "mark_link.png")

    sh_dir = RES_LOTD / "Shared"
    sh_dir.mkdir(parents=True, exist_ok=True)
    for p in (pdui / "shared").glob("*.png"):
        shutil.copy2(p, sh_dir / p.name)

    bg_dir = RES_LOTD / "Background"
    bg_dir.mkdir(parents=True, exist_ok=True)
    for bname in ["noise_bgtec_small.png", "noise_classic_small.png", "noise_gx_small.png", "noise_5ds_small.png", "noise_zexal_small.png", "noise_arcv_small.png"]:
        bp = pdui / bname
        if bp.exists():
            shutil.copy2(bp, bg_dir / bname)

    # Compact 256x256 character Busts into Resources/LOTD/Busts (fast load & low repo size!)
    b_out = RES_LOTD / "Busts"
    b_out.mkdir(parents=True, exist_ok=True)
    for p in BUSTS_SRC.glob("*.png"):
        im = Image.open(p).convert("RGBA")
        if im.size != (256, 256):
            im = im.resize((256, 256), Image.Resampling.LANCZOS)
        im.save(b_out / p.name, format="PNG", optimize=True)

    res_mb = sum(f.stat().st_size for f in RES_LOTD.rglob("*") if f.is_file()) / 1024 / 1024
    print(f"  Resources/LOTD: {sliced_total} sliced UI sprites + frames + busts ({res_mb:.2f} MB)")


def main():
    if "--only-token-art" in sys.argv:
        cards = json.loads((STREAMING / "cards_kr.json").read_text(encoding="utf-8"))["cards"]
        build_token_art(cards)
        return
    if "--only-opponents" in sys.argv:
        cards = json.loads((STREAMING / "cards_kr.json").read_text(encoding="utf-8"))["cards"]
        build_opponents_db(load_lotd_passcodes(), cards)
        return
    if "--only-resources" in sys.argv:
        build_resources_lotd()
        print("Resources preparation COMPLETE!")
        return
    all_codes, lotd_to_pass, mod_by_pass, _pk, _pl, kr_cards = build_cards()
    build_script_pack(all_codes)
    build_card_art(all_codes, lotd_to_pass, mod_by_pass)
    build_token_art(kr_cards)
    build_opponents_db(lotd_to_pass, kr_cards)
    build_resources_lotd()
    print("Asset preparation COMPLETE!")


if __name__ == "__main__":
    main()
