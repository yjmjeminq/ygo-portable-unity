"""Import locally installed Master Duel art, field textures, HUD and sound into DuelVisuals.

Requires UnityPy and Pillow. The Steam installation is read-only. Generated
files belong in the Unity game's local profile.
"""

import argparse
import io
import json
import os
import re
import shutil
import struct
import tempfile
import wave
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.request import urlopen

import UnityPy
import fmod_toolkit
from PIL import Image, ImageChops, ImageDraw
from master_duel_portraits import render_setup_pose


PROJECT = Path(__file__).resolve().parents[1]
PROFILE = Path.home() / "AppData/LocalLow/YGOPortable/Yu-Gi-Oh LOTD Portable/DuelVisuals"
YDK_IDS = "https://raw.githubusercontent.com/pixeltris/YgoMaster/master/YgoMaster/Data/YdkIds.txt"
ART_VARIANTS = "https://raw.githubusercontent.com/daominah/yugioh_master_duel_card_art/main/alt_art_desc.md"
TOKEN_IDS = {20001444: 16567, 3285552: 16760}
HUD_SPRITES = {
    "MasterDuelLogo": "GUI_TitleLogo",
    "PhaseBase": "GUI_PhaseButtonBase2",
    "PhaseRim": "GUI_PhaseCircle",
    "PhaseDraw": "GUI_PhaseDP",
    "PhaseStandby": "GUI_PhaseSP",
    "PhaseMain1": "GUI_PhaseMP1",
    "PhaseBattle": "GUI_PhaseBP",
    "PhaseMain2": "GUI_PhaseMP2",
    "PhaseEnd": "GUI_PhaseEP",
    "PhaseBannerPlayer": "img_Phase_near",
    "PhaseBannerOpponent": "img_Phase_far",
    "LPPlayer": "GUI_LP_Blue",
    "LPOpponent": "GUI_LP_Red",
    "Pause": "GUI_T_DuelButtonSubmenu01",
    "Log": "GUI_T_DuelButtonLog_Off_1",
    "HandGlow": "GUI_T_ComCardLight2",
    "ChainLinks": "GUI_Duel_CardSelectionList_DialogChain",
    "ChainFrame": "GUI_Global_WindowChain_Frame2",
    "ChainActive": "chain_circle_s_base",
    "ChainResolving": "chain_circle_s_baseR",
    "ChainWinding": "ChainWinding01_01",
    "PromptFieldArrow": "GUI_alpha_PagingWindow_Arrow",
    "PromptFieldGlow": "GUI_Duel_CardSelectionList_Open_eff",
    "PromptGroupLabel": "GUI_CardSelectionListGroupLabel",
    "PromptGrave": "GUI_Log_SendToGrave",
    "PromptBanished": "GUI_Log_Exclude",
    "ZoneHandPlayer": "GUI_alpha_Icon_Cardlist1",
    "ZoneHandOpponent": "GUI_alpha_Icon_Cardlist5",
    "ZoneDeckPlayer": "GUI_alpha_Icon_Cardlist9",
    "ZoneDeckOpponent": "GUI_alpha_Icon_Cardlist9",
    "ZoneGravePlayer": "GUI_alpha_Icon_Cardlist3",
    "ZoneGraveOpponent": "GUI_alpha_Icon_Cardlist7",
    "ZoneExtraPlayer": "GUI_alpha_Icon_Cardlist4",
    "ZoneExtraOpponent": "GUI_alpha_Icon_Cardlist8",
    "ZoneBanishedPlayer": "GUI_alpha_Icon_Cardlist2",
    "ZoneBanishedOpponent": "GUI_alpha_Icon_Cardlist6",
    "ZoneFieldPlayer": "GUI_Log_Field_Near",
    "ZoneFieldOpponent": "GUI_Log_Field_Far",
    "WinnerCrown": "GUI_DuelEnd_WinnerIcon",
    "DuelVictory": "img_text_DuelTextWin_01",
}
# Solid letter layers and horizontal sprite centers of the original prefabs, in pixels.
DUEL_TITLES = {
    "DuelStart": (
        ("img_text_DueltexStart_d01", -610.5),
        ("img_text_DueltexStart_u01", -141),
        ("img_text_DueltexStart_e01", 300),
        ("img_text_DueltexStart_l01", 680),
    ),
    "DuelDefeat": (
        ("img_text_DuelDirectAtk_d00", -375.5),
        ("img_text_DuelDirectAtk_e00", -213.5),
        ("img_text_DuelLose_f01", -65.5),
        ("img_text_DuelDirectAtk_e00", 85.5),
        ("img_text_DuelDirectAtk_a00", 232.5),
        ("img_text_DuelDirectAtk_t00", 390.5),
    ),
    "DuelDraw": (
        ("img_text_DuelDirectAtk_d00", -262.5),
        ("img_text_DuelDirectAtk_r00", -93.5),
        ("img_text_DuelDirectAtk_a00", 68.5),
        ("img_text_DuelDraw_w01", 254.5),
    ),
}
EFFECTS = {
    "Strike": "fxt_hitm_flash01.png",
    "Break": "fxt_ef07445_tnd_001.png",
    "Summon": "fxxt_hitm_fin_m04999_circle01.png",
    "Token": "fxt_13631_bub_001.png",
}
SOUNDS = {
    "Draw": "SE_DUEL_DRAW",
    "Summon": "SE_CARD_TOKEN_SUMMON",
    "Attack": "SE_ATTACK_A_LIGHT_DEFAULT_02",
    "DirectAttack": "SE_DIRECT_ATTACK_PLAYER",
    "Damage": "SE_COST_DAMAGE",
    "Chain": "SE_DUELCHAIN_01",
    "DuelMusic": "BGM_DUEL_NORMAL_03",
}
CUT_IN = re.compile(r"/monstercutin/(tcg|ocg)/p(\d+)/(highend_hd|sd)/")


def scan_bundle(path):
    with path.open("rb") as source:
        if source.read(8) != b"UnityFS\0":
            return []
    return [
        (str(path), obj.path_id, obj.type.name, name)
        for name, obj in UnityPy.load(str(path)).container.items()
        if (obj.type.name == "Texture2D"
            and ("/card/images/illust/" in name or "/monstercutin/" in name or Path(name).name in EFFECTS.values()))
        or (obj.type.name == "TextAsset" and "/monstercutin/" in name and name.endswith(("js.json", ".atlas.txt")))
        or (obj.type.name == "Sprite" and "/monstercutin/" in name and name.endswith("_monpng.png"))
    ]


def get_catalog(install, catalogs):
    if catalogs:
        return [row for catalog in catalogs for row in json.loads(catalog.read_text(encoding="utf-8"))]
    folders = (install / "LocalData", install / "masterduel_Data/StreamingAssets/AssetBundle")
    bundles = (path for folder in folders for path in folder.rglob("*") if path.is_file())
    with ThreadPoolExecutor(max_workers=6) as pool:
        return [row for entries in pool.map(scan_bundle, bundles) for row in entries]


def read_asset(row):
    file, path_id, kind, name = row
    entry = UnityPy.load(file).container[name]
    if entry.path_id != path_id or entry.type.name != kind:
        raise ValueError(f"Asset identity changed for {name}")
    return entry.deref_parse_as_object()


def read_reference(path, url):
    if path is not None:
        return path.read_text(encoding="utf-8")
    with urlopen(url, timeout=30) as source:
        return source.read().decode("utf-8")


def artwork_ids(args):
    mapped = defaultdict(set)
    for line in read_reference(args.id_map, YDK_IDS).splitlines():
        fields = line.split()
        if len(fields) == 2 and all(field.isdecimal() for field in fields):
            code, cid = map(int, fields)
            mapped[code].add(cid)

    variants = defaultdict(set)
    bases = {}
    for line in read_reference(args.variant_map, ART_VARIANTS).splitlines():
        fields = [field.strip() for field in line.strip().strip("|").split("|")]
        if len(fields) > 5 and fields[1].isdecimal() and fields[3].isdecimal():
            base, alternative = int(fields[1]), int(fields[3])
            variants[base].add(alternative)
            bases[alternative] = base
    return mapped, variants, bases


def card_images(rows):
    images = {}
    for row in rows:
        _, _, kind, name = row
        cid = Path(name).stem
        if kind != "Texture2D" or "/card/images/illust/" not in name or not cid.isdecimal():
            continue
        art_id = int(cid)
        if art_id not in images or "/common/" in name:
            images[art_id] = row
    return images


def source_overframe_ids(resources):
    table = next((obj.read().m_Script for obj in resources.objects
                  if obj.type.name == "TextAsset" and obj.peek_name() == "of_card_asset"), None)
    if table is None:
        return {}
    if isinstance(table, str):
        table = table.encode("utf-8", "surrogateescape")
    if len(table) % 4:
        raise ValueError("The installed Overframe table has an invalid record size.")
    return {edition: ordinary for edition, ordinary in struct.iter_unpack("<HH", table) if edition and ordinary}


def export_source_overframes(output, args, ids, images):
    cards = json.loads((PROJECT / "Assets/StreamingAssets/cards_kr.json").read_text(encoding="utf-8-sig"))["cards"]
    card_codes = {card["code"] for card in cards if not card["type"] & 0x4000}
    codes_by_id = defaultdict(set)
    for line in read_reference(args.id_map, YDK_IDS).splitlines():
        fields = line.split()
        if len(fields) != 2 or not all(field.isdecimal() for field in fields):
            continue
        code, card_id = map(int, fields)
        if code in card_codes:
            codes_by_id[card_id].add(code)
    for card in cards:
        if card["code"] in card_codes and card["konamiId"]:
            codes_by_id[card["konamiId"]].add(card["code"])

    entries = {}
    for edition, ordinary in sorted(ids.items()):
        row = images.get(edition)
        codes = codes_by_id.get(ordinary)
        if row is None or not row[-1].endswith(".png") or not codes:
            continue
        texture = read_asset(row).image.convert("RGBA")
        if texture.height <= texture.width or texture.getchannel("A").getextrema() != (0, 255):
            print(f"Skipped unexpected Overframe texture {edition}: {texture.size}", flush=True)
            continue
        with io.BytesIO() as image:
            texture.save(image, format="PNG", compress_level=2)
            payload = image.getvalue()
        for code in sorted(codes):
            entries.setdefault(code, (edition, payload))
    if not entries:
        raise ValueError("No complete Overframe textures matched the installed card database.")

    with (output / "overframeart.pack").open("wb") as pack:
        pack.seek(12 + 16 * len(entries))
        blobs, index = {}, []
        for code, (edition, payload) in sorted(entries.items()):
            if edition not in blobs:
                blobs[edition] = pack.tell(), len(payload)
                pack.write(payload)
            index.append(struct.pack("<IiII", code, edition, *blobs[edition]))
        pack.seek(0)
        pack.write(struct.pack("<4sII", b"MDOF", 1, len(entries)))
        pack.writelines(index)
    print(f"Imported {len(blobs)} Overframe textures for {len(entries)} card keys from {len(ids)} matched editions.", flush=True)


def card_variants(args, images):
    ids, variants, bases = artwork_ids(args)
    cards = json.loads((PROJECT / "Assets/StreamingAssets/cards_kr.json").read_text(encoding="utf-8-sig"))["cards"]
    for card in cards:
        code = card["code"]
        token = bool(card["type"] & 0x4000)
        if token and code not in TOKEN_IDS:
            continue
        source_ids = ids.get(code, set())
        normal_ids = {bases.get(cid, cid) for cid in source_ids}
        preferred = card["konamiId"]
        if token:
            base = TOKEN_IDS[code]
        elif preferred and (not normal_ids or preferred in normal_ids):
            base = preferred
        elif card["lotdId"] in normal_ids:
            base = card["lotdId"]
        elif len(normal_ids) == 1:
            base = next(iter(normal_ids))
        else:
            continue
        if card["alias"]:
            # An explicit alternate YDK code keeps its own image when supplied.
            exact = (source_ids & variants[base]) & images.keys()
            if len(exact) == 1:
                yield code, 0, next(iter(exact))
            continue
        if base in images:
            yield code, 0, base
        for variant in sorted(variants[base] & images.keys()):
            yield code, variant, variant


def export_card_art(output, entries, images):
    imported = list(entries)
    offsets, index = {}, []
    with (output / "cardart.pack").open("wb") as pack:
        pack.seek(12 + 16 * len(imported))
        for code, variant, cid in imported:
            if cid not in offsets:
                art = read_asset(images[cid]).image.convert("RGB")
                art.thumbnail((320, 320), Image.Resampling.LANCZOS)
                buf = io.BytesIO()
                art.save(buf, format="JPEG", quality=87)
                offsets[cid] = pack.tell(), buf.tell()
                pack.write(buf.getbuffer())
            index.append(struct.pack("<IiII", code, variant, *offsets[cid]))
        pack.seek(0)
        pack.write(struct.pack("<4sII", b"MDAR", 1, len(index)))
        pack.writelines(index)
    print(f"{len(index)} artworks for {len({code for code, _, _ in imported})} cards; "
          f"{sum(variant != 0 for _, variant, _ in imported)} alternate illustrations", flush=True)
    return imported


def export_cut_ins(output, rows, entries):
    candidates = defaultdict(dict)
    for row in rows:
        _, _, kind, name = row
        match = CUT_IN.search(name)
        if kind not in {"Texture2D", "TextAsset", "Sprite"} or not match:
            continue
        region, cid, quality = match.groups()
        files = candidates[int(cid)].setdefault((quality, region), {})
        filename = Path(name).name
        if filename not in files or kind == "Sprite":
            files[filename] = row

    directory = output / "CutIns"
    directory.mkdir()
    jobs = defaultdict(list)
    for code, variant, cid in entries:
        if cid in candidates:
            jobs[cid].append(directory / f"{code}-{variant}.png")

    def bake(job):
        cid, destinations = job
        for quality in (("highend_hd", "tcg"), ("highend_hd", "ocg"), ("sd", "tcg"), ("sd", "ocg")):
            files = candidates[cid].get(quality, {})
            try:
                image = cut_in_image(cid, files)
            except (KeyError, ValueError, IndexError, StopIteration, OSError):
                continue
            if image is None:
                continue
            image.save(destinations[0], compress_level=2)
            for destination in destinations[1:]:
                shutil.copyfile(destinations[0], destination)
            return len(destinations)
        return 0

    with ThreadPoolExecutor(max_workers=6) as pool:
        count = 0
        for processed, written in enumerate(pool.map(bake, jobs.items()), 1):
            count += written
            if processed % 100 == 0:
                print(f"Processed {processed} of {len(jobs)} cut-in sets", flush=True)
    print(f"{count} assembled or standalone summon cut-ins", flush=True)


def cut_in_image(cid, files):
    name = f"p{cid}"
    if name + "_monpng.png" in files:
        image = read_asset(files[name + "_monpng.png"]).image.convert("RGBA")
        alpha = image.getchannel("A")
        bounds = alpha.getbbox()
        if bounds is None or alpha.getextrema()[0] > 8:
            return None
        image = image.crop(bounds)
        image.thumbnail((950, 780), Image.Resampling.LANCZOS)
        return image
    if name + "js.json" not in files or name + ".atlas.txt" not in files:
        return None

    def text(filename):
        script = read_asset(files[filename]).m_Script
        return script.decode("utf-8-sig") if isinstance(script, bytes) else script

    return render_setup_pose(json.loads(text(name + "js.json")), text(name + ".atlas.txt"),
                             lambda page: read_asset(files[page.lower()]).image.convert("RGBA"))


def publish_cut_ins(source, profile):
    target = profile / "CutIns"
    if target.resolve().parent != profile.resolve():
        raise ValueError("CutIns directory must be inside the game profile")
    previous = source.parent / "previous-cut-ins"
    if target.exists():
        target.replace(previous)
    try:
        source.replace(target)
    except OSError:
        if previous.exists():
            previous.replace(target)
        raise


def export_field(output, textures):
    atlas = textures["Mat_002_01_BaseColor_near"].parse_as_object().image.convert("RGB")
    ground_atlas = textures["Mat_002_02_BaseColor_near"].parse_as_object().image.convert("RGB")
    side = ground_atlas.width
    ground = ground_atlas.crop(tuple(int(side * value) for value in (.40, .33, .61, .54)))
    ground.save(output / "Ground.png")

    size = atlas.width
    half = atlas.transform((1376, 460), Image.Transform.EXTENT,
                           (-.106 * size, .147 * size, 1.106 * size, .499 * size),
                           Image.Resampling.BICUBIC)
    # The baked atlas has empty gutters beyond the stone slab.
    backing = Image.new("RGB", half.size)
    tile = ground.resize((344, 460), Image.Resampling.BICUBIC)
    for i in range(4):
        backing.paste(tile if i % 2 == 0 else tile.transpose(Image.Transpose.FLIP_LEFT_RIGHT), (i * 344, 0))
    edge = Image.new("L", (half.width, 1))
    edge.putdata([max(0, min(255, (min(x, half.width - x - 1) - 92) * 4)) for x in range(half.width)])
    nonempty = half.convert("L").point(lambda value: max(0, min(255, (value - 16) * 8)))
    half = Image.composite(half, backing, ImageChops.multiply(nonempty, edge.resize(half.size)))
    field = Image.new("RGB", (half.width, half.height * 2))
    field.paste(half, (0, 0))
    field.paste(half.transpose(Image.Transpose.ROTATE_180), (0, half.height))
    field.save(output / "Field.png")


def compose_duel_title(sprites, title):
    if any(name not in sprites for name, _ in title):
        return None
    pieces = []
    for name, center in title:
        image = sprites[name].parse_as_object().image.convert("RGBA")
        pieces.append((image, round(center - image.width / 2)))

    left = min(x for _, x in pieces)
    right = max(x + image.width for image, x in pieces)
    height = max(image.height for image, _ in pieces)
    result = Image.new("RGBA", (right - left, height))
    for image, x in pieces:
        result.alpha_composite(image, (x - left, (height - image.height) // 2))
    return result


def crop_stone_well(atlas, x, y, radius):
    well = atlas.crop((x - radius, y - radius, x + radius, y + radius))
    # The original rim is mapped to curved geometry. Mask out the neighboring atlas islands.
    size = radius * 8
    mask = Image.new("L", (size, size))
    ImageDraw.Draw(mask).ellipse((4, 4, size - 5, size - 5), fill=255)
    mask = mask.resize(well.size, Image.Resampling.LANCZOS)
    well.putalpha(ImageChops.multiply(well.getchannel("A"), mask))
    return well


def export_hud(output, sprites, textures):
    sources = {filename: sprites.get(name) for filename, name in HUD_SPRITES.items()}
    sources["DeckBack"] = textures.get("ProtectorIcon1070001")
    sources["ChainCardHighlight"] = textures.get("ChainCardHighlight001")
    directory = output / "Hud"
    directory.mkdir()
    imported = 0
    for filename, asset in sources.items():
        if asset is None:
            continue
        image = asset.parse_as_object().image.convert("RGBA")
        image.save(directory / f"{filename}.png")
        imported += 1

    for filename, title in DUEL_TITLES.items():
        image = compose_duel_title(sprites, title)
        if image is not None:
            image.save(directory / f"{filename}.png")
            imported += 1

    grave_texture = textures.get("Grave_002_BaseColor_near")
    if grave_texture is not None:
        atlas = grave_texture.parse_as_object().image.convert("RGBA")
        if atlas.size == (512, 512):
            crop_stone_well(atlas, 129, 128, 118).save(directory / "GraveWell.png")
            crop_stone_well(atlas, 129, 381, 108).save(directory / "BanishedWell.png")
            imported += 2
    return imported


def export_effects(output, rows):
    directory = output / "Effects"
    directory.mkdir()
    for kind, name in EFFECTS.items():
        row = next((row for row in rows if row[2] == "Texture2D" and row[3].endswith("/" + name)), None)
        if row is None:
            continue
        image = read_asset(row).image.convert("RGBA")
        if kind in {"Summon", "Break"} or image.getchannel("A").getextrema()[0] == 255:
            intensity = image.convert("L")
            if kind == "Token":
                image.putalpha(intensity)
            else:
                image = Image.new("RGBA", image.size, "white")
                image.putalpha(intensity)
        image.save(directory / f"{kind}.png")


def export_audio(output, clips, data_dir):
    directory = output / "Audio"
    directory.mkdir()
    for filename, name in SOUNDS.items():
        if name not in clips:
            continue
        clip = clips[name].parse_as_object()
        resource = clip.m_Resource
        local_resource = data_dir / "resources.resource"
        if resource is not None and resource.m_Source == "resources.resource" and local_resource.is_file():
            with local_resource.open("rb") as file:
                file.seek(resource.m_Offset)
                data = file.read(resource.m_Size)
            samples = fmod_toolkit.raw_to_wav(data, name, clip.m_Channels, clip.m_Frequency)
        else:
            samples = clip.samples
        source = next(iter(samples.values()))
        with wave.open(io.BytesIO(source), "rb") as original:
            if original.getsampwidth() != 2 or original.getnchannels() not in (1, 2):
                raise ValueError(f"Unsupported PCM format for {name}")
            with wave.open(str(directory / f"{filename}.wav"), "wb") as destination:
                destination.setparams(original.getparams())
                destination.writeframes(original.readframes(original.getnframes()))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("install", type=Path, help="Master Duel Steam installation directory")
    parser.add_argument("--output", type=Path, default=PROFILE)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--hud-only", action="store_true", help="Refresh HUD textures and the default card back only")
    mode.add_argument("--overframes-only", action="store_true", help="Refresh the installed Overframe face pack only")
    parser.add_argument("--bundle-catalog", type=Path, action="append", default=[])
    parser.add_argument("--id-map", type=Path)
    parser.add_argument("--variant-map", type=Path)
    args = parser.parse_args()

    data_dir = args.install / "masterduel_Data"
    base = UnityPy.load(str(data_dir / "data.unity3d"))
    textures = {obj.peek_name(): obj for obj in base.objects if obj.type.name == "Texture2D"}
    sprites = {obj.peek_name(): obj for obj in base.objects if obj.type.name == "Sprite"}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="ygo-duel-import-", dir=args.output.parent) as directory:
        output = Path(directory)
        hud_count = 0 if args.overframes_only else export_hud(output, sprites, textures)
        if args.overframes_only:
            rows = get_catalog(args.install, args.bundle_catalog)
            export_source_overframes(output, args, source_overframe_ids(base), card_images(rows))
        elif args.hud_only:
            if hud_count == 0:
                raise ValueError("No installed duel HUD textures were found")
        else:
            clips = {obj.peek_name(): obj for obj in base.objects if obj.type.name == "AudioClip"}
            rows = get_catalog(args.install, args.bundle_catalog)
            images = card_images(rows)
            overframes = source_overframe_ids(base)
            if overframes.keys() & images.keys():
                export_source_overframes(output, args, overframes, images)
            images = {card_id: row for card_id, row in images.items() if card_id not in overframes}
            if not images:
                raise ValueError("No installed card artwork bundles were found")
            export_field(output, textures)
            export_effects(output, rows)
            export_audio(output, clips, data_dir)
            entries = export_card_art(output, card_variants(args, images), images)
            export_cut_ins(output, rows, entries)
        args.output.mkdir(exist_ok=True)
        for file in sorted(output.rglob("*")):
            relative = file.relative_to(output)
            if file.is_file() and relative.parts[0] != "CutIns":
                destination = args.output / relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                os.replace(file, destination)
        if not args.hud_only and not args.overframes_only:
            publish_cut_ins(output / "CutIns", args.output)
    if not args.overframes_only:
        print(f"Imported {hud_count} HUD textures to {args.output}", flush=True)


if __name__ == "__main__":
    main()
