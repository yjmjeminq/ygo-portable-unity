"""Build the checked-in opponent deck order using the local LOTD archive."""

import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import struct
import sys

sys.dont_write_bytecode = True

from modernize_opponents import ONE_COPY, available_scripts, read_card_sets, signature
from prepare_unity_assets import LOTD_MISSING_PASSCODES, TYPE_EXTRA, TYPE_TOKEN, parse_toc, read_ascii


ROOT = Path(__file__).resolve().parents[1]
STREAMING = ROOT / "Assets" / "StreamingAssets"
SECTIONS = ("main", "extra", "side")
SUPPORT = (
    5318639, 66788016, 89718302, 98045062, 14087893, 97169186, 69162969,
    4206964, 56120475, 60082869, 12607053, 97077563, 94192409, 44095762,
    62279055, 29401950, 53582587, 83764718,
)


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def archive_metadata(source):
    toc = parse_toc(source / "YGO_2020.toc")
    with (source / "YGO_2020.dat").open("rb") as stream:
        def read(name):
            offset, size = toc[name]
            stream.seek(offset)
            return stream.read(size)

        cards = read("bin/CARD_Prop.bin")
        passwords = read("bin/CARD_Pass.bin")
        chars = read("main/chardata_E.bin")
        decks = read("main/deckdata_E.bin")

    lotd_to_passcode = {}
    for index in range(1, len(cards) // 8):
        card_id = struct.unpack_from("<I", cards, index * 8)[0] & 0x3FFF
        password = struct.unpack_from("<I", passwords, index * 4)[0]
        if card_id:
            lotd_to_passcode[card_id] = password or LOTD_MISSING_PASSCODES.get(card_id, 0)

    ids = {}
    for index in range(struct.unpack_from("<Q", chars)[0]):
        entry = struct.unpack_from("<8i3Q", chars, 8 + index * 56)
        ids[read_ascii(chars, entry[8]).strip().lower()] = entry[0]

    presets = {}
    for index in range(struct.unpack_from("<Q", decks)[0]):
        entry = struct.unpack_from("<6i4Q", decks, 8 + index * 56)
        presets[read_ascii(decks, entry[6]).strip().lower()] = (entry[4], entry[3])
    return lotd_to_passcode, ids, presets


def read_originals(path, lotd_to_passcode, characters, presets):
    originals = defaultdict(list)
    for file in sorted(path.glob("*.ydc")):
        stem = file.stem.lower()
        parts = stem.split("_")
        if stem in presets:
            char_id, ace = presets[stem]
        elif stem.startswith("arcv_") and parts[-1] in ("a", "b") and len(parts) >= 4:
            char_id = characters.get(parts[-3 if parts[-1] == "a" else -2])
            ace = 0
        else:
            char_id, ace = characters.get(parts[-1]), 0
        if char_id is None:
            continue
        data = file.read_bytes()
        deck = {"sigCard": lotd_to_passcode.get(ace, 0)}
        offset = 8
        for section in SECTIONS:
            size = struct.unpack_from("<H", data, offset)[0]
            offset += 2
            deck[section] = [
                lotd_to_passcode.get(card_id, 0)
                for card_id in struct.unpack_from(f"<{size}H", data, offset)
            ]
            offset += size * 2
        originals[char_id].append((stem, deck))
    return originals


class LevelBuilder:
    def __init__(self, stock):
        self.cards = {card["code"]: card for card in load_json(STREAMING / "cards_kr.json")["cards"]}
        self.pool = available_scripts(STREAMING / "cardscripts.pack")
        self.years = {code: int(year) for year, codes in
                      load_json(ROOT / "Tools" / "opponent_card_versions.json")["firstReleaseYears"].items()
                      for code in codes}
        self.tags = {code: {tag & 0xFFF for tag in tags} - {0, 0x17, 0x46}
                     for code, tags in read_card_sets(STREAMING / "cards_prop.bin").items()}
        self.stock = {code for code in stock if self.playable(code) and not self.year(code)}
        if not self.stock:
            raise ValueError("No stock LOTD cards resolved from the archive.")

        def is_basic(code):
            card = self.cards[code]
            return (card["type"] & 0x11 == 0x11 and not card["type"] & (TYPE_EXTRA | 0x2000000) and
                    1 <= card["level"] <= 4 and 1500 <= card["atk"] <= 2000 and self.key(code) == code)

        self.basics = sorted((code for code in self.stock if is_basic(code)),
                             key=lambda code: (-self.cards[code]["atk"], code))

    def key(self, code):
        return self.cards.get(code, {}).get("alias") or code

    def year(self, code):
        return self.years.get(self.key(code), self.years.get(code, 0))

    def playable(self, code):
        card = self.cards.get(code)
        return (card is not None and card["type"] & 7 and not card["type"] & TYPE_TOKEN and
                (code in self.pool or self.key(code) in self.pool or card["type"] & 0x11 == 0x11))

    def strength(self, deck):
        cards = [self.cards[code] for code in deck["main"] + deck["extra"] if code in self.cards]
        recent = sum(max(0, self.year(card["code"]) - 2019) for card in cards)
        interaction = sum("무효로" in card["desc"] or "제외한다" in card["desc"] for card in cards)
        development = sum("특수 소환" in card["desc"] or "덱에서" in card["desc"] for card in cards)
        return recent * 3 + interaction * 5 + development * 2 + len(deck["extra"]) * 3

    def normal_summon(self, code):
        card = self.cards[code]
        return card["type"] & 1 and not card["type"] & (TYPE_EXTRA | 0x2000000 | 0x80) and 1 <= card["level"] <= 4

    def theme(self, deck):
        hits = Counter(tag for code in deck["main"] + deck["extra"] for tag in self.tags.get(code, ()))
        ace = self.tags.get(deck["sigCard"], set())
        signature_tags = {tag for tag in ace if hits[tag] >= 3}
        return signature_tags or {tag for tag, count in hits.most_common(2) if count >= 4}

    def build_classic(self, char, source, original):
        chosen = {"main": [], "extra": [], "side": []}
        counts = Counter()

        def add(section, code):
            if not self.playable(code):
                return False
            card = self.cards[code]
            key = self.key(code)
            max_cards = 40 if section == "main" else 15
            if (len(chosen[section]) >= max_cards or counts[key] >= (1 if key in ONE_COPY else 3) or
                    (section != "side" and bool(card["type"] & TYPE_EXTRA) != (section == "extra"))):
                return False
            counts[key] += 1
            chosen[section].append(code)
            return True

        tags = self.theme(source)
        ace = source["sigCard"]
        if not self.playable(ace):
            ace = next((code for code in source["main"] if code in self.stock), 0)

        if ace:
            section = "extra" if self.cards[ace]["type"] & TYPE_EXTRA else "main"
            add(section, ace)

        if original:
            for section in SECTIONS:
                for code in source[section]:
                    if code in self.stock and not (code == ace and counts[self.key(code)] >= source[section].count(code)):
                        add(section, code)
        else:
            for code in source["main"]:
                if code in self.stock and len(chosen["main"]) < 23:
                    add("main", code)
            for code in source["extra"]:
                if code in self.stock and len(chosen["extra"]) < 7:
                    add("extra", code)
            recent_theme = [code for code in dict.fromkeys(source["main"]) if self.playable(code) and
                            code not in self.stock and code != ace and self.tags.get(code, set()) & tags]
            for code in recent_theme[:3]:
                add("main", code)

        ace_data = self.cards.get(ace, {})
        normal_count = sum(bool(self.normal_summon(code)) for code in chosen["main"])
        ranked_basics = sorted(self.basics, key=lambda code: (self.cards[code]["race"] != ace_data.get("race"),
                                                            self.cards[code]["attr"] != ace_data.get("attr")))
        if normal_count < 13 and len(chosen["main"]) < 40:
            for code in ranked_basics:
                for _ in range(2):
                    if normal_count < 13 and add("main", code):
                        normal_count += 1
                if normal_count >= 13 or len(chosen["main"]) >= 40:
                    break

        for code in SUPPORT * 2:
            if code in self.stock:
                add("main", code)
        for code in ranked_basics:
            if len(chosen["main"]) == 40:
                break
            add("main", code)

        if len(chosen["main"]) != 40:
            raise ValueError(f"Could not build a classic deck for opponent {char['id']}")
        if not ace:
            ace = chosen["main"][0]
        return {
            "id": 1_000_000 + char["id"], "charId": char["id"], "seriesIdx": char["seriesIdx"],
            "title": self.cards[ace]["name"] + " · 클래식",
            "subtitle": "LOTD 오리지널 카드 중심" if original else "캐릭터 테마 · 구형 지원 카드 중심",
            "sigCard": ace, **chosen,
        }


def named_character(name):
    return name.removeprefix("[2026] ").strip().casefold()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT.parent / "YGO-PC-Full",
                        help="folder containing YGO_2020.toc and YGO_2020.dat")
    args = parser.parse_args()
    passcodes, characters, presets = archive_metadata(args.source)
    originals = read_originals(ROOT / "Assets" / "Data" / "Decks" / "Original", passcodes, characters, presets)
    builder = LevelBuilder(passcodes.values())
    roster = load_json(STREAMING / "opponents_db.json")
    modern_by_char = defaultdict(list)
    for deck in roster["decks"]:
        modern_by_char[deck["charId"]].append(deck)
    by_name = {named_character(char["name"]): char["id"] for char in roster["characters"] if char["id"] in originals}
    entries = []
    original_count = 0
    for char in roster["characters"]:
        modern = sorted(modern_by_char[char["id"]],
                        key=lambda deck: (builder.strength(deck), deck["id"]))
        if not modern:
            continue
        possible = originals.get(char["id"]) or originals.get(by_name.get(named_character(char["name"])), ())
        possible = [(stem, deck) for stem, deck in possible if sum(code in builder.stock for code in deck["main"]) >= 31]
        if possible:
            _, vintage = min(possible, key=lambda item: ("hard" in item[0], builder.strength(item[1]), item[0]))
            classic = builder.build_classic(char, vintage, original=True)
            original_count += 1
        else:
            classic = builder.build_classic(char, modern[0], original=False)
        if any(signature(classic) == signature(deck) for deck in modern):
            raise ValueError(f"Duplicate classic and advanced deck for opponent {char['id']}")
        entries.append({"charId": char["id"], "classic": classic, "advancedIds": [deck["id"] for deck in modern]})

    output = STREAMING / "opponent_deck_levels.json"
    records = (json.dumps(entry, ensure_ascii=False, separators=(",", ":")) for entry in entries)
    output.write_text("{\"opponents\":[\n" + ",\n".join(records) + "\n]}\n", encoding="utf-8")
    print(f"{output.name}: {len(entries)} classic decks ({original_count} original LOTD, {len(entries) - original_count} themed vintage-support builds)")


if __name__ == "__main__":
    main()
