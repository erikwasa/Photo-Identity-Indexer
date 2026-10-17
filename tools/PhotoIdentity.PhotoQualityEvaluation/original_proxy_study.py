#!/usr/bin/env python3
"""Private WI-0193 original-versus-proxy model sensitivity study.

Select 50–100 calibration/challenge photos without consulting human labels;
score original decoded pixels (lossless temporary RGB PNG) and the immutable
review proxy with the same learned model. This is an evaluation tool, not a
production decoder, model choice, or permission to inspect held-out labels.
"""
from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import importlib.metadata
import json
import math
from pathlib import Path
import statistics
import tempfile
import time

from candidate_audit import spearman
from learned_iqa import CANDIDATES, private_output, verified_proxy

PROTOCOL = "wi0193-original-proxy-paired-v1"
SELECTION_PROTOCOL = "wi0193-original-proxy-sample-v1"
SUPPORTED = ("jpeg", "heic", "png", "raw")
MAX_ORIGINAL_BYTES = 256 * 1024 * 1024
MAX_PIXELS = 100_000_000


def load(path):
    with open(path, encoding="utf-8") as file:
        return json.load(file)


def photo_key(photo):
    # Do not include human review data, source paths or camera details.
    return {key: photo[key] for key in (
        "revisionId", "sourceKey", "contentSha256", "proxyRelativePath",
        "proxySha256", "mediaFamily")}


def manifest_candidates(benchmark):
    if benchmark.get("schemaVersion") != 2 or not benchmark.get("benchmarkId"):
        raise ValueError("Expected frozen version 2 benchmark")
    if "profile-id=jpeg-1600-q78" not in benchmark.get("proxyProfile", "").splitlines():
        raise ValueError("Wrong immutable review proxy profile")
    candidates = {}
    for photo in benchmark.get("photos", []):
        # Crucial: do not even examine review fields from the held-out set.
        if photo.get("partition") not in ("calibration", "challenge"):
            continue
        key = photo["revisionId"].casefold()
        if key in candidates:
            raise ValueError("Duplicate eligible benchmark revision")
        family = (photo.get("stratum") or {}).get("mediaFamily")
        if family not in SUPPORTED:
            continue
        slim = {
            "revisionId": photo["revisionId"],
            "sourceKey": photo["sourceKey"],
            "contentSha256": photo["contentSha256"],
            "proxyRelativePath": photo["proxyRelativePath"],
            "proxySha256": photo["proxySha256"],
            "mediaFamily": family,
        }
        candidates[key] = slim
    return candidates


def selection_hash(benchmark_id, photos):
    # Stable across files/OS and unrelated review progress.
    payload = json.dumps({
        "benchmarkId": benchmark_id,
        "photos": [photo_key(photo) for photo in photos],
    }, separators=(",", ":"), sort_keys=True, ensure_ascii=True)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def select_pairs(benchmark, count=80, families=SUPPORTED):
    if not 50 <= count <= 100:
        raise ValueError("Original/proxy sample must have 50 to 100 photos")
    if not families or len(set(families)) != len(families) or any(
            family not in SUPPORTED for family in families):
        raise ValueError("Invalid or duplicate media-family selection")
    eligible = manifest_candidates(benchmark)
    groups = {}
    for family in families:
        entries = [p for p in eligible.values() if p["mediaFamily"] == family]
        groups[family] = sorted(entries, key=lambda p: hashlib.sha256(
            (benchmark["benchmarkId"] + "\n" + SELECTION_PROTOCOL + "\n" +
             p["revisionId"].casefold()).encode("utf-8")).hexdigest())
    if sum(map(len, groups.values())) < count:
        raise ValueError("Insufficient eligible non-held-out photos")
    selected = []
    # Round-robin stratification avoids letting large JPEG strata crowd out
    # HEIC/RAW evidence; results must not be presented as population weighted.
    while len(selected) < count:
        previous = len(selected)
        for family in families:
            if groups[family] and len(selected) < count:
                selected.append(groups[family].pop(0))
        if len(selected) == previous:
            raise ValueError("Could not select requested sample")
    result = {
        "schemaVersion": 1,
        "protocol": SELECTION_PROTOCOL,
        "benchmarkId": benchmark["benchmarkId"],
        "proxyProfile": benchmark["proxyProfile"],
        "photos": selected,
    }
    result["selectionSha256"] = selection_hash(result["benchmarkId"], selected)
    return result


def validate_selection(benchmark, selection):
    if (selection.get("schemaVersion") != 1 or
            selection.get("protocol") != SELECTION_PROTOCOL or
            selection.get("benchmarkId") != benchmark.get("benchmarkId") or
            selection.get("proxyProfile") != benchmark.get("proxyProfile")):
        raise ValueError("Selection provenance differs from benchmark")
    photos = selection.get("photos")
    if not isinstance(photos, list) or not 50 <= len(photos) <= 100:
        raise ValueError("Expected a 50–100 photo immutable selection")
    if selection.get("selectionSha256") != selection_hash(benchmark["benchmarkId"], photos):
        raise ValueError("Selection fingerprint mismatch")
    eligible = manifest_candidates(benchmark)
    seen = set()
    for photo in photos:
        key = photo["revisionId"].casefold()
        if key in seen or key not in eligible or photo_key(photo) != eligible[key]:
            raise ValueError("Selection changed or includes ineligible benchmark content")
        seen.add(key)
    return photos


def safe_original(root, relative, expected_hash):
    relative = relative.replace("\\", "/")
    rel = Path(relative)
    if not relative or rel.is_absolute() or ".." in rel.parts or ":" in relative:
        raise ValueError("Invalid original source key")
    target = root / rel
    if not target.resolve().is_relative_to(root):
        raise ValueError("Original source path escapes configured root")
    for part in (target, *target.parents):
        if part == root.parent:
            break
        if part.is_symlink() or part.is_junction():
            raise ValueError("Original source path crosses a symlink/junction")
        if part == root:
            break
    if not target.is_file() or not 0 < target.stat().st_size <= MAX_ORIGINAL_BYTES:
        raise ValueError("Original not found or over size limit")
    digest = hashlib.sha256()
    with target.open("rb") as reader:
        while block := reader.read(1024 * 1024):
            digest.update(block)
    if digest.hexdigest().casefold() != expected_hash.casefold():
        raise ValueError("Original SHA-256 does not match frozen catalogue content")
    return target


def decode_lossless(original, family, dest, max_original_long_edge=None):
    # Imports are local and opt-in. There is no image dependency in offline CI.
    from PIL import Image, ImageOps
    if family == "raw":
        try:
            import rawpy
        except ImportError as exc:
            raise ValueError("RAW study requires optional rawpy") from exc
        with rawpy.imread(str(original)) as raw:
            # Camera WB and deterministic no-auto-bright do not reproduce
            # Photo Identity's original decode/render policy exactly.
            pixels = raw.postprocess(
                use_camera_wb=True, no_auto_bright=True, output_bps=8)
        image = Image.fromarray(pixels, "RGB")
    else:
        if family == "heic":
            try:
                from pillow_heif import register_heif_opener
            except ImportError as exc:
                raise ValueError("HEIC study requires optional pillow-heif") from exc
            register_heif_opener(thumbnails=False)
        with Image.open(original) as loaded:
            if loaded.width * loaded.height > MAX_PIXELS:
                raise ValueError("Original image pixel limit exceeded")
            loaded.load()
            image = ImageOps.exif_transpose(loaded)
            if image.mode in ("RGBA", "LA") or "transparency" in image.info:
                rgba = image.convert("RGBA")
                canvas = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
                image = Image.alpha_composite(canvas, rgba).convert("RGB")
            else:
                image = image.convert("RGB")
    if image.width * image.height > MAX_PIXELS:
        raise ValueError("Decoded original pixel limit exceeded")
    try:
        if max_original_long_edge is not None and max(image.size) > max_original_long_edge:
            scale = max_original_long_edge / max(image.size)
            target = (max(1, round(image.width * scale)),
                      max(1, round(image.height * scale)))
            bounded = image.resize(target, Image.Resampling.LANCZOS)
            image.close()
            image = bounded
        image.save(dest, "PNG")
    finally:
        image.close()


def score_pairs(benchmark, selection, original_root, proxy_root, metric, device,
                output, metric_factory=None, decode_factory=None,
                max_original_long_edge=None, probe_pairs=None):
    photos = validate_selection(benchmark, selection)
    if max_original_long_edge is not None and not 1601 <= max_original_long_edge <= 12000:
        raise ValueError("Original long-edge cap must be between 1601 and 12000 px")
    if probe_pairs is not None and not 1 <= probe_pairs <= 10:
        raise ValueError("Probe size must be between 1 and 10 pairs")
    planned_pairs = len(photos)
    if probe_pairs is not None:
        photos = photos[:probe_pairs]
    if metric not in CANDIDATES or (device != "cpu" and not device.startswith("cuda")):
        raise ValueError("Unsupported metric or device")
    original_root = Path(original_root).resolve(strict=True)
    proxy_root = Path(proxy_root).resolve(strict=True)
    destination = private_output(output)
    # Fail closed before importing a model or touching output when originals
    # or proxy bytes do not match their frozen hashes.
    verified = []
    for photo in photos:
        original = safe_original(
            original_root, photo["sourceKey"], photo["contentSha256"])
        proxy = verified_proxy(
            proxy_root, photo["proxyRelativePath"], photo["proxySha256"])
        verified.append((photo, original, proxy))
    print(f"{metric}: verified {len(verified)}/{planned_pairs} pairs; "
          f"source input={'full decoded' if max_original_long_edge is None else str(max_original_long_edge) + 'px capped'}; "
          "initializing model...", flush=True)
    if metric_factory is None:
        import pyiqa
        model = pyiqa.create_metric(metric, device=device)
        version = importlib.metadata.version("pyiqa")
    else:
        model = metric_factory(metric, device)
        version = "test-double"
    decoder = decode_lossless if decode_factory is None else decode_factory
    rows = []
    print(f"{metric}: model initialized; scoring {len(verified)} pairs", flush=True)
    for index, (photo, original, proxy) in enumerate(verified, 1):
        pair_started = time.perf_counter()
        with tempfile.TemporaryDirectory(prefix="wi0193-paired-") as temp:
            png = Path(temp) / "decoded-original.png"
            try:
                if max_original_long_edge is None:
                    decoder(original, photo["mediaFamily"], png)
                else:
                    decoder(original, photo["mediaFamily"], png, max_original_long_edge)
                source_score = float(model(str(png)).item())
                proxy_score = float(model(str(proxy)).item())
                if not all(map(math.isfinite, (source_score, proxy_score))):
                    raise ArithmeticError("Non-finite model output")
                failure = None
            except Exception:
                # No file paths, source keys or exception messages in exports.
                source_score = proxy_score = None
                failure = "decode-or-inference-failed"
        rows.append({
            "revisionId": photo["revisionId"],
            "mediaFamily": photo["mediaFamily"],
            "originalScore": source_score,
            "proxyScore": proxy_score,
            "errorCode": failure,
        })
        print(f"{metric}: pair {index}/{len(verified)} "
              f"{'ok' if failure is None else 'failed'}; "
              f"{time.perf_counter() - pair_started:.1f}s", flush=True)
    result = {
        "schemaVersion": 1, "protocol": PROTOCOL,
        "benchmarkId": benchmark["benchmarkId"],
        "selectionSha256": selection["selectionSha256"],
        "modelName": metric, "modelVersion": f"pyiqa-{version}; weights-not-yet-pinned",
        "device": device,
        "sourceDecodePolicy": (
            "Pillow EXIF-transposed RGB lossless PNG; HEIC via optional pillow-heif "
            "8-bit decode; RAW via optional rawpy camera-WB/no-auto-bright 8-bit "
            "demosaic; different renderers/color handling from app are possible"),
        "sampleIsPopulationWeighted": False,
        "plannedPairs": planned_pairs,
        "maxOriginalLongEdge": max_original_long_edge,
        "isProbe": probe_pairs is not None,
        "photos": rows,
    }
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("x", encoding="utf-8") as file:
        json.dump(result, file, indent=2, allow_nan=False)
    return result


def summarize(result):
    if result.get("protocol") != PROTOCOL or not isinstance(result.get("photos"), list):
        raise ValueError("Expected original/proxy paired study export")
    photos = result["photos"]
    valid = [row for row in photos if row.get("errorCode") is None and
             isinstance(row.get("originalScore"), (int, float)) and
             isinstance(row.get("proxyScore"), (int, float)) and
             math.isfinite(row["originalScore"]) and math.isfinite(row["proxyScore"])]
    original = [p["originalScore"] for p in valid]
    proxy = [p["proxyScore"] for p in valid]
    diffs = [b-a for a, b in zip(original, proxy)]
    families = {}
    for family in SUPPORTED:
        rows = [p for p in valid if p["mediaFamily"] == family]
        if rows:
            families[family] = {
                "count": len(rows),
                "medianAbsoluteDifference": statistics.median(abs(p["proxyScore"] -
                                                             p["originalScore"]) for p in rows),
                "spearman": spearman([p["originalScore"] for p in rows],
                                     [p["proxyScore"] for p in rows])
            }
    n = len(valid)
    top_quartile_overlap = None
    if n >= 4:
        top_n = max(1, math.ceil(n / 4))
        a = set(sorted(range(n), key=lambda i: original[i], reverse=True)[:top_n])
        b = set(sorted(range(n), key=lambda i: proxy[i], reverse=True)[:top_n])
        top_quartile_overlap = len(a & b) / top_n
    return {
        "protocol": PROTOCOL, "candidate": result.get("modelName"),
        "sampleCount": len(photos), "plannedPairs": result.get("plannedPairs", len(photos)),
        "maxOriginalLongEdge": result.get("maxOriginalLongEdge"),
        "isProbe": result.get("isProbe", False),
        "scoredPairs": n, "failures": len(photos)-n,
        "complete": (not result.get("isProbe", False)
                     and n == len(photos) and n >= 50),
        "spearmanOriginalVsProxy": spearman(original, proxy) if n > 2 else None,
        "medianProxyMinusOriginal": statistics.median(diffs) if diffs else None,
        "medianAbsoluteScoreShift": statistics.median(map(abs, diffs)) if diffs else None,
        "topQuartileOverlap": top_quartile_overlap,
        "byMediaFamily": families,
        "warning": (
            "Diagnostic original/proxy score stability only; decoder/color policies "
            "may differ from production; no human ratings or held-out labels used. "
            "No automatic exclusion or model/threshold decision."),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    s = sub.add_parser("select")
    s.add_argument("--input", required=True)
    s.add_argument("--count", type=int, default=80)
    s.add_argument("--families", nargs="+", choices=SUPPORTED, default=list(SUPPORTED))
    s.add_argument("--output", required=True)
    q = sub.add_parser("score")
    q.add_argument("--input", required=True)
    q.add_argument("--selection", required=True)
    q.add_argument("--original-root", required=True)
    q.add_argument("--proxy-root", required=True)
    q.add_argument("--metric", required=True, choices=CANDIDATES)
    q.add_argument("--device", default="cpu")
    q.add_argument("--max-original-long-edge", type=int,
                   help="Bound decoded original input to this long edge (e.g. 2560). "
                        "Not a true full-resolution study.")
    q.add_argument("--probe-pairs", type=int,
                   help="Run only the first 1–10 sample pairs as a diagnostic; "
                        "output is never complete evaluation.")
    q.add_argument("--output", required=True)
    q.add_argument("--accept-third-party-weights", action="store_true")
    r = sub.add_parser("report")
    r.add_argument("--scores", required=True)
    args = parser.parse_args()
    if args.command == "select":
        export = select_pairs(load(args.input), args.count, args.families)
        path = private_output(args.output)
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("x", encoding="utf-8") as file:
            json.dump(export, file, indent=2)
        print(f"Selected {len(export['photos'])} private non-held-out original/proxy pairs.")
    elif args.command == "score":
        if not args.accept_third_party_weights:
            parser.error("Explicit third-party weight consent required")
        result = score_pairs(load(args.input), load(args.selection),
                             args.original_root, args.proxy_root,
                             args.metric, args.device, args.output,
                             max_original_long_edge=args.max_original_long_edge,
                             probe_pairs=args.probe_pairs)
        report = summarize(result)
        print(json.dumps(report, indent=2, allow_nan=False))
    else:
        print(json.dumps(summarize(load(args.scores)), indent=2, allow_nan=False))
    print("Private source paths, per-photo scores and human ratings must not be committed.")


if __name__ == "__main__":
    main()
