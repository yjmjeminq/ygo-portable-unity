"""Audit the shipped offline opponent roster and reproducibly sample each opening hand.

A starter establishes the deck's game plan from an empty board. The deck's opening
model can also list two-card starts and standalone defensive plays. Pot of Greed
and Graceful Charity only count when the cards they reveal supply a real play.
The draw model assumes the spells resolve and the AI keeps any needed cards. The
native three-turn smoke test is separate and exercises actual legal actions.
"""

import argparse
from collections import Counter, defaultdict
from itertools import combinations
import random
import re
import sys
from pathlib import Path

sys.dont_write_bytecode = True
from modernize_opponents import EXTRA_TYPES, ONE_COPY, available_scripts, read_json


STREAMING = Path(__file__).resolve().parents[1] / "Assets" / "StreamingAssets"
DRAW = {55144522: 2, 79571449: 3}
OPENING_DRAW = {**DRAW, 70368879: 1, 67616300: 1, 33782437: 1}
SMALL_WORLD = 89558743
CARD_TRAITS = ("race", "attr", "level", "atk", "def")
SERIES_SIZES = {0: 46, 1: 46, 2: 48, 3: 37, 4: 41, 5: 27, 6: 21}
LEVEL_COUNTS = {10: 40, 5: 100, 4: 107, 3: 18, 19: 1}
# Includes summon/attack stoppers and cards restricted to particular engines.
HAND_TRAPS = {
    1697104, 2810642, 2830693, 18964575, 19665973, 23002292, 23434538,
    24508238, 27204311, 34267821, 38491199, 38814750, 40640057, 42141493,
    44330098, 46261704, 46613515, 52038441, 59438930, 60643553, 62015408,
    67750322, 73642296, 74203495, 74762582, 75425043, 84192580, 87126721,
    91800273, 94145021, 97268402, 97639441, 98777036, 10045474, 14558127,
}
EFFECT_NUMBER = re.compile(r"[①②③④⑤⑥⑦⑧⑨]")
HAND_ACTIVATION = re.compile(r"패(?:에서|의 이 카드| / 필드의 이 카드| / 묘지의 이 카드)")


def copy_key(card):
    return card.get("alias") or card["code"]


def is_hand_trap(card):
    """Recognize hand disruption, including cards outside the general AI's staples."""
    if copy_key(card) in HAND_TRAPS:
        return True
    text = card["desc"]
    if card["type"] & 4 and ("발동은 패에서도" in text or "패에서 발동할 수 있다" in text):
        return True
    triggers = ("상대 턴에", "상대 턴의", "상대가", "상대 몬스터의 공격",
                "효과가 발동했을 때", "효과의 발동에 체인")
    disruption = ("무효", "드로우", "제외", "파괴", "릴리스", "배틀 페이즈를 종료",
                  "전투 데미지", "데미지를 0", "공격을", "효과를 적용")
    for effect in EFFECT_NUMBER.split(text):
        # Check the activation clause first, so summoning from hand as resolution
        # does not by itself classify an archetype extender as hand disruption.
        activation = re.split(r"발동할 수 있다[.。]|발동한다[.。]", effect, maxsplit=1)
        if len(activation) != 2 or not any(s in activation[0] for s in triggers):
            continue
        if HAND_ACTIVATION.search(activation[0]) and any(s in activation[1] for s in disruption):
            return True
    return False


def shared_traits(first, second):
    return sum(first[t] == second[t] for t in CARD_TRAITS)


def opening_rate(deck, trials, model):
    main = deck["main"]
    total_copies = Counter(main)
    starters, plays, pairs, routes = model
    rng = random.Random(193 + deck["id"] * 101)
    started = bricks = 0
    accessible = 5 + sum(main.count(code) * amount for code, amount in OPENING_DRAW.items())
    if 84211599 in main:
        accessible += 6
    for _ in range(trials):
        sample = rng.sample(main, accessible)
        hand = Counter(sample[:5])
        used = set()
        offset = 5
        while draw := next((code for code in OPENING_DRAW if hand[code]), None):
            hand[draw] -= 1
            used.add(draw)
            amount = OPENING_DRAW[draw]
            hand.update(sample[offset:offset + amount])
            offset += amount
        if hand[84211599] and not used:
            # Prosperity replaces itself and cannot be combined with a draw.
            # Look at six cards and keep a single card relevant to the hand.
            options = sample[offset:offset+6]
            partner = next((c for c in options for other in hand if hand[other] and
                            ((c, other) in pairs or (other, c) in pairs)), None)
            card = next((c for c in options if c in starters), partner)
            if card is None:
                card = next((c for c in options if c in plays), options[0])
            hand[84211599] -= 1
            hand[card] += 1
        available = set(+hand)
        has_start = bool(available & starters) or any(
            (a, b) in pairs and (a != b or hand[a] >= 2)
            for a in available for b in available
        )
        if not has_start and hand[SMALL_WORLD] and routes:
            has_start = any(
                total_copies[bridge] > hand[bridge] and total_copies[target] > hand[target]
                for code in available for bridge, target in routes.get(code, ())
            )
        started += has_start
        bricks += not has_start and not available & plays
    return started / trials, bricks / trials


def opening_model(deck, known, is_trap):
    model = deck.get("opening")
    if not isinstance(model, dict):
        return None
    solo = model.get("starters", [])
    defensive = model.get("plays", [])
    pairs = model.get("pairs", [])
    if any(len(pair) != 2 for pair in pairs):
        return None
    main = set(deck["main"])
    referenced = set(solo) | set(defensive) | {code for pair in pairs for code in pair}
    if not referenced <= main or referenced & (OPENING_DRAW.keys() | {84211599}):
        return None
    if any(code in is_trap or not known[code]["type"] & 7 for code in solo):
        return None
    routes = defaultdict(list)
    for option in model.get("smallWorld", []):
        if len(option) != 3:
            return None
        first, bridge, target = option
        if (SMALL_WORLD not in main or not {first, bridge, target} <= main or target not in solo
                or len({first, bridge, target}) != 3):
            return None
        if (not all(known[c]["type"] & 1 for c in option)
                or shared_traits(known[first], known[bridge]) != 1
                or shared_traits(known[bridge], known[target]) != 1):
            return None
        routes[first].append((bridge, target))
    return set(solo), set(defensive), frozenset(tuple(pair) for pair in pairs), routes


def unique_index(entries, key, errors):
    indexed = {}
    for item in entries:
        if item[key] in indexed:
            errors.append(f"Duplicate {key} {item[key]}")
        indexed[item[key]] = item
    return indexed


def check(streaming, trials):
    errors = []
    deck_rate_failures = []
    db = read_json(streaming / "opponents_db.json")
    level_catalog = read_json(streaming / "opponent_deck_levels.json")["opponents"]
    known = {card["code"]: card for card in read_json(streaming / "cards_kr.json")["cards"]}
    scripts = available_scripts(streaming / "cardscripts.pack")
    is_trap = {code for code, card in known.items() if is_hand_trap(card)}
    characters = unique_index(db["characters"], "id", errors)
    decks = unique_index(db["decks"], "id", errors)
    levels = unique_index(level_catalog, "charId", errors)
    seen = set()
    signatures = {}
    rates = []
    issue_counts = Counter()
    if Counter(c["seriesIdx"] for c in characters.values()) != SERIES_SIZES:
        errors.append("Incorrect character count by series")
    if levels.keys() != characters.keys():
        errors.append("Character and level mappings do not match")

    for cid, ch in characters.items():
        ids = levels.get(cid, {}).get("deckIds", [])
        if not ids:
            errors.append(f"{cid}: missing explicit deckIds")
            continue
        engines = Counter()
        cores = defaultdict(set)
        for level, did in enumerate(ids, 1):
            deck = decks.get(did)
            label = f'{ch["name"]} Lv.{level} ({did})'

            def report(category, message):
                issue_counts[category] += 1
                errors.append(f"{label}: {message}")

            if not deck or deck["charId"] != cid or deck["seriesIdx"] != ch["seriesIdx"] or did in seen:
                report("mapping", "Invalid, misassigned or repeated deck")
                continue
            seen.add(did)
            main, extra, side = (deck[section] for section in ("main", "extra", "side"))
            if not 40 <= len(main) <= 60 or len(extra) != 15 or len(side) > 15:
                report("sizes", f"Invalid deck size {len(main)}/{len(extra)}/{len(side)}")
            all_cards = main + extra + side
            missing = sorted(set(all_cards) - known.keys())
            if missing:
                report("unregistered", f"Unregistered codes {missing}")
                continue
            if any(
                not known[code]["type"] & 7 or known[code]["type"] & 0x4000 or
                (not known[code]["type"] & 0x10 and code not in scripts and copy_key(known[code]) not in scripts)
                for code in all_cards
            ):
                report("card_scripts", "A card has no playable type or lacks a native script")
            if any(known[c]["type"] & EXTRA_TYPES for c in main) or any(not known[c]["type"] & EXTRA_TYPES for c in extra):
                report("placement", "Main or Extra Deck contains a card of the wrong type")
            copies = Counter(copy_key(known[c]) for c in all_cards)
            if any(count > (1 if c in ONE_COPY else 3) for c, count in copies.items()):
                report("copies", "A same-name card exceeds its copy limit")
            if any(main.count(code) != 1 for code in DRAW):
                report("mandatory_draws", "Pot of Greed and Graceful Charity must appear once each")
            if deck["sigCard"] not in main + extra or deck["sigCard"] in DRAW:
                report("ace", "Signature card is missing or is a mandatory draw spell")
            limit = 2 if ch["seriesIdx"] != 6 and level <= 2 else 4
            hand_traps = sum(code in is_trap for code in all_cards)
            if hand_traps > limit:
                report("hand_traps", f"{hand_traps} hand traps (limit {limit})")

            engine = deck.get("engine", "")
            core = set(deck.get("engineCards", []))
            if not engine or len(core) < 5 or not core <= set(main + extra) or sum(code in core for code in main) < 10:
                report("theme", "Missing distinct engine with a substantial Main Deck presence")
            else:
                engines[engine] += 1
                cores[engine].update(core)
            signature = (tuple(sorted(main)), tuple(sorted(extra)))
            if (cid, signature) in signatures:
                report("clone", f"Same Main and Extra Deck as {signatures[cid, signature]}")
            signatures[cid, signature] = did

            model = opening_model(deck, known, is_trap)
            if model is None:
                report("opening_model", "Missing or invalid opening model")
            elif 40 <= len(main) <= 60:
                started, bricked = opening_rate(deck, trials, model)
                rates.append((started, bricked))
                if started < 0.90 or bricked > 0.05:
                    deck_rate_failures.append((started, bricked, label))

        if ch["seriesIdx"] != 6:
            max_use = 3 if len(ids) == 10 else 2
            min_engines = 4 if len(ids) == 10 else (len(ids) + 1) // 2
            if len(engines) < min_engines or max(engines.values(), default=0) > max_use:
                errors.append(f"{cid}: {dict(engines)} does not meet theme variety requirements")
            for (a, a_cards), (b, b_cards) in combinations(cores.items(), 2):
                if min(len(a_cards - b_cards), len(b_cards - a_cards)) < 3:
                    errors.append(f"{cid}: engines {a} and {b} share essentially the same core")

    lengths = Counter(len(e.get("deckIds", [])) for e in levels.values())
    if lengths != LEVEL_COUNTS or len(seen) != 1401 or seen != decks.keys():
        errors.append(f"Invalid level coverage: {len(seen)} mapped, {len(decks)} presets, {dict(lengths)}")
    for cid, expected in (
        (104, {"dark-magician": 3, "slifer-holactie": 2, "black-luster-soldier": 2, "gaia": 2, "chimera": 1}),
        (94, {"blue-eyes": 3, "chaos-max": 2, "obelisk": 2, "xyz-abc": 3}),
        (103, {"ra": 3, "lava-golem-burn": 3, "revival-slime-lock": 3, "battle-city-dark": 1}),
    ):
        actual = Counter(decks.get(did, {}).get("engine") for did in levels.get(cid, {}).get("deckIds", []))
        if actual != expected:
            errors.append(f"{cid}: unexpected requested engine mix {dict(actual)}")
    if any("데스완구" in known[code]["name"] for d in decks.values() if d["charId"] == 103
           for section in ("main", "extra", "side") for code in d[section] if code in known):
        errors.append("Dark Marik still includes Frightfur")
    main_sizes = Counter(len(d["main"]) for d in decks.values())
    print(f"Static audit: {len(characters)} characters; {len(seen)} mapped decks; {len(errors)} issues {dict(issue_counts)}.")
    print(f"  Main: {min(main_sizes)}-{max(main_sizes)} cards; Extra: 15 required; unknown cards: {issue_counts['unregistered']}.")
    for error in errors[:30]:
        print(f"  {error}")
    print(f"Opening audit: {len(rates)}/{len(seen)} decks x {trials:,} shuffled five-card hands; "
          f"{len(deck_rate_failures)} decks below the requested thresholds.")
    if rates:
        print(f"  Minimum starter rate: {min(s for s, _ in rates):.1%}. "
              f"Maximum brick rate: {max(b for _, b in rates):.1%}.")
    for started, bricked, label in sorted(deck_rate_failures)[:20]:
        print(f"  {label}: starter {started:.1%}, brick {bricked:.1%}")
    return not errors and not deck_rate_failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--streaming", type=Path, default=STREAMING)
    parser.add_argument("--trials", type=int, default=1000)
    args = parser.parse_args()
    if args.trials < 1:
        parser.error("--trials must be positive")
    return 0 if check(args.streaming, args.trials) else 1


if __name__ == "__main__":
    raise SystemExit(main())
