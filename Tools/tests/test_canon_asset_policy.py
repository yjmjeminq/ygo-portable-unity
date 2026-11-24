import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import canon_asset_policy as policy
import generate_tagforce_gx_assets as gx
import generate_all_series_assets as all_series


class RetiredArtTests(unittest.TestCase):
    def test_both_raw_and_normalized_hashes_are_blocked(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            source = base / "renamed.png"
            raw_hash = hashlib.sha256(b"retired raw art").hexdigest()
            asset_hash = hashlib.sha256(b"retired normalized art").hexdigest()
            registry = base / "registry.json"
            registry.write_text(json.dumps({"assets": [{"source_sha256": raw_hash, "asset_sha256": asset_hash}]}), encoding="utf-8")
            blocked = policy.retired_hashes(registry)
            for content in (b"retired raw art", b"retired normalized art"):
                source.write_bytes(content)
                with self.assertRaisesRegex(ValueError, "Retired noncanonical"):
                    policy.reject_retired_asset(source, blocked)
            source.write_bytes(b"different replacement art")
            self.assertEqual(policy.reject_retired_asset(source, blocked), hashlib.sha256(source.read_bytes()).hexdigest())

    def test_existing_gx_and_generic_sprites_also_check_retirement(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "bad.png"
            source.write_bytes(b"bad art")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            with patch.object(policy, "retired_hashes", return_value={digest}):
                with self.assertRaisesRegex(ValueError, "Retired noncanonical"):
                    gx.check_image(source, (512, 512), digest)
                with self.assertRaisesRegex(ValueError, "Retired noncanonical"):
                    all_series.validate_sprite(source, (512, 512))


if __name__ == "__main__":
    unittest.main()
