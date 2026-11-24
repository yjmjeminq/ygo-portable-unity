"""Build character-specific opponent decks while retaining the shipped level IDs.

The default baseline is the pre-rework Git snapshot. The seed catalogue records
the character archetypes available before that snapshot was turned into levels.
Generate into a scratch directory before copying the results to StreamingAssets.
Run audit_opponent_roster and the native smoke test on those results.
"""

import argparse
from collections import Counter, defaultdict
from dataclasses import dataclass
from functools import cache
from itertools import chain
import json
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
from audit_opponent_roster import (
    DRAW, SMALL_WORLD, STREAMING, copy_key, is_hand_trap, opening_rate, shared_traits,
)
from modernize_opponents import EXTRA_TYPES, ONE_COPY, available_scripts, read_card_sets, read_json
from opponent_engine_plans import (
    ATTRIBUTE_PACKAGES, CLASSIC_FIEND_PACKAGE, CUSTOM, EXACT_PLANS, FAMILIES, FEATURED, FEATURED_RELATED, KNOWN_STARTS, OPENING_PACKAGES, RELATED_CARDS, SPECIAL_SUPPORT, WIND_PACKAGE,
)


TOOLS = Path(__file__).resolve().parent
BASELINE_REF = "38a14b4"
SEED_CATALOGUE = TOOLS / "opponent_recipe_seeds.json"
MONSTER, SPELL, TRAP = 1, 2, 4
RITUAL, TOKEN, TUNER = 0x80, 0x4000, 0x1000
FUSION, SYNCHRO, XYZ, LINK = 0x40, 0x2000, 0x800000, 0x4000000
GENERAL_TAGS = {0, 0x17, 0x46, 0x11D, 0x1AC}
# Generic Links or their material-specific counterparts, chosen by the host engine.
LINK_TOOLS = (29301450, 65741786, 86066372, 4280258, 98127546, 60303245,
              41999284, 2857636, 38342335, 50277355, 21887175, 40003819,
              75452921, 50588353, 72330894, 38342336, 25725326)
SYNCHRO_TOOLS = (84815190, 27548199, 50091196, 44508094, 52687916, 60465049,
                 73580471, 25274141, 83994433, 52653092, 90953320, 27054370,
                 33779875, 73539069, 53466826, 29296344, 82044279)
XYZ_TOOLS = (65305468, 99469936, 75433814, 84013237, 82633039, 12014404, 48739166,
             21044178, 95169481, 90448279, 72529749, 22110647, 26216154,
             42566602, 74161717, 48264957, 31320433, 46871387)
# An Extra Deck for control decks may also fuel Extrav/Prosperity. It does not
# count as evidence for the deck's ability to Summon a specific Extra monster.
EXTRA_FODDER = (84433295, 22073844, 18236002, 80532587, 58069384, 90953320, 80170678,
                98506199, 57043117, 2129638, 48739166, 84013237, 3019642)
HAND_TRAP_ORDER = (14558127, 10045474, 23434538, 97268402, 94145021, 42141493, 27204311)
CONTROL_TOOLS = (94192409, 29401950, 53582587, 72302403, 84749824, 40605147,
                 62279055, 12607053, 36361633, 44095762, 97077563, 5851097)
POWER_TOOLS = (24224830, 12580477, 18144506, 24299458, 14532163, 25311006,
               35269904, 10045474, 73628505, 83764718, 81439173, 84211599,
               49238328, 2295440, 9753964, 32807846)
# Generic normal summons are reserved for compatible strategies.
FALLBACK_MAIN = (83764718, 98045062, 5318639, 14087893, 97169186, 69162969,
                 94192409, 29401950, 53582587, 97077563, 81439173, 49238328,
                 84211599, 72302403, 83764719, 89718302, 40605147, 44095762,
                 25311006, 12580477, 18144506, 98645731, 72930878, 77538567,
                 24299458, 35269904, 23434538)
DECK_SEARCH = ("덱에서", "덱의", "덱 / 묘지에서", "패 / 덱에서", "패 / 덱의")
PROACTIVE_RESOLUTION = ("패에 넣", "특수 소환", "세트한다", "필드에 놓", "묘지로 보낸다")
FORBIDDEN_START = ("상대 필드", "상대가", "묘지에서", "존재할 경우", "데미지", "턴 종료")


@dataclass
class Engine:
    key: str
    title: str
    ace: int
    main: Counter
    extra: tuple
    starters: tuple
    core: frozenset


def deck_list(deck, section):
    cards = deck.get(section, [])
    if cards and isinstance(cards[0], list):
        return [code for code, count in cards for _ in range(count)]
    return cards


class Builder:
    def __init__(self, streaming, source_decks):
        self.cards = {c["code"]: c for c in read_json(streaming / "cards_kr.json")["cards"]}
        self.archive = available_scripts(streaming / "cardscripts.pack")
        self.tags = {code: {tag & 0xFFF for tag in tags} - GENERAL_TAGS
                     for code, tags in read_card_sets(streaming / "cards_prop.bin").items()}
        self.years = {code: int(year) for year, codes in
                      read_json(TOOLS / "opponent_card_versions.json")["firstReleaseYears"].items()
                      for code in codes}
        self.valid = {code for code in self.cards if self.playable(code)}
        self.traps = {code for code in self.valid if is_hand_trap(self.cards[code])}
        self.seeds = defaultdict(list)
        for deck in source_decks:
            self.seeds[deck["charId"]].append(deck)
        self.by_tag = defaultdict(list)
        for code in sorted(self.valid):
            if self.key(code) == code:
                for tag in self.tags.get(code, ()):
                    self.by_tag[tag].append(code)
        self.opening_packages = {
            race: (cards, extra, starters) for race, cards, extra, starters in OPENING_PACKAGES
            if set(cards) | set(extra) <= self.valid
        }
        self.classic_fiend = CLASSIC_FIEND_PACKAGE if set(CLASSIC_FIEND_PACKAGE[0]) | set(CLASSIC_FIEND_PACKAGE[1]) <= self.valid else None
        self.wind_package = WIND_PACKAGE if set(WIND_PACKAGE[0]) | set(WIND_PACKAGE[1]) <= self.valid else None
        self.special_packages = {
            name: (cards, extra, starters) for name, (cards, extra, starters) in SPECIAL_SUPPORT.items()
            if set(cards) | set(extra) <= self.valid
        }
        self.attribute_packages = {
            attr: (cards, extra, starters) for attr, cards, extra, starters in ATTRIBUTE_PACKAGES
            if set(cards) | set(extra) <= self.valid
        }
        self.global_engines = {}
        for key, (title, pattern, ace) in FAMILIES.items():
            pattern = re.compile(pattern)
            pool = [code for code in sorted(self.valid) if self.key(code) == code and pattern.search(self.card(code)["name"])]
            pool.extend(code for code in RELATED_CARDS.get(key, ()) if code in self.valid)
            self.global_engines[key] = self.make_engine(key, title, ace, pool)
        for key, (title, ace, main, extra, starters) in CUSTOM.items():
            pool = [code for code in chain(main, extra) if code in self.valid]
            self.global_engines[key] = Engine(key, title, ace, Counter({c: n for c, n in main.items() if c in self.valid}),
                                              tuple(c for c in extra if c in self.valid and self.kind(c) & EXTRA_TYPES),
                                              starters, frozenset(pool))

    def card(self, code):
        return self.cards[code]

    def key(self, code):
        return copy_key(self.card(code))

    def playable(self, code):
        card = self.card(code)
        return card["type"] & 7 and not card["type"] & TOKEN and (
            code in self.archive or copy_key(card) in self.archive or card["type"] & 0x10
        )

    def kind(self, code):
        return self.card(code)["type"]

    def year(self, code):
        return self.years.get(self.key(code), 0)

    def is_normal(self, code):
        return self.kind(code) & MONSTER and not self.kind(code) & (EXTRA_TYPES | RITUAL | 0x2000000)             and 0 < self.card(code)["level"] <= 4

    @cache
    def looks_like_starter(self, code):
        if code in self.traps or code == SMALL_WORLD:
            return False
        if code == 40140448:
            # Sun Mou can send an arbitrary other card from hand to search.
            return True
        if code == 13035077:
            # Diagram destroys a different card on the board or in hand.
            return True
        text = self.card(code)["desc"]
        is_spell = self.kind(code) & SPELL
        for effect in re.split(r"[①②③④⑤⑥]", text):
            condition, separator, result = effect.partition("발동할 수 있다.")
            if is_spell and not separator and ("이 카드의 발동" in effect or "이 카드는 1턴" not in effect):
                result = effect
                condition = effect[:effect.find("덱")] if "덱" in effect else effect
                separator = bool(result)
            if not separator or any(t in condition for t in FORBIDDEN_START):
                continue
            result = re.split(r"이 효과의 발동 후|이 효과를 발동하는 턴|이 턴에, 자신은|이 효과로 특수 소환한", result, maxsplit=1)[0]
            if not any(t in result for t in DECK_SEARCH) or not any(t in result for t in PROACTIVE_RESOLUTION):
                continue
            normal_effect = ("일반 소환했", "일반 소환에 성공", "일반 소환 / 특수 소환",
                             "이 카드가 소환", "이 카드를 소환",
                             "필드의 이 카드를", "자신 메인 페이즈에", "자신의 메인 페이즈에")
            if self.is_normal(code) and any(term in condition for term in normal_effect) and not any(
                    prerequisite in condition for prerequisite in ("묘지의", "다른 몬스터", "컨트롤하고", "이 카드가 특수")):
                return True
            if self.kind(code) & MONSTER and any(t in condition for t in
                    ("패에서 버리고", "이 카드를 패에서 묘지로 보내고", "패의 이 카드를 버리고")):
                return True
            if is_spell and not any(t in condition for t in ("제외하고", "대상으로", "릴리스", "소재", "장 존재", "묘지의")):
                return True
        return False

    def primary_tags(self, deck):
        main = deck_list(deck, "main")
        hits = Counter(tag for code in main for tag in self.tags.get(code, ()))
        return [tag for tag, count in hits.most_common() if count >= 6 and len(
            {code for code in main if tag in self.tags.get(code, ())}) >= 3]

    def make_engine(self, key, title, ace, pool, seed_main=()):
        pool = set(pool)
        weights = Counter(c for c in seed_main if c in self.valid and c not in ONE_COPY)
        if ace in self.valid:
            pool.add(ace)
        pool = {c for c in pool if c in self.valid and c not in ONE_COPY}
        main = Counter()
        extra = []
        for code in sorted(pool, key=lambda c: (-weights[c], -bool(self.looks_like_starter(c)),
                                                   -self.year(c), c)):
            if self.kind(code) & EXTRA_TYPES:
                extra.append(code)
            else:
                count = 3 if code in KNOWN_STARTS.get(key, ()) or self.looks_like_starter(code) else max(2, min(3, weights[code]))
                main[code] = count
        if ace not in pool:
            score = lambda c: (weights[c], bool(self.kind(c) & EXTRA_TYPES), self.year(c), self.card(c)["atk"], -c)
            bosses = [c for c in pool if self.kind(c) & MONSTER and c not in self.traps]
            ace = max(bosses or pool, key=score, default=0)
        return Engine(key, title, ace, main, tuple(extra), KNOWN_STARTS.get(key, ()), frozenset(pool))

    def from_seed(self, seed, *, key=None, expand=True):
        main = deck_list(seed, "main")
        extra = deck_list(seed, "extra")
        ace = seed.get("sigCard", 0)
        tags = self.primary_tags(seed)
        tag = next(iter(tags), 0)
        if len(main) - sum(c in self.traps or c in ONE_COPY for c in main) <= 15 or not tag:
            cover = {c for c in main + extra if c in self.valid and c not in ONE_COPY and c not in self.traps}
        else:
            cover = {c for c in main + extra if tag in self.tags.get(c, ())}
            if expand:
                cover.update(self.by_tag[tag])
        if sum(code in cover for code in main) < 10 or len(cover & set(main)) < 5:
            cover.update(c for c in main if c in self.valid and c not in ONE_COPY and c not in self.traps)
        # Old recipes can contain spells as their ace. Treat an owned monster as ace.
        if ace in DRAW:
            ace = 0
        title = seed["title"].split(" · ")[0]
        if expand:
            cover.update(c for c in extra if c in self.valid)
        core = self.make_engine(key or (f"family-{tag:03x}" if tag else f"preset-{seed['id']}"),
                                title, ace, cover, main + extra)
        combined = core.main.copy()
        if expand:
            # Respect the curated engine first, then offer the original support.
            for c in dict.fromkeys(main):
                if c in self.valid and c not in ONE_COPY and not self.kind(c) & EXTRA_TYPES:
                    combined.setdefault(c, main.count(c))
        return Engine(core.key, core.title, core.ace, combined, core.extra, (), core.core)

    def for_family(self, cid, key):
        profile = self.global_engines[key]
        if key in CUSTOM:
            return profile
        # Borrow specific support and fusion/material pairs from the character's
        # most nearly related preset, without turning it into a second copy.
        recipes = self.seeds[cid]
        def matching(seed):
            main = deck_list(seed, "main")
            return sum(c in profile.core for c in main + deck_list(seed, "extra"))
        seed = max(recipes, key=matching)
        if matching(seed) <= 3:
            return profile
        ace = profile.ace
        if FAMILIES[key][2] == 0 and seed.get("sigCard", 0) in profile.core:
            ace = seed.get("sigCard", 0)
        core = self.make_engine(key, profile.title, ace, profile.core, deck_list(seed, "main"))
        main = core.main.copy()
        for c in dict.fromkeys(deck_list(seed, "main")):
            if (c in self.valid and c not in ONE_COPY and not self.kind(c) & EXTRA_TYPES
                    and self.compatible_with_core(c, core)):
                main.setdefault(c, deck_list(seed, "main").count(c))
        extra = tuple(dict.fromkeys(core.extra + tuple(deck_list(seed, "extra"))))
        return Engine(key, core.title, core.ace, main, extra, core.starters, core.core)

    def support_engines(self, originals):
        race, attr = Counter(), Counter()
        base = set()
        for engine in originals:
            base.update(engine.core)
            for c in engine.main:
                if self.kind(c) & MONSTER and c not in self.traps:
                    race[self.card(c)["race"]] += 1
                    attr[self.card(c)["attr"]] += 1
        def relevance(engine):
            candidates = [self.card(c) for c in engine.core if c in engine.main and self.kind(c) & MONSTER]
            if not candidates:
                return 0
            synergy = sum(race[c["race"]] * 2 + attr[c["attr"]] for c in candidates) / len(candidates)
            return synergy / (1 + len(engine.core & base))
        return sorted((e for e in self.global_engines.values() if len(e.main) >= 9 and e.key not in CUSTOM),
                      key=lambda e: (-relevance(e), -sum(self.looks_like_starter(code) for code in e.main), e.key))

    @staticmethod
    def distinct(a, b):
        return a.key != b.key and min(len(a.core - b.core), len(b.core - a.core)) >= 5

    def plans(self, char, ids):
        cid = char["id"]
        if cid in EXACT_PLANS:
            return [self.for_family(cid, k) for k in EXACT_PLANS[cid]]
        if cid in FEATURED:
            first, second, third, fourth, strongest = FEATURED[cid]
            if strongest == fourth:
                order = (first, second, third, fourth, first, second, third, fourth, first, fourth)
            elif strongest == first:
                order = (first, second, third, fourth, second, third, fourth, first, second, first)
            elif strongest == third:
                order = (first, second, third, fourth, first, second, fourth, third, first, third)
            else:
                order = (first, second, third, fourth, first, third, fourth, second, first, second)
            return [self.for_family(cid, k) for k in order]
        if cid in FEATURED_RELATED or "안나" in char["name"] or "Anna" in char["name"]:
            order = FEATURED_RELATED.get(cid, ("train", "machina", "gadget"))
            first, second, third = [self.for_family(cid, k) for k in order]
            if third.key == first.key:
                candidate = next(e for e in self.support_engines([first, second])
                                 if self.distinct(e, first) and self.distinct(e, second))
                return [first, second, candidate, first] if len(ids) == 4 else [first, second, candidate, second, first]
            return [first, second, second, third] if len(ids) == 4 else [first, second, third, second, third]
        options = []
        for seed in self.seeds[cid]:
            engine = self.from_seed(seed)
            if all(self.distinct(engine, previous) for previous in options):
                options.append(engine)
        if not options:
            raise ValueError(f"Opponent {cid} has no seed recipe")
        required = (len(ids) + 1) // 2
        for engine in self.support_engines(options):
            if len(options) >= required:
                break
            if all(self.distinct(engine, previous) for previous in options):
                options.append(engine)
        if len(options) < required:
            raise ValueError(f"Opponent {cid} needs {required} engines; found {[e.key for e in options]} "
                             f"with {len(options[0].core)} core names for the first.")
        first = options[0]
        final = options[1]
        middle = [e for e in options[2:required]]
        if len(ids) == 4:
            return [first, final, first, final]
        return [first, middle[0], final, first, final]

    def can_base_play(self, code, rank):
        if code in self.traps:
            return True
        data = self.card(code)
        if self.is_normal(code):
            return data["atk"] >= rank or data["def"] >= 1900 or self.looks_like_starter(code)
        if self.kind(code) & TRAP:
            return code not in (31893528, 67287533, 94772232, 30170981) and any(t in data["desc"] for t in
                ("무효", "파괴", "제외", "패에 넣", "드로우", "특수 소환", "공격", "데미지", "장당"))
        return self.looks_like_starter(code)

    def fit_extra(self, main, ranked):
        monsters = [self.card(c) for c in main if self.kind(c) & MONSTER]
        levels = Counter(c["level"] for c in monsters)
        tuners = [c["level"] for c in monsters if c["type"] & TUNER]
        partners = [c["level"] for c in monsters if not c["type"] & TUNER]
        ranks = {lv for lv, hits in levels.items() if hits >= 3}
        synch_levels = {a + b for a in tuners for b in partners}
        has_fusion = any("융합" in self.card(c)["desc"] for c in main)
        new = []
        for code in ranked:
            if code not in self.valid or not self.kind(code) & EXTRA_TYPES:
                continue
            card = self.card(code)
            kind = card["type"]
            if kind & XYZ and card["level"] not in ranks:
                continue
            if kind & SYNCHRO and card["level"] not in synch_levels:
                continue
            if kind & FUSION and not has_fusion:
                continue
            if kind & LINK and len(monsters) < 10:
                continue
            new.append(code)
        return new

    def choose_extra(self, engine, main, level, maximum, add, result):
        high = level >= min(9, maximum)
        own = [c for c in engine.extra if c in self.valid and self.kind(c) & EXTRA_TYPES]
        preferred = [c for c in own if c in engine.core]
        other = self.fit_extra(main, own)
        generics = self.fit_extra(main, LINK_TOOLS + SYNCHRO_TOOLS + XYZ_TOOLS)
        for code in preferred[:(10 if high else 13)]:
            add("extra", code)
        preferred.extend(c for c in preferred[:6] if c in engine.core and self.kind(c) & FUSION)
        if high:
            bosses = (29301450, 84815190, 27548199, 4280258, 86066372)
            selected = [code for code in bosses if code in generics]
            for code in (selected + [code for code in generics if code not in selected])[:5]:
                add("extra", code)
        for code in dict.fromkeys(other + generics + preferred):
            add("extra", code)
        # By type and level, the neutral fallback cards have broad utility with
        # generic material, or can be banished to pay an included Pot's cost.
        for code in EXTRA_FODDER + LINK_TOOLS + SYNCHRO_TOOLS + XYZ_TOOLS:
            if len(result["extra"]) >= 15:
                break
            add("extra", code)

    def tune(self, source, engine, level, maximum, used):
        final = level >= min(9, maximum)
        low = level <= 2 and source["seriesIdx"] != 6
        hand_limit = 2 if low else 4
        result = {section: [] for section in ("main", "extra", "side")}
        copies = Counter()
        hand_total = 0
        main_limit = 40

        def add(section, code, quantity=1):
            nonlocal hand_total
            if code not in self.valid or bool(self.kind(code) & EXTRA_TYPES) != (section == "extra"):
                return
            key = self.key(code)
            max_cards = main_limit if section == "main" else 15
            cap = 1 if key in ONE_COPY else 3
            quantity = min(quantity, cap - copies[key], max_cards - len(result[section]))
            if code in self.traps:
                quantity = min(quantity, hand_limit - hand_total)
                hand_total += quantity
            if quantity > 0:
                copies[key] += quantity
                result[section].extend([code] * quantity)

        for code in DRAW:
            add("main", code)
        ace = engine.ace
        if ace in self.valid and ace not in DRAW:
            add("extra" if self.kind(ace) & EXTRA_TYPES else "main", ace)

        def priority(code):
            desired = code in engine.starters or self.looks_like_starter(code)
            recent_score = self.year(code)
            return (
                code not in engine.core,
                not desired,
                -(min(engine.main[code], 3)),
                -recent_score if final else recent_score,
                not self.is_normal(code),
                -self.card(code)["atk"],
                code,
            )

        small_core = sum(code in engine.core and not self.kind(code) & EXTRA_TYPES for code in engine.main) <= 9
        for code in sorted(engine.main, key=priority):
            if code not in engine.core or len(result["main"]) >= 30:
                continue
            quantity = engine.main[code] - copies[self.key(code)]
            if code in engine.starters or self.looks_like_starter(code):
                quantity = 3 - copies[self.key(code)]
            elif small_core and code not in (31893528, 67287533, 94772232, 30170981):
                quantity = max(quantity, 2 - copies[self.key(code)])
            elif self.is_normal(code) and not self.can_base_play(code, 1600):
                quantity = min(quantity, 1)
            elif not self.is_normal(code) and self.kind(code) & MONSTER and code != ace:
                quantity = min(quantity, 2 if final else 1)
            add("main", code, min(quantity, 30 - len(result["main"])))

        if source["id"] in (20677, 1002020):
            add("main", 13035077, 3)
            add("main", 73628505, 2)
        if engine.key == "family-137":
            add("main", 57103969, 3)
        if source["id"] == 20675:
            # Domain cannot restrict the opposing Extra Deck when this deck
            # carries one. Preserve tribute control with field-independent locks.
            for code, count in ((95457011, 3), (59463312, 2), (47084486, 3),
                                (33746252, 2), (35059553, 2)):
                add("main", code, count)
        own_support = [c for c in engine.main if c not in engine.core and c not in ONE_COPY]
        related = self.supplement_for(engine, result["main"], source, level)
        if low:
            trap_slots = 0 if level == 1 else 1
        else:
            trap_slots = (2 if level % 2 else 3) if not final else 4
        trap_cycle = HAND_TRAP_ORDER[1 + level % 3:] + HAND_TRAP_ORDER[:1 + level % 3]
        if source["seriesIdx"] == 6 or not low:
            for code in trap_cycle[:trap_slots]:
                if hand_total < hand_limit:
                    add("main", code)

        for code in related:
            if len(result["main"]) >= (35 if final else 34):
                break
            if not copies[self.key(code)] and (code not in self.traps or hand_total < trap_slots):
                add("main", code, min(2, (35 if final else 34) - len(result["main"])))
        own_support = sorted((c for c in own_support if c in self.valid), key=priority)
        rotation = level % len(CONTROL_TOOLS)
        utility = POWER_TOOLS if final else CONTROL_TOOLS[rotation:] + CONTROL_TOOLS[:rotation]
        for code in utility[:5]:
            if code not in self.valid or not self.can_use_support(code, result["main"]):
                continue
            if len(result["main"]) < 39 and not copies[self.key(code)]:
                add("main", code)
        for code in chain(own_support, related, utility, FALLBACK_MAIN):
            if len(result["main"]) == 40:
                break
            if code in self.valid and self.can_use_support(code, result["main"]) and not copies[self.key(code)]:
                add("main", code)
        for code in FALLBACK_MAIN:
            if len(result["main"]) == 40:
                break
            if code in self.valid and self.can_use_support(code, result["main"]):
                add("main", code)

        package_extra, proved_starters, package_cards = (), set(), set()
        self.prioritize_searchers(engine, level, result, copies)
        initial = self.opening_model(engine, result["main"], result["extra"])
        if sum(code in initial["starters"] for code in result["main"]) < 16:
            main_limit = 60
            package_extra, proved_starters, package_cards = self.reinforce_opening(
                engine, result["main"], source, level, add, copies)
        for code in package_extra:
            add("extra", code)
        self.choose_extra(engine, result["main"], level, maximum, add, result)
        if not 40 <= len(result["main"]) <= 60 or len(result["extra"]) != 15:
            raise ValueError(f"Deck {source['id']} {engine.title} did not reach the allowed deck sizes")
        if ace not in result["main"] + result["extra"] or ace in DRAW:
            ace = next(code for code in result["main"] if self.kind(code) & MONSTER and code not in self.traps)
        main = result["main"]
        self.improve_counts(engine, result, source, level, proved_starters, package_cards, copies)
        self.improve_weak_profiles(engine, result, source, proved_starters, package_cards, package_extra)
        if source["id"] in (51430, 51434, 51775, 51814, 51438, 85012, 85014, 51812, 51772, 36035):
            self.finish_cyberse(engine, result, proved_starters)
        if source["id"] == 51110:
            # Fubuki's original dragon is the direct target of Black Stone.
            for code, target in ((74677422, 2), (66574418, 3)):
                for _ in range(max(0, target - main.count(code))):
                    spare = next((c for c in (31733941, 64335804, 58257569, 18491580) if c in main
                                  and c != engine.ace), None)
                    if spare is None:
                        main.append(code)
                    else:
                        main[main.index(spare)] = code
            proved_starters.add(66574418)
        if source["id"] == 51687:
            # Replace similarly named Trickstar with the actual Rank 4 starter.
            result["main"][:] = [c for c in main if c != 59604521]
            if main.count(7150545) < 3:
                main.append(7150545)
            proved_starters.add(7150545)
        if source["id"] == 50410:
            # Populate all four distinct 100 ATK targets before adding Gathering.
            cardians = (81752019, 30382214, 17141718, 54135423, 80630522)
            targets = {card: (3 if card == 81752019 else 2) for card in cardians}
            targets[78785392] = 3
            for card, count in targets.items():
                for _ in range(max(0, count - main.count(card))):
                    spare = next((c for c in main if c in (88472456, 11321183, 48092532, 67616300, 33782437)
                                  and c != engine.ace), None)
                    if spare is None:
                        main.append(card)
                    else:
                        main[main.index(spare)] = card
            proved_starters.add(78785392)
        signature = tuple(sorted(main)), tuple(sorted(result["extra"]))
        if signature in used:
            # Change one actual tech choice while preserving the primary engine
            # and the exact Main/Extra quantities.
            replaceable = [c for c in reversed(main) if c not in engine.core and c not in DRAW]
            for code in utility + FALLBACK_MAIN:
                if code not in self.valid or code in self.traps or copies[self.key(code)] or self.kind(code) & EXTRA_TYPES:
                    continue
                if not self.can_use_support(code, main):
                    continue
                for old in replaceable:
                    changed = list(main)
                    changed[changed.index(old)] = code
                    candidate = tuple(sorted(changed)), signature[1]
                    if candidate not in used:
                        main[:] = changed
                        signature = candidate
                        break
                if signature not in used:
                    break
        used.add(signature)
        core = sorted(engine.core & set(main + result["extra"]))
        model = self.opening_model(engine, main, result["extra"])
        model["starters"] = sorted(set(model["starters"]) | (set(main) & set(proved_starters)))
        if SMALL_WORLD in main:
            model["smallWorld"] = self.small_world_routes(main, model["starters"])
        theme = f"{engine.title} · {self.level_label(level, maximum)}"
        return {**source, **result, "sigCard": ace, "title": theme, "engine": engine.key,
                "engineCards": core, "opening": model}

    @staticmethod
    def level_label(level, maximum):
        if level == maximum:
            return "최종 보스"
        if level >= 9:
            return "정예 실전"
        return ("입문", "원작 전술", "우선 전개", "전술 보강", "상급", "실전",
                "실전 전개", "전술 완성")[level - 1] if level <= 8 else f"레벨 {level}"

    def compatible_with_core(self, code, engine):
        if code in engine.core:
            return True
        # Shared monster type alone is insufficient for a cast member's alternate
        # deck. Keep cards named by this engine or genuinely generic utilities.
        if code in FALLBACK_MAIN + POWER_TOOLS + CONTROL_TOOLS:
            return True
        tags = self.tags.get(code, set())
        if not tags:
            return self.is_normal(code) and any(c in engine.core and
                self.kind(c) & MONSTER and self.card(c)["race"] == self.card(code)["race"] and
                self.card(c)["attr"] == self.card(code)["attr"] for c in engine.main)
        return any(tags & self.tags.get(c, set()) for c in engine.main if c in engine.core)

    def supplement_for(self, engine, main, source, level):
        original = [c for c in dict.fromkeys(deck_list(source, "main")) if c in self.valid and
                    not self.kind(c) & EXTRA_TYPES and c not in ONE_COPY and self.compatible_with_core(c, engine)]
        monsters = [self.card(c) for c in main if self.kind(c) & MONSTER]
        race = Counter(c["race"] for c in monsters)
        attr = Counter(c["attr"] for c in monsters)
        strongest_race = next(iter(race.most_common(1)), (0, 0))[0]
        strongest_attr = next(iter(attr.most_common(1)), (0, 0))[0]
        compatible = [c for c in self.valid if c not in self.traps and self.is_normal(c) and self.key(c) == c
                      and self.card(c)["race"] == strongest_race and self.card(c)["attr"] == strongest_attr
                      and not self.tags.get(c) and (self.looks_like_starter(c) or self.card(c)["atk"] >= 1600)]
        def score(code):
            content = self.card(code)
            return (code not in engine.core, not self.can_base_play(code, 1600),
                    not self.looks_like_starter(code), -content["atk"], -self.year(code) if level >= 4 else self.year(code), code)
        return sorted(dict.fromkeys(original + compatible), key=score)

    def provides_access(self, code, target):
        card = self.card(target)
        return {
            # A one-card search counts only for starters it can add or summon.
            73628505: bool(card["type"] & 0x80000),
            32807846: card["race"] == 1 and card["level"] <= 4,
            47325505: card["race"] == 65536 and card["level"] <= 6,
            57103969: card["race"] == 32768 and card["level"] <= 4,
            57160136: card["race"] == 16777216 and card["level"] <= 4,
            67723438: card["race"] == 1048576 and card["level"] <= 3,
            33907039: self.is_normal(target) and card["atk"] == 0,
            60600126: self.is_normal(target) and card["name"].startswith("사이버 드래곤"),
            911883: self.is_normal(target) and bool(card["type"] & 0x10),
        }.get(code, False)

    def searchers_for(self, starters):
        candidates = (73628505, 32807846, 47325505, 57103969, 57160136, 67723438,
                      33907039, 60600126, 911883)
        return {code for code in candidates if code in self.valid and
                any(self.provides_access(code, target) for target in starters if target != code)}

    def prioritize_searchers(self, engine, level, result, copies):
        main = result["main"]
        model = self.opening_model(engine, main, result["extra"])
        solo = set(model["starters"])
        if sum(code in solo for code in main) >= 16:
            return
        for code in sorted(self.searchers_for(solo)):
            count = 2 if code == 73628505 or level == 1 else 3
            # Maintain the thematic core and interaction while changing weaker
            # generic pieces to searchers that open the same deck's own line.
            replaceable = [index for index, old in enumerate(main)
                           if old not in engine.core and old not in solo and old not in DRAW
                           and old not in self.traps and old != code]
            for i in reversed(replaceable[:max(0, min(3 - copies[self.key(code)], count))]):
                copies[self.key(main[i])] -= 1
                main[i] = code
                copies[self.key(code)] += 1
            if copies[self.key(code)]:
                solo.add(code)
            if sum(c in solo for c in main) >= 16:
                break

    def reinforce_opening(self, engine, main, source, level, add, copies):
        initial = self.opening_model(engine, main, [])
        useful = set(initial["starters"])
        candidates = [c for c in engine.main if c in self.valid and c not in self.traps
                      and self.can_use_support(c, main)
                      and (c in engine.starters or self.looks_like_starter(c))]
        protected = set()
        def make_room(limit):
            if len(main) < limit:
                return
            removable = [i for i, c in enumerate(main) if c != engine.ace and c not in DRAW
                         and c not in useful and c not in self.traps and c not in protected]
            # Retain 18 primary cards, or all primary cards when fewer are
            # present. Replace weak off-theme technologies before core cards.
            core_count = sum(c in engine.core for c in main)
            loose = [i for i in removable if main[i] not in engine.core or core_count > 18]
            if not loose:
                return
            i = min(loose, key=lambda i: (main[i] in engine.core, self.can_base_play(main[i], 1600), -i))
            copies[self.key(main.pop(i))] -= 1

        for code in sorted(candidates, key=lambda c: (c not in engine.core, -main.count(c), c)):
            if sum(c in useful for c in main) >= 17:
                break
            if code not in main and code not in engine.core:
                continue
            for _ in range(3 - copies[self.key(code)]):
                make_room(42)
                add("main", code)
            if code in main:
                useful.add(code)

        monsters = [self.card(c) for c in main if self.kind(c) & MONSTER and c not in self.traps]
        races = Counter(c["race"] for c in monsters).most_common()
        attributes = Counter(c["attr"] for c in monsters).most_common()
        race = races[0][0] if races else 0
        attr = attributes[0][0] if attributes else 0
        if engine.key in ("ra", "obelisk", "slifer-holactie"):
            race = 2097152
        # Avoid these cross-engine choices when the archetype can impose its
        # own material/attribute/type lock or has a direct competing plan.
        restricted = engine.key in {
            "blackwing", "speedroid", "windwitch", "resonator", "code-talker",
            "salamangreat", "destiny-board", "paleozoic", "blue-eyes", "timelord",
            "masked-hero", "destiny-hero", "drytron", "infernity", "toon",
        }
        theme_pack = self.opening_packages.get(race) if not restricted else None
        if engine.key in ("shark", "abyss-shark", "mermail", "atlantean"):
            theme_pack = self.opening_packages.get(262144)
        if engine.key in ("cyber-blader", "phantom-knights", "flame-swordsman"):
            theme_pack = self.opening_packages.get(1)
        if engine.key in ("speedroid", "windwitch", "gusto"):
            theme_pack = self.wind_package
        if engine.key in ("firewall", "ignister", "mathmech", "code-talker"):
            theme_pack = self.opening_packages.get(16777216)
        if engine.key == "volcanic":
            theme_pack = self.opening_packages.get(128)
        if engine.key == "train":
            theme_pack = self.opening_packages.get(32)
        attribute_pack = self.attribute_packages.get(attr) if not restricted and (level >= 3 or source["seriesIdx"] == 6) else None
        if source["charId"] == 125:
            theme_pack = self.special_packages.get("alchemy")
        elif source["charId"] == 192 and 86327225 in main:
            theme_pack = self.special_packages.get("ritual")
        elif source["charId"] in (179, 2017) and 31533704 in main:
            theme_pack = self.special_packages.get("combat-machine")
        featured_races = [self.opening_packages.get(c) for c, hits in races[1:] if hits >= 3]
        available = featured_races if not restricted else []
        if not restricted and attr == 0x20 and level <= 2 and race == 8:
            theme_pack = self.classic_fiend
        extras = []
        proved = set()
        selected = 0
        for package in (theme_pack, attribute_pack, *available):
            if selected >= 2:
                break
            if package is None or sum(c in useful for c in main) >= max(17, (len(main) * 37 + 99) // 100):
                continue
            cards, extra, starters = package
            missing = sum(max(0, count - copies[self.key(code)]) for code, count in cards.items())
            if len(extras) + len(extra) > 4 or missing + 40 > 60:
                continue
            if sum(c in self.traps for c in cards) != 0:
                continue
            protected.update(cards)
            for c, count in cards.items():
                for _ in range(max(0, count - copies[self.key(c)])):
                    make_room(46 if level >= 3 else 43)
                    add("main", c)
            if all(copies[self.key(c)] >= amount for c, amount in cards.items()):
                if not set(starters) - proved:
                    continue
                useful.update(starters)
                proved.update(starters)
                extras.extend(extra)
                selected += 1
        if sum(c in useful for c in main) < 16 and len(extras) < 4 and not restricted:
            # Add searchers that reach a starter introduced by the selected
            # package. Existing package pieces are protected during the swap.
            for code in sorted(self.searchers_for(proved)):
                amount = min(3 - copies[self.key(code)], 2 if level <= 2 else 3)
                for _ in range(amount):
                    make_room(47)
                    add("main", code)
                if code in main:
                    proved.add(code)
                    useful.add(code)
        return tuple(dict.fromkeys(extras)), proved, protected

    def improve_counts(self, engine, deck, source, level, proved, protected, copies):
        # Increase access to actual starts already present. Keep the native
        # fixture strategies and the character's primary engine intact.
        if source["id"] in (20672, 20673, 20675, 20687):
            return
        main, extra = deck["main"], deck["extra"]
        model = self.opening_model(engine, main, extra)
        solo = set(model["starters"]) | set(proved)
        pair_roles = Counter(c for pair in model["pairs"] for c in pair)
        if not solo and not pair_roles:
            return

        def swappable(code, preserve_core):
            pool = [i for i, c in enumerate(main) if c != code and c not in DRAW and c not in solo
                    and c != engine.ace and c not in self.traps and c not in protected and c not in (70368879, 84211599, 67616300, 33782437, SMALL_WORLD, 18063928, 25259669, 2460565)
                    and (c not in engine.core or not preserve_core)]
            if not pool:
                return None
            i = min(pool, key=lambda i: (main[i] in engine.core, pair_roles[main[i]], self.can_base_play(main[i], 1500), -i))
            old = main[i]
            if old in engine.core:
                core_cards = set(c for c in main if c in engine.core)
                if sum(c in core_cards for c in main) <= 18 or copies[self.key(old)] == 1 and len(core_cards) <= 6:
                    return None
            return i

        def swap(code, preserve_core):
            i = swappable(code, preserve_core)
            if i is None:
                return False
            copies[self.key(main[i])] -= 1
            main[i] = code
            copies[self.key(code)] += 1
            return True

        def enough():
            return sum(c in solo for c in main) * 100 >= len(main) * 40

        for code in sorted(solo, key=lambda c: (c not in engine.core, -pair_roles[c], -main.count(c), c)):
            while copies[self.key(code)] < 3 and not enough():
                if not swap(code, preserve_core=level <= 2):
                    break
        for code in sorted(pair_roles, key=lambda c: (-pair_roles[c], c)):
            if code in self.traps or pair_roles[code] < 2 or not self.can_base_play(code, 1500):
                continue
            if copies[self.key(code)] < 3 and not enough():
                swap(code, preserve_core=level <= 2)
        if not enough() and SMALL_WORLD in self.valid:
            for _ in range(3 - copies[self.key(SMALL_WORLD)]):
                routes = self.small_world_routes(main, solo)
                if sum(main.count(c) for c in {r[0] for r in routes} - solo) < 5:
                    break
                if not swap(SMALL_WORLD, preserve_core=True):
                    break
        regular_level4 = {c for c in main if self.is_normal(c) and self.card(c)["level"] == 4}
        rank4 = any(self.kind(c) & XYZ and self.card(c)["level"] == 4 for c in extra)
        has_generic_link = any(c in extra for c in (29301450, 2857636, 65741786))
        if not enough() and rank4 and len(regular_level4) >= 4 and engine.key not in (
            "mathmech", "firewall", "code-talker", "ignister", "salamangreat", "infernity"
        ):
            if engine.key in ("shark", "abyss-shark", "atlantean", "mermail"):
                extenders = (18063928,)
            elif any(self.card(c)["race"] == 32 for c in regular_level4):
                extenders = (18063928, 25259669)
            else:
                extenders = (25259669,)
            for code in extenders:
                if code in self.valid:
                    for _ in range(3 - copies[self.key(code)]):
                        if not swap(code, preserve_core=True):
                            break
        elif not enough() and has_generic_link and engine.key not in (
            "mathmech", "firewall", "code-talker", "ignister", "salamangreat", "infernity", "shark", "abyss-shark"
        ):
            normal_openers = {c for c in main if self.is_normal(c) and c in engine.core and c not in self.traps}
            if len(normal_openers) >= 6 and any(self.card(c)["race"] == 1 for c in normal_openers):
                for _ in range(3 - copies[self.key(2460565)]):
                    if not swap(2460565, preserve_core=True):
                        break
        draw_counts = ((70368879, 3), (84211599, 2), (67616300, 3), (33782437, 3))
        for code, target_count in draw_counts:
            if enough() or code not in self.valid:
                break
            if level == 1 and code == 84211599:
                continue
            for _ in range(target_count - copies[self.key(code)]):
                if not swap(code, preserve_core=True):
                    break
        while not enough() and len(main) > 40:
            i = swappable(0, preserve_core=True)
            if i is None or pair_roles[main[i]]:
                break
            copies[self.key(main.pop(i))] -= 1

    def finish_cyberse(self, engine, deck, proved):
        # Both decks use Link Disciple as the one-material link that sends
        # Dotscaper to the GY. Draconnet supplies a second body via Bitron.
        main, extra = deck["main"], deck["extra"]
        if 32995276 not in extra:
            repeat = next((c for c in extra if extra.count(c) > 1 and c != engine.ace), None)
            if repeat is None:
                repeat = next(c for c in reversed(extra) if c != engine.ace and c not in (29301450, 86066372))
            extra[extra.index(repeat)] = 32995276
        required = {62706865: 3, 36211150: 2, 18789533: 3, 16188701: 3}
        # Replace cards which require an established Code Talker board.
        slots = (4433488, 19943114, 36368606, 42461852, 34767865, 82886276)
        for code, count in required.items():
            for _ in range(max(0, count - main.count(code))):
                filler = next((c for c in slots if c in main), None)
                if filler is None:
                    filler = next((c for c in reversed(main) if c not in engine.core and c not in proved
                                   and c not in required and c not in DRAW and c not in self.traps
                                   and c != engine.ace and not self.looks_like_starter(c)), None)
                if filler is not None:
                    main[main.index(filler)] = code
                elif len(main) < 60:
                    main.append(code)
                else:
                    raise ValueError("No slot for the Cyberse starter package")
        proved.discard(34010534)
        proved.update((62706865, 18789533, 16188701))
        if 57160136 in main:
            proved.add(57160136)

    def score_opening(self, deck_id, engine, main, extra, proved):
        opening = self.opening_model(engine, main, extra)
        starters = set(opening["starters"]) | (set(main) & set(proved))
        routes = defaultdict(list)
        if SMALL_WORLD in main:
            for revealed, bridge, result in self.small_world_routes(main, starters):
                routes[revealed].append((bridge, result))
        model = (starters, set(opening["plays"]), set(map(tuple, opening["pairs"])), routes)
        # Use an independent seed for recipe selection. The audit tests the
        # checked-in deck IDs and samples more hands without this offset.
        rate, bricks = opening_rate({"id": deck_id + 9500001, "main": main}, 240, model)
        return rate - max(0.0, bricks - 0.04) * 2.0, starters

    def improve_weak_profiles(self, engine, deck, source, proved, protected, preserved_extra):
        if source["id"] in (20672, 20673, 20675, 20687):
            return
        main, extra = deck["main"], deck["extra"]
        score, starters = self.score_opening(source["id"], engine, main, extra, proved)
        studied = {
            50581: ("psychic",),
            51900: ("rokket-start", "fiendsmith"),
            51391: ("fiendsmith",),
            85050: ("insect-gokipole",),
            85051: ("insect-gokipole",),
            50583: ("psychic", "invoked"),
            50410: ("adventure",),
            52282: ("insect-gokipole",),
            51687: ("shark-cutter", "marincess"),
            51322: ("adventure",),
            36035: ("cyberse-normal",),
            52321: ("adventure",),
            83050: ("insect-gokipole",),
            85030: ("cyberse-normal", "link-twins"),
            83044: ("insect-gokipole",),
            83042: ("insect-gokipole",),
            50411: ("hero-air",),
            51510: ("rank-four",),
            51540: ("snake-eye-fire",),
            51513: ("cyberse-normal", "rank-four"),
            83020: ("snake-eye-fire",),
            52210: ("invoked",),
            51814: ("cyberse-normal",),
            51177: ("invoked",),
            51775: ("cyberse-normal",),
        }.get(source["id"], ())
        target = 0.970 if studied else 0.945
        if score >= target:
            return
        protected = set(protected)
        protected_extra = set(preserved_extra)
        monsters = [self.card(c) for c in main if self.kind(c) & MONSTER and c not in self.traps]
        races = Counter(c["race"] for c in monsters)
        attrs = Counter(c["attr"] for c in monsters)
        # Prefer a package that meshes with the primary monster type. A second
        # engine is optional, so it need not be played in the same turn as a
        # card that restricts materials or normal-summoned effects.
        options = [(count * 3, self.opening_packages[race]) for race, count in races.items()
                   if race in self.opening_packages and count >= 2]
        options.extend((count, self.attribute_packages[attr]) for attr, count in attrs.items()
                       if attr in self.attribute_packages and count >= 3)
        for name, affinity in (("bees", races[2048] * 3), ("speed-machine", races[32] * 2),
                               ("combat-machine", races[32] * 2),
                               ("snake-eye-fire", races[128] * 3),
                               ("ancient-warrior-link", races[32768] * 3)):
            if affinity >= 6 and name in self.special_packages:
                options.append((affinity, self.special_packages[name]))
        if source["charId"] == 125 and "alchemy" in self.special_packages:
            options.append((9, self.special_packages["alchemy"]))
        level_4 = sum(self.is_normal(c) and self.card(c)["level"] == 4 for c in main)
        link_count = sum(bool(self.kind(c) & LINK) for c in extra)
        if "rank-four" in self.special_packages and level_4 >= 4 and (
            source["seriesIdx"] in (3, 6) or sum(bool(self.kind(c) & XYZ) for c in extra) >= 5
        ):
            options.append((level_4, self.special_packages["rank-four"]))
        if "link-twins" in self.special_packages and link_count >= 4 and source["seriesIdx"] in (5, 6):
            options.append((link_count, self.special_packages["link-twins"]))
        # Older decks can pair their own resource plan with a classical
        # Spellbook/GY start, or a hand-opened Adventure sequence. Pick the
        # alternate only if its actual recipe clears the measured deficit.
        if score < 0.87 and source["seriesIdx"] in (0, 1, 2):
            if 0x01 in self.attribute_packages:
                options.append((6, self.attribute_packages[0x01]))
            if races.get(2, 0) >= 2 and 2 in self.opening_packages:
                options.append((8, self.opening_packages[2]))
        if races.get(8, 0) >= 2 and self.classic_fiend is not None:
            options.append((races[8] * 2, self.classic_fiend))
        if attrs.get(8, 0) >= 3 and self.wind_package is not None:
            options.append((attrs[8], self.wind_package))
        additionally = {
            **self.special_packages,
            "adventure": self.attribute_packages.get(0x01),
            "invoked": self.attribute_packages.get(0x20),
            "psychic": self.opening_packages.get(1048576),
            "fiendsmith": self.opening_packages.get(8),
        }
        for name in studied:
            package = additionally.get(name)
            if package is not None:
                options.append((30, package))
        for _ in range(2):
            winner = None
            for affinity, package in options:
                wanted, package_extras, package_starts = package
                if not set(package_starts) - starters or not (set(wanted) & set(main) or affinity >= 6):
                    continue
                if set(wanted) & self.traps:
                    continue
                candidate = list(main)
                support = set(wanted) | protected
                if len(protected | set(wanted)) > 24:
                    continue
                count = Counter(candidate)
                core_count = sum(c in engine.core for c in candidate)
                core_names = set(candidate) & engine.core
                for code, goal in wanted.items():
                    for _ in range(max(0, goal - count[code])):
                        removables = [i for i, c in enumerate(candidate)
                                      if c not in support and c not in DRAW and c not in starters
                                      and c not in self.traps and c != engine.ace
                                      and (c not in engine.core or (core_count > 17 and
                                           (count[c] > 1 or len(core_names) > 6)))]
                        if removables:
                            i = min(removables, key=lambda i: (candidate[i] in engine.core,
                                                             self.can_base_play(candidate[i], 1500), -i))
                            old = candidate.pop(i)
                            count[old] -= 1
                            if old in engine.core:
                                core_count -= 1
                                if count[old] == 0:
                                    core_names.remove(old)
                        if len(candidate) >= 58:
                            break
                        candidate.append(code)
                        count[code] += 1
                if any(count[c] < goal for c, goal in wanted.items()):
                    continue
                extra_candidate = list(extra)
                for card in package_extras:
                    if card in extra_candidate:
                        continue
                    removals = [i for i, c in enumerate(extra_candidate) if c not in engine.core and
                                c not in package_extras and c not in protected_extra and c in EXTRA_FODDER + LINK_TOOLS + SYNCHRO_TOOLS + XYZ_TOOLS]
                    if not removals and studied:
                        removals = [i for i, c in enumerate(extra_candidate)
                                    if extra_candidate.count(c) > 1 and c not in package_extras
                                    and c not in protected_extra and c != engine.ace]
                    if not removals:
                        break
                    extra_candidate.pop(removals[-1])
                    extra_candidate.append(card)
                if not set(package_extras) <= set(extra_candidate):
                    continue
                next_proof = set(proved) | set(package_starts)
                quality, next_starters = self.score_opening(source["id"], engine, candidate, extra_candidate, next_proof)
                priority = (quality, affinity, -len(candidate))
                if quality > score + 0.01 and (winner is None or priority > winner[0]):
                    winner = (priority, candidate, extra_candidate, next_proof, next_starters, support, package_extras)
            if winner is None:
                if studied and len(main) > 40:
                    # Spare copies of a supporting package's secondary
                    # monster are expendable. Keep one copy available.
                    current = list(main)
                    current_count = Counter(current)
                    removable = [c for c in current if c not in starters and c not in DRAW
                                 and c not in engine.core and c not in self.traps and c != engine.ace
                                 and current_count[c] > 1]
                    for code in dict.fromkeys(removable):
                        while current.count(code) > 1 and len(current) > 40:
                            current.remove(code)
                    quality, _ = self.score_opening(source["id"], engine, current, extra, proved)
                    if quality > score:
                        main[:] = current
                break
            priority, candidate, extra_candidate, proof, starters, protected, selected_extra = winner
            protected_extra.update(selected_extra)
            main[:] = candidate
            extra[:] = extra_candidate
            proved.update(proof)
            score = priority[0]
            if score >= target:
                break

    def small_world_routes(self, main, starters):
        monsters = sorted({code for code in main if self.kind(code) & MONSTER})
        starts = set(monsters) & set(starters)
        routes = []
        for source in monsters:
            if source in starts:
                continue
            options = []
            for bridge in monsters:
                if bridge == source or shared_traits(self.card(source), self.card(bridge)) != 1:
                    continue
                for target in sorted(starts - {bridge}):
                    if shared_traits(self.card(bridge), self.card(target)) == 1:
                        options.append((source, bridge, target))
            preferred = lambda entry: (-main.count(entry[1]), -main.count(entry[2]), entry)
            routes.extend(sorted(options, key=preferred)[:5])
        return routes

    def can_use_support(self, code, main):
        if code == 73628505:
            return any(self.kind(c) & 0x80000 for c in main)
        if code == 62706865:
            return any(self.is_normal(c) and self.kind(c) & 0x10 and self.card(c)["level"] <= 2 for c in main)
        if code == 2295440:
            return any(self.is_normal(c) and self.card(c)["level"] == 1 for c in main)
        if code == 32807846:
            return any(self.is_normal(c) and self.card(c)["race"] == 1 for c in main)
        if code == 81439173:
            return any("묘지로 보내졌" in self.card(c)["desc"] for c in main)
        return True

    def opening_model(self, engine, main, extra):
        # Keep the distinction between a direct start, a two-card start, and
        # a card that merely buys a turn. All models are auditable in the JSON.
        solo = set(engine.starters) & set(main)
        played_core = engine.core & set(main)
        usable = set(main)
        if engine.key == "blackwing":
            # Whirlwind is a piece of a two-card starter, not a starter alone.
            solo.discard(91351370)
        if (67547370 in usable and any(self.is_normal(c) and self.kind(c) & 0x10 and self.card(c)["level"] == 1
                                         for c in played_core)):
            solo.update(played_core & {c for c in usable if self.is_normal(c) and self.kind(c) & 0x10
                                      and self.card(c)["level"] == 1})
            if 911883 in usable:
                solo.add(911883)
        if engine.key == "timelord":
            # Timelords can lead into combat on the second turn without a
            # Tribute, or activate Empty Machine when already set.
            solo.update(c for c in played_core if "릴리스 없이 일반 소환" in self.card(c)["desc"]
                        and "몬스터가 존재하지" in self.card(c)["desc"])
        if engine.key == "lava-golem-burn":
            # These cards set up the burn plan on the first two turns without
            # needing to draw a particular monster or Trap with them.
            solo.update(played_core & {3510565, 99517131})
        solo.update(c for c in usable if self.looks_like_starter(c) and self.can_use_support(c, main))
        solo.discard(34010534)
        if 18789533 in usable and (32995276 in extra or 60303245 in extra):
            solo.add(18789533)
        # Trap starters take effect on the opponent's turn but can establish
        # the engine from an empty board. Runick quick-plays have a summon mode
        # when the Extra contains a Runick Fusion, independent of enemy targets.
        runick_fusions = any("루닉" in self.card(c)["name"] or "룬" in self.card(c)["name"] for c in extra if self.kind(c) & FUSION)
        if runick_fusions:
            solo.update(c for c in usable if self.kind(c) & SPELL and "루닉" in self.card(c)["name"] and
                        "엑스트라 덱" in self.card(c)["desc"] and "특수 소환" in self.card(c)["desc"])
        for code in played_core:
            text = self.card(code)["desc"]
            if self.kind(code) & TRAP and code not in self.traps and "덱에서" in text and "특수 소환" in text:
                # Only summon effects that don't require another named card
                # already on the board or target a monster in the graveyard.
                activation = text.split("발동할 수 있다.", 1)[0]
                if "경우" not in activation and "대상으로" not in activation and "릴리스" not in activation:
                    solo.add(code)
        low_links = any(self.kind(c) & LINK and self.card(c)["level"] == 1 for c in extra)
        if low_links:
            solo.update(c for c in played_core if self.is_normal(c) and self.kind(c) & 0x10 and
                        (60303245 in extra and self.card(c)["atk"] <= 1000 or 41999284 in extra and self.card(c)["level"] == 1))
        plays = [c for c in set(main) - solo if self.can_base_play(c, 1500)]
        pairs = set()
        solo.update(self.searchers_for(solo) & usable)
        # Polymerization needs legal material. Never count the spell alone.
        if engine.key in ("xyz-abc", "blue-eyes") and any(self.kind(c) & 0x80000 for c in main) and 73628505 in main:
            solo.add(73628505)
        normals = {c for c in played_core if self.is_normal(c) and c not in self.traps}
        reactive_traps = {c for c in usable if self.kind(c) & TRAP and self.can_base_play(c, 1500)}
        if len(reactive_traps) >= 4:
            # Older control plans can establish a credible field through an
            # archetypal normal summon protected by a separate trap.
            pairs.update((a, b) for a in normals if self.can_base_play(a, 1600) for b in reactive_traps)
        for monster in normals:
            card = self.card(monster)
            for partner in usable - {monster}:
                text = self.card(partner)["desc"]
                if not any(term in text for term in ('특수 소환', '추가로 일반 소환')):
                    continue
                if card["name"] in text and not self.kind(partner) & TRAP and not partner in self.traps:
                    pairs.add((monster, partner))
                elif (self.is_normal(partner) and self.kind(partner) & TUNER
                      and card["level"] + self.card(partner)["level"] in
                      {self.card(c)["level"] for c in extra if self.kind(c) & SYNCHRO}
                      and ('자신 필드' in text and '이 카드를 패에서 특수 소환' in text)
                      and self.tags.get(partner, set()) & self.tags.get(monster, set())):
                    pairs.add((monster, partner))
        generic_link = any(code in extra for code in (29301450, 2857636, 65741786))
        existing_ranks = {self.card(c)["level"] for c in extra if self.kind(c) & XYZ}
        # These two-card starts use real generic Rank 4 or Link-2 material.
        # Card names, levels and spell costs are accounted for individually.
        for id_, target_level in ((18063928, 4), (25259669, 4), (2460565, 3)):
            if id_ not in usable:
                continue
            for partner in usable - {id_}:
                if not self.is_normal(partner) or partner in self.traps:
                    continue
                level = self.card(partner)["level"]
                if id_ == 18063928 and level != 4:
                    continue
                conventional = generic_link or (target_level == level and level in existing_ranks)
                if conventional:
                    pairs.add((id_, partner))
        if 423585 in usable:
            if any(self.is_normal(code) and self.card(code)["level"] == 4 and code != 423585 for code in usable):
                pairs.update((423585, c) for c in usable if self.kind(c) & SPELL
                             and c not in DRAW and c not in (70368879, 67616300, 33782437, 84211599, SMALL_WORLD))
        if 45462149 in extra or any(self.kind(c) & LINK and self.card(c)["level"] == 2
                                    and self.card(c)["name"] == "코드 토커" for c in extra):
            cyberse_normals = {c for c in usable if self.is_normal(c) and self.card(c)["race"] == 16777216}
            hand_material = usable & {2347477, 30114823, 75130221}
            pairs.update((card, extender) for card in cyberse_normals for extender in hand_material - {card})
        if 81752019 in usable:
            pairs.update((81752019, c) for c in usable - {81752019}
                         if self.card(c)["name"].startswith("카디언") and "특수 소환" in self.card(c)["desc"]
                         and ("레벨 2 이하" in self.card(c)["desc"] or "레벨 7 이하" in self.card(c)["desc"]
                              or '카디언" 몬스터 1장을 릴리스' in self.card(c)["desc"]))
        if generic_link:
            for extender in usable & {45894482, 56410040, 65367484}:
                pairs.update((extender, card) for card in normals - {extender})
        if engine.key == "blackwing":
            birds = {c for c in played_core if self.kind(c) & MONSTER and
                     re.match(r"^(A )?BF[ －T(]", self.card(c)["name"])}
            for code in birds:
                if 81470373 in usable and code != 81470373:
                    pairs.add((81470373, code))
                if 91351370 in usable and self.is_normal(code) and any(self.card(t)["atk"] < self.card(code)["atk"] for t in birds):
                    pairs.add((91351370, code))
                for extender in birds - {code}:
                    phrase = self.card(extender)["desc"]
                    if self.is_normal(code) and "자신 필드에" in phrase and "이 카드는 패에서 특수 소환할 수 있다" in phrase:
                        pairs.add((code, extender))
        if engine.key in ("drytron", "cyber-angel", "black-luster-soldier", "chaos-max"):
            ritual = {c for c in usable if self.kind(c) & MONSTER and self.kind(c) & RITUAL}
            if engine.key == "drytron":
                drytron = {c for c in played_core if self.kind(c) & MONSTER and
                           "의식 몬스터 1장을 릴리스" in self.card(c)["desc"]}
                pairs.update((a, b) for a in drytron for b in (drytron | ritual) - {a})
        if 56146300 in usable and 67441879 in usable:
            pairs.update((56146300, c) for c in usable - {56146300}
                         if self.kind(c) & MONSTER and self.card(c)["race"] == 2048)
        if engine.key == "mermail":
            for card in normals:
                if "패의 물 속성 몬스터 1장" in self.card(card)["desc"]:
                    pairs.update((card, b) for b in usable if b != card and self.card(b)["attr"] == 2)
        tribute_wyrms = {c for c in played_core if self.kind(c) & MONSTER
                         and "지속 마법 / 지속 함정" in self.card(c)["desc"]
                         and "어드밴스 소환" in self.card(c)["desc"]}
        continuous = {c for c in usable if self.kind(c) & (SPELL | TRAP) and self.kind(c) & 0x20000}
        pairs.update((c, partner) for c in tribute_wyrms for partner in continuous)
        if engine.key in ("infernity", "phantom-knights"):
            if 81439173 in usable and {13521194, 28985331} & usable:
                solo.add(81439173)
        if engine.key == "phantom-knights" and 36426778 in usable:
            pairs.update((c, 36426778) for c in normals if self.card(c)["level"] == 3)
        if engine.key in ("slifer-holactie", "ra", "obelisk") and 39913299 in usable and 52112003 in usable:
            pairs.add((39913299, 52112003))
        # The separate draw model evaluates cards seen via draw and excavation.
        solo.discard(84211599)
        return {"starters": sorted(solo), "plays": sorted(set(plays) - {84211599}), "pairs": sorted(pairs)}


def write_object(path, data, key):
    header = json.dumps({k: v for k, v in data.items() if k != key}, ensure_ascii=False, indent=2)
    prefix = header[:-2] + ",\n" if data.keys() != {key} else "{\n"
    list_opening = f'  "{key}": [\n'
    decks = []
    for entry in data[key]:
        fields = ",\n".join(
            f"      {json.dumps(field)}: {json.dumps(value, ensure_ascii=False, separators=(',', ':'))}"
            for field, value in entry.items()
        )
        decks.append("    {\n" + fields + "\n    }")
    path.write_text(prefix + list_opening + ",\n".join(decks) + "\n  ]\n}\n", encoding="utf-8")


def baseline_json(ref, relative_path, fallback):
    if ref is None:
        return read_json(fallback)
    if ref.startswith("-"):
        raise ValueError("Pass a commit or ref name as the baseline")
    payload = subprocess.check_output(["git", "show", f"{ref}:{relative_path}"], cwd=TOOLS.parent)
    return json.loads(payload.decode("utf-8-sig"))


def build(streaming, seed_catalogue, output, baseline_ref=None):
    original = baseline_json(baseline_ref, "Assets/StreamingAssets/opponents_db.json", streaming / "opponents_db.json")
    roster = baseline_json(baseline_ref, "Assets/StreamingAssets/opponent_deck_levels.json", streaming / "opponent_deck_levels.json")
    old_by_id = {d["id"]: d for d in original["decks"]}
    for entry in roster["opponents"]:
        if not entry.get("deckIds"):
            classic = entry["classic"]
            old_by_id[classic["id"]] = classic
            entry["deckIds"] = [classic["id"], *entry["advancedIds"]]
    seeds = read_json(seed_catalogue)["decks"]
    authored = baseline_json(baseline_ref, "Tools/opponent_themes.json", TOOLS / "opponent_themes.json")["decks"]
    # The pre-level recipes contribute the two authored character strategies;
    # the modern catalogue adds alternate engines without seven identical copies.
    builder = Builder(streaming, [*seeds, *authored, *original["decks"]])
    characters = {c["id"]: c for c in original["characters"]}
    decks = []
    entries = []
    for entry in roster["opponents"]:
        cid = entry["charId"]
        char = characters[cid]
        ids = entry["deckIds"]
        source = [old_by_id[did] for did in ids]
        if char["seriesIdx"] == 6:
            planned = [builder.from_seed(s, key=f"meta-{s['id']}", expand=False) for s in source]
        else:
            planned = builder.plans(char, ids)
        if len(planned) != len(ids):
            raise ValueError(f"Incorrect plan length for {cid}")
        signatures = set()
        for index, (deck, engine) in enumerate(zip(source, planned), 1):
            decks.append(builder.tune(deck, engine, index, len(ids), signatures))
        entries.append({"charId": cid, "deckIds": ids})
    output.mkdir(parents=True, exist_ok=True)
    write_object(output / "opponents_db.json", {"characters": original["characters"], "decks": decks}, "decks")
    write_object(output / "opponent_deck_levels.json", {"opponents": entries}, "opponents")
    print(f"Wrote {len(entries)} characters and {len(decks)} playable profiles to {output}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=STREAMING)
    parser.add_argument("--seed-catalogue", type=Path, default=SEED_CATALOGUE)
    parser.add_argument("--baseline-ref", default=BASELINE_REF,
                        help="Read source roster and themes from this Git ref. An empty ref uses the source files.")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.source, args.seed_catalogue, args.output, args.baseline_ref or None)


if __name__ == "__main__":
    main()
