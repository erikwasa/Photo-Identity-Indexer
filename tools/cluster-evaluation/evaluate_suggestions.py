#!/usr/bin/env python3
"""Private WI-0081 identity-suggestion accuracy evaluator.

Consumes the pseudonymized exact-model export produced by PhotoIdentity.ClusterEvaluation.
The input and generated reports contain biometric-derived data and must remain private.
No production suggestion rows, thresholds, assignments or review history are changed.
"""

from __future__ import annotations

import argparse
import json
import math
from collections import defaultdict
from datetime import datetime
from pathlib import Path
from statistics import median
from typing import Any, Iterable

import numpy as np

DEFAULT_HIGH_SCORE = 0.70
DEFAULT_HIGH_MARGIN = 0.10
DEFAULT_MEDIUM_SCORE = 0.50


def load_sample(path: Path) -> tuple[dict[str, Any], list[dict[str, Any]], np.ndarray]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if payload.get("schemaVersion") != 1:
        raise ValueError("unsupported cluster-evaluation export schema")
    faces = payload.get("faces") or []
    if len(faces) < 3:
        raise ValueError("at least three exported reviewed faces are required")

    embeddings = np.asarray([face["embedding"] for face in faces], dtype=np.float64)
    if embeddings.ndim != 2 or embeddings.shape[1] == 0 or not np.isfinite(embeddings).all():
        raise ValueError("embeddings must be a finite rectangular matrix")
    norms = np.linalg.norm(embeddings, axis=1)
    if np.any(norms <= 0):
        raise ValueError("embeddings contain a zero vector")
    embeddings = embeddings / norms[:, None]
    return payload, faces, embeddings


def policy_from(payload: dict[str, Any]) -> dict[str, Any]:
    raw = payload.get("suggestionPolicy") or {}
    return {
        "version": raw.get("version"),
        "autoAssignEnabled": raw.get("autoAssignEnabled"),
        "highScoreThreshold": float(raw.get("highScoreThreshold", DEFAULT_HIGH_SCORE)),
        "highMarginThreshold": float(raw.get("highMarginThreshold", DEFAULT_HIGH_MARGIN)),
        "mediumScoreThreshold": float(raw.get("mediumScoreThreshold", DEFAULT_MEDIUM_SCORE)),
        "updatedBy": raw.get("updatedBy"),
        "updatedAtUtc": raw.get("updatedAtUtc"),
    }


def quality_key(face: dict[str, Any], index: int) -> tuple[float, float, int]:
    confidence = face.get("detectorConfidence")
    area = face.get("faceAreaFraction")
    return (
        float(confidence) if confidence is not None else -1.0,
        float(area) if area is not None else -1.0,
        -index,
    )


def select_quality_diverse(
    indices: list[int],
    cap: int,
    faces: list[dict[str, Any]],
    embeddings: np.ndarray,
) -> list[int]:
    if len(indices) <= cap:
        return list(indices)
    if cap < 1:
        return []

    remaining = set(indices)
    first = max(indices, key=lambda index: quality_key(faces[index], index))
    selected = [first]
    remaining.remove(first)

    while remaining and len(selected) < cap:
        best_index = None
        best_key: tuple[float, float, float, int] | None = None
        selected_matrix = embeddings[selected]
        for index in sorted(remaining):
            max_similarity = float(np.max(selected_matrix @ embeddings[index]))
            confidence, area, neg_index = quality_key(faces[index], index)
            key = (-max_similarity, confidence, area, neg_index)
            if best_key is None or key > best_key:
                best_key = key
                best_index = index
        assert best_index is not None
        selected.append(best_index)
        remaining.remove(best_index)
    return selected


def reference_indices(
    target: int,
    indices: Iterable[int],
    content_groups: list[str],
    duplicate_resistant: bool,
) -> list[int]:
    target_content = content_groups[target]
    result = []
    for index in indices:
        if index == target:
            continue
        if duplicate_resistant and content_groups[index] == target_content:
            continue
        result.append(index)
    return result


def rank_target(
    target: int,
    strategy: str,
    duplicate_resistant: bool,
    cap: int,
    faces: list[dict[str, Any]],
    embeddings: np.ndarray,
    content_groups: list[str],
    labelled_by_person: dict[str, list[int]],
) -> list[tuple[str, float]]:
    target_vector = embeddings[target]
    candidates: list[tuple[str, float]] = []

    for person, person_indices in labelled_by_person.items():
        usable = reference_indices(target, person_indices, content_groups, duplicate_resistant)
        if not usable:
            continue

        if strategy == "max-exemplar":
            score = float(np.max(embeddings[usable] @ target_vector))
        elif strategy == "centroid":
            centroid = np.mean(embeddings[usable], axis=0)
            norm = float(np.linalg.norm(centroid))
            if norm <= 0 or not math.isfinite(norm):
                continue
            score = float((centroid / norm) @ target_vector)
        elif strategy == "quality-diverse-cap":
            curated = select_quality_diverse(usable, cap, faces, embeddings)
            if not curated:
                continue
            score = float(np.max(embeddings[curated] @ target_vector))
        else:
            raise ValueError(f"unknown strategy: {strategy}")

        if math.isfinite(score):
            candidates.append((person, score))

    candidates.sort(key=lambda item: (-item[1], item[0]))
    return candidates


def classify(score: float, margin: float | None, policy: dict[str, Any]) -> str:
    if (
        score >= policy["highScoreThreshold"]
        and margin is not None
        and margin >= policy["highMarginThreshold"]
    ):
        return "high"
    if score >= policy["mediumScoreThreshold"]:
        return "medium"
    return "low"


def evaluate_scenario(
    name: str,
    strategy: str,
    duplicate_resistant: bool,
    cap: int,
    faces: list[dict[str, Any]],
    embeddings: np.ndarray,
    truth: list[str | None],
    content_groups: list[str],
    labelled_by_person: dict[str, list[int]],
    policy: dict[str, Any],
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    records: list[dict[str, Any]] = []
    known_total = known_evaluable = known_no_reference = 0
    top1 = top3 = top5 = 0
    unknown_total = unknown_ranked = 0
    high_total = high_correct = 0
    medium_or_higher_total = medium_or_higher_correct = 0
    known_high = known_high_correct = 0
    unknown_high = unknown_medium_or_higher = 0

    for target, label in enumerate(truth):
        true_reference_count = None
        if label is None:
            unknown_total += 1
        else:
            known_total += 1
            true_reference_count = len(reference_indices(
                target,
                labelled_by_person[label],
                content_groups,
                duplicate_resistant,
            ))
            if true_reference_count == 0:
                known_no_reference += 1
                continue

        ranked = rank_target(
            target,
            strategy,
            duplicate_resistant,
            cap,
            faces,
            embeddings,
            content_groups,
            labelled_by_person,
        )
        if not ranked:
            continue

        top_person, top_score = ranked[0]
        second_score = ranked[1][1] if len(ranked) > 1 else None
        margin = top_score - second_score if second_score is not None else None
        confidence_group = classify(top_score, margin, policy)
        correct = label is not None and top_person == label

        true_rank = None
        if label is not None:
            known_evaluable += 1
            for rank, (person, _) in enumerate(ranked, start=1):
                if person == label:
                    true_rank = rank
                    break
            if true_rank == 1:
                top1 += 1
            if true_rank is not None and true_rank <= 3:
                top3 += 1
            if true_rank is not None and true_rank <= 5:
                top5 += 1
        else:
            unknown_ranked += 1

        if confidence_group == "high":
            high_total += 1
            if correct:
                high_correct += 1
            if label is not None:
                known_high += 1
                if correct:
                    known_high_correct += 1
            else:
                unknown_high += 1
        if confidence_group in {"high", "medium"}:
            medium_or_higher_total += 1
            if correct:
                medium_or_higher_correct += 1
            if label is None:
                unknown_medium_or_higher += 1

        records.append({
            "target": target,
            "truth": label,
            "topPerson": top_person,
            "topScore": top_score,
            "margin": margin,
            "confidenceGroup": confidence_group,
            "correct": correct,
            "trueRank": true_rank,
            "trueReferenceCount": true_reference_count,
        })

    metrics = {
        "name": name,
        "strategy": strategy,
        "duplicateResistantHoldout": duplicate_resistant,
        "referenceCapPerPerson": cap if strategy == "quality-diverse-cap" else None,
        "knownTargets": known_total,
        "knownEvaluable": known_evaluable,
        "knownWithoutUsableReference": known_no_reference,
        "top1Accuracy": top1 / known_evaluable if known_evaluable else 0.0,
        "top3Accuracy": top3 / known_evaluable if known_evaluable else 0.0,
        "top5Accuracy": top5 / known_evaluable if known_evaluable else 0.0,
        "highSuggestionCount": high_total,
        "highSuggestionPrecision": high_correct / high_total if high_total else 0.0,
        "knownHighCoverage": known_high / known_evaluable if known_evaluable else 0.0,
        "knownHighPrecision": known_high_correct / known_high if known_high else 0.0,
        "mediumOrHigherSuggestionCount": medium_or_higher_total,
        "mediumOrHigherPrecision": (
            medium_or_higher_correct / medium_or_higher_total if medium_or_higher_total else 0.0
        ),
        "unknownTargets": unknown_total,
        "unknownRanked": unknown_ranked,
        "unknownHighEmissionRate": unknown_high / unknown_ranked if unknown_ranked else 0.0,
        "unknownMediumOrHigherEmissionRate": (
            unknown_medium_or_higher / unknown_ranked if unknown_ranked else 0.0
        ),
    }
    return metrics, records


def summarize_records(records: list[dict[str, Any]], indices: list[int]) -> dict[str, Any]:
    chosen = [record for record in records if record["target"] in indices and record["truth"] is not None]
    if not chosen:
        return {"count": 0, "top1Accuracy": None, "highCount": 0, "highPrecision": None}
    high = [record for record in chosen if record["confidenceGroup"] == "high"]
    margins = [record["margin"] for record in chosen if record["margin"] is not None]
    return {
        "count": len(chosen),
        "top1Accuracy": sum(1 for record in chosen if record["correct"]) / len(chosen),
        "highCount": len(high),
        "highPrecision": (
            sum(1 for record in high if record["correct"]) / len(high) if high else None
        ),
        "medianTopScore": median(record["topScore"] for record in chosen),
        "medianMargin": median(margins) if margins else None,
    }


def bucket_numeric(
    values: list[float | None],
    definitions: list[tuple[str, float | None, float | None]],
) -> dict[str, list[int]]:
    buckets: dict[str, list[int]] = {name: [] for name, _, _ in definitions}
    buckets["missing"] = []
    for index, value in enumerate(values):
        if value is None or not math.isfinite(float(value)):
            buckets["missing"].append(index)
            continue
        numeric = float(value)
        placed = False
        for name, low, high in definitions:
            if (low is None or numeric >= low) and (high is None or numeric < high):
                buckets[name].append(index)
                placed = True
                break
        if not placed:
            buckets["missing"].append(index)
    return buckets


def chronology_buckets(faces: list[dict[str, Any]], truth: list[str | None]) -> dict[str, list[int]]:
    dated: list[tuple[datetime, int]] = []
    for index, face in enumerate(faces):
        if truth[index] is None or not face.get("reviewedAtUtc"):
            continue
        value = str(face["reviewedAtUtc"]).replace("Z", "+00:00")
        try:
            dated.append((datetime.fromisoformat(value), index))
        except ValueError:
            continue
    dated.sort(key=lambda item: (item[0], item[1]))
    if len(dated) < 4:
        return {"insufficient-date-data": [index for _, index in dated]}
    result = {"oldest-quartile": [], "middle-old": [], "middle-new": [], "newest-quartile": []}
    names = list(result)
    for position, (_, index) in enumerate(dated):
        bucket = min(3, (position * 4) // len(dated))
        result[names[bucket]].append(index)
    return result


def contamination_audit(
    faces: list[dict[str, Any]],
    truth: list[str | None],
    content_groups: list[str],
    labelled_by_person: dict[str, list[int]],
) -> dict[str, Any]:
    by_content: dict[str, list[int]] = defaultdict(list)
    for index, group in enumerate(content_groups):
        by_content[group].append(index)

    duplicate_groups = [indices for indices in by_content.values() if len(indices) > 1]
    cross_label = 0
    mixed_review_state = 0
    for indices in duplicate_groups:
        labels = {truth[index] for index in indices if truth[index] is not None}
        has_unknown = any(truth[index] is None for index in indices)
        if len(labels) > 1:
            cross_label += 1
        if labels and has_unknown:
            mixed_review_state += 1

    sizes = sorted(len(indices) for indices in labelled_by_person.values())
    assigned_count = sum(sizes)
    largest = max(sizes) if sizes else 0
    top_tenth_count = max(1, math.ceil(len(sizes) * 0.10)) if sizes else 0
    concentrated = sum(sorted(sizes, reverse=True)[:top_tenth_count]) if sizes else 0

    return {
        "contentGroups": len(by_content),
        "duplicateContentGroups": len(duplicate_groups),
        "facesInDuplicateContentGroups": sum(len(indices) for indices in duplicate_groups),
        "crossLabelDuplicateContentGroups": cross_label,
        "assignedUnknownMixedDuplicateGroups": mixed_review_state,
        "reviewedFacesWhoseOriginalPersonWasMerged": sum(
            1 for face in faces if face.get("reviewedPersonWasMerged")
        ),
        "identityCount": len(sizes),
        "referenceCountMedian": median(sizes) if sizes else 0,
        "referenceCountMaximum": largest,
        "topTenPercentIdentityReferenceShare": concentrated / assigned_count if assigned_count else 0.0,
    }


def segmentation(
    faces: list[dict[str, Any]],
    truth: list[str | None],
    baseline_records: list[dict[str, Any]],
) -> dict[str, Any]:
    confidences = [face.get("detectorConfidence") for face in faces]
    areas = [face.get("faceAreaFraction") for face in faces]

    confidence_buckets = bucket_numeric(confidences, [
        ("lt-0.70", None, 0.70),
        ("0.70-0.85", 0.70, 0.85),
        ("0.85-0.95", 0.85, 0.95),
        ("gte-0.95", 0.95, None),
    ])
    area_buckets = bucket_numeric(areas, [
        ("lt-0.5pct", None, 0.005),
        ("0.5-2pct", 0.005, 0.02),
        ("2-8pct", 0.02, 0.08),
        ("gte-8pct", 0.08, None),
    ])

    reference_buckets: dict[str, list[int]] = defaultdict(list)
    record_by_target = {record["target"]: record for record in baseline_records}
    for target, label in enumerate(truth):
        if label is None:
            continue
        count = (record_by_target.get(target) or {}).get("trueReferenceCount")
        if count is None:
            reference_buckets["no-usable-holdout-reference"].append(target)
        elif count == 1:
            reference_buckets["1"].append(target)
        elif count <= 4:
            reference_buckets["2-4"].append(target)
        elif count <= 9:
            reference_buckets["5-9"].append(target)
        else:
            reference_buckets["10-plus"].append(target)

    return {
        "detectorConfidence": {
            name: summarize_records(baseline_records, indices)
            for name, indices in confidence_buckets.items()
        },
        "faceAreaFraction": {
            name: summarize_records(baseline_records, indices)
            for name, indices in area_buckets.items()
        },
        "trueIdentityReferenceCount": {
            name: summarize_records(baseline_records, indices)
            for name, indices in sorted(reference_buckets.items())
        },
        "reviewChronology": {
            name: summarize_records(baseline_records, indices)
            for name, indices in chronology_buckets(faces, truth).items()
        },
    }


def delta(candidate: dict[str, Any], baseline: dict[str, Any], key: str) -> float:
    return float(candidate[key]) - float(baseline[key])


def evaluate(args: argparse.Namespace) -> dict[str, Any]:
    payload, faces, embeddings = load_sample(args.sample)
    policy = policy_from(payload)
    truth = [face.get("groundTruthLabel") for face in faces]
    content_groups = [
        str(face.get("contentGroup") or face.get("photoGroup") or f"row-{index}")
        for index, face in enumerate(faces)
    ]

    labelled_by_person: dict[str, list[int]] = defaultdict(list)
    for index, label in enumerate(truth):
        if label is not None:
            labelled_by_person[str(label)].append(index)
    if len(labelled_by_person) < 2:
        raise ValueError("at least two reviewed identities are required for suggestion evaluation")

    scenarios = [
        ("production-equivalent-max", "max-exemplar", False),
        ("duplicate-resistant-max", "max-exemplar", True),
        ("duplicate-resistant-centroid", "centroid", True),
        ("duplicate-resistant-quality-diverse-cap", "quality-diverse-cap", True),
    ]
    metrics: dict[str, dict[str, Any]] = {}
    records: dict[str, list[dict[str, Any]]] = {}
    for name, strategy, duplicate_resistant in scenarios:
        scenario_metrics, scenario_records = evaluate_scenario(
            name,
            strategy,
            duplicate_resistant,
            args.reference_cap,
            faces,
            embeddings,
            truth,
            content_groups,
            labelled_by_person,
            policy,
        )
        metrics[name] = scenario_metrics
        records[name] = scenario_records

    baseline = metrics["duplicate-resistant-max"]
    centroid = metrics["duplicate-resistant-centroid"]
    capped = metrics["duplicate-resistant-quality-diverse-cap"]

    return {
        "schemaVersion": 1,
        "sample": {
            "modelId": payload.get("modelId"),
            "modelHash": payload.get("modelHash"),
            "faceCount": payload.get("faceCount", len(faces)),
            "assignedLabelCount": payload.get("assignedLabelCount", len(labelled_by_person)),
            "unknownFaceCount": payload.get("unknownFaceCount", sum(1 for label in truth if label is None)),
            "sampleSelection": payload.get("sampleSelection"),
        },
        "productionSuggestionPolicy": policy,
        "contaminationAudit": contamination_audit(faces, truth, content_groups, labelled_by_person),
        "scenarios": metrics,
        "baselineSegmentation": segmentation(
            faces,
            truth,
            records["duplicate-resistant-max"],
        ),
        "mitigationComparison": {
            "baseline": "duplicate-resistant-max",
            "centroid": {
                "top1AccuracyDelta": delta(centroid, baseline, "top1Accuracy"),
                "highSuggestionPrecisionDelta": delta(centroid, baseline, "highSuggestionPrecision"),
                "knownHighCoverageDelta": delta(centroid, baseline, "knownHighCoverage"),
                "unknownHighEmissionRateDelta": delta(centroid, baseline, "unknownHighEmissionRate"),
            },
            "qualityDiverseCap": {
                "referenceCapPerPerson": args.reference_cap,
                "top1AccuracyDelta": delta(capped, baseline, "top1Accuracy"),
                "highSuggestionPrecisionDelta": delta(capped, baseline, "highSuggestionPrecision"),
                "knownHighCoverageDelta": delta(capped, baseline, "knownHighCoverage"),
                "unknownHighEmissionRateDelta": delta(capped, baseline, "unknownHighEmissionRate"),
            },
        },
        "notes": [
            "Production-equivalent max-exemplar reproduces the current best-exemplar-per-person ranking rule with only the target face removed.",
            "Duplicate-resistant scenarios also remove every reference from the target's exact-content group so duplicate copies cannot make holdout accuracy look better than it is.",
            "Known targets without another usable reference for their true Person are reported separately and excluded from top-k accuracy denominators.",
            "Unknown reviewed faces have no person ground truth; High/Medium emission rates on them are conservative false-positive-risk indicators, not proof that every emitted identity is wrong.",
            "Detector confidence and normalized face area are queue-composition proxies, not direct measures of embedding quality.",
            "Review chronology quartiles help distinguish an actual matcher regression from later review queues containing harder faces.",
            "Centroid and capped quality-diverse references are offline comparison families only. This evaluator does not change production matching semantics.",
            "Run the same workflow separately for each embedding model hash if multiple model revisions need comparison.",
            "The export remains bounded and ordered by face occurrence id; if maximumFaces truncates the reviewed catalogue, record that selection caveat when interpreting results.",
        ],
    }


def pct(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.3%}"


def render_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Private WI-0081 suggestion-accuracy evaluation",
        "",
        "> PRIVATE: derived from local biometric/review data. Do not commit this report.",
        "",
        f"- Exact model: `{report['sample']['modelId']}` / `{report['sample']['modelHash']}`",
        f"- Faces: {report['sample']['faceCount']}",
        f"- Reviewed identities: {report['sample']['assignedLabelCount']}",
        f"- Reviewed Unknown faces: {report['sample']['unknownFaceCount']}",
        "",
        "## Ranking scenarios",
        "",
        "| Scenario | Evaluable known | No holdout ref | Top-1 | Top-3 | Top-5 | High precision | High coverage | Unknown High emission |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for scenario in report["scenarios"].values():
        lines.append(
            "| {name} | {evaluable} | {missing} | {top1} | {top3} | {top5} | {highp} | {highc} | {unknown} |".format(
                name=scenario["name"],
                evaluable=scenario["knownEvaluable"],
                missing=scenario["knownWithoutUsableReference"],
                top1=pct(scenario["top1Accuracy"]),
                top3=pct(scenario["top3Accuracy"]),
                top5=pct(scenario["top5Accuracy"]),
                highp=pct(scenario["highSuggestionPrecision"]),
                highc=pct(scenario["knownHighCoverage"]),
                unknown=pct(scenario["unknownHighEmissionRate"]),
            )
        )

    audit = report["contaminationAudit"]
    lines.extend([
        "",
        "## Reference / contamination audit",
        "",
        f"- Duplicate exact-content groups: **{audit['duplicateContentGroups']}** ({audit['facesInDuplicateContentGroups']} faces)",
        f"- Cross-label duplicate groups: **{audit['crossLabelDuplicateContentGroups']}**",
        f"- Assigned/Unknown mixed duplicate groups: **{audit['assignedUnknownMixedDuplicateGroups']}**",
        f"- Reviewed faces whose original Person was merged: **{audit['reviewedFacesWhoseOriginalPersonWasMerged']}**",
        f"- Median / maximum references per identity: **{audit['referenceCountMedian']} / {audit['referenceCountMaximum']}**",
        f"- Reference share held by largest 10% of identities: **{audit['topTenPercentIdentityReferenceShare']:.3%}**",
        "",
        "## Mitigation deltas vs duplicate-resistant current ranking",
        "",
    ])
    for name, result in report["mitigationComparison"].items():
        if name == "baseline":
            continue
        lines.append(
            f"- **{name}**: top-1 {result['top1AccuracyDelta']:+.3%}; "
            f"High precision {result['highSuggestionPrecisionDelta']:+.3%}; "
            f"High coverage {result['knownHighCoverageDelta']:+.3%}; "
            f"Unknown High emission {result['unknownHighEmissionRateDelta']:+.3%}."
        )

    lines.extend([
        "",
        "## Interpretation constraints",
        "",
        "The production-equivalent row can be optimistic when an exact duplicate of the target exists among confirmed references. Use the duplicate-resistant row as the primary accuracy baseline for WI-0081.",
        "",
        "Unknown-face emission is a conservative risk signal rather than labelled impostor truth. Do not change production thresholds or ranking solely from this report; inspect the segmentation and choose the implementation direction explicitly.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Evaluate current identity suggestion accuracy and offline reference/ranking alternatives."
    )
    parser.add_argument("sample", type=Path)
    parser.add_argument(
        "--report-json",
        type=Path,
        default=Path("private/cluster-evaluation/suggestion-accuracy-report.json"),
    )
    parser.add_argument(
        "--report-md",
        type=Path,
        default=Path("private/cluster-evaluation/suggestion-accuracy-report.md"),
    )
    parser.add_argument("--reference-cap", type=int, default=8)
    args = parser.parse_args()
    if args.reference_cap < 1:
        parser.error("--reference-cap must be at least 1")

    report = evaluate(args)
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_md.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    markdown = render_markdown(report)
    args.report_md.write_text(markdown, encoding="utf-8")
    print(markdown)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
