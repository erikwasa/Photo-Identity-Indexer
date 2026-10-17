#!/usr/bin/env python3
"""Extend private WI-0193 score exports after importing disagreement challenges.

Uses original 580-photo score exports plus the already-scored TOPIQ/MUSIQ
screening pool (or a fresh selected-only ARNIQA run). Never consults human
labels. Produces new versioned 600-photo exports; never overwrites originals.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import statistics
from types import SimpleNamespace

from disagreement_challenges import POOL_INPUT_KIND, POOL_POLICY, SELECTION_POLICY
from learned_iqa import CANDIDATES, p95, private_output, run as run_learned

SELECTED_INPUT_KIND = "review-proxy-disagreement-selected"
SCORE_INPUT_KIND = "review-proxy"
PROTOCOL = "wi0193-score-export-extension-v1"


def load(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def identity(photo):
    # Human reviews may be completed between manifest backups and import.
    # Read no human label, not even for verification or diagnostics.
    return {key: value for key, value in photo.items() if key != "review"}


def validate_expansion(original, expanded, selection):
    if original.get("schemaVersion") != 2 or expanded.get("schemaVersion") != 2:
        raise ValueError("Both benchmark snapshots must have schemaVersion 2")
    benchmark_id = original.get("benchmarkId")
    if not benchmark_id or expanded.get("benchmarkId") != benchmark_id:
        raise ValueError("Frozen benchmark identity mismatch")
    if selection.get("schemaVersion") != 1 or selection.get("benchmarkId") != benchmark_id:
        raise ValueError("Selected challenge export belongs to another benchmark")
    if selection.get("selectionPolicy") != SELECTION_POLICY:
        raise ValueError("Only TOPIQ/MUSIQ disagreement challenge import is supported")
    before, after, added = (item.get("photos") for item in
                            (original, expanded, selection))
    if not isinstance(before, list) or not isinstance(after, list) or not isinstance(added, list):
        raise ValueError("Missing benchmark/challenge photos")
    remaining = original.get("challengeReserve", 0) - original.get("challengeCount", 0)
    if not 1 <= len(added) <= remaining or len(after) != len(before) + len(added):
        raise ValueError("Unexpected count of imported challenge photos")
    for field in ("representativeCount", "calibrationCount", "heldOutCount",
                  "challengeReserve", "proxyProfile", "partitionPolicy", "selectionPolicy"):
        if original.get(field) != expanded.get(field):
            raise ValueError("Frozen benchmark fields changed during challenge import")
    if expanded.get("challengeCount") != original.get("challengeCount", 0) + len(added):
        raise ValueError("Expanded challenge count does not match import")
    if SELECTION_POLICY not in expanded.get("challengeSelectionPolicies", []):
        raise ValueError("Expanded benchmark does not record disagreement selection policy")
    original_ids = [photo["revisionId"].casefold() for photo in before]
    new_ids = [photo["revisionId"].casefold() for photo in added]
    expanded_ids = [photo["revisionId"].casefold() for photo in after]
    if (len(set(expanded_ids)) != len(expanded_ids) or
            expanded_ids != original_ids + new_ids):
        raise ValueError("Expanded benchmark did not preserve immutable photo order")
    orig_hashes = {p["contentSha256"].casefold() for p in before}
    seen_new_hashes = set()
    for old, current in zip(before, after):
        if identity(old) != identity(current):
            raise ValueError("An existing benchmark photo changed during import")
    for selected, current in zip(added, after[len(before):]):
        if (selected.get("partition") != "challenge" or
                selected.get("sampleKind") != "challenge" or
                current.get("partition") != "challenge" or
                current.get("sampleKind") != "challenge"):
            raise ValueError("Imported photos must be challenge-only")
        digest = selected["contentSha256"].casefold()
        if digest in orig_hashes or digest in seen_new_hashes:
            raise ValueError("Added challenge repeats already selected original content")
        seen_new_hashes.add(digest)
        review = selected.get("review") or {}
        if any(review.get(x) is not None for x in
               ("qualityLevel", "personallyImportant", "notes")):
            raise ValueError("Selected challenge file may not contain human ratings")
        if identity(selected) != identity(current):
            raise ValueError("Imported challenge pixels/metadata differ from selected export")
    return before, after, added


def score_selected(original, expanded, selection, proxy_root, metric, device,
                   output, metric_factory=None):
    _before, _after, added = validate_expansion(original, expanded, selection)
    if metric not in CANDIDATES:
        raise ValueError("Unrecognized learned quality model")
    manifest = {
        "schemaVersion": 2,
        "benchmarkId": expanded["benchmarkId"],
        "proxyProfile": expanded["proxyProfile"],
        "photos": [
            {"revisionId": p["revisionId"], "proxyRelativePath": p["proxyRelativePath"],
             "proxySha256": p["proxySha256"]}
            for p in added
        ],
    }
    options = SimpleNamespace(input="<immutable selected challenge subset>",
                              output=output, proxy_root=proxy_root,
                              metric=metric, device=device, limit=None)
    return run_learned(options, metric_factory=metric_factory,
                       manifest_override=manifest, input_kind=SELECTED_INPUT_KIND)


def rows_for(export, benchmark_id, valid_ids, expected_kind):
    if (export.get("schemaVersion") != 1 or
            export.get("benchmarkId") != benchmark_id or
            export.get("inputKind") != expected_kind or
            not isinstance(export.get("candidate"), dict) or
            type(export.get("higherIsBetter")) is not bool or
            not isinstance(export.get("scores"), list) or
            export.get("photoCount") != len(valid_ids) or
            len(export["scores"]) != len(valid_ids)):
        raise ValueError("Score export schema, provenance, or coverage mismatch")
    result = {}
    for item in export["scores"]:
        key = item["revisionId"].casefold()
        score = item.get("rawScore")
        duration = item.get("inferenceMilliseconds")
        if key not in valid_ids or key in result:
            raise ValueError("Score export contains duplicate or foreign revisions")
        if (isinstance(duration, bool) or
                not isinstance(duration, (float, int)) or
                not math.isfinite(duration) or duration < 0):
            raise ValueError("Inference time must be finite and non-negative")
        if score is None:
            if not item.get("errorCode"):
                raise ValueError("Missing inference error code")
        elif (isinstance(score, bool) or
              not isinstance(score, (int, float)) or
              not math.isfinite(score) or item.get("errorCode")):
            raise ValueError("Invalid score or inconsistent error code")
        result[key] = item
    if set(result) != valid_ids:
        raise ValueError("Score export is missing required revisions")
    return result


def extend(original, expanded, selection, base_export, supplement, pool=None):
    before, after, added = validate_expansion(original, expanded, selection)
    bid = original["benchmarkId"]
    orig_ids = {p["revisionId"].casefold() for p in before}
    new_ids = {p["revisionId"].casefold() for p in added}
    original_rows = rows_for(base_export, bid, orig_ids, SCORE_INPUT_KIND)
    kind = supplement.get("inputKind")
    if kind == POOL_INPUT_KIND:
        if (pool is None or pool.get("schemaVersion") != 1 or
                pool.get("benchmarkId") != bid or
                pool.get("selectionPolicy") != POOL_POLICY):
            raise ValueError("Unaltered non-benchmark screening pool is required")
        pool_photos = pool.get("photos")
        if not isinstance(pool_photos, list) or not 2 <= len(pool_photos) <= 500:
            raise ValueError("Invalid original screening pool")
        pool_rows = {p["revisionId"].casefold(): p for p in pool_photos}
        if len(pool_rows) != len(pool_photos):
            raise ValueError("Screening pool contains duplicate revisions")
        if not new_ids.issubset(pool_rows):
            raise ValueError("Selected challenges are missing from screening pool")
        for p in added:
            source = pool_rows[p["revisionId"].casefold()]
            for name in ("contentSha256", "proxySha256", "proxyRelativePath"):
                if source.get(name) != p.get(name):
                    raise ValueError("Selected challenge differs from scored pool proxy")
        supplementary_rows = rows_for(
            supplement, bid, set(pool_rows), POOL_INPUT_KIND)
    elif kind == SELECTED_INPUT_KIND:
        if pool is not None:
            raise ValueError("A screening pool must not accompany selected-only scoring")
        supplementary_rows = rows_for(supplement, bid, new_ids, SELECTED_INPUT_KIND)
    else:
        raise ValueError("Supplement must be a scored screening pool or selected-only subset")

    if (base_export["candidate"] != supplement.get("candidate") or
            base_export["higherIsBetter"] is not supplement.get("higherIsBetter")):
        raise ValueError("Model version, weights descriptor, device, or score direction changed")

    combined = {**original_rows, **{key: supplementary_rows[key] for key in new_ids}}
    durations = [combined[p["revisionId"].casefold()]["inferenceMilliseconds"]
                 for p in after]
    result = deepcopy(base_export)
    result["generatedAtUtc"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    result["photoCount"] = len(after)
    result["scores"] = [deepcopy(combined[p["revisionId"].casefold()]) for p in after]
    result["runtime"] = {
        "totalElapsedMilliseconds": sum(durations),
        "medianInferenceMilliseconds": statistics.median(durations),
        "p95InferenceMilliseconds": p95(durations, 0.95),
    }
    result["reconciliation"] = {
        "protocol": PROTOCOL,
        "originalPhotoCount": len(before),
        "addedChallengeCount": len(added),
        "supplementInputKind": kind,
        "totalElapsedSemantics": "sum of measured per-photo durations, not end-to-end wall time",
    }
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("score-selected", "extend"):
        subparser = sub.add_parser(name)
        subparser.add_argument("--original-benchmark", required=True)
        subparser.add_argument("--benchmark", required=True, help="Expanded imported manifest")
        subparser.add_argument("--selected", required=True, help="20-photo import candidate file")
        subparser.add_argument("--output", required=True, help="New private output; never overwrite")
        if name == "score-selected":
            subparser.add_argument("--metric", choices=CANDIDATES, required=True)
            subparser.add_argument("--proxy-root", required=True)
            subparser.add_argument("--device", default="cpu")
            subparser.add_argument("--accept-third-party-weights", action="store_true")
        else:
            subparser.add_argument("--scores", required=True, help="Old complete 580-photo score export")
            subparser.add_argument("--supplement", required=True, help="Scored pool or selected subset")
            subparser.add_argument("--pool", help="Required when supplement is a scored full screening pool")
    args = parser.parse_args()
    original = load(args.original_benchmark)
    expanded = load(args.benchmark)
    selected = load(args.selected)
    if args.command == "score-selected":
        if not args.accept_third_party_weights:
            parser.error("Explicit --accept-third-party-weights required; pyIQA may download weights")
        export = score_selected(original, expanded, selected, args.proxy_root,
                                args.metric, args.device, args.output)
        print(f"Scored {export['photoCount']} imported private challenge proxies.")
    else:
        export = extend(original, expanded, selected, load(args.scores),
                        load(args.supplement), load(args.pool) if args.pool else None)
        output = private_output(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        with output.open("x", encoding="utf-8") as handle:
            json.dump(export, handle, indent=2, allow_nan=False)
        print(f"Reconciled {export['photoCount']} benchmark scores for "
              f"{export['candidate']['candidateId']}; existing exports left untouched.")
    print("Keep benchmark snapshots, per-photo scores and checkpoint files private.")


if __name__ == "__main__":
    main()
