#!/usr/bin/env python3
"""Import real character art for the 240-entry roster; never draws placeholder people.

Examples:
  python Tools/generate_all_series_assets.py --source-dir TagForceAssets
  python Tools/generate_all_series_assets.py --input yugi.png --id 104
  python Tools/generate_all_series_assets.py --verify-only --require-complete

A separate chibi input is preferred. When only one illustration is provided, its
SD output is a fitted 512px portrait of the same artwork, not a newly drawn chibi.
Opaque inputs need a flat matte background. Complex backgrounds require a real
cutout first; existing transparent alpha and interior white clothing are retained.
"""

import argparse
import json
from collections import Counter
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps, ImageStat
from canon_asset_policy import reject_retired_asset, retired_hashes

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = ROOT / "Tools/all_series_character_manifest.json"
DEFAULT_OUTPUT = ROOT / "Assets/StreamingAssets/TagForce"
SERIES = {"dm": "DM", "gx": "GX", "5ds": "5DS", "zexal": "ZEXAL", "arcv": "ARCV", "vrains": "VRAINS"}
KINDS = {"standing": ("Characters_Standing", "full_", (1024, 1536)),
         "sd": ("Characters_SD", "sd_", (512, 512))}
EXTENSIONS = (".png", ".webp", ".jpg", ".jpeg")


def load_manifest(path):
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict) or set(data) != set(SERIES):
        raise ValueError("Manifest must define dm, gx, 5ds, zexal, arcv and vrains")
    entries = []
    seen = set()
    for series, characters in data.items():
        for entry in characters:
            cid = entry.get("id")
            if type(cid) is not int or cid <= 0 or cid in seen or not entry.get("name"):
                raise ValueError(f"Invalid or duplicate roster entry: {entry}")
            seen.add(cid)
            entries.append({**entry, "series": SERIES[series]})
    if len(entries) != 240:
        raise ValueError(f"Expected the 240-character roster, found {len(entries)}")
    return entries


def transparent_character(image, background="auto", tolerance=30, feather=.75):
    image = ImageOps.exif_transpose(image).convert("RGBA")
    if image.getchannel("A").getextrema()[0] < 255:
        if image.getchannel("A").getbbox() is None:
            raise ValueError("The source image is completely transparent")
        return image
    rgb = image.convert("RGB")
    if background == "auto":
        patch = max(1, min(12, min(image.size) // 16))
        corners = [(0, 0, patch, patch), (image.width-patch, 0, image.width, patch),
                   (0, image.height-patch, patch, image.height),
                   (image.width-patch, image.height-patch, image.width, image.height)]
        samples = [tuple(round(value) for value in ImageStat.Stat(rgb.crop(box)).mean) for box in corners]
        if max(max(sample[channel] for sample in samples) - min(sample[channel] for sample in samples)
               for channel in range(3)) > tolerance:
            raise ValueError("Background is not a uniform matte; provide a transparent cutout or --background #RRGGBB")
        matte = tuple(round(sum(sample[channel] for sample in samples) / 4) for channel in range(3))
    elif background == "white":
        matte = (255, 255, 255)
    elif background == "black":
        matte = (0, 0, 0)
    else:
        value = background.removeprefix("#")
        if len(value) != 6:
            raise ValueError("Background must be auto, white, black or #RRGGBB")
        matte = tuple(int(value[index:index+2], 16) for index in (0, 2, 4))
    difference = ImageChops.difference(rgb, Image.new("RGB", image.size, matte))
    red, green, blue = difference.split()
    maximum = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    candidates = maximum.point(lambda value: 255 if value <= tolerance else 0)
    # One connected flood from an added border removes exterior matte, not white
    # eyes, clothing, or enclosed gaps inside the character illustration.
    connected = Image.new("L", (image.width+2, image.height+2), 255)
    connected.paste(candidates, (1, 1))
    ImageDraw.floodfill(connected, (0, 0), 128)
    alpha = connected.crop((1, 1, image.width+1, image.height+1)).point(lambda value: 0 if value == 128 else 255)
    if alpha.getbbox() is None:
        raise ValueError("Background removal left no visible character")
    if feather:
        # Feather inward so the removed matte cannot become a pale exterior halo.
        alpha = ImageChops.darker(alpha, alpha.filter(ImageFilter.GaussianBlur(feather)))
    image.putalpha(alpha)
    return image


def fit_sprite(image, size):
    bounds = image.getchannel("A").getbbox()
    if bounds is None:
        raise ValueError("No visible character to fit")
    padding = max(12, round(min(size) * .035))
    content = ImageOps.contain(image.crop(bounds), (size[0]-padding*2, size[1]-padding*2), Image.Resampling.LANCZOS)
    result = Image.new("RGBA", size)
    result.alpha_composite(content, ((size[0]-content.width)//2, (size[1]-content.height)//2))
    return result


def validate_sprite(path, size):
    reject_retired_asset(path)
    with Image.open(path) as image:
        image.load()
        if image.format != "PNG" or image.mode != "RGBA" or image.size != size:
            raise ValueError(f"Invalid PNG format or dimensions: {path}")
        alpha = image.getchannel("A")
        if alpha.getextrema()[0] != 0 or alpha.getbbox() is None:
            raise ValueError(f"Invalid character alpha: {path}")


def unity_meta(path):
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        return
    try:
        key = path.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        key = path.resolve().as_posix()
    guid = uuid5(NAMESPACE_URL, "ygo-portable/" + key).hex
    content = f"fileFormatVersion: 2\nguid: {guid}\n"
    if path.is_dir():
        content += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
    meta.write_text(content, encoding="utf-8", newline="\n")


def initialize_folders(output):
    output.mkdir(parents=True, exist_ok=True)
    unity_meta(output)
    for series in SERIES.values():
        parent = output / series
        parent.mkdir(exist_ok=True)
        unity_meta(parent)
        for folder, _, _ in KINDS.values():
            directory = parent / folder
            directory.mkdir(exist_ok=True)
            unity_meta(directory)
            (directory / ".gitkeep").touch(exist_ok=True)


def find_source(directory, entry, kind):
    if directory is None:
        return None
    folder, prefix, _ = KINDS[kind]
    legacy = "Characters_8Head" if kind == "standing" else "Characters_SD"
    filename = prefix + str(entry["id"])
    for relative in (Path(entry["series"])/folder/filename, Path(entry["series"].lower())/folder/filename,
                     Path(folder)/filename, Path(legacy)/filename, Path(filename)):
        for extension in EXTENSIONS:
            candidate = directory / relative.with_suffix(extension)
            if candidate.is_file():
                return candidate
    return None


def run(args):
    entries = load_manifest(args.manifest)
    blocked = retired_hashes()
    if args.series:
        entries = [entry for entry in entries if entry["series"].lower() == args.series.lower()]
    if args.id is not None:
        entries = [entry for entry in entries if entry["id"] == args.id]
        if not entries:
            raise ValueError("Character ID is absent from the selected manifest series")
    if args.input and len(entries) != 1:
        raise ValueError("--input requires one manifest character selected with --id")
    if not args.verify_only:
        initialize_folders(args.output)
    counts = Counter()
    missing = []
    for entry in entries:
        for kind, (folder, prefix, size) in KINDS.items():
            if args.kind != "both" and args.kind != kind:
                continue
            destination = args.output / entry["series"] / folder / f"{prefix}{entry['id']}.png"
            if destination.is_file() and not args.overwrite:
                validate_sprite(destination, size)
                counts["existing"] += 1
                continue
            source = args.input or find_source(args.source_dir, entry, kind)
            if args.verify_only or source is None:
                if destination.is_file():
                    validate_sprite(destination, size)
                    counts["existing"] += 1
                else:
                    missing.append({"series": entry["series"], "id": entry["id"], "kind": kind})
                continue
            reject_retired_asset(source, blocked)
            with Image.open(source) as image:
                prepared = transparent_character(image, args.background, args.tolerance, args.feather)
            prepared = fit_sprite(prepared, size)
            temporary = destination.with_name("." + destination.name + ".importing")
            prepared.save(temporary, "PNG")
            temporary.replace(destination)
            unity_meta(destination)
            validate_sprite(destination, size)
            if entry["series"] == "GX":
                legacy = args.output / "GX" / ("Standing" if kind == "standing" else "SD") / destination.name
                legacy.parent.mkdir(parents=True, exist_ok=True)
                unity_meta(legacy.parent)
                legacy.write_bytes(destination.read_bytes())
                unity_meta(legacy)
            counts["converted"] += 1
    report = {"roster_characters": len(entries), "existing": counts["existing"],
              "converted": counts["converted"], "missing": missing}
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8", newline="\n")
    print(f"Characters: {len(entries)}; existing PNGs: {counts['existing']}; "
          f"converted: {counts['converted']}; missing: {len(missing)}")
    if args.require_complete and missing:
        return 1
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--source-dir", type=Path)
    parser.add_argument("--input", type=Path)
    parser.add_argument("--id", type=int)
    parser.add_argument("--series", choices=list(SERIES)+list(SERIES.values()))
    parser.add_argument("--kind", choices=("both", "standing", "sd"), default="both")
    parser.add_argument("--background", default="auto")
    parser.add_argument("--tolerance", type=int, default=30)
    parser.add_argument("--feather", type=float, default=.75)
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--require-complete", action="store_true")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    if not 0 <= args.tolerance <= 255 or not 0 <= args.feather <= 8:
        parser.error("tolerance must be 0..255 and feather must be 0..8")
    if args.verify_only and (args.input or args.source_dir or args.overwrite):
        parser.error("--verify-only does not accept import/overwrite options")
    try:
        return run(args)
    except (ValueError, OSError) as error:
        parser.exit(2, f"Asset import failed: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
