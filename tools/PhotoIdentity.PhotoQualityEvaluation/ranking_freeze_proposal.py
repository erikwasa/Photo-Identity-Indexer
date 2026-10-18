#!/usr/bin/env python3
"""Prepare ONE calibration-derived, review-only ranking hypothesis.

This computes a fixed TOPIQ NR 75th-percentile score-gap cutoff from
the existing representative CALIBRATION proxy scores. The proposal is
NOT an approved freeze and MUST NOT read held-out/challenge human labels
or authorize a held-out run, auto exclusion, model deployment or ranking.

No image/model inference, network access, or score/review mutation.
"""
from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import math
from pathlib import Path

from calibration_policy_draft import fingerprint
from calibration_selective_ranking import INPUT_PROFILE, threshold_for
from candidate_audit import LEVELS, audit, load
from learned_iqa import private_output

PROTOCOL = "wi0193-selective-ranking-freeze-proposal-v1"
CANDIDATE = "pyiqa-topiq_nr-0.1.16"
MODEL = "topiq_nr"
QUANTILE = 0.75
CALIBRATION_RATED_COUNT = 238
EXPECTED_BENCHMARK_SIZE = 600


def propose(benchmark, export):
    if (benchmark.get("schemaVersion") != 2 or
            not isinstance(benchmark.get("photos"), list) or
            len(benchmark["photos"]) != EXPECTED_BENCHMARK_SIZE or
            INPUT_PROFILE not in benchmark.get("proxyProfile", "").splitlines()):
        raise ValueError("Expected the frozen 600-photo jpeg-1600-q78 benchmark")
    if (export.get("inputKind") != "review-proxy"
            or export.get("candidate", {}).get("candidateId") != CANDIDATE
            or export["candidate"].get("modelName") != MODEL
            or export.get("higherIsBetter") is not True):
        raise ValueError("Expected complete TOPIQ NR 0.1.16 review-proxy scores")
    # Audit validates complete candidate coverage and human labels only on
    # calibration. This proposal never reads held-out or challenge ratings.
    summary = audit(benchmark, [export])
    if summary["candidates"][0]["fullBenchmarkInferenceFailures"] != 0:
        raise ValueError("Score export contains inference failures")
    eligible = [
        p for p in benchmark["photos"]
        if p.get("partition") == "calibration"
        and p.get("sampleKind") == "representative"
        and p.get("review", {}).get("qualityLevel") in LEVELS
    ]
    if len(eligible) != CALIBRATION_RATED_COUNT:
        raise ValueError("Calibration set no longer has 238 reviewed photos")
    ids = [p["revisionId"].casefold() for p in eligible]
    mapping = {
        row["revisionId"].casefold(): row["rawScore"]
        for row in export["scores"]
    }
    values = [mapping[key] for key in ids]
    if any(type(value) not in (float, int) or not math.isfinite(value)
           for value in values):
        raise ValueError("Incomplete or nonfinite calibration scores")
    gaps = [
        abs(values[i] - values[j])
        for i in range(len(values))
        for j in range(i + 1, len(values))
    ]
    cutoff = threshold_for(gaps, QUANTILE)
    if cutoff <= 0 or not math.isfinite(cutoff):
        raise ValueError("Degenerate score-gap distribution")
    # Training on calibration only, NOT cross-validation or held-out.
    return {
        "schemaVersion": 1,
        "protocol": PROTOCOL,
        "status": "review-proposal-not-frozen",
        "benchmarkId": benchmark["benchmarkId"],
        "benchmarkFingerprint": fingerprint({
            "benchmarkId": benchmark["benchmarkId"],
            "representativeCalibrationRevisions": sorted(
                p["revisionId"] for p in benchmark["photos"]
                if p.get("partition") == "calibration"
                and p.get("sampleKind") == "representative"),
        }),
        "calibrationReviewSha256": fingerprint([
            {"revisionId": p["revisionId"],
             "qualityLevel": p.get("review", {}).get("qualityLevel")}
            for p in benchmark["photos"]
            if p.get("partition") == "calibration"
            and p.get("sampleKind") == "representative"
        ]),
        "scoreExportSha256": fingerprint(export),
        "candidateId": CANDIDATE,
        "inputKind": "jpeg-1600-q78-review-proxy",
        "scoreDirection": "higher-is-better",
        "hypothesis": {
            "type": "selective-pairwise-ordering-only",
            "quantile": QUANTILE,
            "trainingGapPopulation": (
                "absolute score differences over ALL unordered pairs "
                "of 238 reviewed representative calibration photos"),
            "calibrationPhotos": len(eligible),
            "calibrationPairs": len(gaps),
            "fixedScoreGapThreshold": cutoff,
            "decisionRule": (
                "On a pair of scored photos, prefer higher score only "
                "when abs(scoreA-scoreB) >= fixed threshold AND gap > 0; "
                "otherwise abstain. Human quality labels are never "
                "part of the preference rule."),
            "exploratoryOrigin": (
                "Candidate and 75th-percentile gap were chosen after "
                "inspecting multi-candidate calibration results; "
                "the earlier fold test is exploratory, not independent "
                "proof of threshold performance."),
            "evaluationPrimaryEndpoint": (
                "Among pairs with different blinded human quality tiers "
                "and meeting the fixed score-gap rule: correct ordering"),
            "evaluationSecondaryEndpoints": [
                "All-pair decision coverage",
                "Different-tier conditional comparison coverage",
                "Ordering and coverage among pairs separated by >=2 human tiers",
                "Uncertainty resampled by photo, NOT independent pairs",
                "Detailed abstention and equal-tier pair accounting",
            ],
            "evaluationInterpretation": (
                "Held-out success is descriptive research-only and cannot "
                "license/deploy models; relative preference does not equal "
                "absolute per-photo quality or exclusion."),
        },
        "checkpointLoadedIdentityVerified": False,
        "frameworkAndCheckpointLicenseApproved": False,
        "heldOutLabelsAccessed": False,
        "researchHypothesisFrozen": False,
        "heldOutEvaluationAllowed": False,
        "productionModelApproved": False,
        "automaticVeryLowExclusionApproved": False,
        "notes": (
            "Private review proposal only. It is not a policy freeze "
            "or permission to open held-out ratings. Requires explicit "
            "operator review, preserved evidence hashes, separate "
            "freeze gate, and deployment rights review."),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True)
    parser.add_argument("--scores", required=True)
    parser.add_argument("--output", required=True,
                        help="New private JSON output; no overwriting")
    args = parser.parse_args()
    result = propose(load(args.input), load(args.scores))
    dest = private_output(args.output)
    dest.parent.mkdir(parents=True, exist_ok=True)
    with dest.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, allow_nan=False)
    print(json.dumps({
        "protocol": result["protocol"],
        "status": result["status"],
        "candidate": result["candidateId"],
        "scoreGapQuantile": result["hypothesis"]["quantile"],
        "calibrationPhotos": result["hypothesis"]["calibrationPhotos"],
        "calibrationPairs": result["hypothesis"]["calibrationPairs"],
        "fixedScoreGapThreshold": result["hypothesis"]["fixedScoreGapThreshold"],
        "researchHypothesisFrozen": result["researchHypothesisFrozen"],
        "heldOutEvaluationAllowed": result["heldOutEvaluationAllowed"],
        "productionModelApproved": result["productionModelApproved"],
        "automaticVeryLowExclusionApproved":
            result["automaticVeryLowExclusionApproved"],
    }, indent=2, allow_nan=False))
    print("Private ranking hypothesis PROPOSAL created; held-out ratings remain sealed.")


if __name__ == "__main__":
    main()
