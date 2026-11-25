"""Package installed visuals for Files > On My iPhone > YGO Portable."""

import argparse
import os
from pathlib import Path
import sys
import tempfile
import zipfile


DEFAULT_SOURCE = Path.home() / "AppData/LocalLow/YGOPortable/Yu-Gi-Oh LOTD Portable/DuelVisuals"
DEFAULT_OUTPUT = Path("G:/내 드라이브/DuelVisuals.zip")
ROOT_FILES = ("cardart.pack", "overframeart.pack", "Field.png", "Ground.png")
VISUAL_FOLDERS = ("CutIns", "Audio", "Hud", "CardIcons", "Effects")
MEDIA_TYPES = {".png", ".jpg", ".jpeg", ".wav", ".mp4"}


def collect_visual_files(source):
    source = source.resolve(strict=True)
    if not source.is_dir():
        raise ValueError(f"DuelVisuals 폴더가 필요합니다. {source}")

    files = {}

    def include(file):
        if file.resolve(strict=True) != file:
            raise ValueError(f"DuelVisuals 안에 실제 파일을 넣으세요. {file}")
        files[file.relative_to(source).as_posix()] = file

    for name in ROOT_FILES:
        file = source / name
        if file.is_file():
            include(file)
    for name in VISUAL_FOLDERS:
        folder = source / name
        if not folder.exists():
            continue
        if not folder.is_dir() or folder.resolve(strict=True) != folder:
            raise ValueError(f"DuelVisuals 안에 실제 폴더를 넣으세요. {folder}")
        for file in sorted(folder.rglob("*")):
            if file.suffix.lower() in MEDIA_TYPES and file.is_file():
                include(file)

    if not files:
        raise ValueError(f"지원하는 일러스트 또는 효과 파일을 찾지 못했습니다. {source}")
    return files


def package_visuals(source, output, overwrite=False):
    files = collect_visual_files(source)
    output = output.absolute()
    if output.suffix.lower() != ".zip":
        raise ValueError(f"출력 파일 확장자는 .zip이어야 합니다. {output}")
    if output.exists() and not overwrite:
        raise FileExistsError(f"이미 ZIP 파일이 있습니다. 교체하려면 --overwrite를 사용하세요. {output}")

    with tempfile.NamedTemporaryFile(prefix=".duel-visuals-", suffix=".part", dir=output.parent, delete=False) as temp:
        partial = Path(temp.name)
    try:
        with zipfile.ZipFile(partial, "w", compresslevel=3) as archive:
            for name, file in files.items():
                compression = zipfile.ZIP_DEFLATED if file.suffix.lower() == ".wav" else zipfile.ZIP_STORED
                archive.write(file, f"DuelVisuals/{name}", compress_type=compression)
        if overwrite:
            os.replace(partial, output)
        elif os.name == "nt":
            partial.rename(output)
        else:
            os.link(partial, output)
    finally:
        partial.unlink(missing_ok=True)
    return len(files)


def main():
    parser = argparse.ArgumentParser(description="아이폰에서 연동할 DuelVisuals.zip을 만듭니다.")
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE, help="DuelVisuals 원본 폴더")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="저장할 ZIP 파일")
    parser.add_argument("--overwrite", action="store_true", help="기존 ZIP 파일 교체")
    options = parser.parse_args()
    try:
        count = package_visuals(options.source, options.output, options.overwrite)
    except (OSError, ValueError, zipfile.LargeZipFile) as error:
        sys.exit(f"DuelVisuals 압축 실패. {error}")
    print(f"{options.output}에 파일 {count:,}개를 담았습니다.")


if __name__ == "__main__":
    main()
