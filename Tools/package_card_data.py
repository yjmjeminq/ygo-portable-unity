"""Build the card database and artwork ZIP installed by the iPhone app."""

import argparse
from pathlib import Path
import tempfile
import zipfile

if __package__:
    from .ipa_local_visuals import prepare_local_visuals
    from .package_duel_visuals import DEFAULT_SOURCE, collect_visual_files
else:
    from ipa_local_visuals import prepare_local_visuals
    from package_duel_visuals import DEFAULT_SOURCE, collect_visual_files


PROJECT = Path(__file__).resolve().parents[1]
CARD_DATA_PREFIX = "CardData-v1/"
CARD_DATA_ZIP = "CardData.zip"


def card_data_filenames(project):
    return (project / "Assets/Resources/CardDataPackFiles.txt").read_text(encoding="utf-8").split()


def package_card_data(project, visuals, destination, *, overwrite=False, interface=None):
    destination = destination.absolute()
    if destination.suffix.lower() != ".zip":
        raise ValueError(f"카드 데이터 파일은 ZIP이어야 합니다. {destination}")
    if destination.exists() and not overwrite:
        raise FileExistsError(f"이미 카드 데이터 ZIP이 있습니다. {destination}")

    streaming = project / "Assets/StreamingAssets"
    files = {name: streaming / name for name in card_data_filenames(project)}
    prepare_local_visuals(visuals)
    visual_files = collect_visual_files(visuals)
    if interface:
        visual_files.update(interface)
    files.update({f"DuelVisuals/{name}": file for name, file in visual_files.items()})
    for name, file in files.items():
        if not file.is_file() or file.stat().st_size == 0:
            raise ValueError(f"카드 데이터 원본이 없거나 비어 있습니다. {name}")

    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(prefix=".card-data-", suffix=".part", dir=destination.parent, delete=False) as temp:
        partial = Path(temp.name)
    try:
        with zipfile.ZipFile(partial, "w", compresslevel=3) as archive:
            for name, source in files.items():
                compress = source.suffix.lower() in (".json", ".bin", ".idx", ".tsv", ".wav") or name == "cardscripts.pack"
                archive.write(source, CARD_DATA_PREFIX + name,
                              compress_type=zipfile.ZIP_DEFLATED if compress else zipfile.ZIP_STORED)
        with zipfile.ZipFile(partial) as archive:
            failure = archive.testzip()
            if failure:
                raise ValueError(f"손상된 카드 데이터 ZIP 항목이 있습니다. {failure}")
        if destination.exists() and not overwrite:
            raise FileExistsError(f"이미 카드 데이터 ZIP이 있습니다. {destination}")
        partial.replace(destination)
    finally:
        partial.unlink(missing_ok=True)
    return len(files)


def main():
    parser = argparse.ArgumentParser(description="iPhone용 CardData.zip 생성")
    parser.add_argument("--project", type=Path, default=PROJECT)
    parser.add_argument("--visuals", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, default=PROJECT / "Builds/iOS/IPA" / CARD_DATA_ZIP)
    parser.add_argument("--overwrite", action="store_true")
    options = parser.parse_args()
    count = package_card_data(options.project, options.visuals, options.output, overwrite=options.overwrite)
    print(f"{options.output}에 카드 데이터와 일러스트 {count:,}개를 저장했습니다. "
          f"({options.output.stat().st_size / 1_000_000:,.1f} MB)")


if __name__ == "__main__":
    main()
