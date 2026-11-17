"""Update Free Duel recipes using installed card data and the release/limit snapshot."""

import json
import struct
from collections import Counter, defaultdict
from pathlib import Path


DATA = Path(__file__).parent
SECTIONS = ("main", "extra", "side")
EXTRA_TYPES = 0x40 | 0x2000 | 0x800000 | 0x4000000
ONE_COPY = {55144522, 79571449, 12580477, 18144506}  # Pot, Charity, Raigeki, Feather Duster
GENERIC_SUMMON_TAGS = {0x17, 0x46}  # Synchro and Fusion spell/effect families shared by many decks
KEEP_DECKS = {36, 340, 34043}  # Themed Earthbound, Rainbow Dark Dragon and D/D/D presets
RECIPE_FILES = ("modern_opponent_recipes_dm_gx.json", "modern_opponent_recipes_later_series.json")
MATCHING_META_DECKS = {20670, 20683}  # Mahad's Dark Magician and Era's Numeron

# Support that is useful outside its own archetype, with conservative counts.
GENERAL_SUPPORT = (
    (42141493, 2),  # Mulcharmy Fuwalos
    (24299458, 2),  # Forbidden Droplet
    (25311006, 2),  # Triple Tactics Talent
    (84192580, 2),  # Mulcharmy Purulia
    (35269904, 2),  # Triple Tactics Thrust
    (52038441, 2),  # Ghost Mourner & Moonlit Chill
    (87126721, 1),  # Mulcharmy Meowls
    (98829635, 1),  # Forbidden Crown
)
OLDER_SUPPORT = ((14558127, 2), (10045474, 2), (97268402, 2), (8267140, 2), (14087893, 2))


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def read_card_sets(path):
    entries = path.read_bytes()
    header = struct.unpack_from("<4sII", entries)
    if header[0:2] != (b"YGOP", 1):
        raise ValueError("Unknown packed card format")
    cards = {
        record[0]: frozenset(tag & 0xfff for tag in record[-8:] if tag)
        for record in struct.iter_unpack("<IIIIIIQiiII8H", entries[12:])
    }
    if len(cards) != header[2]:
        raise ValueError("Packed card count mismatch")
    return cards


def available_scripts(path):
    data = path.read_bytes()
    magic, version, count = struct.unpack_from("<4sII", data)
    if (magic, version) != (b"YGOS", 1):
        raise ValueError("Unknown packed script format")
    scripts = set()
    for name_offset, name_size, _, _ in struct.iter_unpack("<IIII", data[12:12 + count * 16]):
        name = data[name_offset:name_offset + name_size].decode("utf-8")
        if name.startswith("c") and name.endswith(".lua") and name[1:-4].isdigit():
            scripts.add(int(name[1:-4]))
    return scripts


def signature(deck):
    return tuple(tuple(sorted(deck[section])) for section in SECTIONS[:2])


class OpponentDeckCatalog:
    def __init__(self, cards, props_path):
        self.cards = cards
        self.tags = read_card_sets(props_path)
        scripts = available_scripts(props_path.with_name("cardscripts.pack"))
        snapshot = read_json(DATA / "opponent_card_versions.json")
        self.years = {code: int(year) for year, codes in snapshot["firstReleaseYears"].items() for code in codes}
        self.limits = {code: int(cap) for cap, codes in snapshot["ocgLimits"].items() for code in codes}
        self.themes = defaultdict(list)
        for code in self.years.keys() & cards.keys():
            card = cards[code]
            if (card.get("alias") or card["type"] & 0x4000 or
                    (code not in scripts and not card["type"] & 0x10)):
                continue
            for tag in self.tags[code]:
                self.themes[tag].append(code)

    def key(self, code):
        return self.cards[code].get("alias") or code

    def recent(self, code):
        return self.years.get(self.key(code), self.years.get(code, 0))

    def deck_themes(self, deck):
        counts = Counter()
        names = defaultdict(set)
        for code in deck["main"] + deck["extra"]:
            for tag in self.tags[code]:
                if code not in ONE_COPY:
                    counts[tag] += 1 if self.cards[code]["type"] & EXTRA_TYPES else 2
                    names[tag].add(self.key(code))
        ace_tags = self.tags[deck["sigCard"]]
        ranked = sorted(counts, key=lambda tag: (-counts[tag], tag not in ace_tags, tag))
        return set(tag for tag in ranked[:4] if (len(names[tag]) >= 3 and counts[tag] >= 10) or
                   (tag in ace_tags and len(names[tag]) >= 2 and counts[tag] >= 5))

    def cap(self, code, themes, ace):
        key = self.key(code)
        if key in ONE_COPY:
            return 1
        if code == ace or self.tags[code] & (themes - GENERIC_SUMMON_TAGS):
            return 3
        return self.limits.get(key, 3)

    def prepare(self, base, recipe, variant):
        deck = base.copy()
        if recipe:
            deck.update({
                "title": recipe["title"], "sigCard": recipe["sigCard"],
                "subtitle": recipe.get("subtitle", "캐릭터 테마 · 현대 지원"),
            })
            for section in SECTIONS:
                deck[section] = [code for code, count in recipe.get(section, []) for _ in range(count)]

        ace = deck["sigCard"]
        themes = self.deck_themes(deck)
        original = {section: Counter(deck[section]) for section in SECTIONS}
        for section in SECTIONS:
            deck[section] = []
        counts = Counter()

        def add(section, code, count):
            card = self.cards[code]
            if card["type"] & 0x4000 or (section != "side" and
                    bool(card["type"] & EXTRA_TYPES) != (section == "extra")):
                return
            key = self.key(code)
            space = (40 if section == "main" else 15) - len(deck[section])
            copies = min(count, space, self.cap(code, themes, ace) - counts[key])
            if copies > 0:
                deck[section].extend([code] * copies)
                counts[key] += copies

        def ranked_original(section):
            def priority(code):
                card = self.cards[code]
                same_theme = bool(self.tags[code] & themes)
                is_normal = bool(card["type"] & 0x10)
                return (code != ace, not same_theme, not bool(self.recent(code)), is_normal,
                        -original[section][code], code)
            return sorted(original[section], key=priority)

        def candidate_rank(code):
            card = self.cards[code]
            desc = card["desc"]
            searches = "덱에서" in desc and ("패에 넣" in desc or "특수 소환" in desc)
            main_starter = not card["type"] & 1 or (not card["type"] & (0x80 | 0x2000000) and
                           (card["level"] <= 4 or "이 카드를 패에서 특수 소환" in desc))
            return (not searches, not main_starter, -len(self.tags[code] & themes), -self.recent(code), code)

        for section in SECTIONS:
            if ace in original[section]:
                add(section, ace, original[section][ace])

        if recipe:
            # Preserve authored combinations. Refill only when a non-theme copy exceeds a cap.
            for section in SECTIONS:
                for code, copies in original[section].items():
                    if code != ace:
                        add(section, code, copies)
        else:
            sources = {code for tag in themes for code in self.themes[tag]}
            types = {self.cards[code]["type"] & EXTRA_TYPES for code in original["extra"]}
            ace_text = self.cards[ace]["desc"]
            important_materials = {
                code for code in original["main"] if self.cards[code]["type"] & 1 and
                '"' + self.cards[code]["name"] + '"' in ace_text
            }
            for code in ranked_original("extra"):
                if code != ace and self.tags[code] & themes:
                    add("extra", code, original["extra"][code])
            for code in ranked_original("main"):
                if code != ace and (code in important_materials or (self.recent(code) and self.tags[code] & themes)):
                    add("main", code, original["main"][code])

            # Reserve enough original names for searches, materials and other archetype costs.
            modern_names = sum(not self.cards[code]["type"] & EXTRA_TYPES for code in sources)
            old_slots = 18 if modern_names >= 4 else 23
            for code in ranked_original("main"):
                if code != ace and not counts[self.key(code)] and self.tags[code] & themes:
                    room = old_slots - sum(not self.recent(card) for card in deck["main"])
                    if room <= 0:
                        break
                    add("main", code, min(room, original["main"][code]))

            for code in sorted(sources, key=candidate_rank):
                if code in original["main"] or code in original["extra"]:
                    continue
                kind = self.cards[code]["type"] & EXTRA_TYPES
                if kind:
                    same_level = any(self.cards[old]["type"] & kind and
                                     self.cards[old]["level"] == self.cards[code]["level"] for old in original["extra"])
                    if (kind == 0x40 and kind in types or kind == 0x4000000 or
                            kind in (0x2000, 0x800000) and same_level):
                        add("extra", code, 1)
                elif len(deck["main"]) < 32:
                    card = self.cards[code]
                    # Rituals and Special-Summon-only bosses should come from authored recipes
                    # or from variants that already included them with their summon method.
                    if not card["type"] & (0x80 | 0x2000000) and (
                            not card["type"] & 1 or card["level"] <= 4 or
                            "이 카드를 패에서 특수 소환" in card["desc"]):
                        add("main", code, 2 if candidate_rank(code)[:2] == (False, False) else 1)
            for code in ranked_original("extra"):
                if not counts[self.key(code)]:
                    add("extra", code, original["extra"][code])

            # Keep newer support already known to work with this variant.
            for code in ranked_original("main"):
                if len(deck["main"]) < 34 and self.recent(code) and not counts[self.key(code)]:
                    add("main", code, original["main"][code])
            for code in ranked_original("main"):
                if len(deck["main"]) >= 22:
                    break
                if not counts[self.key(code)] and code not in ONE_COPY:
                    add("main", code, original["main"][code])

        support = GENERAL_SUPPORT[variant % 3:] + GENERAL_SUPPORT[:variant % 3]
        if not recipe:
            types = {self.cards[code]["type"] & EXTRA_TYPES for code in deck["extra"]}
            if 0x4000000 in types:
                support = ((71278040, 2),) + support  # Parallel eXceed needs a Link Summon
            if 0x2000 in types:
                support = ((77202120, 2),) + support  # Assault Synchron supports Synchro turns
            if 0x40 in types and important_materials:
                support = ((6498706, 2),) + support  # Fusion Deployment needs named material
        for code, copies in support:
            add("main", code, max(0, copies - counts[self.key(code)]))
        for code in ranked_original("main"):
            add("main", code, original["main"][code] - counts[self.key(code)])
        for code, copies in OLDER_SUPPORT:
            add("main", code, max(0, copies - counts[self.key(code)]))

        if len(deck["main"]) != 40 or ace not in deck["main"] + deck["extra"]:
            raise ValueError(f"Incomplete modern opponent deck {deck['id']} ({deck['title']})")
        if not base["isMeta2026"]:
            deck["featured"] = True
            if not recipe:
                deck["subtitle"] = "캐릭터 테마 · 현대 지원"
        return deck


def modernize_opponents(characters, decks, cards, props_path):
    catalog = OpponentDeckCatalog(cards, props_path)
    recipes = {}
    for path in RECIPE_FILES:
        for raw_id, recipe in read_json(DATA / path).items():
            deck_id = int(raw_id)
            if deck_id in recipes:
                raise ValueError(f"Duplicate modern recipe {deck_id}")
            recipes[deck_id] = recipe
    by_character = defaultdict(list)
    for deck in decks:
        by_character[deck["charId"]].append(deck)
    if set(recipes) - {deck["id"] for deck in decks}:
        raise ValueError("Modern recipes refer to missing opponent decks")

    chosen = {}
    for character in characters:
        candidates = by_character[character["id"]]
        if character["isMeta2026"]:
            chosen.update({base["id"]: catalog.prepare(base, None, index) for index, base in enumerate(candidates)})
            continue

        def priority(base):
            return (base["id"] not in recipes, base["id"] not in KEEP_DECKS,
                    not base.get("featured", False), -sum(bool(catalog.recent(code)) for code in base["main"]))

        seen = set()
        for index, base in enumerate(sorted(candidates, key=priority)):
            deck_id = base["id"]
            required = deck_id in recipes or deck_id in KEEP_DECKS
            if len(seen) >= 3 or (len(seen) >= 2 and not required and not base.get("featured", False)):
                if not required:
                    continue
            deck = catalog.prepare(base, recipes.get(deck_id), index)
            key = signature(deck)
            if key not in seen:
                chosen[deck_id] = deck
                seen.add(key)
        if len(seen) < 2:
            raise ValueError(f"Missing distinct modern decks for {character['name']}")

    meta_decks = [deck for deck in chosen.values() if 20670 <= deck["id"] <= 20689 and
                  deck["id"] not in MATCHING_META_DECKS]
    if meta_decks:
        opponent_id = 2020
        if any(character["id"] == opponent_id for character in characters):
            raise ValueError("Duplicate meta opponent ID")
        profile = {
            "id": opponent_id, "seriesIdx": 6, "seriesName": "2026 메타", "name": "메타 덱 상대",
            "bio": "테마별 메타 덱을 골라 상대할 수 있습니다.", "portraitCard": meta_decks[0]["sigCard"],
            "isMeta2026": True,
        }
        characters.append(profile)
        for deck in meta_decks:
            deck["charId"] = opponent_id
            deck["subtitle"] = "메타 덱"

    return [chosen[deck["id"]] for deck in decks if deck["id"] in chosen]
