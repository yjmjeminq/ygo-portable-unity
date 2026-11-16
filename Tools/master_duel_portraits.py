"""Render a Master Duel cut-in still from its Spine atlas and setup pose."""

import math
import re

from PIL import Image, ImageDraw


def read_atlas(text):
    regions = {}
    for block in re.split(r"\n\s*\n", text.strip()):
        page, *lines = [line.strip() for line in block.splitlines() if line.strip()]
        properties = {}
        premultiplied = False
        for line in lines:
            key, separator, value = line.partition(":")
            if not separator:
                properties = {}
                regions[line] = (page, premultiplied, properties)
            elif key == "pma":
                premultiplied = value.strip() == "true"
            else:
                properties[key] = value.strip()
    return regions


def vector(text):
    return tuple(map(int, text.split(",")))


def region_image(page, premultiplied, properties):
    if "bounds" in properties:
        x, y, w, h = vector(properties["bounds"])
    else:
        x, y = vector(properties["xy"])
        w, h = vector(properties["size"])
    rotate = properties.get("rotate") in {"90", "true"}
    stored_w, stored_h = (h, w) if rotate else (w, h)
    if x < 0 or y < 0 or x + stored_w > page.width or y + stored_h > page.height:
        raise ValueError("Atlas region exceeds the page")
    tile = page.crop((x, y, x + stored_w, y + stored_h))
    if rotate:
        tile = tile.transpose(Image.Transpose.ROTATE_270)
    if premultiplied:
        tile = Image.frombytes("RGBa", tile.size, tile.tobytes()).convert("RGBA")
    if "offsets" in properties:
        ox, oy, width, height = vector(properties["offsets"])
    else:
        ox, oy = vector(properties.get("offset", "0,0"))
        width, height = vector(properties.get("orig", f"{w},{h}"))
    if (width, height) == tile.size:
        return tile
    full = Image.new("RGBA", (width, height))
    full.paste(tile, (ox, height - oy - h))
    return full


def local_transform(data):
    rotation = data.get("rotation", 0)
    rx = math.radians(rotation + data.get("shearX", 0))
    ry = math.radians(rotation + 90 + data.get("shearY", 0))
    sx, sy = data.get("scaleX", 1), data.get("scaleY", 1)
    return (
        math.cos(rx) * sx, math.cos(ry) * sy,
        math.sin(rx) * sx, math.sin(ry) * sy,
        data.get("x", 0), data.get("y", 0),
    )


def point(matrix, x, y):
    a, b, c, d, tx, ty = matrix
    return a * x + b * y + tx, c * x + d * y + ty


def world_transform(parent, local, inheritance):
    pa, pb, pc, pd, _, _ = parent
    a, b, c, d, x, y = local
    x, y = point(parent, x, y)
    if inheritance == "onlyTranslation":
        return a, b, c, d, x, y
    if inheritance in {"noScale", "noScaleOrReflection"}:
        length = math.hypot(pa, pc)
        if length > 0:
            pa, pc = pa / length, pc / length
            pb, pd = -pc, pa
    return pa * a + pb * c, pa * b + pb * d, pc * a + pd * c, pc * b + pd * d, x, y


def mesh_vertices(attachment, bone, bones):
    vertices = attachment["vertices"]
    if len(vertices) == len(attachment["uvs"]):
        return [point(bone, x, y) for x, y in zip(vertices[::2], vertices[1::2])]
    result = []
    cursor = 0
    while cursor < len(vertices):
        count = int(vertices[cursor])
        cursor += 1
        x = y = 0
        for _ in range(count):
            index, vx, vy, weight = vertices[cursor:cursor + 4]
            tx, ty = point(bones[int(index)], vx, vy)
            x += tx * weight
            y += ty * weight
            cursor += 4
        result.append((x, y))
    return result


def rectangle_vertices(attachment, bone):
    w, h = attachment["width"] * .5, attachment["height"] * .5
    rotation = local_transform(attachment)
    return [point(bone, *point(rotation, x, y)) for x, y in ((-w, h), (w, h), (w, -h), (-w, -h))]


def draw_triangle(target, texture, screen, uv):
    left = max(0, math.floor(min(x for x, _ in screen)))
    top = max(0, math.floor(min(y for _, y in screen)))
    right = min(target.width, math.ceil(max(x for x, _ in screen)) + 1)
    bottom = min(target.height, math.ceil(max(y for _, y in screen)) + 1)
    if left >= right or top >= bottom:
        return
    (ax, ay), (bx, by), (cx, cy) = [(x - left, y - top) for x, y in screen]
    u = (bx - ax, by - ay)
    v = (cx - ax, cy - ay)
    determinant = u[0] * v[1] - u[1] * v[0]
    if abs(determinant) < .01:
        return
    (s0, t0), (s1, t1), (s2, t2) = [(x * texture.width, y * texture.height) for x, y in uv]

    def inverse(start, first, second):
        dx = ((first - start) * v[1] - (second - start) * u[1]) / determinant
        dy = ((second - start) * u[0] - (first - start) * v[0]) / determinant
        return dx, dy, start - ax * dx - ay * dy

    size = (right - left, bottom - top)
    tile = texture.transform(size, Image.Transform.AFFINE, inverse(s0, s1, s2) + inverse(t0, t1, t2),
                             Image.Resampling.BILINEAR)
    mask = Image.new("L", size)
    ImageDraw.Draw(mask).polygon(((ax, ay), (bx, by), (cx, cy)), fill=255)
    target.paste(tile, (left, top), mask)


def render_setup_pose(skeleton, atlas_text, load_page):
    atlas = read_atlas(atlas_text)
    by_name, bones = {}, []
    for bone in skeleton["bones"]:
        matrix = local_transform(bone)
        if "parent" in bone:
            inheritance = bone.get("inherit", bone.get("transform", "normal"))
            matrix = world_transform(by_name[bone["parent"]], matrix, inheritance)
        by_name[bone["name"]] = matrix
        bones.append(matrix)

    skins = skeleton["skins"]
    if isinstance(skins, list):
        skin = next(item["attachments"] for item in skins if item["name"] == "default")
    else:
        skin = skins["default"]
    parts = []
    for slot in skeleton["slots"]:
        name = slot.get("attachment")
        attachment = skin.get(slot["name"], {}).get(name)
        if attachment is None:
            continue
        kind = attachment.get("type", "region")
        region = attachment.get("path", attachment.get("name", name))
        if kind not in {"region", "mesh"} or region not in atlas:
            continue
        alpha = int(slot.get("color", "ffffffff")[-2:], 16) / 255
        if alpha < .01:
            continue
        bone = by_name[slot["bone"]]
        if kind == "mesh":
            vertices = mesh_vertices(attachment, bone, bones)
            uvs = list(zip(attachment["uvs"][::2], attachment["uvs"][1::2]))
            triangles = attachment["triangles"]
        else:
            vertices = rectangle_vertices(attachment, bone)
            uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
            triangles = (0, 1, 2, 0, 2, 3)
        parts.append((region, vertices, uvs, triangles, alpha))
    if not parts:
        return None
    xs, ys = zip(*(vertex for _, vertices, _, _, _ in parts for vertex in vertices))
    xmin, xmax, ymin, ymax = min(xs), max(xs), min(ys), max(ys)
    if xmax <= xmin or ymax <= ymin:
        return None
    scale = min(920 / (xmax - xmin), 756 / (ymax - ymin))
    canvas = Image.new("RGBA", (math.ceil((xmax - xmin) * scale) + 4, math.ceil((ymax - ymin) * scale) + 4))
    pages = {}
    for region, vertices, uvs, triangles, alpha in parts:
        page_name, premultiplied, properties = atlas[region]
        if page_name not in pages:
            pages[page_name] = load_page(page_name)
        tile = region_image(pages[page_name], premultiplied, properties)
        screen = [((x - xmin) * scale + 2, (ymax - y) * scale + 2) for x, y in vertices]
        layer = Image.new("RGBA", canvas.size)
        faces = zip(triangles[::3], triangles[1::3], triangles[2::3])
        for triangle in faces:
            draw_triangle(layer, tile, [screen[i] for i in triangle], [uvs[i] for i in triangle])
        if alpha < 1:
            layer.putalpha(layer.getchannel("A").point(lambda value: round(value * alpha)))
        canvas.alpha_composite(layer)
    bounds = canvas.getbbox()
    return canvas.crop(bounds) if bounds else None
