#!/usr/bin/env python3
"""WI-0195 bounded research-only TOPIQ scores for REAL preview candidates.

Planning is entirely offline and does NOT import pyiqa, infer, open proxy JPEGs,
read human labels or authorize weights. Scoring is an explicit separate action.
Never touches originals, cloud hydration, catalogue or production selection.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import importlib
import importlib.metadata
import json
import math
from pathlib import Path
import statistics
import time

from collection_shadow_simulator import (
    FROZEN_CANDIDATE, PROTOCOL, read_scores, sha, validate_scenarios,
    private_path, load,
)
from learned_iqa import PROFILE, verified_proxy

MAP_PROTOCOL = "wi0195-private-candidate-proxies-v1"
BATCH_PROTOCOL = "wi0195-bounded-research-quality-scores-v1"
MAX_BATCH = 240
MAX_NEW = 120
MODEL_VERSION = "0.1.16"


def source_fingerprint(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def checked_inputs(scenario_path, proxy_path, baseline_path):
    scenarios = load(scenario_path)
    parsed = validate_scenarios(scenarios)
    if len(parsed) > 12:
        raise ValueError("At most 12 research scenarios in a batch")
    requested = {row["revisionId"] for _, _, _, rows in parsed for row in rows}
    if len(requested) > MAX_BATCH:
        raise ValueError("Research batch exceeds 240 distinct candidate photos")

    proxy_map = load(proxy_path)
    if (not isinstance(proxy_map, dict) or
            set(proxy_map) != {
                "schemaVersion", "protocol", "scenarioFileSha256",
                "proxyRoot", "proxyProfile", "photos", "catalogReadOnly",
                "noOriginalsHydrated", "inferencePerformed"} or
            proxy_map["protocol"] != MAP_PROTOCOL or
            proxy_map["schemaVersion"] != 1 or
            proxy_map["scenarioFileSha256"] != source_fingerprint(scenario_path) or
            proxy_map["proxyProfile"] != PROFILE or
            proxy_map["catalogReadOnly"] is not True or
            proxy_map["noOriginalsHydrated"] is not True or
            proxy_map["inferencePerformed"] is not False or
            not isinstance(proxy_map["photos"], list)):
        raise ValueError("Source-bound read-only proxy map is required")
    photos = {}
    root = Path(proxy_map["proxyRoot"]).resolve(strict=True)
    if not root.is_dir():
        raise ValueError("Configured review-proxy root is not a directory")
    for row in proxy_map["photos"]:
        if (not isinstance(row, dict) or set(row) !=
                {"revisionId", "relativePath", "sha256", "byteLength"}):
            raise ValueError("Unrecognized proxy entry")
        revision = row["revisionId"].casefold()
        if (revision in photos or not isinstance(row["relativePath"], str)
                or not isinstance(row["sha256"], str)
                or len(row["sha256"]) != 64
                or any(char not in "0123456789abcdef" for char in row["sha256"])
                or type(row["byteLength"]) is not int
                or not 1 <= row["byteLength"] <= 16*1024*1024):
            raise ValueError("Invalid or duplicated proxy")
        photos[revision] = row
    if set(photos) != requested:
        raise ValueError("Proxy set differs from captured collection candidates")

    baseline = load(baseline_path)
    old = read_scores(baseline)
    if (baseline["candidate"]["candidateId"] != FROZEN_CANDIDATE or
            baseline.get("schemaVersion") != 1 or
            baseline["higherIsBetter"] is not True):
        raise ValueError("Existing frozen TOPIQ version does not match")
    already = {
        revision: value for revision, value in old.items()
        if revision in requested and value is not None
    }
    missing = sorted(requested - set(already))
    return {
        "scenarios": scenarios, "map": proxy_map, "original": baseline,
        "existing": already, "missing": missing, "requested": requested,
        "root": root, "photos": photos,
        "sourceHash": source_fingerprint(scenario_path),
        "proxyHash": source_fingerprint(proxy_path),
        "originalHash": source_fingerprint(baseline_path),
        "scenarioCount": len(parsed),
    }


def summary(inputs):
    remaining = len(inputs["missing"])
    estimate_seconds = round(remaining * 4.423, 1)
    return {
        "protocol": BATCH_PROTOCOL,
        "status": "plan-only-no-inference",
        "scenarioCount": inputs["scenarioCount"],
        "distinctScenarioCandidates": len(inputs["requested"]),
        "reusedKnownTOPIQScores": len(inputs["existing"]),
        "additionalCandidatePhotosNeedingScores": remaining,
        "maximumNewPhotosPerAuthorizedBatch": MAX_NEW,
        "plannedBatchFitsHardCap": remaining <= MAX_NEW,
        "estimatedAdditionalCpuSecondsAtPriorMedian": estimate_seconds,
        "scoreGapUnchanged": 0.16755089163780212,
        "modelVersionUsedForPlan": FROZEN_CANDIDATE,
        "weightsAndLicenseNotApprovedForProduction": True,
        "inferencePerformed": False,
        "catalogueReadOnly": True,
        "originalsAccessed": False,
        "productionRankingApproved": False,
    }


def score(inputs, output, *, accept_weights=False, metric_factory=None):
    if not accept_weights:
        raise ValueError("Explicit research-only third-party weights permission is required")
    if output.exists():
        raise ValueError("Refusing to overwrite a private score export")
    if len(inputs["missing"]) > MAX_NEW:
        raise ValueError("Bounded new-score cap exceeded; use a smaller scenario cohort")
    # Verify ALL proxy hashes before loading any third-party model. Proxies with
    # reused scores must also be current; source map was read-only.
    paths = {
        revision: verified_proxy(
            inputs["root"], inputs["photos"][revision]["relativePath"],
            inputs["photos"][revision]["sha256"])
        for revision in sorted(inputs["requested"])
    }
    for revision, path in paths.items():
        if path.stat().st_size != inputs["photos"][revision]["byteLength"]:
            raise ValueError("Durable proxy byte length mismatch")

    if metric_factory is None:
        if importlib.metadata.version("pyiqa") != MODEL_VERSION:
            raise ValueError("Research pyiqa version changed from frozen 0.1.16")
        pyiqa = importlib.import_module("pyiqa")  # can load local third-party weights
        metric = pyiqa.create_metric("topiq_nr", device="cpu")
    else:
        metric = metric_factory()
    timer = time.perf_counter()
    timings = []
    scored = dict(inputs["existing"])
    failure = set()
    for revision in inputs["missing"]:
        start = time.perf_counter()
        try:
            value = float(metric(str(paths[revision])).item())
            if not math.isfinite(value):
                raise ArithmeticError("non-finite model score")
            scored[revision] = value
        except Exception:
            scored[revision] = None
            failure.add(revision)
        timings.append(round((time.perf_counter()-start)*1000, 3))

    export = {
        "schemaVersion": 1,
        "protocol": BATCH_PROTOCOL,
        "benchmarkId": "wi0195-private-scenario-" + inputs["sourceHash"][:24],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "inputKind": "review-proxy",
        "higherIsBetter": True,
        "candidate": dict(inputs["original"]["candidate"]),
        "sourceScenarioSha256": inputs["sourceHash"],
        "sourceProxyManifestSha256": inputs["proxyHash"],
        "reusedScoreExportSha256": inputs["originalHash"],
        "frozenRawScoreGap": 0.16755089163780212,
        "noHumanLabelsUsed": True,
        "productionWeightsLicenseApproved": False,
        "productionRankingApproved": False,
        "runtime": {
            "newlyInferred": len(inputs["missing"]),
            "success": len(inputs["missing"]) - len(failure),
            "failures": len(failure),
            "medianNewInferenceMilliseconds": statistics.median(timings) if timings else 0,
            "totalWallMilliseconds": round((time.perf_counter()-timer)*1000, 3),
        },
        "scores": [{
            "revisionId": revision,
            "rawScore": scored[revision],
            "inferenceMilliseconds": 0,
            "errorCode": "inference-failed" if revision in failure else None,
            "diagnostics": [],
        } for revision in sorted(inputs["requested"])],
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8") as handle:
        json.dump(export, handle, indent=2, allow_nan=False)
    return {
        "protocol": BATCH_PROTOCOL,
        "status": "bounded-private-research-scores-written",
        "distinctCandidatePhotos": len(inputs["requested"]),
        "reusedExistingScores": len(inputs["existing"]),
        "newlyInferred": len(inputs["missing"]),
        "successfulNewInferences": len(inputs["missing"]) - len(failure),
        "inferenceFailures": len(failure),
        "productionWeightsLicenseApproved": False,
        "productionRankingApproved": False,
        "noHumanLabelsUsed": True,
        "originalsAccessed": False,
        "catalogueReadOnly": True,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("plan", "score"))
    parser.add_argument("--scenarios", required=True)
    parser.add_argument("--proxies", required=True)
    parser.add_argument("--existing-scores", required=True)
    parser.add_argument("--output", help="Required ONLY for explicit score mode")
    parser.add_argument("--accept-research-only-third-party-weights", action="store_true")
    args = parser.parse_args()
    try:
        sources = [private_path(p, required=True) for p in
                   (args.scenarios, args.proxies, args.existing_scores)]
        if len(set(sources)) != 3:
            raise ValueError("Research input paths must be distinct")
        inputs = checked_inputs(*sources)
        if args.mode == "plan":
            if args.output or args.accept_research_only_third_party_weights:
                raise ValueError("Plan never accepts scoring output or permissions")
            result = summary(inputs)
        else:
            if not args.output:
                raise ValueError("Explicit private --output is required for scoring")
            output = private_path(args.output, required=False)
            if output in sources:
                raise ValueError("Score output must not replace an input")
            result = score(
                inputs, output,
                accept_weights=args.accept_research_only_third_party_weights)
    except (OSError, ValueError, TypeError, KeyError, ImportError) as err:
        # Do not print private paths/revisions; output error *type* only.
        raise SystemExit("WI-0195 bounded scoring rejected (" +
                         type(err).__name__ + ")") from None
    print(json.dumps(result, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
