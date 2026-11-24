import argparse
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
SPEC = importlib.util.spec_from_file_location("all_series_assets", Path(__file__).resolve().parents[1] / "generate_all_series_assets.py")
assets = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(assets)


class CharacterAssetConversionTests(unittest.TestCase):
    def test_external_matte_removed_but_white_clothing_retained(self):
        image = Image.new("RGB", (80, 120), "white")
        draw = ImageDraw.Draw(image)
        draw.rectangle((20, 10, 60, 110), fill="navy")
        draw.rectangle((30, 30, 50, 90), fill="white")
        result = assets.transparent_character(image)
        alpha = result.getchannel("A")
        self.assertEqual(result.getpixel((0, 0))[3], 0)
        self.assertEqual(result.getpixel((40, 60)), (255, 255, 255, 255))
        self.assertTrue(any(0 < value < 255 for value in alpha.getdata()))

    def test_existing_alpha_and_eyes_are_preserved(self):
        image = Image.new("RGBA", (40, 60))
        ImageDraw.Draw(image).rectangle((10, 5, 30, 55), fill=(255, 255, 255, 190))
        result = assets.transparent_character(image)
        self.assertEqual(result.tobytes(), image.tobytes())

    def test_nonuniform_background_requires_cutout(self):
        image = Image.new("RGB", (80, 80), "white")
        ImageDraw.Draw(image).rectangle((40, 0, 79, 79), fill="black")
        with self.assertRaisesRegex(ValueError, "not a uniform matte"):
            assets.transparent_character(image)

    def test_fitting_keeps_transparency_and_requested_dimensions(self):
        image = Image.new("RGBA", (30, 80))
        ImageDraw.Draw(image).rectangle((5, 2, 25, 78), fill="blue")
        for size in ((512, 512), (1024, 1536)):
            result = assets.fit_sprite(image, size)
            self.assertEqual(result.size, size)
            self.assertEqual(result.getpixel((0, 0))[3], 0)
            bounds = result.getchannel("A").getbbox()
            self.assertGreater(bounds[0], 0)
            self.assertLess(bounds[2], size[0])

    def test_manifest_has_240_unique_ids_and_rejects_duplicates(self):
        entries = assets.load_manifest(assets.DEFAULT_MANIFEST)
        self.assertEqual(len(entries), 240)
        self.assertEqual(len({entry["id"] for entry in entries}), 240)
        with tempfile.TemporaryDirectory() as directory:
            data = json.loads(assets.DEFAULT_MANIFEST.read_text(encoding="utf-8-sig"))
            data["dm"][1]["id"] = data["dm"][0]["id"]
            path = Path(directory) / "roster.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate"):
                assets.load_manifest(path)

    def test_single_input_installs_pair_and_existing_art_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            source = base / "character.png"
            image = Image.new("RGBA", (40, 70))
            ImageDraw.Draw(image).rectangle((10, 2, 30, 68), fill="red")
            image.save(source)
            args = argparse.Namespace(manifest=assets.DEFAULT_MANIFEST, output=base/"TagForce", source_dir=None,
                                      input=source, id=104, series=None, kind="both", background="auto",
                                      tolerance=30, feather=.75, overwrite=False, verify_only=False,
                                      require_complete=True, report=None)
            self.assertEqual(assets.run(args), 0)
            standing = args.output / "DM/Characters_Standing/full_104.png"
            sd = args.output / "DM/Characters_SD/sd_104.png"
            before = standing.read_bytes(), sd.read_bytes()
            Image.new("RGBA", (40, 70), "green").save(source)
            self.assertEqual(assets.run(args), 0)
            self.assertEqual(before, (standing.read_bytes(), sd.read_bytes()))
            sd.unlink()
            args.input = None
            args.verify_only = True
            self.assertEqual(assets.run(args), 1)
            self.assertFalse(sd.exists(), "Missing art must not turn into a placeholder")


if __name__ == "__main__":
    unittest.main()
