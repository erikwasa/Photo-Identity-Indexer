#!/usr/bin/env python3
"""Attest the explicitly approved TOPIQ ranking research freeze, locally only.

Reads an existing private proposal, frozen benchmark, and complete score export.
Never accesses held-out/challenge HUMAN reviews, decodes photos or loads models.
Does NOT authorize held-out evaluation, deployment or automatic exclusion.
"""
from __future__ import annotations

import argparse
import json

from calibration_policy_draft import fingerprint
from candidate_audit import load
from learned_iqa import private_output
from ranking_freeze_proposal import CANDIDATE, propose

PROTOCOL = "wi0193-selective-ranking-research-freeze-v1"
THRESHOLD = 0.16755089163780212
QUANTILE = 0.75
PHOTOS = 238
PAIRS = 28203
APPROVED_DATE = "2026-10-18"
CRITERIA = {
    "minimumDifferentTierSelectedAccuracy": 0.80,
    "minimumAllPairDecisionCoverage": 0.20,
    "minimumTwoOrMoreTierSelectedAccuracy": 0.80,
    "requireNonzeroSelectedDifferentTierPairs": True,
    "requireNonzeroSelectedTwoOrMoreTierPairs": True,
    "representativeHeldOutOnly": True,
    "includeSameTierInAllPairCoverage": True,
    "reportSameTierSelections": True,
    "reportPhotoResampledUncertainty": True,
    "reportAllNumeratorsDenominatorsAbstentionsAndFailures": True,
    "heldOutTuningOrAlternativeCandidateAllowed": False,
}


def freeze(proposal, benchmark, scores):
    """Return a private attestation; refuse evidence drift or broader approval."""
    if not isinstance(proposal, dict):
        raise ValueError("Expected the checkpoint-15 private proposal")
    # Regenerates the *calibration-only* draft for complete score coverage and
    # validates all evidence fingerprints. propose() never consults held-out or
    # challenge human reviews, even when they happen to exist in the manifest.
    regenerated = propose(benchmark, scores)
    if proposal != regenerated:
        raise ValueError("Private proposal has drifted from frozen calibration evidence")
    hypothesis = proposal["hypothesis"]
    if (proposal["status"] != "review-proposal-not-frozen"
            or proposal["candidateId"] != CANDIDATE
            or proposal["inputKind"] != "jpeg-1600-q78-review-proxy"
            or proposal["scoreDirection"] != "higher-is-better"
            or hypothesis["type"] != "selective-pairwise-ordering-only"
            or hypothesis["quantile"] != QUANTILE
            or hypothesis["calibrationPhotos"] != PHOTOS
            or hypothesis["calibrationPairs"] != PAIRS
            or type(hypothesis["fixedScoreGapThreshold"]) is not float
            or hypothesis["fixedScoreGapThreshold"] != THRESHOLD):
        raise ValueError("Proposed model, input, cohort or exact threshold is not approved")
    for flag in (
            "researchHypothesisFrozen", "heldOutEvaluationAllowed",
            "heldOutLabelsAccessed", "productionModelApproved",
            "automaticVeryLowExclusionApproved",
            "checkpointLoadedIdentityVerified",
            "frameworkAndCheckpointLicenseApproved"):
        if proposal[flag] is not False:
            raise ValueError("Proposal must have all approval and access flags false")
    return {
        "schemaVersion": 1,
        "protocol": PROTOCOL,
        "status": "frozen-research-hypothesis-held-out-sealed",
        "approvedDate": APPROVED_DATE,
        "approvalBasis": "explicit-maintainer-approval-of-checkpoint-15-TOPIQ-p75",
        "benchmarkId": proposal["benchmarkId"],
        "benchmarkFingerprint": proposal["benchmarkFingerprint"],
        "calibrationReviewSha256": proposal["calibrationReviewSha256"],
        "scoreExportSha256": proposal["scoreExportSha256"],
        "proposalSha256": fingerprint(proposal),
        "candidateId": CANDIDATE,
        "inputKind": "jpeg-1600-q78-review-proxy",
        "scoreDirection": "higher-is-better",
        "scoreGapTrainingQuantile": QUANTILE,
        "calibrationPhotos": PHOTOS,
        "calibrationPairs": PAIRS,
        "fixedScoreGapThreshold": THRESHOLD,
        "decisionRule": (
            "For two distinct photo scores, prefer the higher score iff "
            "abs(scoreA-scoreB) >= fixedScoreGapThreshold and the gap is "
            "positive; otherwise abstain. Human labels are not decision inputs."
        ),
        "researchCriteria": dict(CRITERIA),
        "researchHypothesisFrozen": True,
        "heldOutEvaluationAllowed": False,
        "heldOutLabelsAccessed": False,
        "productionModelApproved": False,
        "automaticVeryLowExclusionApproved": False,
        "checkpointLoadedIdentityVerified": False,
        "frameworkAndCheckpointLicenseApproved": False,
        "warning": (
            "Research freeze only. The held-out evaluator must be separately "
            "implemented, reviewed and explicitly authorized before reading "
            "held-out human labels. This never licenses model deployment."
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--proposal", required=True)
    parser.add_argument("--input", required=True)
    parser.add_argument("--scores", required=True)
    parser.add_argument("--output", required=True, help="New private path, never overwrite")
    args = parser.parse_args()
    result = freeze(load(args.proposal), load(args.input), load(args.scores))
    destination = private_output(args.output)
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("x", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2, allow_nan=False)
        handle.write("\n")
    # No benchmark IDs, private hashes, human labels, paths or per-photo scores.
    print(json.dumps({
        "protocol": result["protocol"],
        "status": result["status"],
        "candidate": result["candidateId"],
        "fixedScoreGapThreshold": result["fixedScoreGapThreshold"],
        "researchCriteria": result["researchCriteria"],
        "researchHypothesisFrozen": result["researchHypothesisFrozen"],
        "heldOutEvaluationAllowed": result["heldOutEvaluationAllowed"],
        "heldOutLabelsAccessed": result["heldOutLabelsAccessed"],
        "productionModelApproved": result["productionModelApproved"],
        "automaticVeryLowExclusionApproved":
            result["automaticVeryLowExclusionApproved"],
    }, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
