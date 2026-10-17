#!/usr/bin/env python3
"""Private WI-0193 TOPIQ/MUSIQ disagreement challenge selection.

Score *unreviewed non-benchmark* screening pools; select up to the remaining
challenge reserve by relative candidate rank disagreement. Never use human
ratings from calibration, challenge or held-out partitions for selection.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from types import SimpleNamespace

from candidate_audit import ranks
from learned_iqa import private_output, run as run_learned

POOL_POLICY = "m37-disagreement-screening-pool-v1"
SELECTION_POLICY = "m37-topiq-musiq-rank-disagreement-v1"
POOL_INPUT_KIND = "review-proxy-disagreement-pool"
MODELS = ("topiq_nr", "musiq")
MAX_POOL = 500


def load(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def validate_pool(benchmark, pool):
    if benchmark.get("schemaVersion") != 2:
        raise ValueError("Expected version 2 frozen benchmark")
    if pool.get("schemaVersion") != 1 or pool.get("selectionPolicy") != POOL_POLICY:
        raise ValueError("Expected explicit WI-0193 pool-only export")
    if not benchmark.get("benchmarkId") or benchmark["benchmarkId"] != pool.get("benchmarkId"):
        raise ValueError("Pool benchmark ID does not match frozen benchmark")
    if "profile-id=jpeg-1600-q78" not in benchmark.get("proxyProfile", "").splitlines():
        raise ValueError("Unsupported benchmark review-proxy profile")
    if benchmark.get("challengeReserve", 0) <= benchmark.get("challengeCount", 0):
        raise ValueError("No remaining reserved challenge slots")
    photos = pool.get("photos")
    if not isinstance(photos, list) or not 2 <= len(photos) <= MAX_POOL:
        raise ValueError("Pool size must be 2 to 500")
    core = benchmark.get("photos", [])
    existing_ids = {p["revisionId"].casefold() for p in core}
    existing_hashes = {p["contentSha256"].casefold() for p in core}
    pool_ids, pool_hashes = set(), set()
    for photo in photos:
        revision = photo["revisionId"].casefold()
        digest = photo["contentSha256"].casefold()
        if (revision in existing_ids or digest in existing_hashes or
                revision in pool_ids or digest in pool_hashes):
            raise ValueError("Pool overlaps benchmark or contains duplicate content")
        if not photo.get("proxyRelativePath") or not photo.get("proxySha256"):
            raise ValueError("Pool photo is missing its immutable review-proxy reference")
        if photo.get("partition") != "challenge" or photo.get("sampleKind") != "challenge":
            raise ValueError("Pool must be challenge-only")
        review = photo.get("review") or {}
        if any(review.get(field) is not None for field in
               ("qualityLevel", "personallyImportant", "notes")):
            raise ValueError("Screening pool cannot carry human ratings")
        pool_ids.add(revision)
        pool_hashes.add(digest)
    return photos


def score_pool(benchmark, pool, proxy_root, metric, device, output, metric_factory=None):
    photos = validate_pool(benchmark, pool)
    if metric not in MODELS:
        raise ValueError("Disagreement pool supports only TOPIQ NR and MUSIQ")
    # Construct the in-memory inference input with *only* the minimum required
    # proxy attributes; never send source keys/partition/reviews to the runner.
    manifest = {
        "schemaVersion": 2,
        "benchmarkId": benchmark["benchmarkId"],
        "proxyProfile": benchmark["proxyProfile"],
        "photos": [
            {"revisionId": photo["revisionId"],
             "proxyRelativePath": photo["proxyRelativePath"],
             "proxySha256": photo["proxySha256"]}
            for photo in photos
        ],
    }
    args = SimpleNamespace(
        input="<in-memory-disagreement-pool>",
        proxy_root=proxy_root,
        output=output,
        metric=metric,
        device=device,
        limit=None,
    )
    return run_learned(args, metric_factory=metric_factory,
                       manifest_override=manifest, input_kind=POOL_INPUT_KIND)


def validated_scores(benchmark, photos, export, model):
    candidate = export.get("candidate") or {}
    if export.get("schemaVersion") != 1 or export.get("benchmarkId") != benchmark["benchmarkId"]:
        raise ValueError("Candidate score export benchmark mismatch")
    if export.get("inputKind") != POOL_INPUT_KIND:
        raise ValueError("Candidate scores must come from the non-benchmark screening pool")
    if candidate.get("modelName") != model or export.get("higherIsBetter") is not True:
        raise ValueError("Unexpected candidate model or raw-score direction")
    scores = export.get("scores")
    if not isinstance(scores, list) or len(scores) != len(photos):
        raise ValueError("Candidate score export does not cover the complete pool")
    expected = {p["revisionId"].casefold() for p in photos}
    mapped = {}
    for item in scores:
        key = item["revisionId"].casefold()
        value = item.get("rawScore")
        if (key in mapped or key not in expected or isinstance(value, bool) or
                not isinstance(value, (int, float)) or not math.isfinite(value) or
                item.get("errorCode")):
            raise ValueError("Foreign, duplicate, missing or invalid candidate score")
        mapped[key] = float(value)
    if set(mapped) != expected:
        raise ValueError("Candidate export has missing revisions")
    return mapped


def select(benchmark, pool, topiq_export, musiq_export, count=20):
    photos = validate_pool(benchmark, pool)
    available = benchmark["challengeReserve"] - benchmark["challengeCount"]
    if not 1 <= count <= available or count > len(photos):
        raise ValueError("Requested count must fit available challenge slots and pool")
    a = validated_scores(benchmark, photos, topiq_export, "topiq_nr")
    b = validated_scores(benchmark, photos, musiq_export, "musiq")
    ids = [p["revisionId"].casefold() for p in photos]
    # Within this *new*, unlabeled non-benchmark pool, ranks are only used to
    # find disagreement examples. This is not archive-relative quality scoring
    # or a calibrated output threshold.
    ar, br = ranks([a[k] for k in ids]), ranks([b[k] for k in ids])
    denom = len(ids) - 1
    differences = {key: abs(ar[i] - br[i]) / denom for i, key in enumerate(ids)}

    def tie_key(photo):
        data = (benchmark["benchmarkId"] + "\nmodel-disagreement\n" +
                photo["revisionId"].lower()).encode("utf-8")
        return hashlib.sha256(data).hexdigest()

    ranked = sorted(photos, key=lambda p: (
        -differences[p["revisionId"].casefold()], tie_key(p)))
    selected = []
    for photo in ranked[:count]:
        # Importer will clear these review fields again, but the exported
        # selection never propagates human ratings from anywhere.
        new_photo = dict(photo)
        new_photo["partition"] = "challenge"
        new_photo["sampleKind"] = "challenge"
        new_photo["review"] = {
            "qualityLevel": None, "personallyImportant": None, "notes": None}
        new_photo["challengeReasons"] = sorted(set(
            (photo.get("challengeReasons") or []) +
            ["model-rank-disagreement-topiq-nr-musiq-v1"]))
        selected.append(new_photo)
    return {
        "schemaVersion": 1,
        "benchmarkId": benchmark["benchmarkId"],
        "selectionPolicy": SELECTION_POLICY,
        "photos": selected,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    score = sub.add_parser("score", help="Score pool proxies with TOPIQ or MUSIQ")
    score.add_argument("--metric", choices=MODELS, required=True)
    score.add_argument("--device", default="cpu")
    score.add_argument("--proxy-root", required=True)
    score.add_argument("--accept-third-party-weights", action="store_true")
    selection = sub.add_parser("select", help="Build importable 20-photo challenge file")
    selection.add_argument("--topiq-scores", required=True)
    selection.add_argument("--musiq-scores", required=True)
    selection.add_argument("--count", type=int, default=20)
    for command in (score, selection):
        command.add_argument("--input", required=True, help="Original frozen benchmark")
        command.add_argument("--pool", required=True, help="Non-benchmark pool-only export")
        command.add_argument("--output", required=True, help="Private new JSON file")
    args = parser.parse_args()
    benchmark, pool = load(args.input), load(args.pool)
    # Validate before downloading model weights or writing any file.
    validate_pool(benchmark, pool)
    if args.command == "score":
        if not args.accept_third_party_weights:
            parser.error("Explicit --accept-third-party-weights is required")
        result = score_pool(benchmark, pool, args.proxy_root, args.metric,
                            args.device, args.output)
        print(f"Scored {result['photoCount']} unreviewed pool proxies with {args.metric}.")
    else:
        selection_result = select(
            benchmark, pool, load(args.topiq_scores),
            load(args.musiq_scores), args.count)
        out = private_output(args.output)
        out.parent.mkdir(parents=True, exist_ok=True)
        with out.open("x", encoding="utf-8") as stream:
            json.dump(selection_result, stream, indent=2, allow_nan=False)
        print(f"Selected {len(selection_result['photos'])} private disagreement "
              "challenge photos; import only after inspecting reserve and pool.")
    print("Do not commit private pool, scores, candidate export, or any photo metadata.")


if __name__ == "__main__":
    main()
