"""Build the original, offline GX interface skins and glyphs."""

from pathlib import Path
from uuid import NAMESPACE_URL, uuid5

from PIL import Image, ImageDraw


OUTPUT = Path(__file__).resolve().parents[1] / "Assets/StreamingAssets/TagForce/UI/GxChrome"
SUPERSAMPLE = 3
STEEL = (163, 192, 218)
ICE = (111, 231, 246)
BRASS = (246, 202, 127)


def create(size):
    return Image.new("RGBA", tuple(v * SUPERSAMPLE for v in size)), SUPERSAMPLE


def save(image, name):
    result = image.resize(tuple(v // SUPERSAMPLE for v in image.size), Image.Resampling.LANCZOS)
    result.save(OUTPUT / f"{name}.png", optimize=True)
    metadata(OUTPUT / f"{name}.png", folder=False)


def metadata(path, folder):
    relative = path.relative_to(OUTPUT.parents[2]).as_posix()
    guid = uuid5(NAMESPACE_URL, f"ygo-portable-unity/{relative}").hex
    Path(str(path) + ".meta").write_text(
        f"fileFormatVersion: 2\nguid: {guid}\n" + ("folderAsset: yes\n" if folder else "") +
        "DefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n",
        encoding="utf-8",
    )


def skin(name, size, top, bottom, glint, alpha, corner, *, buttons=False):
    image, scale = create(size)
    width, height = image.size
    cut = corner * scale

    def perimeter(inset):
        x = inset * scale
        right, bottom_edge = width - x - 1, height - x - 1
        return [(x + cut, x), (right - 5 * scale, x), (right, x + 5 * scale),
                (right, bottom_edge - cut), (right - cut, bottom_edge),
                (x + 5 * scale, bottom_edge), (x, bottom_edge - 5 * scale), (x, x + cut)]

    mask = Image.new("L", image.size)
    ImageDraw.Draw(mask).polygon(perimeter(4), fill=255)
    draw = ImageDraw.Draw(image)
    for y in range(height):
        ratio = y / (height - 1)
        tint = tuple(round(a + (b - a) * ratio) for a, b in zip(top, bottom))
        draw.line((0, y, width, y), fill=(*tint, alpha))
    clipped = Image.new("RGBA", image.size)
    clipped.paste(image, mask=mask)
    draw = ImageDraw.Draw(clipped)
    draw.line(perimeter(4) + [perimeter(4)[0]], fill=(5, 9, 17, 255), width=3 * scale)
    draw.line(perimeter(7) + [perimeter(7)[0]], fill=(*glint, 144), width=scale)
    draw.line(perimeter(10)[:2], fill=(*glint, 226), width=scale)
    for x in range(21, 44, 6):
        draw.line((x * scale, 15 * scale, (x + 7) * scale, 15 * scale), fill=(*glint, 125), width=scale)
    if not buttons:
        draw.line((width - 58 * scale, height - 18 * scale, width - (corner + 12) * scale, height - 18 * scale),
                  fill=(*glint, 116), width=scale)
        draw.polygon([(width - 44 * scale, height - 23 * scale), (width - 39 * scale, height - 18 * scale),
                      (width - 44 * scale, height - 13 * scale), (width - 49 * scale, height - 18 * scale)],
                     fill=(*glint, 205))
    save(clipped, name)


def glyph(name):
    image, s = create((64, 64))
    draw = ImageDraw.Draw(image)
    white = (250, 250, 250, 255)
    def line(coords, width=4):
        draw.line([(x*s, y*s) for x, y in coords], fill=white, width=width*s, joint="curve")

    def polygon(coords):
        draw.polygon([(x*s, y*s) for x, y in coords], fill=white)

    def ellipse(box, fill=None, width=4):
        draw.ellipse(tuple(v*s for v in box), fill=fill, outline=white, width=width*s)

    if name == "dot":
        ellipse((16, 16, 48, 48), white)
    elif name == "arrow":
        polygon(((32, 7), (53, 47), (32, 39), (11, 47)))
    elif name == "hexagon":
        polygon(((20, 9), (44, 9), (59, 32), (44, 55), (20, 55), (5, 32)))
    elif name == "star":
        polygon(((32, 5), (39, 25), (60, 32), (39, 39), (32, 59), (25, 39), (4, 32), (25, 25)))
    elif name == "ring":
        ellipse((8, 8, 56, 56))
    elif name == "lock":
        draw.arc((18*s, 8*s, 46*s, 40*s), start=180, end=360, fill=white, width=4*s)
        line(((18, 24), (18, 33), (46, 33), (46, 24)))
        draw.rounded_rectangle((12*s, 30*s, 52*s, 55*s), 5*s, fill=white)
        draw.ellipse((29*s, 36*s, 35*s, 44*s), fill=(0, 0, 0, 0))
    elif name == "alert":
        polygon(((27, 8), (37, 8), (35, 37), (29, 37)))
        ellipse((27, 44, 37, 54), white)
    elif name == "chevron":
        line(((24, 14), (42, 32), (24, 50)), width=5)
    elif name == "map":
        line(((8, 19), (24, 12), (40, 18), (56, 11), (56, 47),
              (40, 54), (24, 48), (8, 55), (8, 19)))
        line(((24, 12), (24, 48)))
        line(((40, 18), (40, 54)))
    elif name == "menu":
        for y in (18, 32, 46):
            line(((14, y), (50, y)), width=5)
    save(image, name)


def joystick(name, thumb):
    diameter = 112 if thumb else 192
    image, s = create((diameter, diameter))
    draw = ImageDraw.Draw(image)
    def circle(pad, fill, rim, width):
        draw.ellipse((pad*s, pad*s, (diameter-pad)*s, (diameter-pad)*s), fill=fill, outline=rim, width=width*s)

    circle(5, (4, 11, 21, 166), (*STEEL, 155), 2)
    circle(12 if thumb else 21, (28, 68, 88, 180), (*ICE, 175), 2)
    circle(32 if thumb else 65, (11, 29, 44, 149), (*STEEL, 96), 2)
    save(image, name)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    metadata(OUTPUT, folder=True)
    skin("panel", (192, 144), (20, 37, 57), (9, 17, 31), STEEL, 243, 23)
    skin("glass", (192, 144), (16, 33, 49), (10, 20, 36), STEEL, 220, 19)
    skin("accent-panel", (192, 144), (18, 51, 66), (10, 23, 39), ICE, 246, 23)
    skin("gold-panel", (192, 144), (38, 39, 53), (14, 19, 34), BRASS, 246, 23)
    skin("dialogue", (224, 160), (15, 32, 50), (6, 14, 27), STEEL, 230, 31)
    skin("speaker", (192, 96), (31, 53, 75), (12, 25, 48), STEEL, 253, 17, buttons=True)
    skin("button", (192, 96), (41, 58, 80), (19, 35, 54), STEEL, 246, 18, buttons=True)
    skin("button-primary", (192, 96), (27, 80, 95), (13, 43, 62), ICE, 255, 18, buttons=True)
    skin("button-gold", (192, 96), (96, 69, 46), (44, 34, 31), BRASS, 255, 18, buttons=True)
    skin("interlude", (224, 160), (22, 29, 40), (9, 13, 23), BRASS, 235, 30)
    for icon in ("dot", "arrow", "star", "hexagon", "ring", "lock", "alert", "chevron", "map", "menu"):
        glyph(icon)
    joystick("stick-base", False)
    joystick("stick-thumb", True)
    atlas = OUTPUT.parents[1] / "GX/Maps/world_map.png"
    if atlas.is_file():
        preview = OUTPUT / "academy-preview.png"
        with Image.open(atlas) as source:
            thumbnail = source.convert("RGB")
            thumbnail.thumbnail((896, 504), Image.Resampling.LANCZOS)
            thumbnail.save(preview, optimize=True)
        metadata(preview, folder=False)


if __name__ == "__main__":
    main()
