"""Resolve the story's era-specific decklists against the shipped OCG data."""

import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import unicodedata

from build_5ds_story_json import ROOT
from modernize_opponents import available_scripts, read_card_sets


DATA = ROOT / "Tools/Story5Ds"
STREAMING = ROOT / "Assets/StreamingAssets"
OUTPUT = STREAMING / "story_5ds_rpg_decks.json"
FORBIDDEN = 0x4000 | 0x800000 | 0x1000000 | 0x4000000  # token, Xyz, Pendulum, Link
EXTRA = 0x40 | 0x2000  # Fusion, Synchro
NORMAL = 0x10

MODES = {"standard", "riding", "cinematic", "cinematicRiding"}
RESOLUTIONS = {"win", "opponentWin", "interrupted", "sparring", "continues", "canonicalOpponentWin"}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def card_key(name):
    return "".join(ch.casefold() for ch in unicodedata.normalize("NFKC", name)
                   if ch.isalnum() or ch == "∞")


def resolve_decks(recipes, cards, scripts, engine_codes):
    by_name = defaultdict(list)
    for card in cards:
        if not card["alias"]:
            by_name[card_key(card["name"])].append(card)

    def resolve(deck, name, section):
        matches = by_name[card_key(name)]
        if len(matches) != 1:
            raise ValueError(f"{deck}: {name!r} matched {len(matches)} cards in cards_kr.json")
        card = matches[0]
        kind, code = card["type"], card["code"]
        if kind & FORBIDDEN or (section == "main" and kind & EXTRA) or (section == "extra" and not kind & EXTRA):
            raise ValueError(f"{deck}: {name} has the wrong card type for the {section or 'ace'} slot")
        if code not in engine_codes or (code not in scripts and not kind & NORMAL):
            raise ValueError(f"{deck}: {name} has no shipped engine data or effect script")
        return code

    decks = {}
    resolved_recipes = {}
    ids = set()
    for spec in recipes:
        key, id_ = spec["key"], spec["id"]
        if key in decks or id_ in ids or not 75001 <= id_ <= 75040:
            raise ValueError(f"Invalid or duplicate deck key/ID: {key}/{id_}")
        ids.add(id_)
        if "base" in spec:
            base = resolved_recipes[spec["base"]]
            replacements = spec.get("replacements", {})
            slots = {name for section in ("main", "extra", "side") for name in base.get(section, ())}
            if not replacements.keys() <= slots:
                raise ValueError(f"{key}: unknown slot in base deck replacements")
            spec = {**{section: [replacements.get(name, name) for name in base.get(section, ())]
                       for section in ("main", "extra", "side")}, **spec}
        resolved_recipes[key] = spec
        deck = {"id": id_, "key": key, "charId": spec["characterId"], "charName": spec["character"],
                "seriesIdx": 2, "title": spec["title"], "subtitle": "5D's · Beyond Destiny",
                "difficultyTier": spec["tier"], "sigCard": resolve(key, spec["ace"], None)}
        for section in ("main", "extra", "side"):
            codes = []
            for entry in spec.get(section, []):
                count, name = entry.split(" ", 1)
                if not 1 <= int(count) <= 3:
                    raise ValueError(f"{key}: invalid count for {name}")
                codes.extend([resolve(key, name, section)] * int(count))
            deck[section] = codes
        copies = Counter(deck["main"] + deck["extra"] + deck["side"])
        if (not 40 <= len(deck["main"]) <= 60 or len(deck["extra"]) > 15 or len(deck["side"]) > 15 or
                deck["sigCard"] not in copies or any(count > 3 for count in copies.values())):
            raise ValueError(f"{key}: invalid deck size, ace absent, or more than three copies")
        decks[key] = deck
    return decks


def resolve_encounters(encounters, campaign, decks):
    ids = set()
    for encounter in encounters:
        number, scene_number = encounter["episode"], encounter["scene"]
        episode = campaign["episodes"][number - 1]
        scene = episode["scenes"][scene_number - 1]
        line = encounter.get("line", scene["line"])
        key = (number, scene_number, line)
        if key in ids:
            raise ValueError(f"Duplicate duel at {key}")
        ids.add(key)
        if ("line" in encounter and not any(event["line"] == line and event["kind"] == "system"
                                             for event in scene["events"])):
            raise ValueError(f"Encounter at {key} is not anchored to a manuscript duel cue")
        if encounter["mode"] not in MODES or encounter["resolution"] not in RESOLUTIONS:
            raise ValueError(f"Unknown duel mode or resolution at {key}")
        yield {
            "id": f"{scene['id']}_duel_{line}",
            "episodeId": episode["id"],
            "sceneId": scene["id"],
            "line": line,
            "playerDeckId": decks[encounter["player"]]["id"],
            "opponentDeckId": decks[encounter["opponent"]]["id"],
            "mode": encounter["mode"],
            "resolution": encounter["resolution"],
        }


def build():
    cards = read_json(STREAMING / "cards_kr.json")["cards"]
    scripts = available_scripts(STREAMING / "cardscripts.pack")
    properties = read_card_sets(STREAMING / "cards_prop.bin")
    decks = resolve_decks(read_json(DATA / "decks.json"), cards, scripts, properties)
    campaign = read_json(STREAMING / "story_5ds_script.json")
    encounters = list(resolve_encounters(read_json(DATA / "encounters.json"), campaign, decks))
    return {"version": 1, "series": "5DS", "decks": list(decks.values()), "encounters": encounters}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        data = build()
        output = (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        if args.check:
            if OUTPUT.read_bytes().replace(b"\r\n", b"\n") != output:
                raise ValueError(f"Generated deck data is out of date: {OUTPUT}")
        else:
            OUTPUT.write_bytes(output)
    except (OSError, IndexError, KeyError, ValueError) as error:
        parser.exit(1, f"{error}\n")
    print(f"{len(data['decks'])} decks and {len(data['encounters'])} encounter anchors verified")


if __name__ == "__main__":
    main()
