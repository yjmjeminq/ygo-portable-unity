"""Generate original high-resolution card badges and optional local overrides."""

import argparse
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5

from PIL import Image, ImageColor, ImageDraw, ImageFilter, ImageFont

if __package__:
    from .generate_duel_icons import ATTRIBUTES, FLAME, RACES, ROOT, FONT
else:
    from generate_duel_icons import ATTRIBUTES, FLAME, RACES, ROOT, FONT


SIZE = 256
OUTPUT = ROOT / "Assets/Resources/Duel/CardIcons"
ATTRIBUTE_LABELS = {
    "earth": "地", "water": "水", "fire": "炎", "wind": "風", "light": "光",
    "dark": "闇", "god": "神", "spell": "魔", "trap": "罠",
}
RACE_NAMES = {
    "soldier": "warrior", "wizard": "spellcaster", "angel": "fairy", "devil": "fiend",
    "undead": "zombie", "aquarius": "aqua", "flame": "pyro", "sandrock": "rock",
    "bird": "wingedbeast", "botanical": "plant", "beastbtl": "beastwarrior",
    "dinosaurs": "dinosaur", "poseidon": "seaserpent", "reptiles": "reptile",
    "god": "divinebeast", "creator": "creatorgod", "mystdragon": "wyrm", "cyverse": "cyberse",
}
SUBTYPES = {
    "quickplay": ("#77e0ff", RACES["thunder"][0], ()),
    "continuous": ("#6ff0c8", ("19,49 27,34 39,34 50,44 61,34 74,34 81,48 76,62 64,66 50,56 37,66 25,63",),
                   ("27,49 34,41 43,49 35,57", "56,50 65,41 73,49 66,57")),
    "equip": ("#ffd18c", ("57,23 75,20 72,39 49,62 55,69 48,76 40,68 30,78 22,70 32,60 24,52 31,45 39,54",
                         "24,22 43,24 49,34 44,42 61,55 71,47 77,55 70,63 79,70 71,78 61,68 54,77 47,70 54,63 30,40"), ()),
    "field": ("#78edda", ("50,18 58,42 82,50 58,58 50,82 42,58 18,50 42,42",),
              ("50,39 61,50 50,61 39,50",)),
    "ritual": ("#ffc481", FLAME + ("24,67 76,67 68,76 57,76 63,84 37,84 43,76 32,76",),
               ("47,48 58,62 53,67 40,67 37,62",)),
    "counter": ("#ffabd7", ("20,45 44,24 44,37 63,37 76,45 81,59 76,72 65,79 41,79 41,67 63,67 69,63 69,56 63,50 44,50 44,64",), ()),
}


def xy(outline):
    return [tuple(round(int(value) * SIZE / 100) for value in pair.split(",")) for pair in outline.split()]


def plate_mask(inset, bevel):
    mask = Image.new("L", (SIZE, SIZE))
    draw = ImageDraw.Draw(mask)
    if bevel:
        p = xy("25,8 75,8 92,25 92,75 75,92 25,92 8,75 8,25")
        scale = 1 - inset / 108
        draw.polygon([(128 + (x - 128) * scale, 128 + (y - 128) * scale) for x, y in p], fill=255)
    else:
        margin = 16 + inset
        draw.ellipse((margin, margin, SIZE - margin - 1, SIZE - margin - 1), fill=255)
    return mask


def gradient(stops):
    texture = Image.new("RGBA", (1, SIZE))
    colors = [ImageColor.getrgb(color) for color in stops]
    rows = []
    for y in range(SIZE):
        pos = y / (SIZE - 1) * (len(colors) - 1)
        i = min(len(colors) - 2, int(pos))
        rows.append(tuple(round(a + (b - a) * (pos - i)) for a, b in zip(colors[i], colors[i + 1])) + (255,))
    texture.putdata(rows)
    return texture.resize((SIZE, SIZE))


def render(accent, symbol, cutouts, *, orb=False, label=None):
    base = Image.new("RGBA", (SIZE, SIZE))
    plate = plate_mask(0, not orb)
    glow = Image.new("RGBA", base.size, accent)
    base.paste(glow, mask=plate.filter(ImageFilter.GaussianBlur(9)).point(lambda v: v * 0.48))

    rim = gradient(("#b4cbd3", "#354654", "#16222f", accent, "#546574"))
    base.paste(rim, mask=plate)
    edge = plate_mask(5, not orb)
    base.paste("#081625", mask=edge)
    face = plate_mask(10, not orb)
    fill = gradient(("#25394a", "#142a3b", "#091724", "#102936"))
    tint = Image.blend(fill, glow, .18 if orb else .07)
    base.paste(tint, mask=face)

    shine = Image.new("L", base.size)
    draw = ImageDraw.Draw(shine)
    draw.ellipse((43, 39, 200, 111), fill=94 if orb else 44)
    gloss = Image.new("RGBA", base.size, "#d7f7ff")
    base.paste(gloss, mask=shine.filter(ImageFilter.GaussianBlur(22)))

    glyph = Image.new("L", base.size)
    draw = ImageDraw.Draw(glyph)
    font = ImageFont.truetype(str(FONT), 124) if label else None
    if font and bytes(font.getmask(label)) != bytes(font.getmask("\U0010ffff")):
        l, t, r, b = draw.textbbox((0, 0), label, font=font)
        draw.text(((SIZE - r - l) / 2, (SIZE - b - t) / 2), label, font=font, fill=255)
    else:
        for contour in symbol:
            draw.polygon(xy(contour), fill=255)
        for contour in cutouts:
            draw.polygon(xy(contour), fill=0)
    base.paste(glow, mask=glyph.filter(ImageFilter.GaussianBlur(8)).point(lambda v: min(255, v * 1.1)))
    base.paste(gradient(("#ffffff", "#defffd", accent, accent)), mask=glyph)
    return base.resize((128, 128), Image.Resampling.LANCZOS)


def images():
    for name, (color, symbol, cutouts) in ATTRIBUTES.items():
        relative = f"attribute/{'divine' if name == 'god' else name}.png"
        yield relative, render(color, symbol, cutouts, orb=True, label=ATTRIBUTE_LABELS[name])
    for name, (symbol, cutouts) in RACES.items():
        relative = f"race/{RACE_NAMES.get(name, name)}.png"
        color = "#f6ca78" if name in ("god", "creator") else "#8fe3e9"
        yield relative, render(color, symbol, cutouts)
    for name, (color, symbol, cutouts) in SUBTYPES.items():
        yield f"spelltrap/{name}.png", render(color, symbol, cutouts)


def unity_meta(path, *, folder=False):
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        return
    guid = uuid5(NAMESPACE_URL, "ygo-portable/" + path.relative_to(ROOT).as_posix()).hex
    importer = ("folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" if folder else
                "TextureImporter:\n  serializedVersion: 13\n  mipmaps:\n    enableMipMap: 0\n"
                "    sRGBTexture: 1\n  textureSettings:\n    filterMode: 1\n    wrapU: 1\n    wrapV: 1\n"
                "  nPOTScale: 0\n  alphaIsTransparency: 1\n  textureType: 0\n")
    meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\n{importer}", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--local-visuals", type=Path, help="Also copy missing badges to this DuelVisuals folder.")
    options = parser.parse_args()
    local = options.local_visuals / "CardIcons" if options.local_visuals else None
    OUTPUT.mkdir(parents=True, exist_ok=True)
    unity_meta(OUTPUT, folder=True)
    for relative, image in images():
        path = OUTPUT / relative
        path.parent.mkdir(exist_ok=True)
        unity_meta(path.parent, folder=True)
        image.save(path, optimize=True)
        unity_meta(path)
        if local:
            destination = local / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            if not destination.exists():
                destination.write_bytes(path.read_bytes())


if __name__ == "__main__":
    main()
