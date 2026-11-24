# -*- coding: utf-8 -*-
import argparse
import hashlib
import json
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5
from PIL import Image, ImageOps
from canon_asset_policy import reject_retired_asset, retired_hashes

ROOT = Path(__file__).resolve().parents[1]
BASE_DIR = ROOT / "Assets/StreamingAssets/TagForce/GX"
MANIFEST = ROOT / "Tools/tagforce_gx_asset_manifest.json"

# 45종 원작 캐릭터. 플레이어는 기존 Story/Protagonist 리소스를 사용한다.
CHAR_SPECS = [('119', '유우키 쥬다이'),
 ('209', '유우키 쥬다이 (유벨)'),
 ('214', '패왕 쥬다이'),
 ('137', '마루후지 료'),
 ('207', '헬 카이저 료'),
 ('117', '만죠메 쥰'),
 ('3100', '만죠메 쥰 (빛)'),
 ('107', '텐죠인 아스카'),
 ('199', '텐죠인 아스카 (빛)'),
 ('111', '텐죠인 후부키'),
 ('110', '다크니스 후부키'),
 ('128', '마루후지 쇼'),
 ('118', '마에다 하야토'),
 ('113', '미사와 다이치'),
 ('132', '티라노 켄잔'),
 ('109', '에드 피닉스'),
 ('120', '요한 안데르센'),
 ('121', '짐 크로커다일'),
 ('112', '오브라이언'),
 ('106', '아몬 가람'),
 ('114', '사오토메 레이'),
 ('133', '크로노스'),
 ('125', '다이토쿠지'),
 ('108', '암나엘'),
 ('127', '사메지마 교장'),
 ('115', '나폴레옹 교감'),
 ('123', '마르탄 (유벨)'),
 ('136', '유벨'),
 ('131', '타이탄'),
 ('3101', '어둠의 타이탄'),
 ('122', '카게마루 (젊음)'),
 ('3102', 'D.D.'),
 ('126', '사이오 타쿠마'),
 ('213', '사이오 미즈치'),
 ('116', '카뮬라'),
 ('129', '타니아'),
 ('202', '돈 잘우그'),
 ('201', '아비도스 3세'),
 ('130', '프로페서 코브라'),
 ('225', '후지와라 유스케'),
 ('3103', '트루맨'),
 ('124', '다크니스'),
 ('215', '아유카와 에미'),
 ('205', '하라다 준코'),
 ('206', '하마구치 모모에')]

KINDS = {
    "sd": ("Characters_SD", "SD", "sd_", (512, 512)),
    "standing": ("Characters_Standing", "Standing", "full_", (1024, 1536)),
}
REMOVED_CHARACTER_IDS = {"player_heroine"}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def paths(cid, kind):
    output, runtime, prefix, _ = KINDS[kind]
    runtime_id = cid
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
    reject_retired_asset(path)
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
        records = [record for record in records if record["id"] not in REMOVED_CHARACTER_IDS]
        sources = {(record["id"], record["kind"]): Path(record["source"])
                   for record in records}
        if len(sources) != len(records) or not set(sources).issubset(expected):
            raise ValueError("Duplicate or unexpected generated character records")
        pending = expected - (set(entries) | set(sources))
        if pending and not args.allow_partial:
            raise ValueError(f"Still missing {len(pending)} actual character images")
        # Decode and validate every source before writing any sprite.
        prepared = []
        blocked = retired_hashes()
        for (cid, kind), source in sources.items():
            source_hash = reject_retired_asset(source, blocked)
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
                                        "canon_audit_note": "File-format provenance only; canonical appearance requires a separate source review.",
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
    print(f"Sprite format and runtime copies validated: {len(entries)}/{len(expected)}; "
          f"pending: {len(pending)}. This does not certify canonical appearance.")


if __name__ == "__main__":
    main()
