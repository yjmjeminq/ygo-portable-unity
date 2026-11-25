"""Restore only the staged Tag Force data when the feature is ready."""

import argparse
import filecmp
from pathlib import Path
import re
import shutil


PROJECT = Path(__file__).resolve().parents[1]
SCENARIO = re.compile(r"tagforce_[a-z0-9_]+\.json(?:\.meta)?")
WORLD_SCRIPT = re.compile(r"tagforce_[a-z0-9_]+_script\.json(?:\.meta)?")


def staged_files(source):
    art = source / "TagForce"
    if not art.is_dir():
        raise ValueError(f"Missing staged artwork folder: {art}")
    files = [path for path in art.rglob("*") if path.is_file()]
    files.extend(path for path in source.iterdir() if path.is_file() and
                 (path.name == "TagForce.meta" or SCENARIO.fullmatch(path.name)))
    if not any(path.name == "tagforce_gx_script.json" for path in files):
        raise ValueError("Missing the staged GX scenario.")
    for path in files:
        if not path.resolve().is_relative_to(source):
            raise ValueError(f"Staged file resolves outside its source: {path}")
    return sorted(files)


def restore(source, destination, check):
    source = source.resolve(strict=True)
    destination = destination.resolve()
    if source.is_relative_to(destination) or destination.is_relative_to(source):
        raise ValueError("The staging and destination folders must be separate.")
    files = staged_files(source)
    pending = []
    for path in files:
        relative = path.relative_to(source)
        if len(relative.parts) == 1 and WORLD_SCRIPT.fullmatch(path.name):
            relative = Path("TagForce") / relative
        target = destination / relative
        if not target.resolve().is_relative_to(destination):
            raise ValueError(f"Destination resolves outside StreamingAssets: {target}")
        if target.exists():
            if not target.is_file() or not filecmp.cmp(path, target, shallow=False):
                raise ValueError(f"Destination already contains a different file: {target}")
        else:
            pending.append((path, target))

    if check:
        size = sum(path.stat().st_size for path in files)
        print(f"Verified {len(files)} staged files ({size / 2**20:.1f} MiB).")
        return
    for path, target in pending:
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
    print(f"Restored {len(pending)} files to {destination}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=PROJECT.parent / "TagForce_Staged_Assets")
    parser.add_argument("--destination", type=Path, default=PROJECT / "Assets" / "StreamingAssets")
    parser.add_argument("--check", action="store_true", help="Verify the source and destination without copying.")
    args = parser.parse_args()
    try:
        restore(args.source, args.destination, args.check)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
