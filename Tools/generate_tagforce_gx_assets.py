# -*- coding: utf-8 -*-
import argparse
import hashlib
import json
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[1]
BASE_DIR = ROOT / "Assets/StreamingAssets/TagForce/GX"
MANIFEST = ROOT / "Tools/tagforce_gx_asset_manifest.json"

# 46종 캐릭터별 고유 비주얼 테마 정의 (원작 애니메이션 고증)
CHAR_SPECS = [
    ("player_heroine", "여주인공", "HEROINE", (211, 47, 47), (255, 255, 255), "RED CAP & PONYTAIL"),
    ("119", "유우키 쥬다이", "JUDAI", (211, 47, 47), (245, 176, 65), "SLIFER RED HERO"),
    ("209", "유우키 쥬다이 (유벨)", "JUDAI-YUBEL", (136, 14, 79), (46, 204, 113), "ODD-EYES FUSION"),
    ("214", "패왕 쥬다이", "SUPREME KING", (26, 26, 26), (255, 215, 0), "DARK SUPREME ARMOR"),
    ("137", "마루후지 료", "KAISER RYO", (13, 71, 161), (255, 255, 255), "OBELISK BLUE WHITE"),
    ("207", "헬 카이저 료", "HELL KAISER", (33, 33, 33), (142, 36, 170), "UNDERGROUND CHOKER"),
    ("117", "만죠메 쥰", "CHAZZ THUNDER", (17, 17, 17), (255, 235, 59), "BLACK COAT THUNDER"),
    ("3100", "만죠메 쥰 (빛)", "WHITE CHAZZ", (255, 255, 255), (176, 190, 197), "SOCIETY OF LIGHT"),
    ("107", "텐죠인 아스카", "ALEXIS", (25, 118, 210), (255, 160, 0), "OBELISK BLUE QUEEN"),
    ("199", "텐죠인 아스카 (빛)", "WHITE ALEXIS", (224, 247, 250), (128, 222, 234), "WHITE ICE PRINCESS"),
    ("111", "텐죠인 후부키", "ATTICUS", (25, 118, 210), (255, 112, 67), "HAWAIIAN UKULELE"),
    ("110", "다크니스 후부키", "DARKNESS", (26, 26, 26), (229, 57, 53), "SKULL DRAGON MASK"),
    ("128", "마루후지 쇼", "SYRUS", (211, 47, 47), (129, 212, 250), "SLIFER RED GLASSES"),
    ("118", "마에다 하야토", "CHUMLEY", (211, 47, 47), (141, 110, 99), "KOALA BANDANA"),
    ("113", "미사와 다이치", "BASTION", (245, 127, 23), (46, 125, 50), "RA YELLOW 6-DECKS"),
    ("132", "티라노 켄잔", "HASSLEBERRY", (245, 127, 23), (198, 40, 40), "DINO FOSSIL VEST"),
    ("109", "에드 피닉스", "ASTER PHOENIX", (74, 20, 140), (224, 224, 224), "DESTINY HERO SUIT"),
    ("120", "요한 안데르센", "JESSE", (0, 150, 136), (233, 30, 99), "CRYSTAL BEAST TEAL"),
    ("121", "짐 크로커다일", "JIM COOK", (109, 76, 65), (76, 175, 80), "FOSSIL CROCODILE"),
    ("112", "오브라이언", "AXEL BRODIE", (38, 50, 56), (255, 87, 34), "VOLCANIC GUN DISK"),
    ("106", "아몬 가람", "ADRIAN", (27, 94, 32), (255, 215, 0), "CLOUD WEST SUIT"),
    ("114", "사오토메 레이", "BLAIR", (211, 47, 47), (255, 183, 77), "RED MAIDEN TWINTAIL"),
    ("133", "크로노스", "CROWLER", (142, 36, 170), (255, 215, 0), "ANTIQUE GEAR COAT"),
    ("125", "다이토쿠지", "BANNER", (121, 85, 72), (255, 236, 179), "CAT PHARAOH LOWPONY"),
    ("108", "암나엘", "AMNAEL", (26, 26, 26), (255, 215, 0), "ALCHEMY MASK HOOD"),
    ("127", "사메지마 교장", "SHEPPARD", (13, 71, 161), (255, 215, 0), "HEADMASTER MUSTACHE"),
    ("115", "나폴레옹 교감", "BONAPARTE", (27, 94, 32), (229, 57, 53), "BICORNE HAT DEPUTY"),
    ("123", "마르탄 (유벨)", "MARCEL", (25, 118, 210), (255, 235, 59), "YUBEL POSSESSED BOY"),
    ("136", "유벨", "YUBEL", (74, 20, 140), (0, 230, 118), "DEMON DRAGON WINGS"),
    ("131", "타이탄", "TITAN", (62, 39, 35), (255, 215, 0), "SHADOW DUEL BIKER"),
    ("3101", "어둠의 타이탄", "DARK TITAN", (26, 26, 26), (183, 28, 28), "MATADOR DARK CAPE"),
    ("122", "카게마루 (젊음)", "KAGEMARU", (26, 26, 26), (229, 57, 53), "SACRED BEAST MUSCLE"),
    ("3102", "D.D.", "THE D", (49, 27, 146), (255, 215, 0), "HEAVYWEIGHT CHAMPION"),
    ("126", "사이오 타쿠마", "SARTORIUS", (237, 231, 246), (123, 31, 162), "ARCANA TAROT SUIT"),
    ("213", "사이오 미즈치", "SARINA", (183, 28, 28), (255, 255, 255), "SHRINE MAIDEN MIKO"),
    ("116", "카뮬라", "CAMULA", (74, 20, 140), (229, 57, 53), "VAMPIRE BAT GOTHIC"),
    ("129", "타니아", "TANIA", (230, 81, 0), (255, 235, 59), "AMAZONESS LEOPARD"),
    ("202", "돈 잘우그", "DON ZALOOG", (74, 20, 140), (255, 215, 0), "SCORPION REVOLVER"),
    ("201", "아비도스 3세", "ABYDOS", (255, 215, 0), (13, 71, 161), "PHARAOH NEMES GOLD"),
    ("130", "프로페서 코브라", "VIPER", (55, 71, 79), (229, 57, 53), "DEATHBELT MILITARY"),
    ("225", "후지와라 유스케", "YUSUKE", (25, 118, 210), (179, 229, 252), "CLEAR PRISM DISK"),
    ("3103", "트루맨", "MR. T", (17, 17, 17), (255, 255, 255), "FEDORA BLACK SUIT"),
    ("124", "다크니스", "NIGHTSHROUD", (10, 10, 10), (213, 0, 0), "ZERO INFINITY CORE"),
    ("215", "아유카와 에미", "FONDA", (255, 255, 255), (142, 36, 170), "DOCTOR CLINIC COAT"),
    ("205", "하라다 준코", "JASMINE", (25, 118, 210), (171, 71, 188), "BLUE HIGH PONYTAIL"),
    ("206", "하마구치 모모에", "MINDY", (25, 118, 210), (141, 110, 99), "BLUE BOB SHORT")
]

KINDS = {
    "sd": ("Characters_SD", "SD", "sd_", (512, 512)),
    "standing": ("Characters_Standing", "Standing", "full_", (1024, 1536)),
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def paths(cid, kind):
    output, runtime, prefix, _ = KINDS[kind]
    runtime_id = "protagonist" if cid == "player_heroine" else cid
    return (BASE_DIR / output / f"{prefix}{cid}.png",
            BASE_DIR / runtime / f"{prefix}{runtime_id}.png")


def unity_meta(path):
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        return
    guid = uuid5(NAMESPACE_URL, "ygo-portable/" + path.relative_to(ROOT).as_posix()).hex
    content = f"fileFormatVersion: 2\nguid: {guid}\n"
    if path.is_dir():
        content += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
    meta.write_text(content, encoding="utf-8", newline="\n")


def normalize(source, size):
    with Image.open(source) as image:
        image.load()
        if image.mode != "RGBA" or image.getchannel("A").getextrema()[0] != 0:
            raise ValueError(f"Source needs genuine alpha transparency: {source}")
        bounds = image.getchannel("A").getbbox()
        if bounds is None:
            raise ValueError(f"Source is completely transparent: {source}")
        padding = max(12, round(min(size) * .035))
        content = ImageOps.contain(image.crop(bounds),
                                  (size[0] - padding * 2, size[1] - padding * 2),
                                  Image.Resampling.LANCZOS)
        result = Image.new("RGBA", size)
        result.alpha_composite(content, ((size[0] - content.width) // 2,
                                         (size[1] - content.height) // 2))
        return result


def check_image(path, size, expected_hash):
    if not path.is_file() or digest(path) != expected_hash:
        raise ValueError(f"Missing or changed character artwork: {path}")
    with Image.open(path) as image:
        image.load()
        if image.format != "PNG" or image.mode != "RGBA" or image.size != size:
            raise ValueError(f"Invalid sprite format or dimensions: {path}")
        alpha = image.getchannel("A")
        if alpha.getextrema()[0] != 0 or alpha.getbbox() is None:
            raise ValueError(f"Invalid sprite transparency: {path}")


def main():
    parser = argparse.ArgumentParser(description=(
        "Install actual generated GX character art and sync the game sprite paths. "
        "Never substitutes text/shape placeholders for character illustrations."))
    parser.add_argument("--generated-records", type=Path,
                        help="Directory of built-in image generation result JSON records.")
    parser.add_argument("--allow-partial", action="store_true",
                        help="Install available sources while the remaining images generate.")
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    expected = {(spec[0], kind) for spec in CHAR_SPECS for kind in KINDS}
    entries = {}
    if MANIFEST.exists():
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        entries = {(entry["id"], entry["kind"]): entry for entry in manifest["assets"]}
    if args.verify_only and args.generated_records:
        parser.error("--verify-only cannot install generated sources")
    if args.generated_records:
        records = [json.loads(path.read_text(encoding="utf-8-sig"))
                   for path in sorted(args.generated_records.glob("*.json"))]
        sources = {(record["id"], record["kind"]): Path(record["source"])
                   for record in records}
        if len(sources) != len(records) or not set(sources).issubset(expected):
            raise ValueError("Duplicate or unexpected generated character records")
        pending = expected - (set(entries) | set(sources))
        if pending and not args.allow_partial:
            raise ValueError(f"Still missing {len(pending)} actual character images")
        # Decode and validate every source before writing any sprite.
        prepared = []
        for (cid, kind), source in sources.items():
            source_hash = digest(source)
            if entries.get((cid, kind), {}).get("source_sha256") == source_hash:
                continue
            prepared.append((cid, kind, source_hash, normalize(source, KINDS[kind][3])))
        for cid, kind, source_hash, image in prepared:
            output, _ = paths(cid, kind)
            output.parent.mkdir(parents=True, exist_ok=True)
            image.save(output, "PNG")
            entries[cid, kind] = {"id": cid, "kind": kind,
                                  "file": output.relative_to(ROOT).as_posix(),
                                  "sha256": digest(output), "source_sha256": source_hash}
        MANIFEST.write_text(json.dumps({"generator": "built-in image_gen",
                                        "assets": sorted(entries.values(),
                                                         key=lambda item: (item["id"], item["kind"]))},
                                       ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    pending = expected - set(entries)
    if pending and not args.allow_partial:
        raise ValueError(f"Missing {len(pending)} actual character images. "
                         "Generate the artwork first; this script cannot draw character illustrations.")
    if set(entries) - expected:
        raise ValueError("Unexpected character IDs in installed artwork manifest")
    for (cid, kind), entry in entries.items():
        output, runtime = paths(cid, kind)
        check_image(output, KINDS[kind][3], entry["sha256"])
        if not args.verify_only:
            runtime.parent.mkdir(parents=True, exist_ok=True)
            if not runtime.exists() or digest(runtime) != entry["sha256"]:
                runtime.write_bytes(output.read_bytes())
            for path in (output.parent, runtime.parent, output, runtime):
                unity_meta(path)
        check_image(runtime, KINDS[kind][3], entry["sha256"])
    print(f"Validated {len(entries)}/{len(expected)} actual character sprites; "
          f"pending: {len(pending)}. Runtime SD/Standing copies match.")


if __name__ == "__main__":
    main()
