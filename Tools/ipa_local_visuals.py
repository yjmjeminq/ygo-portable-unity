"""Attach a player's installed duel visuals to an unsigned IPA on the local PC."""

import hashlib
import re
import struct
import zipfile
from pathlib import Path


HEADER = struct.Struct("<4sII")
ENTRY = struct.Struct("<IiII")
PNG_HEADER = bytes.fromhex("89504e470d0a1a0a")


def artwork_index(path, magic):
    size = path.stat().st_size
    if size < HEADER.size:
        raise ValueError(f"Incomplete local {path.name} header.")
    with path.open("rb") as source:
        tag, version, count = HEADER.unpack(source.read(HEADER.size))
        end_of_index = HEADER.size + count * ENTRY.size
        if (tag, version) != (magic, 1) or end_of_index > size:
            raise ValueError(f"Invalid local {path.name} index.")
        entries = {}
        for _ in range(count):
            code, variant, offset, length = ENTRY.unpack(source.read(ENTRY.size))
            key = (code, variant)
            if key in entries or not code or variant < 0 or not length or offset < end_of_index or offset + length > size:
                raise ValueError(f"Invalid artwork index for {code}-{variant}.")
            entries[key] = (offset, length)
    return entries


def prepare_local_visuals(directory):
    """Validate the installed art pack, including alternates with no summon portrait."""
    directory = directory.resolve(strict=True)
    cutins = (directory / "CutIns").resolve(strict=True)
    if cutins.parent != directory:
        raise ValueError("CutIns must be inside the configured duel visuals folder.")
    portraits = {}
    for source in sorted(cutins.glob("*.png")):
        match = re.fullmatch(r"([1-9][0-9]*)-(0|[1-9][0-9]*)[.]png", source.name)
        if not match or not source.is_file() or source.resolve(strict=True).parent != cutins:
            raise ValueError(f"Unexpected local portrait: {source.name}")
        key = tuple(map(int, match.groups()))
        with source.open("rb") as image:
            if image.read(len(PNG_HEADER)) != PNG_HEADER:
                raise ValueError(f"Invalid portrait: {source.name}")
        portraits[key] = source
    if not portraits:
        raise ValueError("No local cut-in PNG files were found.")

    source_pack = directory / "cardart.pack"
    missing = portraits.keys() - artwork_index(source_pack, b"MDAR").keys()
    if missing:
        raise ValueError(f"Missing matching artwork for {len(missing)} installed cut-ins.")

    visuals = {"cardart.pack": source_pack, **{f"CutIns/{file.name}": file for file in portraits.values()}}
    overframes = directory / "overframeart.pack"
    if overframes.exists():
        index = artwork_index(overframes, b"MDOF")
        if not index or len({code for code, _ in index}) != len(index):
            raise ValueError("Expected one Overframe face per listed card.")
        with overframes.open("rb") as source:
            for (code, variant), (offset, length) in index.items():
                source.seek(offset)
                if variant <= 0 or length <= len(PNG_HEADER) or source.read(len(PNG_HEADER)) != PNG_HEADER:
                    raise ValueError(f"Invalid Overframe face for {code}-{variant}.")
        visuals[overframes.name] = overframes
    return visuals


def _app_visual_path(package):
    apps = [name[:-len("/Info.plist")] for name in package.namelist()
            if re.fullmatch(r"Payload/[^/]+[.]app/Info[.]plist", name)]
    if len(apps) != 1:
        raise ValueError("The IPA must contain one iPhone app.")
    return apps[0] + "/Data/Raw/DuelVisuals/"


def _digest(handle):
    sha = hashlib.sha256()
    for chunk in iter(lambda: handle.read(1024 * 1024), b""):
        sha.update(chunk)
    return sha.digest()


def append_local_visuals(ipa, sources):
    with zipfile.ZipFile(ipa, "a") as package:
        prefix = _app_visual_path(package)
        if any(name.startswith(prefix) for name in package.namelist()):
            raise ValueError("The IPA already contains a duel visuals package.")
        for relative, source in sources.items():
            package.write(source, prefix + relative, compress_type=zipfile.ZIP_STORED)

    with zipfile.ZipFile(ipa) as package:
        prefix = _app_visual_path(package)
        for relative, source in sources.items():
            with source.open("rb") as original, package.open(prefix + relative) as bundled:
                if _digest(original) != _digest(bundled):
                    raise ValueError(f"The packaged file did not match {relative}.")
