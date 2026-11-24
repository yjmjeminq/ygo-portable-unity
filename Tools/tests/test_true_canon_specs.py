import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("canon_specs", Path(__file__).resolve().parents[1] / "apply_true_canon_specs.py")
canon = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(canon)


class CanonSourceContractTests(unittest.TestCase):
    def setUp(self):
        self.catalogue = canon.read_json(canon.ROOT / "Tools/all_series_canon_specs.json")
        self.manifest = canon.read_json(canon.ROOT / "Tools/all_series_character_manifest.json")

    def test_incomplete_review_cannot_be_published_as_verified(self):
        with self.assertRaisesRegex(ValueError, "lack complete primary-source review"):
            canon.build(self.catalogue, self.manifest)
        records = canon.build(self.catalogue, self.manifest, allow_pending=True)
        self.assertEqual(len(records), 240)
        for record in records:
            if not record["generationAllowed"]:
                self.assertIsNone(record["comfyPromptStanding"])
                self.assertIsNone(record["comfyPromptSd"])

    def test_another_characters_reference_cannot_verify_an_identity(self):
        camula = next(row for row in self.catalogue["characters"] if row["id"] == 116)
        wrong = copy.deepcopy(camula)
        wrong["id"] = 94
        self.assertIn("hair_color", canon.pending_reasons(wrong, self.catalogue["sources"], False))
        self.assertIn("outfit", canon.pending_reasons(wrong, self.catalogue["sources"], False))

    def test_unknown_keywords_do_not_become_facts(self):
        fact = {"state": "observed", "description": "unknown", "source_ids": ["camula_profile"]}
        self.assertFalse(canon.fact_is_reviewed(fact, self.catalogue["sources"], 116))

    def test_primary_source_cannot_be_a_fan_site_or_url_spoof(self):
        self.assertTrue(canon.official_url("https://uploads1.yugioh.com/character/191/detail/detail/camula-lg.png"))
        self.assertFalse(canon.official_url("https://yugioh.fandom.com/wiki/Camula"))
        self.assertFalse(canon.official_url("https://yugioh.com.example.com/characters/camula"))
        self.assertFalse(canon.official_url("https://yugioh.com@evil.example/camula"))

    def test_wrong_source_host_aborts_the_master(self):
        data = copy.deepcopy(self.catalogue)
        data["sources"]["camula_profile"]["page_url"] = "https://example.com/camula"
        with self.assertRaisesRegex(ValueError, "primary-source"):
            canon.build(data, self.manifest, allow_pending=True)

    def test_video_reference_must_belong_to_known_official_channel_and_exact_upload(self):
        source = {"page_url": "https://www.youtube.com/watch?v=g1CxW2l-HPY",
                  "image_url": "https://i.ytimg.com/vi/g1CxW2l-HPY/hqdefault.jpg",
                  "channel_id": "UCZuVXrpKTUn_KEzRQgFgMWg", "official_channel_verified": True}
        self.assertTrue(canon.primary_visual_source(source))
        source["image_url"] = "https://i.ytimg.com/vi/other_upload/hqdefault.jpg"
        self.assertFalse(canon.primary_visual_source(source))
        source["image_url"] = "https://i.ytimg.com/vi/g1CxW2l-HPY/hqdefault.jpg"
        source["channel_id"] = "a_fan_channel"
        self.assertFalse(canon.primary_visual_source(source))

    def test_gx_jobs_share_verified_master_prompts_and_disable_pending_forms(self):
        records = canon.build(self.catalogue, self.manifest, allow_pending=True)
        jobs = canon.gx_prompt_jobs(records, self.catalogue)
        self.assertEqual(len(jobs), 90)
        self.assertEqual(len({(job["id"], job["kind"]) for job in jobs}), 90)
        by_id = canon.records_with_supplemental(records, self.catalogue, False)
        self.assertNotIn("player_heroine", {job["id"] for job in jobs})
        for job in jobs:
            record = by_id[int(job["id"])]
            field = "comfyPromptStanding" if job["kind"] == "standing" else "comfyPromptSd"
            self.assertEqual(job["prompt"], record[field])
            self.assertEqual(job["generationAllowed"], record["generationAllowed"])

    def test_full_body_framing_requires_an_owned_reviewed_reference(self):
        row = copy.deepcopy(next(row for row in self.catalogue["characters"] if row["id"] == 116))
        row["full_body_source_ids"] = ["joey_profile"]
        self.assertIn("full_body_reference", canon.pending_reasons(row, self.catalogue["sources"], False))
        row["full_body_source_ids"] = []
        row["reference_subject_selection"] = "Select only Camula on the left"
        standing, sd = canon.make_prompts(row)
        self.assertIn("do not extrapolate unseen", standing)
        self.assertIn("Select only Camula on the left", sd)

    def test_film_designs_are_not_silently_certified_as_tv_anime(self):
        row = next(row for row in self.catalogue["characters"] if row["id"] == 203)
        self.assertEqual(row["canonical_medium"], "film")
        self.assertIn("canonical_medium", canon.pending_reasons(row, self.catalogue["sources"], False))
        self.assertNotIn("canonical_medium", canon.pending_reasons(row, self.catalogue["sources"], True))

    def test_saved_original_media_decision_keeps_media_labels_and_can_be_overridden(self):
        data = copy.deepcopy(self.catalogue)
        data["policy"]["non_anime_media_allowed"] = True
        approved = canon.build(data, self.manifest, allow_pending=True)
        anime_only = canon.build(data, self.manifest, allow_pending=True, allow_non_anime=False)
        sera = next(row for row in approved if row["id"] == 203)
        self.assertTrue(sera["generationAllowed"])
        self.assertEqual(sera["canonicalMedium"], "film")
        self.assertFalse(next(row for row in anime_only if row["id"] == 203)["generationAllowed"])

    def test_publisher_cdns_are_bound_to_the_publisher_and_manga_cannot_be_silently_colorized(self):
        source = {"page_url": "https://www.viz.com/blog/posts/yu-gi-oh-zexal-vol-5-2256",
                  "image_url": "https://de7i3bh7bgh0d.cloudfront.net/drupal/art/YGZv5_02.jpg"}
        self.assertTrue(canon.primary_visual_source(source))
        source["page_url"] = "https://www.yugioh.com/characters/camula"
        self.assertFalse(canon.primary_visual_source(source))
        row = copy.deepcopy(next(row for row in self.catalogue["characters"] if row["id"] == 3305))
        self.assertEqual(row["render_mode"], "monochrome")
        self.assertIn("Do not colorize", canon.make_prompts(row)[0])
        row.pop("render_mode")
        self.assertIn("monochrome_reference_color_policy", canon.pending_reasons(row, self.catalogue["sources"], True))

    def test_licensed_episode_still_requires_catalogue_metadata_and_publisher_binding(self):
        source = {"page_url": "https://tv.apple.com/us/episode/your-reign-is-over/umc.cmc.50ir82mlfs619tl7e2n4qn8pj",
                  "image_url": "https://is1-ssl.mzstatic.com/image/thumb/OwYBmkyLuUnUxWZqwW5Xlw/1200x675.jpg",
                  "licensed_catalogue_verified": True, "copyright_notice": "© 2017 NAS • TV TOKYO",
                  "page_sha256": "a" * 64}
        self.assertFalse(canon.primary_visual_source(source))
        source["page_meta_image_url"] = source["image_url"]
        self.assertTrue(canon.primary_visual_source(source))
        source["image_url"] = "https://is1-ssl.mzstatic.com/image/thumb/unrelated/1200x675.jpg"
        self.assertFalse(canon.primary_visual_source(source))
        source["image_url"] = source["page_meta_image_url"]
        source["page_url"] = "https://tv.apple.com.example.com/us/episode/queen/id"
        self.assertFalse(canon.primary_visual_source(source))

    def test_updating_visuals_removes_retired_fields_and_preserves_dialogue_and_player(self):
        records = canon.build(self.catalogue, self.manifest, allow_pending=True)
        original = canon.read_json(canon.ROOT / "Assets/StreamingAssets/tagforce_gx_script.json")
        original["personas"][0]["forms"][0]["visualSpec"]["inventedAura"] = "discard me"
        expected = copy.deepcopy(original)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "gx.json"
            path.write_text(json.dumps(original), encoding="utf-8")
            count = canon.update_gx(path, records, self.catalogue, False)
            result = canon.read_json(path)
        self.assertEqual(count, 45)
        self.assertEqual(result["protagonist"], expected["protagonist"])
        for before, after in zip(expected["personas"], result["personas"]):
            self.assertEqual(before["personaId"], after["personaId"])
            for old_form, new_form in zip(before["forms"], after["forms"]):
                self.assertEqual({key: value for key, value in old_form.items() if key != "visualSpec"},
                                 {key: value for key, value in new_form.items() if key != "visualSpec"})
                self.assertNotIn("inventedAura", new_form["visualSpec"])


if __name__ == "__main__":
    unittest.main()
