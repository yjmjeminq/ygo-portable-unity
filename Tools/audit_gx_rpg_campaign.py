"""Audit the shipped GX RPG campaign without rewriting the story or decks."""

from collections import Counter
import json
import math
from pathlib import Path
import re
import struct
import sys

ROOT = Path(__file__).resolve().parents[1] / "Assets" / "StreamingAssets"
ART = ROOT / "TagForce" / "GX"
BRANCHES = ("preDialogues", "postWinDialogues", "postLoseDialogues")
EMOTIONS = {"smile", "blush", "sad", "determined", "shock", "hurt", "angry"}
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
# TYPE_TOKEN, TYPE_SYNCHRO, TYPE_XYZ, TYPE_PENDULUM and TYPE_LINK
FORBIDDEN_CARD_TYPES = 0x4000 | 0x2000 | 0x800000 | 0x1000000 | 0x4000000
STAGE_ACTIONS = {"enter", "exit", "move", "face", "focus"}
KEY = re.compile(r"^[A-Za-z0-9_-]+$")


def read(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8-sig"))


def png(path):
    try:
        with path.open("rb") as stream:
            header = stream.read(33)
        if len(header) == 33 and header[:8] == PNG_SIGNATURE and header[12:16] == b"IHDR":
            width, height = struct.unpack(">II", header[16:24])
            if width and height:
                return width, height
    except OSError:
        pass
    return None


def signed_area(vertices):
    return sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(vertices, vertices[1:] + vertices[:1])) / 2


def simple_polygon(points):
    if not isinstance(points, list) or len(points) < 3:
        return False
    try:
        vertices = [(point["x"], point["y"]) for point in points]
        if len(set(vertices)) != len(vertices) or any(
            not math.isfinite(value) or not 0 <= value <= 1 for vertex in vertices for value in vertex
        ):
            return False
    except (TypeError, KeyError, ValueError):
        return False
    if abs(signed_area(vertices)) < 0.00001:
        return False

    def cross(a, b, c):
        return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])

    edges = list(zip(vertices, vertices[1:] + vertices[:1]))
    for i, (a, b) in enumerate(edges):
        for j in range(i + 2, len(edges)):
            if i == 0 and j == len(edges) - 1:
                continue
            c, d = edges[j]
            if (max(a[0], b[0]) < min(c[0], d[0]) or max(c[0], d[0]) < min(a[0], b[0]) or
                    max(a[1], b[1]) < min(c[1], d[1]) or max(c[1], d[1]) < min(a[1], b[1])):
                continue
            if cross(a, b, c) * cross(a, b, d) <= 0 and cross(c, d, a) * cross(c, d, b) <= 0:
                return False
    return True


def main():
    errors = []

    def require(condition, error):
        if not condition:
            errors.append(error)

    story = read("story_gx_script.json")
    episodes = story["episodes"]
    deck_rows = read("story_gx_rpg_decks.json")["decks"]
    decks = {deck["id"]: deck for deck in deck_rows}
    cards = {card["code"]: card for card in read("cards_kr.json")["cards"]}
    characters = {actor["id"]: actor for actor in read("opponents_db.json")["characters"]}
    world = read("tagforce_gx.json")
    regions = {spot["spotId"]: spot for spot in world["spots"]}
    scenes = {spot["spotId"]: spot for spot in world.get("fieldScenes", [])}
    names = {name: actor["id"] for actor in characters.values() for name in actor.get("aliases") or []}
    names.update({actor["name"]: actor["id"] for actor in characters.values()})
    names.update({episode["opponentName"]: episode["charId"] for episode in episodes})
    narration = {"", "설명", "내레이션", "나레이션", story["protagonist"]}

    require(len(episodes) == story["totalEpisodes"] == 53 and len({e["id"] for e in episodes}) == 53,
            "Story must contain 53 unique sequences.")
    require(sum(bool(e.get("isCutsceneOnly")) for e in episodes) == 19, "Expected 19 automatic cutscenes.")
    require(len(deck_rows) == 37 and set(decks) == set(range(69001, 69035)) | {69101, 69102, 69103}, "Wrong GX deck IDs.")
    pov = {"gx_ep_17": 69101, "gx_ep_27": 69102, "gx_ep_33": 69103}
    require({e["id"]: e.get("forcedPlayerDeckId") for e in episodes if e.get("playAsCharacterId") == 107} == pov,
            "Wrong Asuka POV decks or episodes.")
    require(len(regions) == len(world["spots"]) == 12, "Expected 12 unique GX fields.")
    world_map = world["assetConfig"]["worldMapPath"]
    require(png(ROOT / world_map), f"Missing PNG {world_map}.")

    for name, spot in {**regions, **scenes}.items():
        require(KEY.fullmatch(name), f"Invalid map ID {name}.")
        require(png(ROOT / spot["mapPath"]), f"Invalid map PNG {spot['mapPath']}.")
        require(simple_polygon(spot.get("walkPolygon")), f"Invalid walking polygon at {name}.")
        for obstacle in spot.get("obstacles", []):
            require(simple_polygon(obstacle.get("vertices")), f"Invalid collision polygon at {name}.")
        routes = spot.get("routes", [])
        require(len(routes) == len(set(routes)) and all(route in regions and route != name for route in routes),
                f"Unknown or duplicate connection at {name}.")
        if name in regions:
            require(all(0 < spot.get(key, 0) < 1 for key in ("worldX", "worldY")), f"Invalid map pin at {name}.")

    for deck in decks.values():
        number = deck["id"]
        main_list, extra, side = (deck.get(zone) or [] for zone in ("main", "extra", "side"))
        codes = main_list + extra + side
        require(40 <= len(main_list) <= 45, f"{number}: main deck size {len(main_list)}.")
        require(deck["sigCard"] in main_list + extra, f"{number}: signature card is missing from the deck.")
        counts = Counter()
        for code in codes:
            card = cards.get(code)
            require(card and (card["type"] & FORBIDDEN_CARD_TYPES) == 0, f"{number}: unknown/forbidden card {code}.")
            if card:
                counts[(cards.get(card.get("alias")) or card)["name"].strip()] += 1
        for name, count in counts.items():
            require(count <= 3, f"{number}: {name} has {count} copies across its zones.")

    cg = set()
    used_actors = set()
    first_appearance = {}
    lines_read = 0
    for index, episode in enumerate(episodes):
        chapter = episode["id"]
        character = episode["charId"]
        region = regions.get(episode["regionId"])
        require(character in characters, f"{chapter}: unknown actor {character}.")
        require(region and region["unlockAfter"] <= index, f"{chapter}: invalid or locked destination.")
        used_actors.add(character)
        cutscene = bool(episode.get("isCutsceneOnly"))
        objective = episode.get("fieldObjective", "")
        require(isinstance(objective, str) and 0 < len(objective.strip()) <= 24 and "\n" not in objective,
                f"{chapter}: missing or oversized field objective.")
        if not cutscene:
            deck_id = episode.get("deckId")
            require(deck_id in decks and decks[deck_id]["episodeId"] == chapter and decks[deck_id]["charId"] == character,
                    f"{chapter}: deck does not match its episode and actor.")
            pre = episode["preDialogues"]
            choice = episode.get("choiceAfterLine", 0)
            position_valid = type(choice) is int and 1 <= choice < len(pre)
            require(position_valid and pre[choice - 1].get("speaker") and
                    not any(re.search(r"듀얼[!！.]", line["text"]) for line in pre[:choice]),
                    f"{chapter}: dialogue choice must come before the duel call.")
            options = episode.get("choices") or []
            require(len(options) == 3 and {option.get("stance") for option in options} == {"Challenge", "Calm", "Bond"} and
                    all(option.get("text", "").strip() and option.get("response", "").strip() for option in options),
                    f"{chapter}: provide an authored reply and response for each duel stance.")
        portrait = episode.get("playAsCharacterId", 0)
        require(portrait == 0 or (portrait == 107 and episode.get("forcedPlayerDeckId") == pov.get(chapter)),
                f"{chapter}: invalid forced player portrait/deck.")
        for branch in BRANCHES:
            lines = episode.get(branch) or []
            require(bool(lines) or cutscene and branch != "preDialogues", f"{chapter}: missing {branch}.")
            for number, line in enumerate(lines, 1):
                lines_read += 1
                label = f"{chapter}:{branch}:{number}"
                speaker = line.get("speaker")
                require(isinstance(speaker, str) and (speaker in names or speaker in narration),
                        f"{label}: unknown speaker {speaker}.")
                if isinstance(speaker, str) and speaker not in narration and speaker in names:
                    used_actors.add(names[speaker])
                    if branch != "postLoseDialogues":
                        first_appearance.setdefault(names[speaker], index)
                require(isinstance(line.get("text"), str) and line["text"].strip(), f"{label}: empty dialogue.")
                require(not line.get("emotion") or line["emotion"] in EMOTIONS, f"{label}: unknown expression.")
                require(not line.get("fx") or line["fx"] in {"shake", "flash", "heartbeat", "fade"},
                        f"{label}: unknown dialogue effect.")
                if "sceneTime" in line:
                    stamp = line["sceneTime"]
                    require(isinstance(stamp, str) and 0 < len(stamp.strip()) <= 29 and "\n" not in stamp
                            and speaker in {"", "설명", "내레이션", "나레이션"} and line.get("fx") == "fade",
                            f"{label}: a time transition requires a short caption and a narration fade.")
                if line.get("sceneMap"):
                    require(line["sceneMap"] in scenes or line["sceneMap"] in regions, f"{label}: invalid scenery reference.")
                if line.get("cutscene"):
                    cg.add(line["cutscene"])
                if line.get("memoryTitle"):
                    require(bool(line.get("cutscene")), f"{label}: a replayable memory lacks its CG.")
                for action in line.get("stage") or []:
                    actor = action.get("actor")
                    move = action.get("action")
                    require(actor == 0 or actor in characters, f"{label}: invalid stage actor {actor}.")
                    if actor:
                        used_actors.add(actor)
                        if move == "enter" and branch != "postLoseDialogues":
                            first_appearance.setdefault(actor, index)
                    require(move in STAGE_ACTIONS and isinstance(action.get("step", 0), int) and action.get("step", 0) >= 0,
                            f"{label}: invalid stage action.")
                    if move == "face":
                        require(action.get("target") == 0 or action.get("target") in characters, f"{label}: invalid facing target.")
                        if action.get("target"):
                            used_actors.add(action["target"])
                    if move == "move":
                        require(all(isinstance(action.get(axis), (int, float)) and 0 <= action[axis] <= 1
                                    for axis in ("x", "y")), f"{label}: invalid stage mark.")

    require(len(cg) == 28, f"Expected 28 distinct CGs, found {len(cg)}.")
    for key in sorted(cg):
        require(KEY.fullmatch(key) and png(ART / "Cutscenes" / (key + ".png")), f"Missing or invalid CG {key}.")

    roster = {actor["charId"] for actor in world["roster"]}
    require(len(roster) == len(world["roster"]), "Duplicate GX roster NPC.")
    for actor in world["roster"]:
        actor_id = actor["charId"]
        unlock = actor.get("unlockAfter", 0)
        moves = actor.get("storyMoves") or []
        require(actor_id in characters and actor["spotId"] in regions, f"Unknown roster NPC/location {actor_id}.")
        require(type(unlock) is int and 0 <= unlock <= len(episodes), f"Invalid unlock for roster NPC {actor_id}.")
        if not actor.get("storyOnly"):
            introduction = first_appearance.get(actor_id)
            require(introduction is not None and type(unlock) is int and unlock > introduction,
                    f"Roster NPC {actor_id} can roam before its story introduction.")
        progress = [move["after"] for move in moves]
        require(progress == sorted(set(progress)) and all(
            type(when) is int and unlock <= when <= len(episodes) and
            (not move.get("spotId") if move.get("hidden") else move.get("spotId") in regions)
            for when, move in zip(progress, moves)), f"Invalid or out-of-order roster move for {actor_id}.")
    for key in sorted(roster | used_actors):
        require(png(ART / "Standing" / f"full_{key}.png"), f"Missing standing portrait {key}.")
        require(png(ART / "DialoguePortraits" / f"bust_{key}.png"), f"Missing dialogue portrait {key}.")
        require(png(ART / "Characters_SD" / f"sd_{key}.png") or key not in roster and
                png(ART / "SD" / f"sd_{key}.png"), f"Missing field SD {key}.")
        if key in roster:
            atlas = png(ART / "WalkCycles" / f"sd_{key}_field.png")
            require(atlas and all(size % 3 == 0 for size in atlas), f"Missing/invalid 3x3 walk atlas {key}.")

    for path in [
        ART / "Standing/full_campaign_protagonist.png",
        ART / "Characters_SD/sd_protagonist.png",
        ART / "WalkCycles/sd_protagonist_field.png",
        ART / "DialoguePortraits/bust_campaign_protagonist.png",
        *(ART / "DialoguePortraits" / f"bust_campaign_protagonist_{emotion}.png" for emotion in sorted(EMOTIONS - {"hurt"})),
    ]:
        require(png(path), f"Missing or invalid protagonist asset {path.name}.")

    if errors:
        for error in errors:
            print(f"FAIL {error}")
        print(f"GX data: FAIL ({len(errors)})")
        return 1
    print(f"GX data: PASS (53 sequences, {lines_read} dialogue lines, {len(decks)} decks, "
          f"{len(regions)} fields, {len(cg)} CGs, {len(roster)} roster NPCs and {len(roster | used_actors)} distinct actors)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
