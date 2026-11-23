"""Render the portable game's icon atlas from geometric shapes and its Korean font."""

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


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
    "action": "발동", "attack": "↗", "summon": "소환", "summonsp": "특수",
    "turn_def": "표시", "setmonst": "세트", "set": "세트", "look": "◉", "surrender": "종료",
}
PHASES = {"draw": "D", "standby": "S", "main1": "1", "battle": "B", "main2": "2", "end": "E"}


def entries():
    for group, labels, color, shape in (
        ("spell", SPELLS, "#8ce0c4", "orb"),
        ("trap", TRAPS, "#e7a7d7", "orb"),
        ("filter", FILTERS, "#bad6e1", "plate"),
        ("action", ACTIONS, "#ffdf99", "plate"),
        ("phase", PHASES, "#e2f5ff", "hex"),
    ):
        for key, label in labels.items():
            yield f"{group}/{key}", label, color, shape
    yield "check", "✓", "#ffffff", "glyph"


def render(label, color, shape):
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


def main():
    icons = list(entries())
    rows = (len(icons) + COLUMNS - 1) // COLUMNS
    atlas = Image.new("RGBA", (COLUMNS * CELL, rows * CELL))
    for index, (_, label, color, shape) in enumerate(icons):
        atlas.paste(render(label, color, shape), ((index % COLUMNS) * CELL, (index // COLUMNS) * CELL))
    destination = RESOURCES / "Duel"
    atlas.save(destination / "DuelUiIcons.png", optimize=True)
    table = {"cell": CELL, "columns": COLUMNS, "names": [item[0] for item in icons]}
    (destination / "DuelUiIconMap.json").write_text(json.dumps(table, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
