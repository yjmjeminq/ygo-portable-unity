"""Check the shipped 5D's RPG decks against the source OpenDuel cards.cdb."""

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import sqlite3


STREAMING = Path(__file__).resolve().parents[1] / "Assets" / "StreamingAssets"
EXTRA_TYPE = 0x40 | 0x2000
FORBIDDEN_TYPE = 0x800000 | 0x1000000 | 0x4000000 | 0x4000
MEKLORDS = frozenset({4, 17, 23, 27})
ACE_OWNERS = (
    (r"\b(?:Black Rose Dragon|Bramble Rose Dragon|Black Rose Moonlight Dragon|Ruddy Rose Dragon|Garden Rose|Rage Rose Witch|Splendid Rose)\b", {1}),
    (r"\b(?:Red Rising Dragon|Scarred Dragon Archfiend|Red Dragon Archfiend|Red Nova Dragon|Red Supernova Dragon|Red Dragon Archfiend/Assault Mode)\b", {16}),
    (r"\b(?:Stardust (?:Dragon|Spark|Chronicle|Sifr|Warrior|Charge Warrior|Assault Warrior)|Shooting (?:Quasar Dragon|Majestic Star Dragon)|Cosmic Blazar Dragon|Satellite Warrior|Junk [\w ]+|Formula Synchron|Accel Synchron)\b", {35}),
    (r"Shooting Star Dragon T\.G\. EX|\bT\.G\.", {3204}),
    (r"\b(?:Baronne|Chevalier|Sorciere|Sauge) de Fleur\b", {32}),
    (r"\b(?:Infernity |Hundred Eyes Dragon)", {19, 3200}),
    (r"\b(?:Power Tool .*Dragon|Life Stream Dragon|Morphtronic Earfon)\b", {21}),
    (r"\bAncient Fairy (?:Life )?Dragon\b", {24}),
    (r"\b(?:Goyo |Montage Dragon)", {34}),
    (r"\b(?:Flying Fortress SKY FIRE|Dark Flattop)\b", {13, 3201}),
    (r"\b(?:Machina Fortress|Machina Citadel|Machina Ruinforce)\b", {210}),
    (r"\bBeelze(?:us)? of the Diabolic Dragons\b", {3202}),
    (r"\bIron Chain Dragon\b", {26}),
    (r"\bTurbo Cannon\b", {28}),
    (r"\b(?:Fortune Lady Every|Fortune Lady Dark)\b", {220, 221}),
    (r"\b(?:Reptilianne Hydra|Reptilianne Melusine)\b", {25, 218, 219}),
    (r"\b(?:Sun Dragon Inti|Moon Dragon Quilla|Earthbound Immortal Wiraqocha Rasca)\b", {3203}),
    (r"\b(?:Earthbound Immortal (?:Uru|Red Nova)|Underground Arachnid)\b", {30}),
    (r"\b(?:Earthbound Immortal Cusillu|Zeman the Ape King)\b", {11}),
    (r"\bEarthbound Immortal Chacu Challhua\b", {3201}),
    (r"\bEarthbound Immortal Ccapac Apu\b", {3200}),
    (r"\bEarthbound Immortal Aslla piscu\b", {221}),
    (r"\bEarthbound Immortal Ccarayhua\b", {25, 219}),
    (r"\bMalefic (?:Truth|Paradox|Paradigm) Dragon\b", {216}),
    (r"\bThunder Unicorn\b", {2}),
    (r"\bVoltic Bicorn\b", {6}),
    (r"\bLightning Tricorn\b", {18}),
    (r"\bThor, Lord of the Aesir\b", {12}),
    (r"\bLoki, Lord of the Aesir\b", {7}),
    (r"\bOdin, Father of the Aesir\b", {12, 7}),
    (r"\bMeklord Emperor Wisel\b", {4, 27}),
    (r"\bMeklord Emperor Skiel\b", {4, 23}),
    (r"\bMeklord Emperor Granel\b", {4, 17}),
    (r"\bMeklord Astro (?:Mekanikle|Dragon Asterisk)\b", {4}),
    (r"\bMeklord Astro Dragon Triskelion\b", MEKLORDS),
)

TIER_REQUIREMENTS = (
    frozenset({5318639, 44095762}),
    frozenset({97268402, 10045474, 24224830}),
    frozenset({14558127, 24224830, 10045474, 83764718, 53129443}),
    frozenset({14558127, 23434538, 24224830, 10045474, 24299458, 41420027}),
)


def tier(episode):
    return sum(episode > end for end in (8, 18, 34))


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def load_cdb(path):
    with sqlite3.connect(f"{path.resolve().as_uri()}?mode=ro", uri=True) as database:
        return {code: (name, kind, alias) for code, name, kind, alias in database.execute(
            "SELECT d.id,t.name,d.type,d.alias FROM datas d JOIN texts t ON t.id=d.id")}


def audit(cards, decks, episodes, shipped):
    errors = []
    if [deck["id"] for deck in decks] != list(range(59001, 59051)):
        errors.append("Expected the 50 dedicated deck IDs in episode order.")
    cast = {episode["deckId"]: episode["charId"] for episode in episodes}
    for deck in decks:
        number = deck["id"] - 59000
        char = deck["charId"]
        issues = []
        main, extra, side = (deck[key] for key in ("main", "extra", "side"))
        if cast.get(deck["id"]) != char:
            issues.append("episode opponent does not match")
        if len(main) != 40 or len(extra) > 15 or len(side) > 15:
            issues.append(f"invalid section sizes {(len(main), len(extra), len(side))}")
        if deck["sigCard"] not in main + extra:
            issues.append("signature card is not in the playable deck")
        if number > 8 and "입문" in deck["title"]:
            issues.append("later episode still has an introductory title")
        if not TIER_REQUIREMENTS[tier(number)] <= set(main):
            issues.append("missing phase-specific support")
        copies = Counter()
        for section, codes in (("main", main), ("extra", extra), ("side", side)):
            for code in codes:
                if code not in cards or code not in shipped:
                    issues.append(f"{code} missing from cards.cdb or shipped card data")
                    continue
                name, kind, alias = cards[code]
                copies[alias or code] += 1
                if kind & FORBIDDEN_TYPE:
                    issues.append(f"forbidden type {name} ({code})")
                if char in MEKLORDS and kind & 0x2000:
                    issues.append(f"Meklord uses Synchro {name}")
                if section != "side" and bool(kind & EXTRA_TYPE) != (section == "extra"):
                    issues.append(f"{name} is in the wrong deck section")
                for pattern, owners in ACE_OWNERS:
                    if re.search(pattern, name, re.IGNORECASE) and char not in owners:
                        issues.append(f"foreign ace {name} ({code})")
                        break
        issues.extend(f"{code} exceeds three copies" for code, count in copies.items() if count > 3)
        errors.extend(f"Ep {number:02}: {issue}" for issue in dict.fromkeys(issues))
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cdb", type=Path, required=True, help="Source OpenDuelEngine/cards.cdb")
    args = parser.parse_args()
    cards = load_cdb(args.cdb)
    decks = read(STREAMING / "story_5ds_rpg_decks.json")["decks"]
    episodes = read(STREAMING / "story_5ds_script.json")["episodes"]
    shipped = {card["code"] for card in read(STREAMING / "cards_kr.json")["cards"]}
    errors = audit(cards, decks, episodes, shipped)
    if errors:
        parser.exit(1, "\n".join(errors) + "\n")
    print(f"Verified {len(decks)} decks against {len(cards)} CDB cards: all 40-card main decks, "
          "no unknown IDs, prohibited types or foreign signature aces.")


if __name__ == "__main__":
    main()
