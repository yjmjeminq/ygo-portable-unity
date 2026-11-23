"""Render original character placeholders for the GX SD and standing slots."""

import argparse
from dataclasses import dataclass
from hashlib import blake2b
from io import BytesIO
import json
from pathlib import Path
from random import Random
import re
from uuid import NAMESPACE_URL, uuid5

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
STREAMING = ROOT / "Assets" / "StreamingAssets"
GX_ROOT = Path("TagForce/GX")
MANIFEST = STREAMING / "tagforce_gx_script.json"
FORMATS = (("SD", "sd", "sdSpritePath", (512, 512)),
           ("Standing", "full", "standingSpritePath", (900, 1400)))
# File IDs keep this tool usable on checkouts without the local campaign JSON.
GROUPS = ((119, (119, 209, 214)), (137, (137, 207)), (117, (117, 3100)),
          (107, (107, 199)), (111, (111, 110)), (125, (125, 108)), (131, (131, 3101)),
          (136, (123, 136)), *((i, (i,)) for i in (128, 118, 113, 132, 120, 109, 121,
              112, 106, 133, 127, 115, 114, 215, 205, 206, 126, 213, 116, 129, 202,
              201, 122, 130, 3102, 225, 3103, 124)))

INK = "#202334"
SKIN_COLORS = ("#ffd7b1", "#f7ba92", "#d68e68", "#b47558", "#875945", "#edc1a9")
HAIR_COLORS = ("#442e45", "#694044", "#336067", "#36596e", "#604c7e", "#945648",
               "#866130", "#69474d", "#364b52", "#865c70", "#4a3b3a", "#43766b")
EYE_COLORS = ("#386c71", "#806637", "#635288", "#456286", "#80554e", "#39695a")
UNIFORMS = (("#314c69", "#79c9d0"), ("#2f5e60", "#e0b76e"), ("#653e57", "#e6a78a"),
            ("#54476f", "#b5d5da"), ("#2d4e4d", "#a6c88a"), ("#674b43", "#efcf95"))

HAIR_SHAPES = (
    ((7, 43), (13, 22), (27, 9), (56, 5), (77, 13), (92, 28), (93, 77), (83, 89), (72, 41), (28, 41), (20, 90), (6, 79)),
    ((11, 47), (4, 32), (20, 31), (19, 17), (39, 17), (42, 4), (58, 14), (69, 5), (73, 19), (91, 17), (86, 33), (96, 45), (86, 65), (74, 43), (29, 42), (14, 66)),
    ((10, 52), (13, 26), (28, 11), (56, 6), (77, 14), (88, 27), (92, 49), (84, 68), (82, 44), (28, 38), (17, 68)),
    ((10, 44), (17, 18), (34, 6), (62, 6), (83, 20), (90, 43), (86, 69), (76, 60), (71, 41), (31, 39), (20, 72)),
    ((10, 51), (11, 29), (23, 18), (41, 16), (57, 4), (81, 9), (89, 21), (92, 51), (82, 64), (76, 42), (30, 41), (17, 77)),
    ((8, 37), (12, 24), (23, 23), (28, 10), (48, 10), (59, 4), (69, 14), (83, 14), (88, 28), (96, 31), (93, 58), (82, 70), (74, 46), (27, 46), (17, 70), (6, 54)),
)


@dataclass(frozen=True)
class Style:
    skin: str
    hair: str
    eyes: str
    cut: int
    outerwear: str
    accent: str
    emblem: int
    glasses: bool
    skirt: bool
    broad: bool


def rng(key):
    seed = blake2b(f"original-gx-sprite-v1/{key}".encode(), digest_size=8).digest()
    return Random(int.from_bytes(seed, "big"))


def style_for(actor, form):
    face = rng(actor)
    clothes = rng(f"uniform/{form}")
    outerwear, accent = clothes.choice(UNIFORMS)
    return Style(face.choice(SKIN_COLORS), face.choice(HAIR_COLORS), face.choice(EYE_COLORS),
                 face.randrange(len(HAIR_SHAPES)), outerwear, accent, clothes.randrange(4),
                 face.randrange(6) == 0, clothes.randrange(3) == 0, face.randrange(3) == 0)


def mix(color, target, amount):
    def channels(rgb):
        return (int(rgb[i:i + 2], 16) for i in (1, 3, 5))
    return tuple(round(a + (b - a) * amount) for a, b in zip(channels(color), channels(target)))


class Pen:
    def __init__(self, image, frame):
        self.draw = ImageDraw.Draw(image)
        self.left, self.top, width, height = frame
        self.sx, self.sy = width / 100, height / 100
        self.stroke = max(2, round(min(self.sx, self.sy) * 1.5))

    def point(self, point):
        x, y = point
        return round(self.left + x * self.sx), round(self.top + y * self.sy)

    def shape(self, points, color, outline=None):
        self.draw.polygon([self.point(p) for p in points], fill=color, outline=outline, width=self.stroke)

    def oval(self, box, color, outline=None):
        x0, y0, x1, y1 = box
        self.draw.ellipse((self.point((x0, y0)), self.point((x1, y1))), fill=color,
                          outline=outline, width=self.stroke)

    def line(self, points, color, weight=1):
        self.draw.line([self.point(p) for p in points], fill=color,
                       width=max(1, round(self.stroke * weight)), joint="curve")


def draw_outfit(pen, look):
    body_shade = mix(look.outerwear, INK, .48)
    trim = look.accent
    width = 4 if look.broad else 0
    for direction in (-1, 1):
        def arm(points):
            return [(50 + direction * x, y) for x, y in points]
        pen.shape(arm(((17, 9), (30 + width, 8), (38 + width, 39), (28 + width, 48),
                       (22, 44), (25, 34), (15, 19))), body_shade, INK)
        pen.shape(arm(((27 + width, 44), (37 + width, 44), (36 + width, 48), (31 + width, 53),
                       (25 + width, 52), (23 + width, 48))), look.skin, INK)
        pen.line(arm(((25 + width, 42), (37 + width, 40))), trim)

    for leg in (31, 53):
        if look.skirt:
            pen.shape(((leg, 63), (leg+17, 63), (leg+15, 91), (leg+2, 91)), look.skin, INK)
        else:
            pen.shape(((leg-2, 46), (leg+19, 46), (leg+16, 88), (leg+3, 88)), "#38445a", INK)
            pen.line(((leg+9, 64), (leg+9, 83)), "#57637a")
        pen.shape(((leg+1, 87), (leg+16, 87), (leg+18, 93), (leg+23, 95), (leg+22, 100),
                   (leg-1, 100), (leg-3, 96)), "#272c3f", INK)
        pen.line(((leg+1, 97), (leg+20, 97)), "#606d80")

    if look.skirt:
        pen.shape(((33, 43), (67, 43), (79, 66), (69, 68), (31, 68), (21, 66)), "#38445a", INK)
        for x in (34, 43, 57, 66):
            pen.line(((50+(x-50)*.7, 48), (x, 67)), "#57637a")
        pen.line(((25, 64), (75, 64)), trim, .65)

    pen.shape(((42, 1), (58, 1), (60, 11), (40, 11)), look.skin, INK)
    pen.shape(((37, 4), (63, 4), (72 + width, 10), (67, 28), (68, 47),
               (59, 50), (40, 48), (32, 49), (34, 27), (28 - width, 11)), look.outerwear, INK)
    pen.shape(((39, 6), (50, 10), (60, 6), (55, 21), (48, 30), (43, 17)), "#e6e4dc", INK)
    pen.shape(((61, 11), (66, 18), (61, 36), (65, 47), (52, 48), (55, 29)), body_shade)
    pen.line(((54, 12), (48, 26), (44, 44), (40, 47)), trim)
    pen.line(((32, 40), (42, 41)), trim, .7)
    pen.line(((57, 40), (67, 39)), trim, .7)
    pen.oval((55, 23, 65, 30), "#1f2c39", trim)
    emblems = (((60, 24), (63, 27), (60, 29), (57, 27)),
               ((57, 27), (60, 24), (63, 27), (60, 27), (60, 29)),
               ((57, 25), (63, 25), (63, 27), (57, 27)),
               ((60, 24), (63, 28), (57, 28)))
    pen.shape(emblems[look.emblem], trim)


def draw_head(pen, look):
    skin_shade = mix(look.skin, "#874d50", .24)
    highlight = mix(look.hair, "#f2c7a6", .34)
    if look.cut == 3:
        pen.shape(((66, 28), (87, 29), (96, 46), (97, 76), (84, 91), (82, 64), (66, 55)), look.hair, INK)
        pen.line(((90, 33), (88, 51), (92, 65)), highlight)
    pen.shape(HAIR_SHAPES[look.cut], look.hair, INK)
    pen.oval((14, 52, 27, 73), skin_shade, INK)
    pen.oval((74, 52, 86, 73), skin_shade, INK)
    pen.shape(((22, 34), (35, 24), (64, 24), (78, 34), (79, 63), (73, 79),
               (61, 89), (50, 93), (38, 89), (27, 79), (21, 64)), look.skin, INK)
    pen.shape(((76, 46), (77, 65), (68, 82), (60, 86), (67, 71), (67, 53)), skin_shade)
    for left in (30, 58):
        pen.oval((left, 51, left+13, 62), "#fff6e8", INK)
        pen.oval((left+3, 52, left+10, 61), look.eyes)
        pen.oval((left+5, 53, left+9, 60), INK)
        pen.oval((left+4, 53, left+7, 56), "#ffffff")
        pen.line(((left, 48), (left+7, 46), (left+13, 47)), look.hair, .9)
    if look.glasses:
        pen.line(((28, 51), (43, 51), (43, 63), (29, 63), (28, 51)), "#3b4b61", .8)
        pen.line(((57, 51), (72, 51), (71, 63), (57, 63), (57, 51)), "#3b4b61", .8)
        pen.line(((43, 54), (57, 54)), "#3b4b61", .8)
    pen.line(((51, 59), (49, 69), (53, 70)), skin_shade, .65)
    pen.line(((44, 77), (50, 79), (57, 76)), "#8f5557", .85)
    if look.cut == 4:
        fringe = ((23, 29), (46, 16), (78, 16), (87, 30), (78, 59), (69, 47), (63, 32),
                  (50, 45), (38, 48), (42, 37), (23, 51), (17, 55))
    else:
        fringe = ((16, 32), (30, 17), (56, 13), (80, 22), (89, 40), (82, 57), (75, 43),
                  (66, 36), (58, 49), (55, 38), (41, 46), (39, 34), (29, 42), (22, 58))
    pen.shape(fringe, look.hair, INK)
    pen.line(((28, 27), (44, 20), (61, 20), (77, 27)), highlight, 1.15)
    if look.cut == 5:
        pen.line(((75, 33), (81, 37)), look.accent, 1.55)


def render(look, size, sd):
    scale = 2
    width, height = size
    image = Image.new("RGBA", (width * scale, height * scale))
    # The same face and uniform proportions are made readable at both display scales.
    head = (.30, .047, .40, .405) if sd else (.355, .032, .290, .174)
    body = (.27, .385, .46, .55) if sd else (.235, .181, .53, .767)

    def pen(box):
        x, y, w, h = box
        return Pen(image, (x * width * scale, y * height * scale, w * width * scale, h * height * scale))

    draw_outfit(pen(body), look)
    draw_head(pen(head), look)
    return image.resize(size, Image.Resampling.LANCZOS)


def cast(manifest):
    if manifest is None:
        yield "protagonist", "protagonist", {}
        for actor, forms in GROUPS:
            for form in forms:
                yield actor, str(form), {}
        return
    data = json.loads(manifest.read_text(encoding="utf-8-sig"))
    yield "protagonist", "protagonist", data["protagonist"]
    for persona in data["personas"]:
        for form in persona["forms"]:
            yield persona["baseCharId"], str(form["formCharId"]), form


def plan(manifest):
    paths = set()
    for identity, form, lookup in cast(manifest):
        if not re.fullmatch(r"(?:protagonist|[1-9][0-9]*)", form):
            raise ValueError(f"Invalid GX form ID: {form!r}")
        for group, prefix, key, size in FORMATS:
            path = GX_ROOT / group / f"{prefix}_{form}.png"
            if lookup and lookup.get(key) != path.as_posix():
                raise ValueError(f"GX manifest path does not match its slot: {lookup.get(key)!r}")
            if path in paths:
                raise ValueError(f"Duplicate GX PNG path: {path}")
            paths.add(path)
            yield path, size, style_for(identity, form), group == "SD"


def validate(path, size):
    with Image.open(path) as image:
        if image.format != "PNG" or image.mode != "RGBA" or image.size != size:
            raise ValueError(f"Expected {size[0]}x{size[1]} RGBA PNG: {path}")
        low, high = image.getchannel("A").getextrema()
        if low != 0 or high != 255:
            raise ValueError(f"Expected transparent space and an opaque character: {path}")


def meta(path, root, is_folder=False):
    relative = path.relative_to(root).as_posix()
    guid = uuid5(NAMESPACE_URL, "ygo-portable/Assets/StreamingAssets/" + relative).hex
    contents = f"fileFormatVersion: 2\nguid: {guid}\n"
    if is_folder:
        contents += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
    try:
        with path.with_name(path.name + ".meta").open("x", encoding="utf-8", newline="\n") as output:
            output.write(contents)
    except FileExistsError:
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=MANIFEST if MANIFEST.is_file() else None)
    parser.add_argument("--output", type=Path, default=STREAMING, help="Destination StreamingAssets folder.")
    parser.add_argument("--check", action="store_true", help="Validate current PNGs only.")
    options = parser.parse_args()
    entries = list(plan(options.manifest))
    root = options.output.resolve()
    if options.check:
        missing = [relative for relative, _, _, _ in entries if not (root / relative).is_file()]
        if missing:
            parser.exit(1, f"GX PNGs missing: {len(missing)}/{len(entries)}; first missing: {missing[0].as_posix()}\n")
    generated = existing = 0
    for relative, size, look, sd in entries:
        path = root / relative
        if not options.check and not path.is_file():
            path.parent.mkdir(parents=True, exist_ok=True)
            image = BytesIO()
            render(look, size, sd).save(image, format="PNG", optimize=True)
            try:
                with path.open("xb") as output:
                    output.write(image.getvalue())
                generated += 1
            except FileExistsError:
                existing += 1
        else:
            existing += 1
        validate(path, size)
        if not options.check:
            meta(path, root)
            for parent in path.parents:
                if parent == root:
                    break
                meta(parent, root, is_folder=True)
    print(f"GX PNGs verified: {len(entries)} ({generated} generated, {existing} already present).")


if __name__ == "__main__":
    main()
