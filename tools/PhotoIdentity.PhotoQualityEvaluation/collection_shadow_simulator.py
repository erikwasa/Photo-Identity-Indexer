#!/usr/bin/env python3
"""WI-0195 offline, read-only collection-selection SHADOW study.

Research-only frozen TOPIQ score-gap comparator; no absolute quality tiers,
Very Low classifier, production ranking, automatic redundancy suppression,
WI-0193 held-out human labels, originals, database or network access.

Private scenario JSON and score exports remain local. Stdout reports counts only.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path

PROTOCOL = "wi0195-collection-shadow-v1"
SCENARIO_PROTOCOL = "wi0195-collection-scenarios-v1"
BLIND_PROTOCOL = "wi0195-collection-blind-comparison-v1"
AUDIT_PROTOCOL = "wi0195-collection-shadow-audit-v1"
LABEL_PROTOCOL = "wi0195-collection-review-labels-v1"
FROZEN_CANDIDATE = "pyiqa-topiq_nr-0.1.16"
FROZEN_GAP = 0.16755089163780212
MAX_SCENARIOS = 40
MAX_CANDIDATES = 2000
MAX_REVIEW_VARIANTS = 40
SOURCE_KINDS = ("smart", "creative", "search")
CANDIDATE_KINDS = ("direct-anchor", "contextual-addition", "search")
REVIEW_CHOICES = ("left", "right", "same", "unsure")


def private_path(name, *, required):
    root = (Path.cwd() / "private").resolve()
    path = Path(name).resolve(strict=required)
    if not path.is_relative_to(root) or path == root:
        raise ValueError("All input/output files must remain under private/")
    if required and not path.is_file():
        raise ValueError("Missing private input")
    return path


def load(path):
    with path.open("r", encoding="utf-8") as src:
        return json.load(src)


def serialized(data):
    return (json.dumps(data, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=True, allow_nan=False)).encode("utf-8")


def sha(data):
    return hashlib.sha256(serialized(data)).hexdigest()


def _fields(value, expected, name):
    if not isinstance(value, dict) or set(value) != set(expected):
        raise ValueError("Invalid " + name + " contract")


def _id(value):
    if (not isinstance(value, str) or not 1 <= len(value) <= 128
            or value.strip() != value
            or any(c in value for c in "/\\\r\n:")):
        raise ValueError("Invalid private revision identifier")
    return value.casefold()


def read_scores(scores):
    # Reject any embedded labels, score-to-tier mappings, or output drift.
    if (not isinstance(scores, dict) or
            scores.get("schemaVersion") != 1 or
            scores.get("higherIsBetter") is not True or
            scores.get("inputKind") != "review-proxy" or
            not isinstance(scores.get("candidate"), dict) or
            scores["candidate"].get("candidateId") != FROZEN_CANDIDATE or
            not isinstance(scores.get("scores"), list) or
            len(scores["scores"]) > 10000):
        raise ValueError("Frozen TOPIQ research score provenance mismatch")
    by_id = {}
    for row in scores["scores"]:
        if (not isinstance(row, dict) or
                set(row) != {"revisionId", "rawScore", "inferenceMilliseconds",
                             "errorCode", "diagnostics"}):
            raise ValueError("Unexpected private score fields; no labels allowed")
        revision = _id(row["revisionId"])
        value = row["rawScore"]
        if (revision in by_id or
                (value is not None and
                 (type(value) not in (int, float) or not math.isfinite(value)))):
            raise ValueError("Invalid or duplicate score entry")
        by_id[revision] = value
    return by_id


def scenarios_from_creative_preview(preview, *, target):
    """Adapt exact read-only Creative preview 'selectedCandidates' response.

    Request the preview at a larger target than the shadow's smaller target
    to obtain a real candidate shortlist without changing the collection.
    """
    if (not isinstance(preview, dict) or
            not isinstance(preview.get("selectedCandidates"), list) or
            not isinstance(preview.get("collectionId"), str)):
        raise ValueError("Expected a captured Creative preview JSON")
    candidates = []
    for rank, row in enumerate(preview["selectedCandidates"]):
        if (not isinstance(row, dict) or
                row.get("kind") not in ("direct-anchor", "contextual-addition") or
                type(row.get("selectionScore")) is not int):
            raise ValueError("Unsupported Creative preview candidate row")
        candidates.append({
            "revisionId": row["revisionId"], "rank": rank,
            "relevanceScore": row["selectionScore"],
            "kind": row["kind"],
        })
    return {"kind": "creative",
            "scenarioId": preview["collectionId"],
            "targetCount": target,
            "candidates": candidates}


def validate_scenarios(data):
    _fields(data, ("schemaVersion", "protocol", "scenarios"), "scenario")
    if data["schemaVersion"] != 1 or data["protocol"] != SCENARIO_PROTOCOL:
        raise ValueError("Unsupported scenario input protocol")
    rows = data["scenarios"]
    if not isinstance(rows, list) or not 1 <= len(rows) <= MAX_SCENARIOS:
        raise ValueError("Scenario count outside bounds")
    seen_scenarios = set()
    parsed = []
    for case in rows:
        _fields(case, ("kind", "scenarioId", "targetCount", "candidates"),
                "scenario row")
        name = _id(case["scenarioId"])
        if name in seen_scenarios or case["kind"] not in SOURCE_KINDS:
            raise ValueError("Repeated scenario or invalid source kind")
        seen_scenarios.add(name)
        candidates = case["candidates"]
        target = case["targetCount"]
        if (not isinstance(candidates, list) or
                not 2 <= len(candidates) <= MAX_CANDIDATES or
                type(target) is not int or not 1 <= target < len(candidates)):
            raise ValueError("Shadow target must be smaller than captured pool")
        ids = set()
        normalized = []
        for row in candidates:
            _fields(row, ("revisionId", "rank", "relevanceScore", "kind"),
                    "candidate row")
            revision = _id(row["revisionId"])
            if (revision in ids or row["kind"] not in CANDIDATE_KINDS
                    or type(row["rank"]) is not int
                    or type(row["relevanceScore"]) is not int):
                raise ValueError("Invalid candidate row")
            ids.add(revision)
            normalized.append({
                "revisionId": revision, "rank": row["rank"],
                "relevanceScore": row["relevanceScore"], "kind": row["kind"],
            })
        if set(row["rank"] for row in normalized) != set(range(len(normalized))):
            raise ValueError("Scenario baseline ranks must be contiguous")
        normalized.sort(key=lambda row: row["rank"])
        # Preserve the exact captured baseline order (which may already be
        # chronological/paced). Quality may reorder only CONTIGUOUS runs
        # with identical original candidate-kind and selection score.
        parsed.append((case["kind"], name, target, normalized))
    return parsed


def quality_shadow(candidates, scores):
    """Swap only when both photos share the EXACT original relevance tier.

    No score gap -> abstain. Missing score -> abstain. Scores never affect
    eligibility or permit crossing relevance/kind boundaries.
    """
    reordered = []
    swaps = 0
    comparisons = 0
    missing_comparisons = 0
    undecided = 0
    groups = []
    for row in candidates:
        key = (row["kind"], row["relevanceScore"])
        if not groups or groups[-1][0] != key:
            groups.append((key, []))
        groups[-1][1].append(row)
    for _, group in groups:
        pool = list(group)
        while pool:
            first = pool[0]
            primary = scores.get(first["revisionId"])
            contender_index = 0
            for idx in range(1, len(pool)):
                challenger = scores.get(pool[idx]["revisionId"])
                if primary is None or challenger is None:
                    missing_comparisons += 1
                    continue
                comparisons += 1
                if challenger - primary >= FROZEN_GAP:
                    contender_index = idx
                    primary = challenger
                else:
                    undecided += 1
            if contender_index:
                swaps += 1
            reordered.append(pool.pop(contender_index))
    return reordered, {
        "qualityPromotions": swaps,
        "scoreComparisons": comparisons,
        "abstentions": undecided + missing_comparisons,
        "missingScoreComparisons": missing_comparisons,
    }


def simulate(scenarios, score_export):
    parsed = validate_scenarios(scenarios)
    scores = read_scores(score_export)
    case_rows = []
    for kind, scenario_id, target, candidates in parsed:
        ranking, metric = quality_shadow(candidates, scores)
        baseline = [x["revisionId"] for x in candidates[:target]]
        shadow = [x["revisionId"] for x in ranking[:target]]
        if len(shadow) != target or len(set(shadow)) != target:
            raise ValueError("Shadow cannot change target size or duplicate photos")
        matched = sum(x["revisionId"] in scores and
                      scores[x["revisionId"]] is not None for x in candidates)
        case_rows.append({
            "scenarioId": scenario_id,
            "kind": kind,
            "targetCount": target,
            "candidateCount": len(candidates),
            "scoreCoverage": matched,
            "qualityPromotionCount": metric["qualityPromotions"],
            "scoreComparisons": metric["scoreComparisons"],
            "abstentions": metric["abstentions"],
            "missingScoreComparisons": metric["missingScoreComparisons"],
            "baseline": baseline,
            "qualityShadow": shadow,
            "changedSelectionCount": len(set(baseline) ^ set(shadow)) // 2,
        })
    return case_rows


def build_outputs(scenarios, scores):
    rows = simulate(scenarios, scores)
    blind_cases, private_cases = [], []
    for row in rows:
        id_digest = sha([row["scenarioId"], row["kind"]])[:20]
        ident = "scene-" + id_digest
        # Alternating deterministic presentation prevents showing arm names.
        flip = int(sha(ident)[0], 16) % 2 == 0
        left = row["qualityShadow"] if flip else row["baseline"]
        right = row["baseline"] if flip else row["qualityShadow"]
        blind_cases.append({
            "comparisonId": ident,
            "leftRevisions": left,
            "rightRevisions": right,
        })
        private_cases.append({
            **row, "comparisonId": ident,
            "blindLeftArm": "quality-shadow" if flip else "baseline",
            "blindRightArm": "baseline" if flip else "quality-shadow",
        })
    blind = {
        "schemaVersion": 1, "protocol": BLIND_PROTOCOL,
        "status": "private-blind-shadow-comparison-only",
        "comparisons": blind_cases,
    }
    audit = {
        "schemaVersion": 1, "protocol": AUDIT_PROTOCOL,
        "status": "offline-collection-shadow-not-production",
        "blindSha256": sha(blind),
        "scenarioManifestSha256": sha(scenarios),
        "scoreManifestSha256": sha(scores),
        "frozenCandidateId": FROZEN_CANDIDATE,
        "frozenRawScoreGap": FROZEN_GAP,
        "relevanceConstraint": "same-candidate-kind-and-identical-selection-score-only",
        "arms": {
            "baseline": "existing-preview-order",
            "quality-shadow": "research-only-selective-pair-preference",
            "very-low-exclusion": "disabled-no-validated-absolute-tier",
            "redundancy-suppression": "disabled-WI0194-no-go",
            "combined": "disabled-no-approved-redundancy-policy",
        },
        "comparisons": private_cases,
        "productionChangesMade": False,
        "heldOutQualityLabelsAccessed": False,
        "automaticSuppressionAuthorized": False,
    }
    return blind, audit


def save_new_only(out_blind, out_audit, blind, audit):
    if out_blind == out_audit or out_blind.exists() or out_audit.exists():
        raise ValueError("Private outputs must be new and distinct")
    written = []
    try:
        for path, value in ((out_blind, blind), (out_audit, audit)):
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as file:
                file.write((json.dumps(value, indent=2, allow_nan=False) + "\n").encode())
            written.append(path)
    except OSError:
        for path in written:
            path.unlink(missing_ok=True)
        raise


def summarize(audit):
    comparisons = audit["comparisons"]
    return {
        "protocol": PROTOCOL,
        "status": audit["status"],
        "scenarioCount": len(comparisons),
        "sourceKinds": dict(Counter(x["kind"] for x in comparisons)),
        "totalCandidatePhotosAcrossScenarios": sum(x["candidateCount"] for x in comparisons),
        "scoredCandidateOccurrences": sum(x["scoreCoverage"] for x in comparisons),
        "targetPhotoSlots": sum(x["targetCount"] for x in comparisons),
        "changedSelectionSlots": sum(x["changedSelectionCount"] for x in comparisons),
        "qualityPromotionsWithinSameRelevanceTier": sum(x["qualityPromotionCount"] for x in comparisons),
        "scoreComparisons": sum(x["scoreComparisons"] for x in comparisons),
        "missingScoreComparisons": sum(x["missingScoreComparisons"] for x in comparisons),
        "arms": audit["arms"],
        "productionChangesMade": False,
        "heldOutQualityLabelsAccessed": False,
        "automaticSuppressionAuthorized": False,
        "limitations": (
            "Only already captured collection-preview candidates are considered. "
            "Relevance/kind ties are the ONLY place quality can promote photos; "
            "higher relevance, eligibility, result count and anchor/context precedence "
            "cannot change. Frozen TOPIQ p75 research pairwise ordering was not "
            "approved for production and does not assign absolute quality tiers. "
            "Missing score coverage causes abstention. No valid Very Low classifier "
            "or automatic redundancy suppression rule exists, so those arms are disabled. "
            "Source preview selections are not proven representative collection scenarios. "
            "No inference, human-label evaluation or originals were accessed."
        ),
    }


def review_summary(blind, audit, reviews):
    if (not isinstance(blind, dict) or blind.get("protocol") != BLIND_PROTOCOL
            or audit.get("protocol") != AUDIT_PROTOCOL
            or sha(blind) != audit.get("blindSha256")
            or not isinstance(reviews, dict)
            or set(reviews) != {"schemaVersion", "protocol", "blindSha256",
                               "reviews"}
            or reviews["protocol"] != LABEL_PROTOCOL
            or reviews["schemaVersion"] != 1
            or reviews["blindSha256"] != sha(blind)):
        raise ValueError("Private blind/audit/review mismatch")
    mapping = {row["comparisonId"]: row for row in audit["comparisons"]}
    if set(mapping) != {row["comparisonId"] for row in blind["comparisons"]}:
        raise ValueError("Comparison identities differ")
    votes = Counter()
    reviewed = set()
    for answer in reviews["reviews"]:
        _fields(answer, ("comparisonId", "choice"), "review")
        cid, choice = answer["comparisonId"], answer["choice"]
        if cid not in mapping or cid in reviewed or choice not in REVIEW_CHOICES:
            raise ValueError("Invalid blinded review choice")
        reviewed.add(cid)
        if choice in ("same", "unsure"):
            votes[choice] += 1
        else:
            votes[mapping[cid]["blindLeftArm"] if choice == "left" else
                  mapping[cid]["blindRightArm"]] += 1
    return {
        "protocol": "wi0195-shadow-preference-review-v1",
        "reviewed": len(reviewed),
        "remaining": len(mapping) - len(reviewed),
        "preferences": dict(votes),
        "productionPolicyApproved": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_subparsers(dest="action", required=True)
    prep = group.add_parser("prepare")
    prep.add_argument("--scenarios", required=True)
    prep.add_argument("--scores", required=True)
    prep.add_argument("--blind-output", required=True)
    prep.add_argument("--audit-output", required=True)
    importer = group.add_parser("import-creative-preview")
    importer.add_argument("--input", required=True)
    importer.add_argument("--target-count", type=int, required=True)
    importer.add_argument("--output", required=True)
    report = group.add_parser("report")
    report.add_argument("--blind", required=True)
    report.add_argument("--audit", required=True)
    args = parser.parse_args()
    try:
        if args.action == "import-creative-preview":
            src = private_path(args.input, required=True)
            dest = private_path(args.output, required=False)
            if src == dest or dest.exists():
                raise ValueError("Creative scenario output must be a new private file")
            captured = load(src)
            previews = captured if isinstance(captured, list) else [captured]
            if not 1 <= len(previews) <= MAX_SCENARIOS:
                raise ValueError("Invalid number of captured previews")
            scenarios = {"schemaVersion": 1, "protocol": SCENARIO_PROTOCOL,
                         "scenarios": [
                             scenarios_from_creative_preview(x, target=args.target_count)
                             for x in previews]}
            validate_scenarios(scenarios)
            dest.parent.mkdir(parents=True, exist_ok=True)
            with dest.open("xb") as file:
                file.write((json.dumps(scenarios, indent=2) + "\n").encode())
            print(json.dumps({
                "protocol": SCENARIO_PROTOCOL,
                "status": "private-readonly-creative-preview-import",
                "scenarioCount": len(scenarios["scenarios"]),
                "productionChangesMade": False,
            }, indent=2))
            return
        if args.action == "prepare":
            paths = [private_path(x, required=y)
                     for x, y in ((args.scenarios, True), (args.scores, True),
                                   (args.blind_output, False),
                                   (args.audit_output, False))]
            if len(set(paths)) != 4:
                raise ValueError("Source and output files must be distinct")
            s = load(paths[0])
            score = load(paths[1])
            blind, audit = build_outputs(s, score)
            save_new_only(paths[2], paths[3], blind, audit)
        else:
            blind = load(private_path(args.blind, required=True))
            audit = load(private_path(args.audit, required=True))
            if sha(blind) != audit.get("blindSha256"):
                raise ValueError("Private blind/audit hash mismatch")
        print(json.dumps(summarize(audit), indent=2, allow_nan=False))
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as err:
        raise SystemExit("WI-0195 shadow simulation failed (" +
                         type(err).__name__ + ")") from None


if __name__ == "__main__":
    main()
