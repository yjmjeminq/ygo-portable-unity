"""Render the portable game's icon atlas from geometric shapes and its Korean font."""

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "Assets" / "Resources"
FONT = RESOURCES / "Fonts" / "NotoSansKR.ttf"
CELL = 128
COLUMNS = 8
SCALE = 2

SPELLS = {
    "quick_play": "속공", "continuous": "지속", "equip": "장착",
    "field": "필드", "ritual": "의식", "normal": "일반",
}
TRAPS = {"continuous": "지속", "counter": "반격", "normal": "일반"}
FILTERS = {
    "frame": "카드", "type": "종족", "icon": "유형", "attr": "속성",
    "level": "LV", "rank": "R", "linknum": "L", "linkdir": "↗",
    "pend": "P", "genre": "효과", "atk": "ATK", "def": "DEF",
}
ACTIONS = {
    "action": ("#ffcd67", ("49,11 59,39 85,49 59,59 49,87 39,59 13,49 39,39",
                            "77,10 81,20 91,24 81,28 77,38 73,28 63,24 73,20",
                            "23,65 27,73 35,77 27,81 23,89 19,81 11,77 19,73"),
               ("49,38 60,49 49,60 38,49",)),
    "attack": ("#ffa08d", ("82,13 87,34 59,62 65,70 57,78 49,70 32,87 23,80 40,62 32,54 40,46 48,53 75,18",),
               ("76,25 76,32 55,53 52,50",)),
    "summon": ("#7ae9d3", ("50,11 75,36 60,36 60,55 40,55 40,36 25,36",
                            "24,56 76,56 82,62 82,84 76,90 24,90 18,84 18,62"),
               ("29,66 71,66 71,80 29,80",)),
    "summonsp": ("#83dcff", ("50,12 67,34 57,34 57,49 43,49 43,34 33,34",
                              "23,37 33,50 27,50 27,63 19,63 19,50 13,50",
                              "77,37 87,50 81,50 81,63 73,63 73,50 67,50",
                              "50,53 84,66 91,74 84,82 50,93 16,82 9,74 16,66"),
                 ("50,63 78,74 50,83 22,74",)),
    "turn_def": ("#b9d4ff", ("31,30 62,13 82,50 51,67",
                              "21,38 21,60 35,74 60,74 60,65 83,80 60,93 60,85 33,85 10,64 10,38"),
                 ("43,34 58,25 70,46 55,54",)),
    "setmonst": ("#a6dae5", ("26,15 74,15 81,22 81,50 69,50 69,27 31,27 31,50 19,50 19,22",
                              "50,35 80,51 73,77 50,91 27,77 20,51"),
                 ("50,49 65,57 62,69 50,77 38,69 35,57",)),
    "set": ("#a6dae5", ("40,12 60,12 60,33 74,33 50,58 26,33 40,33",
                         "16,59 84,59 90,65 90,84 84,90 16,90 10,84 10,65"),
            ("23,70 77,70 77,80 23,80",)),
    "look": ("#dbedf0", ("9,50 26,31 50,22 74,31 91,50 74,69 50,78 26,69",
                          "50,39 61,50 50,61 39,50"),
             ("23,50 29,44 38,38 31,50 38,62 29,56", "77,50 71,44 62,38 69,50 62,62 71,56")),
    "surrender": ("#ffc4a4", ("25,14 36,14 36,20 57,20 78,24 78,58 57,52 36,52 36,87 25,87",
                               "19,82 46,82 46,90 19,90"), ()),
}
CANCEL_ACTION = ("#d5e5eb", ("28,17 50,39 72,17 83,28 61,50 83,72 72,83 50,61 28,83 17,72 39,50 17,28",), ())
PHASES = {"draw": "D", "standby": "S", "main1": "1", "battle": "B", "main2": "2", "end": "E"}

# Polygon points use a 100×100 canvas. A separate cutout keeps the marks readable at HUD size.
FLAME = ("50,20 66,36 64,49 72,44 77,61 73,71 61,79 42,79 27,71 23,60 29,47 33,55 44,42",)
SUN = ("50,23 56,39 73,30 65,46 80,50 65,55 73,71 56,63 50,80 44,63 27,71 35,55 20,50 35,46 27,30 44,39",)
PRISM = ("50,22 75,47 63,74 37,74 25,47",)
ATTRIBUTES = {
    "earth": ("#f4ae74", ("20,70 37,39 50,52 64,29 81,70",), ("46,69 62,48 69,69",)),
    "water": ("#74caff", ("50,19 74,51 75,64 65,77 50,82 35,77 25,64 26,51",), ("36,58 48,69 61,69 52,75 41,72",)),
    "fire": ("#ff8677", FLAME, ("50,52 63,67 57,74 43,74 38,67",)),
    "wind": ("#68e6bb", ("20,42 33,34 61,31 72,25 80,32 66,42 38,43 20,49", "23,58 45,49 76,50 80,56 52,60 35,68 23,66", "39,73 56,66 69,67 58,75 42,77"), ()),
    "light": ("#ffdf81", SUN, ("50,42 58,50 50,58 42,50",)),
    "dark": ("#cab6ff", ("55,23 49,39 52,55 64,64 81,63 71,76 54,81 37,78 25,66 21,49 29,33 40,26", "68,24 71,32 79,36 71,39 68,48 65,39 57,36 65,32"), ()),
    "god": ("#f5ecad", ("50,18 58,39 80,50 58,61 50,82 42,61 20,50 42,39",), ("50,39 61,50 50,61 39,50",)),
    "spell": ("#71e1d0", ("22,35 44,39 50,45 56,39 78,35 78,68 56,72 50,78 44,72 22,68", "50,20 54,28 62,31 54,34 50,42 46,34 38,31 46,28"), ("46,49 54,49 54,69 50,73 46,69",)),
    "trap": ("#f49ad1", ("22,35 33,26 67,26 78,35 78,65 67,74 33,74 22,65",), ("29,49 39,38 50,52 61,38 71,49 61,64 50,54 39,64",)),
}

RACES = {
    "soldier": (("22,22 34,25 75,69 70,76 26,34", "78,22 74,34 30,76 24,69 66,25",
                 "29,40 39,29 61,29 71,40 71,60 62,74 38,74 29,60"),
                ("34,48 66,48 63,55 54,55 54,67 46,67 46,55 37,55",)),
    "wizard": (("23,60 33,31 53,23 50,37 61,60 70,66 64,74 21,74 17,66",
                "63,73 73,41 79,43 70,76", "78,19 81,26 88,29 81,32 78,39 75,32 68,29 75,26"),
               ("27,57 58,57 61,62 25,62",)),
    "angel": (("18,34 44,48 46,66 31,60 20,48", "82,34 56,48 54,66 69,60 80,48", "34,24 66,24 72,31 66,38 34,38 28,31"), ("38,28 62,28 65,31 62,34 38,34 35,31",)),
    "devil": (("17,41 27,31 37,47 43,34 57,34 63,47 73,31 83,41 82,60 69,55 65,69 50,78 35,69 31,55 18,60",),
              ("33,47 47,53 43,61 36,57", "67,47 53,53 57,61 64,57")),
    "undead": (("30,28 70,28 78,39 78,62 66,66 66,78 34,78 34,66 22,62 22,39",), ("28,43 44,43 44,54 36,59 28,54", "56,43 72,43 72,54 64,59 56,54", "46,67 54,67 54,78 46,78")),
    "machine": (("42,20 58,20 60,30 68,34 76,31 83,43 76,50 76,58 82,64 73,75 65,71 57,76 57,82 43,82 43,76 35,71 27,75 18,64 24,58 24,50 17,43 24,31 32,34 40,30",), ("42,42 58,42 62,50 58,59 42,59 38,50",)),
    "aquarius": (("50,18 64,36 64,44 56,51 44,51 36,44 36,36",
                  "19,59 36,51 50,57 64,62 81,54 81,63 64,72 50,67 36,61 19,69",
                  "22,73 36,68 51,75 63,78 78,72 78,79 63,85 50,83 35,77 22,82"), ()),
    "flame": (FLAME, ("50,52 63,67 57,74 43,74 38,67",)),
    "sandrock": (("17,67 33,45 43,46 53,25 65,29 76,50 84,58 80,72 64,78 32,78",), ("40,48 47,52 39,64 34,63", "59,33 70,51 63,54 56,49")),
    "bird": (("21,33 38,38 55,47 58,35 68,32 80,41 69,45 66,57 49,65 36,77 39,63 24,63 30,56 18,48 37,51",), ()),
    "botanical": (("50,18 58,27 70,29 69,40 75,50 64,54 57,65 44,65 36,54 25,50 31,39 29,29 41,27",
                   "20,57 38,60 50,72 63,61 81,57 72,73 56,77 54,84 46,84 44,77 28,73"),
                  ("44,33 56,33 61,43 57,52 43,52 39,43",)),
    "insect": (("27,23 40,31 47,25 53,25 60,31 73,23 76,29 64,39 70,48 78,51 78,59 69,56 66,72 50,79 34,72 31,56 22,59 22,51 30,48 36,39 24,29",), ("46,42 54,42 54,73 46,73",)),
    "thunder": (("56,19 31,55 49,55 43,81 73,42 55,42 62,19",), ()),
    "dragon": (("25,23 45,35 61,24 59,38 76,45 82,57 69,66 56,64 45,77 23,77 33,63 23,59 33,47 20,39 34,40",), ("58,43 68,47 57,51", "62,59 73,53 79,57 70,61")),
    "beast": (("19,37 31,23 42,34 34,58 26,69 20,65 28,38", "40,39 50,25 59,35 54,60 43,79 36,74 46,40", "60,41 70,28 81,39 75,57 62,75 56,72 67,40"), ()),
    "beastbtl": (("21,27 34,38 48,31 59,39 58,54 69,63 62,74 47,77 34,65 27,51 19,43",
                  "53,28 66,23 80,29 80,43 69,45 65,58 58,56 63,43 51,39"),
                 ("40,45 53,43 49,51 42,51", "50,62 62,63 57,69 48,67")),
    "dinosaurs": (("22,55 32,30 63,27 79,37 80,52 64,53 58,61 52,55 40,62 57,65 66,71 54,77 28,75 20,68",), ("54,36 66,35 62,43 54,43",)),
    "fish": (("23,32 39,43 49,35 66,30 79,36 84,49 79,63 66,70 48,65 39,58 23,69",),
             ("67,42 75,42 75,49 67,49", "36,47 62,47 62,53 36,53", "42,40 47,40 47,60 42,60", "53,36 58,36 58,64 53,64")),
    "poseidon": (("23,25 36,24 44,18 49,29 60,33 59,44 49,47 39,42 33,49 38,56 65,55 78,63 78,73 68,82 51,82 44,75 64,74 67,69 60,66 32,66 22,58 20,47 26,37 20,32",),
                 ("43,32 52,34 48,39 42,38",)),
    "reptiles": (("31,23 58,23 72,33 72,44 53,53 41,60 41,66 69,65 77,71 67,78 37,78 26,70 26,55 34,48 57,38 51,33 30,34 22,28",), ("53,23 61,26 59,31 51,29",)),
    "psychic": (("17,38 28,22 72,22 83,38 76,39 68,30 32,30 24,39",
                 "17,62 24,61 32,70 68,70 76,61 83,62 72,78 28,78",
                 "17,46 34,46 42,36 53,59 62,45 83,45 83,53 66,53 52,69 40,49 35,54 17,54"), ()),
    "god": (("50,20 63,41 81,34 77,59 61,65 50,82 39,65 23,59 19,34 37,41",), ("50,44 57,55 50,66 43,55",)),
    "creator": (PRISM, ("50,31 66,47 50,58 34,47", "46,62 54,62 54,74 46,74")),
    "mystdragon": (("23,27 44,30 53,20 60,32 73,29 79,43 64,54 49,52 41,59 43,67 66,69 75,79 38,79 27,68 28,55 43,43 31,41",), ("58,37 66,37 63,43 56,43",)),
    "cyverse": (("29,29 41,29 41,21 47,21 47,29 53,29 53,21 59,21 59,29 71,29 71,41 79,41 79,47 71,47 71,53 79,53 79,59 71,59 71,71 59,71 59,79 53,79 53,71 47,71 47,79 41,79 41,71 29,71 29,59 21,59 21,53 29,53 29,47 21,47 21,41 29,41",), ("39,39 61,39 61,61 39,61",)),
    "illusion": (("50,20 83,74 17,74",), ("30,59 43,49 50,49 42,59 50,68 43,68", "70,59 57,49 50,49 59,59 50,68 57,68")),
}


def entries():
    for group, labels, color, shape in (
        ("spell", SPELLS, "#8ce0c4", "orb"),
        ("trap", TRAPS, "#e7a7d7", "orb"),
        ("filter", FILTERS, "#bad6e1", "plate"),
        ("action", ACTIONS, None, "pictogram"),
        ("phase", PHASES, "#e2f5ff", "hex"),
    ):
        for key, label in labels.items():
            yield f"{group}/{key}", label, color, shape
    yield "check", "✓", "#ffffff", "glyph"


def render(label, color, shape):
    if shape == "pictogram":
        return render_action(*label)
    size = CELL * SCALE
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)

    def xy(points):
        return [(round(x * size), round(y * size)) for x, y in points]

    def box(inset):
        return (round(inset * size), round(inset * size),
                round((1 - inset) * size), round((1 - inset) * size))

    if shape == "orb":
        draw.ellipse(box(.063), fill="#122635", outline=color, width=3 * SCALE)
        draw.ellipse(box(.104), fill="#263d4c", outline="#50717b", width=SCALE)
        x0, y0, x1, y1 = box(.152)
        draw.arc((x0, y0, x1, y1), 207, 294, fill=color, width=2 * SCALE)
    elif shape == "plate":
        draw.rounded_rectangle(box(.069), radius=round(size * .17), fill="#1b343f",
                               outline=color, width=3 * SCALE)
    elif shape == "hex":
        corners = xy(((.26, .11), (.74, .11), (.96, .50), (.74, .89), (.26, .89), (.04, .50)))
        draw.polygon(corners, fill="#263e50")
        draw.line(corners + [corners[0]], fill=color, width=4 * SCALE, joint="curve")

    font_size = round(size * (.64 if len(label) == 1 else .37))
    while True:
        font = ImageFont.truetype(str(FONT), font_size)
        left, top, right, bottom = draw.textbbox((0, 0), label, font=font, stroke_width=SCALE)
        if right - left <= size * .72:
            break
        font_size -= 2
    x = (size - right - left) / 2
    y = (size - top - bottom) / 2
    draw.text((x, y), label, font=font, fill="#ffffff" if shape == "glyph" else color,
              stroke_width=SCALE, stroke_fill="#0a1822" if shape != "glyph" else color)
    return image.resize((CELL, CELL), Image.Resampling.LANCZOS)


def contour_points(outline, size):
    return [tuple(round(int(coord) * size / 100) for coord in pair.split(",")) for pair in outline.split()]


def render_action(accent, symbol, cutouts):
    size = CELL * SCALE
    mask = Image.new("L", (size, size))
    draw = ImageDraw.Draw(mask)
    for contour in symbol:
        draw.polygon(contour_points(contour, size), fill=255)
    for contour in cutouts:
        draw.polygon(contour_points(contour, size), fill=0)
    image = Image.new("RGBA", mask.size, accent)
    image.putalpha(mask.filter(ImageFilter.GaussianBlur(7)).point(lambda value: value * .52))
    glyph = Image.new("RGBA", mask.size, accent)
    glyph.paste("#fff9ea", (0, 0, size, size // 2), mask.crop((0, 0, size, size // 2)))
    glyph.putalpha(mask)
    image.alpha_composite(glyph)
    return image.resize((CELL, CELL), Image.Resampling.LANCZOS)


def render_identity(symbol, cutouts, accent, race=False):
    size = CELL * SCALE
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)
    ground = "#162836" if race else "#12202e"

    plate = contour_points("25,8 75,8 92,25 92,75 75,92 25,92 8,75 8,25" if race else
                           "34,7 66,7 93,34 93,66 66,93 34,93 7,66 7,34", size)
    draw.polygon(plate, fill=ground)
    draw.line(plate + plate[:1], fill=accent, width=4 * SCALE, joint="curve")
    for contour in symbol:
        draw.polygon(contour_points(contour, size), fill=accent)
    for contour in cutouts:
        draw.polygon(contour_points(contour, size), fill=ground)
    return image.resize((CELL, CELL), Image.Resampling.LANCZOS)


def main():
    icons = [(name, render(label, color, shape)) for name, label, color, shape in entries()]
    icons.extend((f"attribute/{key}", render_identity(symbol, cutouts, accent))
                 for key, (accent, symbol, cutouts) in ATTRIBUTES.items())
    icons.extend((f"race/{key}", render_identity(symbol, cutouts, "#aee5e6", race=True))
                 for key, (symbol, cutouts) in RACES.items())
    icons.append(("action/cancel", render_action(*CANCEL_ACTION)))
    rows = (len(icons) + COLUMNS - 1) // COLUMNS
    atlas = Image.new("RGBA", (COLUMNS * CELL, rows * CELL))
    for index, (_, icon) in enumerate(icons):
        atlas.paste(icon, ((index % COLUMNS) * CELL, (index // COLUMNS) * CELL))
    destination = RESOURCES / "Duel"
    atlas.save(destination / "DuelUiIcons.png", optimize=True)
    table = {"cell": CELL, "columns": COLUMNS, "names": [name for name, _ in icons]}
    (destination / "DuelUiIconMap.json").write_text(json.dumps(table, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
