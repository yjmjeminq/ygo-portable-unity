#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build character prompts exclusively from reviewed primary-source evidence.

Unreviewed facts remain unknown and disable generation. Character names, deck
themes and previous AI prompts are never used to infer appearances. Importing
this module does not modify the story, protagonist, prompts, or asset files.
"""

import argparse
import json
import re
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(__file__).resolve().parents[1]
GX_LEGACY_SCRIPT = ROOT / "Assets/StreamingAssets/TagForce/tagforce_gx_script.json"
SERIES = {"dm": "DM", "gx": "GX", "5ds": "5DS", "zexal": "ZEXAL", "arcv": "ARCV", "vrains": "VRAINS"}
FACTS = ("hair_color", "hair_style", "facial_hair", "outfit", "duel_disk")
VERIFIED_STATES = {"observed", "not_visible_in_selected_pose", "not_applicable"}
PRIMARY_DOMAINS = ("yugioh.com", "yugioh-card.com", "konami.com", "tv-tokyo.co.jp", "nasinc.co.jp", "yugioh.jp", "yu-gi-oh.jp",
                   "viz.com", "shueisha.co.jp", "shonenjump.com")
PRIMARY_VIDEO_CHANNELS = {"UCZuVXrpKTUn_KEzRQgFgMWg": "Official Yu-Gi-Oh! / KONAMI Cross Media NY"}
PUBLISHER_IMAGE_CDNS = {"viz.com": {"de7i3bh7bgh0d.cloudfront.net", "dw9to29mmj727.cloudfront.net"}}


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name("." + path.name + ".writing")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    temporary.replace(path)


def official_url(url):
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    return parsed.scheme == "https" and any(host == domain or host.endswith("." + domain) for domain in PRIMARY_DOMAINS)


def primary_visual_source(source):
    page, picture = source.get("page_url", ""), source.get("image_url", "")
    # Licensed episode catalogs expose NAS / TV Tokyo stills on Apple's image
    # CDN. Accept only a reviewed catalog page with recorded copyright and the
    # exact image URL found in that page's metadata, never arbitrary CDN files.
    page_parts, image_parts = urlparse(page), urlparse(picture)
    if page_parts.scheme == "https" and page_parts.hostname == "tv.apple.com":
        return (page_parts.path.split("/")[2:3] in (["episode"], ["movie"])
                and source.get("licensed_catalogue_verified") is True
                and bool(source.get("copyright_notice"))
                and bool(source.get("page_sha256"))
                and source.get("page_meta_image_url") == picture
                and image_parts.scheme == "https"
                and image_parts.hostname == "is1-ssl.mzstatic.com"
                and image_parts.path.startswith("/image/thumb/"))
    if page_parts.scheme == "https" and page_parts.hostname in {"myvideo.net.tw", "www.myvideo.net.tw"}:
        return (re.fullmatch(r"/details/0/\d+", page_parts.path) is not None
                and source.get("licensed_catalogue_verified") is True
                and source.get("schema_type") in {"TVEpisode", "Movie"}
                and bool(source.get("page_sha256"))
                and source.get("page_meta_image_url") == picture
                and image_parts.scheme == "https"
                and image_parts.hostname == "img.myvideo.net.tw"
                and image_parts.path.startswith("/images/MED010/"))
    if page_parts.scheme == "https" and page_parts.hostname == "x.com":
        # Konami links this account from the Duel Links home page. Match the
        # promotional image to the metadata of the specific reviewed post.
        return (re.fullmatch(r"/YuGiOh_DL_INFO/status/\d+", page_parts.path) is not None
                and source.get("official_account_verified") is True
                and source.get("official_account_page_url") == "https://www.konami.com/yugioh/duel_links/ja/"
                and source.get("twitter_creator") == "@YuGiOh_DL_INFO"
                and bool(source.get("page_sha256"))
                and source.get("page_meta_image_url") == picture
                and image_parts.scheme == "https"
                and image_parts.hostname == "pbs.twimg.com"
                and image_parts.path.startswith("/media/"))
    if page_parts.scheme == "https" and page_parts.hostname == "pluto.tv":
        episode = re.fullmatch(r"/(?:us/)?shows/\d+/episode/([0-9a-f]{24})/", page_parts.path)
        return (episode is not None
                and source.get("licensed_catalogue_verified") is True
                and bool(source.get("page_sha256"))
                and source.get("page_meta_image_url") == picture
                and image_parts.scheme == "https"
                and image_parts.hostname == "thumbnails.cbsig.net"
                and image_parts.path == f"/ptv/episodes/{episode[1]}/screenshot16_9.jpg")
    if page_parts.scheme == "https" and page_parts.hostname == "www.videomarket.jp":
        episode = re.fullmatch(r"/title/(\d{6})/A(\d{6})(\d{3})999H01", page_parts.path)
        return (episode is not None and episode[1] == episode[2]
                and source.get("licensed_catalogue_verified") is True
                and bool(source.get("copyright_notice"))
                and bool(source.get("page_sha256"))
                and source.get("page_meta_image_url") == picture
                and image_parts.scheme == "https"
                and image_parts.hostname == "imgv.vm-movie.jp"
                and image_parts.path == f"/image/android/640x360/{episode[1][:3]}/s{episode[1]}{episode[3]}a.jpg")
    if page_parts.scheme == "https" and page_parts.hostname == "duellinks.gamea.co":
        # These two pages display captures of the original game visuals.
        # The hosting articles themselves do not establish character details.
        article = re.fullmatch(r"/c/(nrv00zyp|33y1ms7t)", page_parts.path)
        return (article is not None
                and source.get("original_game_capture_reviewed") is True
                and source.get("origin_game_url") == "https://www.konami.com/yugioh/duel_links/ja/"
                and source.get("page_embedded_image_url") == picture
                and bool(source.get("page_sha256"))
                and image_parts.scheme == "https"
                and image_parts.hostname == "duellinks.gamea.co"
                and re.fullmatch(rf"/file/content(?:_top)?/umi92hxs/{article[1]}/(?:\d+/)?[a-f0-9]+_\d+\.(?:png|jpg)", image_parts.path) is not None)
    if page == "https://yugioh-starlight.com/archives/37389101.html":
        # This review uses the embedded broadcast frame, not the blog's text.
        return (source.get("original_broadcast_capture_reviewed") is True
                and source.get("origin_episode_url") == "https://www.tv-tokyo.co.jp/broad_tvtokyo/program/detail/201404/23144_201404061730.html"
                and source.get("page_embedded_image_url") == picture
                and bool(source.get("page_sha256"))
                and image_parts.scheme == "https"
                and image_parts.hostname == "livedoor.blogimg.jp"
                and re.fullmatch(r"/maxut/imgs/[0-9a-f]/[0-9a-f]/[0-9a-f]{8}\.jpg", image_parts.path) is not None)
    if official_url(page):
        if not picture or official_url(picture):
            return True
        page_host, image = urlparse(page).hostname, urlparse(picture)
        return image.scheme == "https" and any(
            (page_host == publisher or (page_host or "").endswith("."+publisher)) and image.hostname in cdns
            for publisher, cdns in PUBLISHER_IMAGE_CDNS.items())
    # A publisher's verified upload may live on YouTube rather than its own
    # domain. Bind the public thumbnail to that exact upload and known channel;
    # a random YouTube upload or image is never sufficient primary evidence.
    parsed = urlparse(page)
    video_id = parse_qs(parsed.query).get("v", [None])[0]
    return (parsed.scheme == "https" and parsed.hostname in {"www.youtube.com", "youtube.com"}
            and parsed.path == "/watch" and video_id is not None
            and source.get("channel_id") in PRIMARY_VIDEO_CHANNELS
            and source.get("official_channel_verified") is True
            and picture in {f"https://i.ytimg.com/vi/{video_id}/hqdefault.jpg",
                            f"https://i.ytimg.com/vi/{video_id}/maxresdefault.jpg"})


def roster_rows(manifest):
    if set(manifest) != set(SERIES):
        raise ValueError("The roster must contain the six explicit series keys")
    rows = [{**entry, "series": SERIES[series]} for series, entries in manifest.items() for entry in entries]
    ids = [entry["id"] for entry in rows]
    if len(rows) != 240 or len(set(ids)) != 240 or any(type(cid) is not int or cid <= 0 for cid in ids):
        raise ValueError("The master roster must have exactly 240 unique positive integer IDs")
    return rows


def validate_catalogue(catalogue, roster):
    by_id = {}
    for row in catalogue.get("characters", []):
        if row["id"] in by_id:
            raise ValueError(f"Duplicate canon record: {row['id']}")
        by_id[row["id"]] = row
    if set(by_id) != {row["id"] for row in roster}:
        raise ValueError("Canon catalogue IDs must match the complete 240-character roster")
    for row in roster:
        if by_id[row["id"]].get("series") != row["series"]:
            raise ValueError(f"Wrong series for character {row['id']}")
    sources = catalogue.get("sources", {})
    for source_id, source in sources.items():
        if not primary_visual_source(source):
            raise ValueError(f"Source is not an approved primary-source page: {source_id}")
    return by_id, sources


def sources_reviewed(references, sources):
    return bool(references) and all(source_id in sources and sources[source_id].get("visually_reviewed") is True
                                    for source_id in references)


def fact_is_reviewed(fact, sources, character_id=None):
    if not isinstance(fact, dict) or fact.get("state") not in VERIFIED_STATES:
        return False
    if not isinstance(fact.get("description"), str) or not fact["description"].strip():
        return False
    if fact["description"].strip().casefold() in {"unknown", "unspecified", "tbd", "official appearance", "match reference", "same as reference"}:
        return False
    references = fact.get("source_ids", [])
    return sources_reviewed(references, sources) and (character_id is None or all(
        character_id in sources[source_id].get("applicable_character_ids", []) for source_id in references))


def pending_reasons(row, sources, allow_non_anime):
    reasons = [key for key in FACTS if not fact_is_reviewed(row.get("facts", {}).get(key), sources, row["id"])]
    if not row.get("selected_form"):
        reasons.append("selected_form")
    if not row.get("identity_verified", False):
        reasons.append("character_identity")
    media = row.get("canonical_medium")
    if media != "anime" and not (allow_non_anime and media in {"manga", "game", "card", "film"}):
        reasons.append("canonical_medium")
    if row.get("variant_unverified", False):
        reasons.append("unverified_variant")
    used_sources = {sid for fact in row.get("facts", {}).values() if isinstance(fact, dict)
                    for sid in fact.get("source_ids", []) if sid in sources}
    if any(sources[sid].get("color_mode") == "monochrome" for sid in used_sources) and row.get("render_mode") != "monochrome":
        reasons.append("monochrome_reference_color_policy")
    full_body = row.get("full_body_source_ids", [])
    if full_body and (not sources_reviewed(full_body, sources) or any(
            row["id"] not in sources[source_id].get("applicable_character_ids", []) for source_id in full_body)):
        reasons.append("full_body_reference")
    return reasons


def make_prompts(row):
    facts = row["facts"]
    details = "; ".join(f"{key.replace('_', ' ')}: {facts[key]['description']}" for key in FACTS)
    subject = row.get("canonical_name") or row["name"]
    count = "the two named members together in one grouped sprite" if row.get("subject_count", 1) == 2 else "exactly one subject"
    selection = row.get("reference_subject_selection")
    shared = (f"{subject}, {row['series']}, selected official appearance: {row['selected_form']}. {details}. "
              + ("Strict black, white and grayscale rendering matching the original manga panel. Do not colorize or invent natural hair, skin or costume hues. "
                 if row.get("render_mode") == "monochrome" else "")
              + (f"Reference subject selection: {selection}. " if selection else "") +
              f"Use {count}. Match the cited official reference, retaining facial structure, costume, accessories "
              "and device geometry. Never merge different seasons, incarnations, anime and manga designs. "
              "Do not invent facial hair, headgear, armor, weapons, a duel disk, or themed costume decorations. "
              "Clean anime linework and cel shading; genuine transparent alpha background; no text, labels, "
              "emblems as substitutes, frames, interface, watermark, scenery, or ground plane. ")
    framing = ("Use the full-body framing of the reviewed reference. " if row.get("full_body_source_ids") else
               "Match the framing of the reviewed reference; do not extrapolate unseen lower-body clothing, footwear or accessories. ")
    standing = (shared + framing + "Standing illustration at the subject's actual canonical proportions, "
                "including any nonhuman body or life-support apparatus; no universal eight-head human redesign. "
                "Clear margins inside a vertical canvas for 1024x1536 output.")
    sd = (shared + framing + "2.5-head chibi SD adaptation; simplify proportions while retaining every reviewed identity "
          "feature, costume color, facial-hair state and device shape. Complete silhouette with clear margins, "
          "square composition for 512x512 output.")
    return standing, sd


def master_record(roster_row, canon, sources, allow_non_anime):
    reasons = pending_reasons(canon, sources, allow_non_anime)
    standing, sd = (None, None) if reasons else make_prompts(canon)
    source_ids = sorted({source_id for fact in canon.get("facts", {}).values() if isinstance(fact, dict)
                         for source_id in fact.get("source_ids", []) if source_id in sources}
                        | {source_id for source_id in canon.get("full_body_source_ids", []) if source_id in sources})
    return {"id": roster_row["id"], "series": roster_row["series"], "name": roster_row["name"],
            "canonicalName": canon.get("canonical_name"), "canonicalMedium": canon.get("canonical_medium"),
            "renderMode": canon.get("render_mode", "color"),
            "selectedForm": canon.get("selected_form"), "verificationStatus": "pending" if reasons else "reviewed",
            "generationAllowed": not reasons, "pendingFields": reasons, "facts": canon.get("facts", {}),
            "reviewedFields": [key for key in FACTS if fact_is_reviewed(canon.get("facts", {}).get(key), sources, canon["id"])],
            "referenceUrls": sorted({sources[source_id]["page_url"] for source_id in source_ids}),
            "referenceImages": sorted({sources[source_id]["image_url"] for source_id in source_ids if sources[source_id].get("image_url")}),
            "referenceSubjectSelection": canon.get("reference_subject_selection"),
            "comfyPromptStanding": standing, "comfyPromptSd": sd}


def visual_spec(record):
    def value(key):
        return record["facts"][key]["description"] if key in record["reviewedFields"] else None
    color, style = value("hair_color"), value("hair_style")
    return {"hair": "; ".join(item for item in (color, style) if item) or None,
            "facialHair": value("facial_hair"), "outfit": value("outfit"), "duelDisk": value("duel_disk"),
            "canonReviewStatus": record["verificationStatus"], "generationAllowed": record["generationAllowed"],
            "canonicalMedium": record["canonicalMedium"], "renderMode": record["renderMode"],
            "pendingFields": record["pendingFields"], "referenceUrls": record["referenceUrls"],
            "comfyPromptStanding": record["comfyPromptStanding"], "comfyPromptSd": record["comfyPromptSd"]}


def build(catalogue, manifest, allow_pending=False, allow_non_anime=None):
    if allow_non_anime is None:
        allow_non_anime = catalogue.get("policy", {}).get("non_anime_media_allowed", False)
    roster = roster_rows(manifest)
    by_id, sources = validate_catalogue(catalogue, roster)
    records = [master_record(row, by_id[row["id"]], sources, allow_non_anime) for row in roster]
    pending = [record for record in records if not record["generationAllowed"]]
    if pending and not allow_pending:
        raise ValueError(f"{len(pending)}/240 characters lack complete primary-source review; no unverified prompts will be published")
    return records


def records_with_supplemental(records, catalogue, allow_non_anime):
    by_id = {record["id"]: record for record in records}
    sources = catalogue.get("sources", {})
    for row in catalogue.get("supplemental_characters", []):
        if row["id"] in by_id:
            raise ValueError(f"Supplemental ID duplicates master roster: {row['id']}")
        by_id[row["id"]] = master_record(row, row, sources, allow_non_anime)
    return by_id


def gx_prompt_jobs(records, catalogue, allow_non_anime=False):
    gx_records = [record for record in records_with_supplemental(records, catalogue, allow_non_anime).values()
                  if record["series"] == "GX"]
    if len(gx_records) != 45:
        raise ValueError("GX prompt export requires the 43 roster forms and two explicit supplemental forms")
    jobs = []
    for record in sorted(gx_records, key=lambda item: item["id"]):
        for kind, folder, prefix, field in (("sd", "SD", "sd_", "comfyPromptSd"),
                                             ("standing", "Standing", "full_", "comfyPromptStanding")):
            jobs.append({"id": str(record["id"]), "name": record["name"], "kind": kind,
                         "canonicalMedium": record["canonicalMedium"], "renderMode": record["renderMode"],
                         "generationAllowed": record["generationAllowed"],
                         "verificationStatus": record["verificationStatus"], "pendingFields": record["pendingFields"],
                         "referenceUrls": record["referenceUrls"], "referenceImages": record["referenceImages"],
                         "referenceSubjectSelection": record["referenceSubjectSelection"], "prompt": record[field],
                         "target": f"Assets/StreamingAssets/TagForce/GX/{folder}/{prefix}{record['id']}.png"})
    return jobs


def review_report(records, catalogue):
    pending = [record for record in records if not record["generationAllowed"]]
    return {"complete": not pending, "total": len(records), "reviewed": len(records)-len(pending),
            "pending": len(pending), "scopeDecision": catalogue.get("policy", {}).get("roster_scope_decision"),
            "coverage": "Visible features of the selected official references; unseen anatomy and clothing are not invented.",
            "artworkGeneration": "This build produces reviewed prompts and data, not replacement character PNGs.",
            "reviewedByMedium": {medium: sum(row["canonicalMedium"] == medium and row["generationAllowed"] for row in records)
                                 for medium in ("anime", "film", "manga", "game", "card")},
            "series": {series: {"total": sum(row["series"] == series for row in records),
                                "reviewed": sum(row["series"] == series and row["generationAllowed"] for row in records)}
                       for series in SERIES.values()},
            "pendingCharacters": [{key: row[key] for key in ("id", "series", "name", "canonicalName",
                                                               "canonicalMedium", "pendingFields")} for row in pending]}


def update_gx(path, records, catalogue, allow_non_anime):
    data = read_json(path)
    by_id = records_with_supplemental(records, catalogue, allow_non_anime)
    updated = 0
    for persona in data.get("personas", []):
        for form in persona.get("forms", []):
            cid = form["formCharId"]
            if cid not in by_id:
                raise ValueError(f"GX form has no canon record: {cid}")
            # Replace rather than merge: invented eyes, auras and palettes cannot
            # survive from the retired dictionary. Never rewrite the protagonist.
            form["visualSpec"] = visual_spec(by_id[cid])
            updated += 1
    write_json(path, data)
    return updated


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalogue", type=Path, default=ROOT / "Tools/all_series_canon_specs.json")
    parser.add_argument("--manifest", type=Path, default=ROOT / "Tools/all_series_character_manifest.json")
    parser.add_argument("--output", type=Path, default=ROOT / "Tools/all_series_asset_prompts.json")
    parser.add_argument("--gx-script", type=Path, default=GX_LEGACY_SCRIPT)
    parser.add_argument("--gx-prompts", type=Path, default=ROOT / "Tools/tagforce_gx_asset_prompts.json")
    parser.add_argument("--report", type=Path, default=ROOT / "Tools/canon_review_status.json")
    parser.add_argument("--allow-pending", action="store_true", help="Publish disabled records with null prompts for incomplete reviews")
    parser.add_argument("--allow-non-anime", action="store_true", default=None,
                        help="Allow labeled original media (otherwise use the saved roster-scope decision)")
    parser.add_argument("--anime-only", action="store_false", dest="allow_non_anime",
                        help="Audit TV-anime designs only, overriding the saved media decision")
    parser.add_argument("--update-gx", action="store_true")
    parser.add_argument("--audit-only", action="store_true")
    args = parser.parse_args()
    try:
        catalogue = read_json(args.catalogue)
        allow_non_anime = (catalogue.get("policy", {}).get("non_anime_media_allowed", False)
                           if args.allow_non_anime is None else args.allow_non_anime)
        records = build(catalogue, read_json(args.manifest), args.allow_pending, allow_non_anime)
        gx_jobs = gx_prompt_jobs(records, catalogue, allow_non_anime)
        if not args.audit_only:
            write_json(args.output, records)
            write_json(args.gx_prompts, gx_jobs)
            write_json(args.report, review_report(records, catalogue))
            if args.update_gx:
                count = update_gx(args.gx_script, records, catalogue, allow_non_anime)
                print(f"Replaced GX visual specs: {count}; protagonist preserved")
        ready = sum(record["generationAllowed"] for record in records)
        print(f"Master records: {len(records)}; reviewed and generation-enabled: {ready}; pending: {len(records)-ready}")
        return 0
    except (ValueError, KeyError, OSError, json.JSONDecodeError) as error:
        parser.exit(2, f"Canon build refused: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
