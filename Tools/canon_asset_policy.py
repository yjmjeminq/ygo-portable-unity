"""Reject byte-identical artwork retired by the official-appearance audit."""
import hashlib
import json
from pathlib import Path

REGISTRY = Path(__file__).resolve().parents[1] / "Tools/canon_asset_invalidation.json"


def retired_hashes(registry=REGISTRY):
    data = json.loads(Path(registry).read_text(encoding="utf-8-sig"))
    return {item[field] for item in data["assets"] for field in ("source_sha256", "asset_sha256") if item.get(field)}


def reject_retired_asset(path, blocked=None):
    digest = hashlib.sha256(Path(path).read_bytes()).hexdigest()
    if digest in (retired_hashes() if blocked is None else blocked):
        raise ValueError(f"Retired noncanonical image cannot be reinstalled: {path}")
    return digest
