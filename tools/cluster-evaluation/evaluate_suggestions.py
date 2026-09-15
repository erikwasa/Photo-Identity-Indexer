#!/usr/bin/env python3
"""Private WI-0081 identity-suggestion accuracy evaluator.

Consumes the pseudonymized exact-model export produced by PhotoIdentity.ClusterEvaluation.
Input and reports contain biometric-derived data and must remain private. The evaluator
is read-only with respect to the production catalogue.
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
    return payload, faces, embeddings / norms[:, None]


def read_policy(payload: dict[str, Any]) -> dict[str, Any]:
    policy = payload.get("suggestionPolicy") or {}
    return {
        "version": policy.get("version"),
        "autoAssignEnabled": policy.get("autoAssignEnabled"),
        "highScoreThreshold": float(policy.get("highScoreThreshold", DEFAULT_HIGH_SCORE)),
        "highMarginThreshold": float(policy.get("highMarginThreshold", DEFAULT_HIGH_MARGIN)),
        "mediumScoreThreshold": float(policy.get("mediumScoreThreshold", DEFAULT_MEDIUM_SCORE)),
        "updatedAtUtc": policy.get("updatedAtUtc"),
    }


def reference_indices(
    target: int,
    candidates: Iterable[int],
    content_groups: list[str],
    duplicate_resistant: bool,
) -> list[int]:
    target_group = content_groups[target]
    return [
        index for index in candidates
        if index != target and (not duplicate_resistant or content_groups[index] != target_group)
    ]


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
        return indices
    first = max(indices, key=lambda index: quality_key(faces[index], index))
    selected = [first]
    remaining = set(indices) - {first}
    while remaining and len(selected) < cap:
        selected_matrix = embeddings[selected]
        best_index = max(
            sorted(remaining),
            key=lambda index: (
                -float(np.max(selected_matrix @ embeddings[index])),
                *quality_key(faces[index], index),
            ),
        )
        selected.append(best_index)
        remaining.remove(best_index)
    return selected


def rank_target(
    target: int,
    strategy: str,
    duplicate_resistant: bool,
    cap: int,
    faces: list[dict[str, Any]],
    embeddings: np.ndarray,
    content_groups: list[str],
    by_person: dict[str, list[int]],
) -> list[tuple[str, float]]:
    target_vector = embeddings[target]
    ranked: list[tuple[str, float]] = []
    for person, person_indices in by_person.items():
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
            score = float(np.max(embeddings[curated] @ target_vector))
        else:
            raise ValueError(f"unknown ranking strategy: {strategy}")
        if math.isfinite(score):
            ranked.append((person, score))
    ranked.sort(key=lambda item: (-item[1], item[0]))
    return ranked


def confidence_group(score: float, margin: float | None, policy: dict[str, Any]) -> str:
    if (
        score >= policy["highScoreThreshold"]
        and margin is not None
        and margin >= policy["highMarginThreshold"]
    ):
        return "high"
    return "medium" if score >= policy["mediumScoreThreshold"] else "low"


def evaluate_scenario(
    name: str,
    strategy: str,
    duplicate_resistant: bool,
    cap: int,
    faces: list[dict[str, Any]],
    embeddings: np.ndarray,
    truth: list[str | None],
    content_groups: list[str],
    by_person: dict[str, list[int]],
    policy: dict[str, Any],
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    records: list[dict[str, Any]] = []
    known_targets = known_without_reference = 0
    unknown_targets = unknown_ranked = 0

    for target, label in enumerate(truth):
        true_reference_count = None
        if label is None:
            unknown_targets += 1
        else:
            known_targets += 1
            true_reference_count = len(reference_indices(
                target,
                by_person[label],
                content_groups,
                duplicate_resistant,
            ))
            if true_reference_count == 0:
                known_without_reference += 1
                continue

        ranked = rank_target(
            target,
            strategy,
            duplicate_resistant,
            cap,
            faces,
            embeddings,
            content_groups,
            by_person,
        )
        if not ranked:
            continue
        if label is None:
            unknown_ranked += 1

        top_person, top_score = ranked[0]
        second_score = ranked[1][1] if len(ranked) > 1 else None
        margin = top_score - second_score if second_score is not None else None
        group = confidence_group(top_score, margin, policy)
        true_rank = None
        genuine_score = None
        best_impostor_score = None
        if label is not None:
            for rank, (person, score) in enumerate(ranked, start=1):
                if person == label:
                    true_rank = rank
                    genuine_score = score
                elif best_impostor_score is None:
                    best_impostor_score = score

        records.append({
            "target": target,
            "truth": label,
            "topPerson": top_person,
            "topScore": top_score,
            "margin": margin,
            "confidenceGroup": group,
            "correct": label is not None and top_person == label,
            "trueRank": true_rank,
            "trueReferenceCount": true_reference_count,
            "genuineScore": genuine_score,
            "bestImpostorScore": best_impostor_score,
        })

    known = [record for record in records if record["truth"] is not None]
    high = [record for record in records if record["confidenceGroup"] == "high"]
    known_high = [record for record in known if record["confidenceGroup"] == "high"]
    medium_or_higher = [
        record for record in records if record["confidenceGroup"] in {"high", "medium"}
    ]
    unknown = [record for record in records if record["truth"] is None]
    unknown_high = [record for record in unknown if record["confidenceGroup"] == "high"]
    unknown_medium_or_higher = [
        record for record in unknown if record["confidenceGroup"] in {"high", "medium"}
    ]

    def top_k(k: int) -> float:
        return sum(
            1 for record in known
            if record["trueRank"] is not None and record["trueRank"] <= k
        ) / len(known) if known else 0.0

    return {
        "name": name,
        "strategy": strategy,
        "duplicateResistantHoldout": duplicate_resistant,
        "referenceCapPerPerson": cap if strategy == "quality-diverse-cap" else None,
        "knownTargets": known_targets,
        "knownEvaluable": len(known),
        "knownWithoutUsableReference": known_without_reference,
        "top1Accuracy": top_k(1),
        "top3Accuracy": top_k(3),
        "top5Accuracy": top_k(5),
        "highSuggestionCount": len(high),
        "highSuggestionPrecision": (
            sum(1 for record in high if record["correct"]) / len(high) if high else 0.0
        ),
        "knownHighCoverage": len(known_high) / len(known) if known else 0.0,
        "knownHighPrecision": (
            sum(1 for record in known_high if record["correct"]) / len(known_high)
            if known_high else 0.0
        ),
        "mediumOrHigherSuggestionCount": len(medium_or_higher),
        "mediumOrHigherPrecision": (
            sum(1 for record in medium_or_higher if record["correct"]) / len(medium_or_higher)
            if medium_or_higher else 0.0
        ),
        "unknownTargets": unknown_targets,
        "unknownRanked": unknown_ranked,
        "unknownHighEmissionRate": len(unknown_high) / len(unknown) if unknown else 0.0,
        "unknownMediumOrHigherEmissionRate": (
            len(unknown_medium_or_higher) / len(unknown) if unknown else 0.0
        ),
    }, records


def distribution(values: list[float]) -> dict[str, Any]:
    if not values:
        return {"count": 0}
    array = np.asarray(values, dtype=np.float64)
    return {
        "count": len(values),
        "min": float(np.min(array)),
        "p05": float(np.quantile(array, 0.05)),
        "p25": float(np.quantile(array, 0.25)),
        "median": float(np.quantile(array, 0.50)),
        "p75": float(np.quantile(array, 0.75)),
        "p95": float(np.quantile(array, 0.95)),
        "max": float(np.max(array)),
    }


def score_behavior(records: list[dict[str, Any]], policy: dict[str, Any]) -> dict[str, Any]:
    known = [record for record in records if record["truth"] is not None]
    paired = [
        record for record in known
        if record["genuineScore"] is not None and record["bestImpostorScore"] is not None
    ]
    genuine = [float(record["genuineScore"]) for record in known if record["genuineScore"] is not None]
    impostor = [float(record["bestImpostorScore"]) for record in paired]
    gaps = [
        float(record["genuineScore"] - record["bestImpostorScore"]) for record in paired
    ]
    return {
        "genuineScore": distribution(genuine),
        "bestImpostorScore": distribution(impostor),
        "genuineMinusBestImpostor": distribution(gaps),
        "pairedTargetCount": len(paired),
        "impostorOutranksOrTiesGenuineRate": (
            sum(1 for gap in gaps if gap <= 0) / len(gaps) if gaps else 0.0
        ),
        "genuineBelowMediumRate": (
            sum(1 for value in genuine if value < policy["mediumScoreThreshold"]) / len(genuine)
            if genuine else 0.0
        ),
        "genuineBelowHighScoreRate": (
            sum(1 for value in genuine if value < policy["highScoreThreshold"]) / len(genuine)
            if genuine else 0.0
        ),
        "bestImpostorAtOrAboveMediumRate": (
            sum(1 for value in impostor if value >= policy["mediumScoreThreshold"]) / len(impostor)
            if impostor else 0.0
        ),
        "bestImpostorAtOrAboveHighScoreRate": (
            sum(1 for value in impostor if value >= policy["highScoreThreshold"]) / len(impostor)
            if impostor else 0.0
        ),
    }


def summarize(records: list[dict[str, Any]], indices: list[int]) -> dict[str, Any]:
    selected = [
        record for record in records
        if record["target"] in indices and record["truth"] is not None
    ]
    if not selected:
        return {"count": 0, "top1Accuracy": None, "highCount": 0, "highPrecision": None}
    high = [record for record in selected if record["confidenceGroup"] == "high"]
    margins = [record["margin"] for record in selected if record["margin"] is not None]
    return {
        "count": len(selected),
        "top1Accuracy": sum(1 for record in selected if record["correct"]) / len(selected),
        "highCount": len(high),
        "highPrecision": (
            sum(1 for record in high if record["correct"]) / len(high) if high else None
        ),
        "medianTopScore": median(record["topScore"] for record in selected),
        "medianMargin": median(margins) if margins else None,
    }


def numeric_buckets(
    values: list[float | None],
    ranges: list[tuple[str, float | None, float | None]],
) -> dict[str, list[int]]:
    result = {name: [] for name, _, _ in ranges}
    result["missing"] = []
    for index, value in enumerate(values):
        if value is None or not math.isfinite(float(value)):
            result["missing"].append(index)
            continue
        numeric = float(value)
        for name, low, high in ranges:
            if (low is None or numeric >= low) and (high is None or numeric < high):
                result[name].append(index)
                break
        else:
            result["missing"].append(index)
    return result


def chronology_buckets(faces: list[dict[str, Any]], truth: list[str | None]) -> dict[str, list[int]]:
    dated: list[tuple[datetime, int]] = []
    for index, face in enumerate(faces):
        if truth[index] is None or not face.get("reviewedAtUtc"):
            continue
        try:
            value = str(face["reviewedAtUtc"]).replace("Z", "+00:00")
            dated.append((datetime.fromisoformat(value), index))
        except ValueError:
            continue
    dated.sort(key=lambda item: (item[0], item[1]))
    if len(dated) < 4:
        return {"insufficient-date-data": [index for _, index in dated]}
    names = ["oldest-quartile", "middle-old", "middle-new", "newest-quartile"]
    result = {name: [] for name in names}
    for position, (_, index) in enumerate(dated):
        result[names[min(3, position * 4 // len(dated))]].append(index)
    return result


def segment(
    faces: list[dict[str, Any]],
    truth: list[str | None],
    records: list[dict[str, Any]],
) -> dict[str, Any]:
    confidence = numeric_buckets(
        [face.get("detectorConfidence") for face in faces],
        [("lt-0.70", None, 0.70), ("0.70-0.85", 0.70, 0.85),
         ("0.85-0.95", 0.85, 0.95), ("gte-0.95", 0.95, None)],
    )
    area = numeric_buckets(
        [face.get("faceAreaFraction") for face in faces],
        [("lt-0.5pct", None, 0.005), ("0.5-2pct", 0.005, 0.02),
         ("2-8pct", 0.02, 0.08), ("gte-8pct", 0.08, None)],
    )
    record_by_target = {record["target"]: record for record in records}
    reference: dict[str, list[int]] = defaultdict(list)
    for target, label in enumerate(truth):
        if label is None:
            continue
        count = (record_by_target.get(target) or {}).get("trueReferenceCount")
        key = (
            "no-usable-holdout-reference" if count is None else
            "1" if count == 1 else
            "2-4" if count <= 4 else
            "5-9" if count <= 9 else
            "10-plus"
        )
        reference[key].append(target)
    return {
        "detectorConfidence": {name: summarize(records, indices) for name, indices in confidence.items()},
        "faceAreaFraction": {name: summarize(records, indices) for name, indices in area.items()},
        "trueIdentityReferenceCount": {
            name: summarize(records, indices) for name, indices in sorted(reference.items())
        },
        "reviewChronology": {
            name: summarize(records, indices)
            for name, indices in chronology_buckets(faces, truth).items()
        },
    }


def contamination_audit(
    faces: list[dict[str, Any]],
    truth: list[str | None],
    content_groups: list[str],
    by_person: dict[str, list[int]],
) -> dict[str, Any]:
    by_content: dict[str, list[int]] = defaultdict(list)
    for index, group in enumerate(content_groups):
        by_content[group].append(index)
    duplicate_groups = [indices for indices in by_content.values() if len(indices) > 1]
    sizes = sorted(len(indices) for indices in by_person.values())
    assigned = sum(sizes)
    top_count = max(1, math.ceil(len(sizes) * 0.10)) if sizes else 0
    top_share = sum(sorted(sizes, reverse=True)[:top_count]) / assigned if assigned else 0.0
    return {
        "contentGroups": len(by_content),
        "duplicateContentGroups": len(duplicate_groups),
        "facesInDuplicateContentGroups": sum(len(indices) for indices in duplicate_groups),
        "crossLabelDuplicateContentGroups": sum(
            1 for indices in duplicate_groups
            if len({truth[index] for index in indices if truth[index] is not None}) > 1
        ),
        "assignedUnknownMixedDuplicateGroups": sum(
            1 for indices in duplicate_groups
            if any(truth[index] is None for index in indices)
            and any(truth[index] is not None for index in indices)
        ),
        "reviewedFacesWhosePersonHasMergeHistory": sum(
            1 for face in faces
            if face.get("reviewedPersonHasMergeHistory", face.get("reviewedPersonWasMerged", False))
        ),
        "identitiesWithMergeHistory": len({
            truth[index] for index, face in enumerate(faces)
            if truth[index] is not None
            and face.get("reviewedPersonHasMergeHistory", face.get("reviewedPersonWasMerged", False))
        }),
        "identityCount": len(sizes),
        "referenceCountMedian": median(sizes) if sizes else 0,
        "referenceCountMaximum": max(sizes) if sizes else 0,
        "topTenPercentIdentityReferenceShare": top_share,
    }


def metric_delta(candidate: dict[str, Any], baseline: dict[str, Any], key: str) -> float:
    return float(candidate[key]) - float(baseline[key])


def evaluate(args: argparse.Namespace) -> dict[str, Any]:
    payload, faces, embeddings = load_sample(args.sample)
    policy = read_policy(payload)
    truth = [face.get("groundTruthLabel") for face in faces]
    content_groups = [
        str(face.get("contentGroup") or face.get("photoGroup") or f"row-{index}")
        for index, face in enumerate(faces)
    ]
    by_person: dict[str, list[int]] = defaultdict(list)
    for index, label in enumerate(truth):
        if label is not None:
            by_person[str(label)].append(index)
    if len(by_person) < 2:
        raise ValueError("at least two reviewed identities are required")

    definitions = [
        ("production-equivalent-max", "max-exemplar", False),
        ("duplicate-resistant-max", "max-exemplar", True),
        ("duplicate-resistant-centroid", "centroid", True),
        ("duplicate-resistant-quality-diverse-cap", "quality-diverse-cap", True),
    ]
    scenarios: dict[str, dict[str, Any]] = {}
    records: dict[str, list[dict[str, Any]]] = {}
    for name, strategy, duplicate_resistant in definitions:
        scenarios[name], records[name] = evaluate_scenario(
            name, strategy, duplicate_resistant, args.reference_cap,
            faces, embeddings, truth, content_groups, by_person, policy,
        )

    baseline_name = "duplicate-resistant-max"
    baseline = scenarios[baseline_name]
    centroid = scenarios["duplicate-resistant-centroid"]
    capped = scenarios["duplicate-resistant-quality-diverse-cap"]
    return {
        "schemaVersion": 1,
        "sample": {
            "modelId": payload.get("modelId"),
            "modelHash": payload.get("modelHash"),
            "faceCount": payload.get("faceCount", len(faces)),
            "assignedLabelCount": payload.get("assignedLabelCount", len(by_person)),
            "unknownFaceCount": payload.get("unknownFaceCount", sum(1 for value in truth if value is None)),
            "sampleSelection": payload.get("sampleSelection"),
        },
        "productionSuggestionPolicy": policy,
        "contaminationAudit": contamination_audit(faces, truth, content_groups, by_person),
        "scenarios": scenarios,
        "baselineScoreBehavior": score_behavior(records[baseline_name], policy),
        "baselineSegmentation": segment(faces, truth, records[baseline_name]),
        "mitigationComparison": {
            "baseline": baseline_name,
            "centroid": {
                "top1AccuracyDelta": metric_delta(centroid, baseline, "top1Accuracy"),
                "highSuggestionPrecisionDelta": metric_delta(centroid, baseline, "highSuggestionPrecision"),
                "knownHighCoverageDelta": metric_delta(centroid, baseline, "knownHighCoverage"),
                "unknownHighEmissionRateDelta": metric_delta(centroid, baseline, "unknownHighEmissionRate"),
            },
            "qualityDiverseCap": {
                "referenceCapPerPerson": args.reference_cap,
                "top1AccuracyDelta": metric_delta(capped, baseline, "top1Accuracy"),
                "highSuggestionPrecisionDelta": metric_delta(capped, baseline, "highSuggestionPrecision"),
                "knownHighCoverageDelta": metric_delta(capped, baseline, "knownHighCoverage"),
                "unknownHighEmissionRateDelta": metric_delta(capped, baseline, "unknownHighEmissionRate"),
            },
        },
        "notes": [
            "Production-equivalent max-exemplar reproduces the current best-exemplar-per-Person ranking rule with only the target face removed.",
            "Duplicate-resistant scenarios also remove every reference from the target's exact-content group.",
            "Known targets without another usable same-Person reference are reported separately and excluded from top-k denominators.",
            "Genuine score is the score for the reviewed Person; best-impostor score is the highest score from any other reviewed Person.",
            "Unknown-face High/Medium emission is a conservative false-positive-risk signal, not labelled impostor truth.",
            "Detector confidence and normalized face area are queue-composition proxies rather than direct embedding-quality labels.",
            "Review chronology quartiles help distinguish matcher drift from a later, harder review queue.",
            "Centroid and bounded quality-diverse references are offline comparisons only and do not alter production behavior.",
            "Evaluate different embedding model hashes separately rather than pooling revisions.",
            "If maximumFaces truncates the reviewed corpus, record that selection caveat when interpreting the report.",
        ],
    }


def pct(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.3%}"


def number(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.4f}"


def render_markdown(report: dict[str, Any]) -> str:
    sample = report["sample"]
    lines = [
        "# Private WI-0081 suggestion-accuracy evaluation",
        "",
        "> PRIVATE: derived from local biometric/review data. Do not commit this report.",
        "",
        f"- Exact model: `{sample['modelId']}` / `{sample['modelHash']}`",
        f"- Faces: {sample['faceCount']}",
        f"- Reviewed identities: {sample['assignedLabelCount']}",
        f"- Reviewed Unknown faces: {sample['unknownFaceCount']}",
        "",
        "## Ranking scenarios",
        "",
        "| Scenario | Evaluable known | No holdout ref | Top-1 | Top-3 | Top-5 | Conservative High precision | High coverage | Unknown High emission |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for scenario in report["scenarios"].values():
        lines.append(
            f"| {scenario['name']} | {scenario['knownEvaluable']} | {scenario['knownWithoutUsableReference']} | "
            f"{pct(scenario['top1Accuracy'])} | {pct(scenario['top3Accuracy'])} | {pct(scenario['top5Accuracy'])} | "
            f"{pct(scenario['highSuggestionPrecision'])} | {pct(scenario['knownHighCoverage'])} | "
            f"{pct(scenario['unknownHighEmissionRate'])} |"
        )

    behavior = report["baselineScoreBehavior"]
    genuine = behavior["genuineScore"]
    impostor = behavior["bestImpostorScore"]
    gap = behavior["genuineMinusBestImpostor"]
    lines.extend([
        "",
        "## Duplicate-resistant genuine / impostor behavior",
        "",
        f"- Genuine score median / p05 / p95: **{number(genuine.get('median'))} / {number(genuine.get('p05'))} / {number(genuine.get('p95'))}**",
        f"- Best-impostor score median / p05 / p95: **{number(impostor.get('median'))} / {number(impostor.get('p05'))} / {number(impostor.get('p95'))}**",
        f"- Genuine-minus-impostor median / p05: **{number(gap.get('median'))} / {number(gap.get('p05'))}**",
        f"- Best impostor outranks or ties genuine: **{pct(behavior['impostorOutranksOrTiesGenuineRate'])}**",
        f"- Genuine below Medium threshold: **{pct(behavior['genuineBelowMediumRate'])}**",
        f"- Best impostor at/above Medium threshold: **{pct(behavior['bestImpostorAtOrAboveMediumRate'])}**",
        f"- Best impostor at/above High score threshold: **{pct(behavior['bestImpostorAtOrAboveHighScoreRate'])}**",
    ])

    audit = report["contaminationAudit"]
    lines.extend([
        "",
        "## Reference / contamination audit",
        "",
        f"- Duplicate exact-content groups: **{audit['duplicateContentGroups']}** ({audit['facesInDuplicateContentGroups']} faces)",
        f"- Cross-label duplicate groups: **{audit['crossLabelDuplicateContentGroups']}**",
        f"- Assigned/Unknown mixed duplicate groups: **{audit['assignedUnknownMixedDuplicateGroups']}**",
        f"- Identities with merge history: **{audit['identitiesWithMergeHistory']}** ({audit['reviewedFacesWhosePersonHasMergeHistory']} reviewed faces)",
        f"- Median / maximum references per identity: **{audit['referenceCountMedian']} / {audit['referenceCountMaximum']}**",
        f"- Reference share held by largest 10% of identities: **{pct(audit['topTenPercentIdentityReferenceShare'])}**",
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
        "The production-equivalent row can be optimistic when an exact duplicate of the target is a confirmed reference. Use the duplicate-resistant row as the primary WI-0081 accuracy baseline.",
        "",
        "Unknown-face emission is a conservative risk signal rather than labelled impostor truth. Inspect the JSON segmentation before selecting a mitigation. Do not change production thresholds or ranking solely from this report.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Evaluate current identity suggestion accuracy and offline reference/ranking alternatives."
    )
    parser.add_argument("sample", type=Path)
    parser.add_argument("--report-json", type=Path, default=Path("private/cluster-evaluation/suggestion-accuracy-report.json"))
    parser.add_argument("--report-md", type=Path, default=Path("private/cluster-evaluation/suggestion-accuracy-report.md"))
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
