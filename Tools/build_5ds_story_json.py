"""Compile the Beyond Destiny manuscript without rewriting any source text."""

import argparse
import hashlib
import json
import re
import struct
import wave
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Docs/Story5Ds"
OUTPUT = ROOT / "Assets/StreamingAssets/story_5ds_script.json"
SOURCE_HASHES = ROOT / "Tools/Story5Ds/source-sha256.json"
ASSET_MANIFEST = ROOT / "Tools/Story5Ds/assets.json"
AUDIO_MANIFEST = ROOT / "Tools/Story5Ds/audio.json"
ASSET_ROOT = ROOT / "Assets/StreamingAssets/TagForce/5DS"
EPISODE_COUNT = 37
SCENE_COUNT = 182
CHARACTER_COUNT = 252_024

EPISODE = re.compile(r"\[에피소드\] 제(\d+)화[:：]\s*(.+)")
SCENE = re.compile(r"\[SCENE (\d+)[:：]\s*(.+)\]")
SEPARATOR = re.compile(r"(?:={8,}|-{8,})")
FOOTER = re.compile(r"\[제(\d+)화 끝(?: - 대단원)?\]")
CUE = re.compile(r"\*\s+(BG|BGM|SFX|스탠딩|연출|BGM / SFX)[:：]\s*(\S.*)")
SPEECH = re.compile(
    r"(\[[^\]\r\n]+\]|[^:\[\]()『』]{1,40}?)(?:\s*\((.*)\))?[:：]\s*(.*)"
)
PORTRAIT = re.compile(r"\[([^\[\]()]+?)\s+\((.+?)\)\]")
AUDIO_KEY = re.compile(r"\bBGM_[A-Za-z0-9_]+\b")

# The key is stable across alias, voice-over, and outfit changes. Names in the
# script are kept verbatim in each event.
SPEAKERS = {
    "hero": ("주인공",),
    "aki": ("아키", "이자요이 아키"),
    "sherry": ("셰리", "셰리 르블랑"),
    "devine": ("디바인", "디바인의 목소리", "모니터 속 디바인의 음성"),
    "yusei": ("유세이", "후도 유세이"),
    "jack": ("잭", "잭 아틀라스"),
    "crow": ("크로우", "크로우 호건"),
    "mizoguchi": ("미조구치",),
    "misty": ("미스티", "미스티 로라", "미스티의 목소리"),
    "rex": ("렉스 고드윈",),
    "zone": ("Z-ONE",),
    "luka": ("루카",),
    "lua": ("루아",),
    "ghost_a": ("고스트 A",),
    "vargas": ("발가스",),
    "kid": ("키드",),
    "aki_sherry": ("아키 & 셰리",),
    "aki_father": ("아키의 아버지",),
    "aki_mother": ("아키의 어머니",),
    "arcadia_member": ("아르카디아 조직원",),
    "arcadia_agent": ("아르카디아 상급 요원",),
    "arcadia_officer": ("아르카디아 간부",),
    "arcadia_guard": ("아르카디아 경호원",),
    "arcadia_captain": ("아르카디아 경호원 대장",),
    "arcadia_operative_a": ("아르카디아 공작원 A",),
    "fleeing_member_a": ("도망치는 조직원 A",),
    "fleeing_member_b": ("도망치는 조직원 B",),
    "netherworld_commander": ("명계의 사도 지휘관",),
    "netherworld_a": ("명계의 사도 A",),
    "netherworld_b": ("명계의 사도 B",),
    "security_radio": ("시큐리티 무전",),
    "crowd": ("관중들",),
    "spectator_a": ("관중 A",),
    "spectator_b": ("관중 B",),
    "spectator_c": ("관중 C",),
    "reporter_a": ("기자 A",),
    "opponent_anchor": ("상대 앵커",),
    "announcer": ("MC",),
    "unknown_voice": ("???",),
}
SPEAKER_KEYS = {name: key for key, names in SPEAKERS.items() for name in names}


def fail(file, line, message):
    raise ValueError(f"{file.name}:{line}: {message}")


def speaker_key(file, line, name):
    if name not in SPEAKER_KEYS:
        fail(file, line, f"Unknown speaker {name!r}")
    return SPEAKER_KEYS[name]


def heading(file, line, text):
    if SEPARATOR.fullmatch(text):
        return {"kind": "format"}
    if closing := FOOTER.fullmatch(text):
        return {"kind": "episodeEnd", "number": int(closing[1]), "text": text}
    if text == "[내레이션]":
        return {"kind": "narration"}
    if text == "[시스템 텍스트]":
        return {"kind": "system"}
    if cue := CUE.fullmatch(text):
        return directive(file, line, cue[1], cue[2])
    if text.startswith(("* ", "[SCENE")):
        fail(file, line, f"Malformed or unsupported cue {text!r}")
    if not text.startswith(('"', '“', '(', '『')) and (speech := SPEECH.fullmatch(text)):
        name, tone, inline = speech.groups()
        if name.startswith("[") or tone is not None or not inline or name in SPEAKER_KEYS:
            name = name.strip("[]").strip()
            kind = "thought" if tone and "속마음" in tone else "dialogue"
            return {"kind": kind, "speaker": name, "speakerKey": speaker_key(file, line, name),
                    "tone": tone or "", "text": inline}
    return None


def directive(file, line, name, description):
    kind = {"BG": "background", "BGM": "music", "SFX": "sound", "스탠딩": "standing",
            "연출": "direction", "BGM / SFX": "audio"}[name]
    event = {"kind": kind, "text": description}
    if kind == "standing":
        cast = [{"speaker": name, "speakerKey": speaker_key(file, line, name), "pose": pose}
                for name, pose in PORTRAIT.findall(description)]
        if not cast or len(cast) != description.count("["):
            fail(file, line, f"Invalid standing cue {description!r}")
        event["cast"] = cast
    elif kind == "music":
        clips = AUDIO_KEY.findall(description)
        if len(clips) != description.count("BGM_"):
            fail(file, line, f"Invalid BGM key in {description!r}")
        if clips:
            event.update(action="play", clipKeys=clips)
        elif "페이드 아웃" in description or "서서히 종료" in description:
            event["action"] = "fadeOut"
        elif "종료" in description or description.startswith("정적"):
            event["action"] = "stop"
        else:
            event["action"] = "direction"
    return event


def check_dialogue(file, event):
    text = event["text"].strip()
    if text.count('"') % 2 or text.count("“") != text.count("”"):
        fail(file, event["line"], "Unbalanced quotation marks in dialogue")


def scene_events(file, lines, start, stop, scene_key):
    events = []
    cursor = start
    while cursor < stop:
        line = lines[cursor].strip()
        event = heading(file, cursor + 1, line)
        first_body_line = cursor + 1
        if event is None:
            event = {"kind": "prose" if line else "format"}
            first_body_line = cursor
        end = cursor + 1
        if event["kind"] in {"narration", "system", "dialogue", "thought", "prose"}:
            while end < stop and heading(file, end + 1, lines[end].strip()) is None:
                end += 1
            inline = event.get("text")
            body = "".join(lines[first_body_line:end]).strip()
            event["text"] = "\n".join(part for part in (inline, body) if part)
            if not event["text"]:
                fail(file, cursor + 1, f"Empty {event['kind']} block")
            if event["kind"] in {"dialogue", "thought"}:
                check_dialogue(file, {**event, "line": cursor + 1})
        if event["kind"] == "background":
            event["key"] = f"{scene_key}_bg_{sum(e['kind'] == 'background' for e in events) + 1}"
        if event["kind"] == "sound":
            event["key"] = f"{scene_key}_sfx_{sum(e['kind'] == 'sound' for e in events) + 1}"
        event.update(line=cursor + 1, source="".join(lines[cursor:end]))
        events.append(event)
        cursor = end
    return events


def asset_catalog(episodes):
    backgrounds = {}
    sounds = {}
    music = {}
    for episode in episodes:
        for scene in episode["scenes"]:
            for event in scene["events"]:
                if event["kind"] == "background":
                    backgrounds[event["key"]] = event["text"]
                elif event["kind"] == "sound":
                    sounds[event["key"]] = event["text"]
                elif event["kind"] == "music":
                    for key in event.get("clipKeys", ()):
                        descriptions = music.setdefault(key, [])
                        if event["text"] not in descriptions:
                            descriptions.append(event["text"])
    return {"backgrounds": backgrounds, "music": music, "sounds": sounds}


def parse_episode(file, number, source=None):
    if source is None:
        source = file.read_bytes()
    has_bom = source.startswith(b"\xef\xbb\xbf")
    lines = source.decode("utf-8-sig").splitlines(keepends=True)
    titles = [match for line in lines if (match := EPISODE.fullmatch(line.strip()))]
    if len(titles) != 1 or int(titles[0][1]) != number:
        fail(file, 1, f"Expected a single title for episode {number}")
    marks = [(index, match) for index, line in enumerate(lines)
             if (match := SCENE.fullmatch(line.strip()))]
    scene_numbers = [int(match[1]) for _, match in marks]
    if not marks or scene_numbers != list(range(1, len(marks) + 1)):
        fail(file, 1, "Scene numbers must be contiguous and start at 1")
    episode_key = f"beyond_destiny_ep_{number:02d}"
    scenes = []
    current_bg = None
    for cursor, (index, match) in enumerate(marks):
        stop = marks[cursor + 1][0] if cursor + 1 < len(marks) else len(lines)
        key = f"{episode_key}_scene_{cursor + 1:02d}"
        events = scene_events(file, lines, index + 1, stop, key)
        first_bg = next((e["key"] for e in events if e["kind"] == "background"), None)
        if current_bg is None and first_bg is None:
            fail(file, index + 1, "The opening scene has no background")
        scenes.append({"id": key, "number": cursor + 1, "title": match[2], "line": index + 1,
                       "sourceHeading": lines[index], "backgroundKey": first_bg or current_bg, "events": events})
        for event in events:
            if event["kind"] == "background":
                current_bg = event["key"]

    endings = [(scene["number"], event["number"]) for scene in scenes for event in scene["events"]
               if event["kind"] == "episodeEnd"]
    if endings != [(len(scenes), number)]:
        fail(file, len(lines), "Expected this episode's ending marker in its last scene")
    episode = {"id": episode_key, "number": number, "title": titles[0][2], "sourceFile": file.name,
               "sourceSha256": hashlib.sha256(source).hexdigest(), "sourceBom": has_bom,
               "sourcePreamble": "".join(lines[:marks[0][0]]), "scenes": scenes}
    if restore_source(episode) != source:
        fail(file, 1, "Structured events did not round-trip to the source bytes")
    return episode


def restore_source(episode):
    chunks = [episode["sourcePreamble"]]
    for scene in episode["scenes"]:
        chunks.append(scene["sourceHeading"])
        chunks.extend(event["source"] for event in scene["events"])
    return "".join(chunks).encode("utf-8-sig" if episode["sourceBom"] else "utf-8")


def bind_backgrounds(scenes, backgrounds, manifest, require_all):
    bindings = {}
    paths = set()
    for entry in manifest["maps"]:
        path = entry["path"]
        if (not re.fullmatch(r"Maps/[a-z0-9_]+\.png", path) or path in paths or
                not entry["backgroundKeys"] or not (ASSET_ROOT / path).is_file()):
            raise ValueError(f"Invalid, duplicate, or missing Beyond Destiny map: {path}")
        paths.add(path)
        for key in entry["backgroundKeys"]:
            if key not in backgrounds or key in bindings:
                raise ValueError(f"Unknown or duplicated background key: {key}")
            bindings[key] = path
    if require_all and bindings.keys() != backgrounds.keys():
        remaining = sorted(backgrounds.keys() - bindings.keys())
        raise ValueError(f"{len(remaining)} background cues need artwork, starting with {remaining[0]}")
    for scene in scenes:
        if path := bindings.get(scene["backgroundKey"]):
            scene["backgroundPath"] = path
        for event in scene["events"]:
            if event["kind"] == "background" and (path := bindings.get(event["key"])):
                event["assetPath"] = path
    return len(bindings)


def bind_cutscenes(scenes, manifest):
    by_scene = {scene["id"]: scene for scene in scenes}
    keys = set()
    for entry in manifest["cutscenes"]:
        key, path = entry["key"], entry["path"]
        if (not re.fullmatch(r"ep[0-9]{2}_[a-z_]+", key) or key in keys or
                path != f"Cutscenes/{key}.png" or not (ASSET_ROOT / path).is_file()):
            raise ValueError(f"Invalid, duplicate, or missing Beyond Destiny cutscene: {path}")
        keys.add(key)
        scene = by_scene.get(entry["scene"])
        if scene is None:
            raise ValueError(f"Cutscene {key} references an unknown scene: {entry['scene']}")
        start, end = entry["firstLine"], entry["lastLine"]
        visible = [cue for cue in scene["events"] if start <= cue["line"] <= end and
                   cue["kind"] in {"dialogue", "thought", "narration", "system", "prose"}]
        if not visible or visible[0]["line"] != start or visible[-1]["line"] != end:
            raise ValueError(f"Cutscene {key} does not start and end on readable cues in {scene['id']}")
        for cue in visible:
            if "cutsceneKey" in cue:
                raise ValueError(f"Cutscene {key} overlaps another CG at {scene['id']}:{cue['line']}")
            cue["cutsceneKey"] = key
    return len(keys)


def bind_portraits(episodes, manifest):
    art_dir = "Characters_Standing"
    portraits = {}
    for entry in manifest["characters"]:
        path = entry["path"]
        if not path.startswith(art_dir + "/"):
            continue
        key = Path(path).stem
        if not re.fullmatch(r"Characters_Standing/[a-z0-9_]+\.png", path) or key in portraits:
            raise ValueError(f"Invalid or duplicate Beyond Destiny portrait: {path}")
        source = ASSET_ROOT / path
        with source.open("rb") as image:
            header = image.read(26)
        if len(header) != 26 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[25] != 6:
            raise ValueError(f"Beyond Destiny portrait lacks RGBA alpha: {path}")
        width, height = struct.unpack(">II", header[16:24])
        if [width, height] != entry["resolution"] or height <= width:
            raise ValueError(f"Invalid Beyond Destiny portrait dimensions: {path}")
        portraits[key] = path.removesuffix(".png")

    design = manifest["dialoguePortraits"]
    voice_only = set(design["voiceOnlySpeakers"])
    duet = "aki_sherry"
    if not voice_only <= SPEAKERS.keys() or "hero" in voice_only or duet in voice_only:
        raise ValueError("The Beyond Destiny voice-only cast contains an unknown or staged speaker")
    actors = SPEAKERS.keys() - voice_only - {duet}
    if missing := actors - portraits.keys():
        raise ValueError(f"Missing Beyond Destiny cast portraits: {sorted(missing)}")
    voice_aliases = set(design["voiceOnlyAliases"])
    if not voice_aliases <= {alias for actor in actors for alias in SPEAKERS[actor]}:
        raise ValueError("Unknown Beyond Destiny voice-only alias")
    expressions = {}
    expression_art = set()
    for rule in design["expressions"]:
        base, image, words = rule["base"], rule["art"], rule["tone"]
        if base not in portraits or image not in portraits or image in expression_art or not all(words) or not words:
            raise ValueError(f"Unbound Beyond Destiny expression: {rule}")
        expressions.setdefault(base, []).append((image, words))
        expression_art.add(image)

    # Wardrobe is durable across scenes, while viewpoint and visible cast are local to a scene.
    wardrobe = {}
    viewpoints, narration = design["viewpoints"], design["narrationPortraits"]
    scene_keys = {scene["id"] for episode in episodes for scene in episode["scenes"]}
    if not viewpoints.keys() <= scene_keys or not set(viewpoints.values()) <= actors:
        raise ValueError("Unknown Beyond Destiny scene or actor in viewpoint overrides")
    used_narration = set()

    def remember_wardrobe(actor, pose):
        everyday = any(word in pose for word in ("일상", "평상복", "사복"))
        if actor == "aki":
            if "가면" in pose:
                wardrobe[actor] = "aki" if any(word in pose for word in ("없", "벗", "해제")) else "aki_masked"
            if "라이딩 슈트" in pose:
                wardrobe[actor] = "aki_riding"
            elif everyday:
                wardrobe[actor] = "aki_casual"
        elif actor == "sherry":
            if "타락" in pose:
                wardrobe[actor] = "sherry_fallen"
            elif everyday or any(word in pose for word in ("평소 복장", "드레스 코트")):
                wardrobe[actor] = "sherry"

    used = set()

    def likeness(actor, tone=""):
        base = wardrobe.get(actor, actor)
        image = next((image for image, words in expressions.get(base, ())
                      if any(word in tone for word in words)), base)
        used.add(image)
        return portraits[image]

    for episode in episodes:
        for scene in episode["scenes"]:
            stage = {}
            focal_actor = viewpoints.get(scene["id"], "hero")
            for cue in scene["events"]:
                if cue["kind"] == "standing":
                    for member in cue["cast"]:
                        actor, pose = member["speakerKey"], member["pose"]
                        if actor not in actors:
                            continue
                        remember_wardrobe(actor, pose)
                        stage.pop(actor, None)
                        if "퇴장" not in cue["text"]:
                            stage[actor] = pose
                if cue["kind"] not in {"dialogue", "thought", "narration", "system", "prose"}:
                    continue
                actor = cue.get("speakerKey")
                tone = cue.get("tone", "")
                if actor == duet:
                    cue.update(heroPortrait=likeness("aki", tone), guestPortrait=likeness("sherry", tone), speakerSide=3)
                    continue
                if focal_actor == "hero" and scene["id"] == "beyond_destiny_ep_01_scene_05" and "흉터" in cue["text"]:
                    cue["heroPortrait"] = portraits["hero_scar"]
                    used.add("hero_scar")
                else:
                    focal_tone = tone if actor == focal_actor else stage.get(focal_actor, "")
                    if focal_actor == "hero" and cue["kind"] == "thought":
                        focal_tone += " " + cue["text"]
                    cue["heroPortrait"] = likeness(focal_actor, focal_tone)
                if actor in voice_only or cue.get("speaker") in voice_aliases or "도청기 너머" in tone or "통신기 너머" in tone:
                    continue
                if actor in actors:
                    if actor == focal_actor:
                        cue["speakerSide"] = 1
                    else:
                        cue.update(guestPortrait=likeness(actor, tone or stage.get(actor, "")), speakerSide=2)
                if cue.get("guestPortrait") is None:
                    key = f"{episode['id']}:{cue['line']}"
                    if frame := narration.get(key):
                        if cue["kind"] != "narration" or frame["actor"] not in actors:
                            raise ValueError(f"Invalid Beyond Destiny narrator portrait at {key}")
                        used_narration.add(key)
                        cue["guestPortrait"] = likeness(frame["actor"], frame["tone"])
                    else:
                        guest = next((member for member in reversed(stage) if member != focal_actor), None)
                        if guest:
                            cue["guestPortrait"] = likeness(guest, stage[guest])
    if used_narration != narration.keys():
        raise ValueError("A Beyond Destiny narrator portrait does not match the manuscript")
    if missing := expression_art - used:
        raise ValueError(f"Painted expressions have no manuscript cue: {sorted(missing)}")
    return len(actors), len(expression_art)


def bind_audio(episodes, catalog, manifest):
    if manifest.get("version") != 1:
        raise ValueError("Unknown Beyond Destiny audio manifest")
    music, sounds, directions = (manifest[name] for name in ("music", "sounds", "directions"))
    for keys, label in ((music.keys(), "music"), (sounds.keys(), "sounds")):
        if keys != catalog[label].keys():
            missing = sorted(catalog[label].keys() - keys)
            extra = sorted(keys - catalog[label].keys())
            raise ValueError(f"Beyond Destiny {label} bindings differ from manuscript: missing={missing}, extra={extra}")

    verified = set()

    def asset(folder, name):
        if not isinstance(name, str) or not re.fullmatch(r"[a-z0-9_]+", name):
            raise ValueError(f"Invalid Beyond Destiny audio clip name: {name!r}")
        path = f"Audio/{folder}/{name}.wav"
        if path not in verified:
            try:
                with wave.open(str(ASSET_ROOT / path), "rb") as clip:
                    if (clip.getsampwidth() != 2 or clip.getnchannels() not in (1, 2) or
                            not 8000 <= clip.getframerate() <= 48000 or clip.getnframes() < 1000):
                        raise ValueError(f"Unsupported PCM format for {path}")
            except (OSError, EOFError, wave.Error) as error:
                raise ValueError(f"Missing or invalid Beyond Destiny audio clip: {path}") from error
            verified.add(path)
        return path

    # Validate every source key, even the outgoing key on a two-track transition.
    music_paths = {key: asset("Music", clip) for key, clip in music.items()}
    direction_keys = set()
    for episode in episodes:
        for scene in episode["scenes"]:
            for cue in scene["events"]:
                kind, description = cue["kind"], cue.get("text", "")
                if kind == "music":
                    if cue["action"] == "direction":
                        key = f"{episode['id']}:{cue['line']}"
                        if key not in directions:
                            raise ValueError(f"Unbound Beyond Destiny music direction: {key}")
                        direction_keys.add(key)
                        cue.update(action="crossfade", assetPath=asset("Music", directions[key]))
                    elif cue["action"] == "play":
                        clips = cue["clipKeys"]
                        cue["assetPath"] = music_paths[clips[-1]]
                        if len(clips) > 1:
                            cue["action"] = "cut" if "급정지" in description else "crossfade"
                elif kind == "sound":
                    match = sounds[cue["key"]]
                    action = match["action"]
                    if action not in ("play", "loop", "silence"):
                        raise ValueError(f"Unknown sound action {action!r} at {cue['key']}")
                    cue["action"] = action
                    if action != "silence":
                        cue["assetPath"] = asset("Ambience" if action == "loop" else "Effects", match.get("clip"))
                    if accent := match.get("accent"):
                        cue["audioAccent"] = asset("Effects", accent)
                elif kind == "audio":
                    if "빗소리만" in description:
                        cue.update(action="fadeOut", assetPath=asset("Ambience", "rain"))
                    else:
                        cue["action"] = "fadeOut" if any(word in description for word in ("페이드", "서서히", "여운")) else "stop"
    if direction_keys != directions.keys():
        raise ValueError(f"Unused Beyond Destiny audio directions: {sorted(directions.keys() - direction_keys)}")
    return len(verified)


def build(source, *, require_all_maps=False):
    hashes = json.loads(SOURCE_HASHES.read_text(encoding="utf-8"))
    episode_files = [f"yugioh_5ds_ep{number}_script.txt" for number in range(1, EPISODE_COUNT + 1)]
    files = ["5d_s_story_bible.md", *episode_files]
    if hashes.keys() != set(files):
        raise ValueError("The source checksum manifest must cover the story bible and all 37 episodes")
    contents = {name: (source / name).read_bytes() for name in files}
    for name, content in contents.items():
        if hashlib.sha256(content).hexdigest() != hashes[name]:
            raise ValueError(f"Approved manuscript checksum mismatch: {source / name}")
    episodes = [parse_episode(source / name, number, contents[name])
                for number, name in enumerate(episode_files, 1)]
    scenes = [scene for episode in episodes for scene in episode["scenes"]]
    characters = sum(len(restore_source(episode).decode("utf-8-sig").replace("\r\n", "\n"))
                     for episode in episodes)
    if len(scenes) != SCENE_COUNT or characters != CHARACTER_COUNT:
        raise ValueError(f"Expected {SCENE_COUNT} scenes and {CHARACTER_COUNT} characters; "
                         f"got {len(scenes)} and {characters}")
    catalog = asset_catalog(episodes)
    for scene in scenes:
        if scene["backgroundKey"] not in catalog["backgrounds"]:
            raise ValueError(f"Undefined background for {scene['id']}")
    manifest = json.loads(ASSET_MANIFEST.read_text(encoding="utf-8"))
    if manifest.get("version") != 1:
        raise ValueError("Unknown Beyond Destiny asset manifest")
    mapped_count = bind_backgrounds(scenes, catalog["backgrounds"], manifest, require_all_maps)
    cutscenes = bind_cutscenes(scenes, manifest)
    cards = {item["code"]: item["path"] for item in manifest["duelCards"]}
    if cards != {511090001: "DuelCards/speed_world_2.png"} or not all((ASSET_ROOT / path).is_file() for path in cards.values()):
        raise ValueError("Speed World 2 must have original standalone card artwork")
    tracks = {item["key"]: item for item in manifest["ridingBackdrops"]}
    if set(tracks) != {"night", "wrgp"}:
        raise ValueError("Both Beyond Destiny riding venues need dedicated track art")
    for key, track in tracks.items():
        path = f"Maps/spot_riding_{key}.png"
        art = ASSET_ROOT / path
        if track["path"] != path or not art.is_file():
            raise ValueError(f"Missing Beyond Destiny riding venue: {path}")
        with art.open("rb") as image:
            header = image.read(24)
        if (header[:8] != b"\x89PNG\r\n\x1a\n" or
                [int.from_bytes(header[offset:offset + 4], "big") for offset in (16, 20)] != track["resolution"]):
            raise ValueError(f"Unexpected racing background resolution: {path}")
    audio = bind_audio(episodes, catalog, json.loads(AUDIO_MANIFEST.read_text(encoding="utf-8")))
    portraits, expressions = bind_portraits(episodes, manifest)
    return {"schemaVersion": 1, "id": "beyond_destiny", "title": "5D's: Beyond Destiny",
            "linear": True, "sourceBibleSha256": hashes["5d_s_story_bible.md"],
            "speakers": SPEAKERS, "cueCatalog": catalog, "mappedBackgrounds": mapped_count,
            "mappedCutscenes": cutscenes, "mappedAudioClips": audio, "portraitActors": portraits,
            "portraitExpressions": expressions, "episodes": episodes}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=SOURCE)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    parser.add_argument("--check", action="store_true", help="Validate source and compare with the committed JSON")
    parser.add_argument("--require-all-maps", action="store_true", help="Fail on any unbound manuscript background")
    args = parser.parse_args()
    try:
        data = build(args.source, require_all_maps=args.require_all_maps)
        generated = (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        if args.check:
            # Match Git for Windows checkout newline normalization.
            if args.output.read_bytes().replace(b"\r\n", b"\n") != generated:
                raise ValueError(f"Generated campaign is out of date: {args.output}")
        else:
            args.output.write_bytes(generated)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
    print(f"{EPISODE_COUNT} episodes, {SCENE_COUNT} scenes, {CHARACTER_COUNT:,} characters verified")
    print(f"{data['mappedBackgrounds']}/{len(data['cueCatalog']['backgrounds'])} background cues bound to artwork")
    print(f"{data['mappedCutscenes']} event CGs bound to manuscript line ranges")
    print(f"{data['portraitActors']} speaking roles and {data['portraitExpressions']} painted expressions bound to dialogue")
    print(f"{len(data['cueCatalog']['music'])} BGM keys and {len(data['cueCatalog']['sounds'])} SFX cues bound to {data['mappedAudioClips']} audio clips")


if __name__ == "__main__":
    main()
