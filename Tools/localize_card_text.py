"""Fill missing Korean card fields without changing card identities or rules data."""

import argparse
from collections import defaultdict
import json
from pathlib import Path
import re
import sqlite3
import tempfile
import unicodedata


ROOT = Path(__file__).resolve().parent.parent
TEXT = Path(__file__).with_name("card_text_ko.json")
HANGUL = re.compile(r"[\u1100-\u11ff\u3130-\u318f\uac00-\ud7a3]")
QUOTES = str.maketrans("“”’‘", "\"\"''")
TOKEN = 0x4000
PRINTED_TOKEN = re.compile(
    r'This card can be used as (?:a|an|the) "([^"]+)"\. \*\s*If used for another Token, '
    r"apply that Token's Type/Attribute/\s*Level/ATK/DEF\."
)
GENERATED_TOKEN = re.compile(r'Special Summoned with the effect of "([^"]+)"\.')


def has_korean(text):
    return HANGUL.search(text or "") is not None


def source_key(text):
    return " ".join(unicodedata.normalize("NFKC", text or "").translate(QUOTES).split())


def prefer_korean(*texts):
    texts = [text for text in texts if text and text.strip()]
    return next((text for text in texts if has_korean(text)), texts[0] if texts else "")


def unambiguous(index):
    return {key: next(iter(values)) for key, values in index.items() if len(values) == 1}


def source_name(card, source):
    return source_key(source.get(card["code"], (card["name"],))[0] or card["name"]).casefold()


def token_description(english, names):
    match = PRINTED_TOKEN.fullmatch(english)
    if match and (name := names.get(source_key(match[1]).casefold())):
        return (f'이 카드는 "{name}"으로 사용할 수 있다.\n\n'
                "※ 다른 토큰으로 사용할 경우, 그 토큰의 종족 / 속성 / 레벨 / 공격력 / 수비력을 적용한다.")

    match = GENERATED_TOKEN.fullmatch(english)
    if match and (name := names.get(source_key(match[1]).casefold())):
        return f'"{name}"의 효과로 특수 소환된다.'
    return None


def localize_card_text(cards, source, supplemental):
    names = defaultdict(set)
    descriptions = defaultdict(set)
    for card in cards:
        original = source.get(card["code"])
        if not original:
            continue
        name, desc = original[:2]
        key = source_key(name).casefold()
        if key and has_korean(card["name"]):
            names[key].add(card["name"])
        if key and desc and has_korean(card["desc"]):
            descriptions[(key, source_key(desc))].add(card["desc"])

    names = unambiguous(names)
    names.update({source_key(name).casefold(): text for name, text in supplemental["names"].items()})
    descriptions = unambiguous(descriptions)
    for code, entry in supplemental["descriptions"].items():
        original = source.get(int(code))
        if original:
            descriptions[(source_key(original[0]).casefold(), source_key(entry["source"]))] = entry["text"]

    for card in cards:
        if not has_korean(card["name"]):
            key = source_key(card["name"]).casefold()
            card["name"] = names.get(key, card["name"])

        if not has_korean(card["desc"]):
            english = source_key(card["desc"])
            translated = descriptions.get((source_name(card, source), english))
            if translated is None and card["type"] & TOKEN:
                translated = token_description(english, names)
            if translated is not None:
                card["desc"] = translated


def missing_text(cards, supplemental):
    named = supplemental["names_as_printed"]
    return {
        field: [card["code"] for card in cards if not has_korean(card[field])
                and not (field == "name" and named.get(str(card["code"])) == card["name"])]
        for field in ("name", "desc")
    }


def load_supplemental():
    return json.loads(TEXT.read_text(encoding="utf-8"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database", type=Path, help="Source cards.cdb used by prepare_unity_assets.py")
    parser.add_argument("--cards", type=Path, default=ROOT / "Assets/StreamingAssets/cards_kr.json")
    parser.add_argument("--write", action="store_true", help="Update the card asset after a complete localization pass")
    args = parser.parse_args()

    database = args.database.resolve().as_uri() + "?mode=ro"
    with sqlite3.connect(database, uri=True) as connection:
        source = {code: (name, desc) for code, name, desc in
                  connection.execute("SELECT id, name, desc FROM texts")}
    document = json.loads(args.cards.read_text(encoding="utf-8-sig"))
    supplemental = load_supplemental()
    localize_card_text(document["cards"], source, supplemental)
    missing = missing_text(document["cards"], supplemental)
    for field, codes in missing.items():
        print(f"{len(codes)} missing {field}: {', '.join(map(str, codes))}")
    if any(missing.values()):
        return 1
    if args.write:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=args.cards.parent, delete=False) as output:
            temp_path = Path(output.name)
            json.dump(document, output, ensure_ascii=False, separators=(",", ":"))
        try:
            temp_path.replace(args.cards)
        finally:
            temp_path.unlink(missing_ok=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
