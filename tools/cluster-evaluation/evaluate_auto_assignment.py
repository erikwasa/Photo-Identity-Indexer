#!/usr/bin/env python3
"""Private WI-0117 multi-evidence automatic-assignment evaluator.

Consumes the pseudonymized exact-model export produced by PhotoIdentity.ClusterEvaluation.
The evaluator is read-only with respect to the production catalogue. It compares the
accepted current High rule with candidate multi-reference and cluster-corroborated
expansions. It never creates canonical assignments.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from sklearn.cluster import DBSCAN

DEFAULT_HIGH_SCORE = 0.70
DEFAULT_HIGH_MARGIN = 0.10
DEFAULT_MEDIUM_SCORE = 0.50
DEFAULT_CLUSTER_EPS = 0.30
DEFAULT_CLUSTER_MIN_SAMPLES = 3
DEFAULT_MIN_CLUSTER_SUPPORT_COUNT = 3
DEFAULT_MIN_CLUSTER_SUPPORT_SHARE = 0.60
DEFAULT_MIN_CLUSTER_CORE_SHARE = 0.60
DEFAULT_MAX_CLUSTER_COMPETING_COUNT = 1
DEFAULT_MAX_CLUSTER_COMPETING_SHARE = 0.20


@dataclass(frozen=True)
class CandidateRule:
    name: str
    family: str
    min_margin: float
    support_count: int = 0
    support_score: float = 0.0
    require_strong_cluster: bool = False


def normalized_embeddings(rows: list[dict[str, Any]]) -> np.ndarray:
    values = np.asarray([row["embedding"] for row in rows], dtype=np.float64)
    if values.ndim != 2 or values.shape[1] == 0 or not np.isfinite(values).all():
        raise ValueError("embeddings must be a finite rectangular matrix")
    norms = np.linalg.norm(values, axis=1)
    if np.any(norms <= 0):
        raise ValueError("embeddings contain a zero vector")
    return values / norms[:, None]


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


def load_sample(
    path: Path,
) -> tuple[
    dict[str, Any],
    list[dict[str, Any]],
    np.ndarray,
    list[dict[str, Any]],
    np.ndarray,
    dict[str, Any],
]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if payload.get("schemaVersion") != 1:
        raise ValueError("unsupported cluster-evaluation export schema")
    targets = payload.get("faces") or []
    references = payload.get("referenceFaces") or []
    if len(targets) < 3:
        raise ValueError("at least three reviewed target faces are required")
    if len(references) < 2:
        raise ValueError("at least two confirmed production references are required")
    if any(reference.get("groundTruthLabel") is None for reference in references):
        raise ValueError("all production references must have a Person label")
    return (
        payload,
        targets,
        normalized_embeddings(targets),
        references,
        normalized_embeddings(references),
        read_policy(payload),
    )


def evidence_group(row: dict[str, Any], fallback: str) -> str:
    return str(row.get("contentGroup") or row.get("photoGroup") or row.get("id") or fallback)


def split_name(target: dict[str, Any], index: int) -> str:
    # Split by exact-content group so an image and its exact copies cannot land in both
    # policy-selection and holdout-validation partitions.
    key = evidence_group(target, f"target-{index}")
    bucket = hashlib.sha256(key.encode("utf-8")).digest()[0] % 5
    return "selection" if bucket < 3 else "holdout"


def build_reference_indexes(
    references: list[dict[str, Any]],
) -> tuple[dict[str, np.ndarray], dict[str, np.ndarray], dict[str, np.ndarray]]:
    by_person_lists: dict[str, list[int]] = defaultdict(list)
    by_content_lists: dict[str, list[int]] = defaultdict(list)
    by_id_lists: dict[str, list[int]] = defaultdict(list)
    for index, reference in enumerate(references):
        by_person_lists[str(reference["groundTruthLabel"])].append(index)
        by_content_lists[evidence_group(reference, f"reference-{index}")].append(index)
        if reference.get("id") is not None:
            by_id_lists[str(reference["id"])].append(index)
    return (
        {key: np.asarray(values, dtype=np.int64) for key, values in by_person_lists.items()},
        {key: np.asarray(values, dtype=np.int64) for key, values in by_content_lists.items()},
        {key: np.asarray(values, dtype=np.int64) for key, values in by_id_lists.items()},
    )


def top_independent_scores(
    similarities: np.ndarray,
    indices: np.ndarray,
    references: list[dict[str, Any]],
    limit: int = 4,
) -> list[float]:
    if indices.size == 0:
        return []
    best_by_group: dict[str, float] = {}
    for reference_index in indices:
        score = float(similarities[int(reference_index)])
        if not math.isfinite(score):
            continue
        group = evidence_group(references[int(reference_index)], f"reference-{int(reference_index)}")
        previous = best_by_group.get(group)
        if previous is None or score > previous:
            best_by_group[group] = score
    return sorted(best_by_group.values(), reverse=True)[:limit]


def rank_targets(
    targets: list[dict[str, Any]],
    target_embeddings: np.ndarray,
    references: list[dict[str, Any]],
    reference_embeddings: np.ndarray,
    policy: dict[str, Any],
    batch_size: int,
) -> list[dict[str, Any]]:
    by_person, by_content, by_id = build_reference_indexes(references)
    people = sorted(by_person)
    records: list[dict[str, Any]] = []

    for batch_start in range(0, len(targets), batch_size):
        batch_end = min(len(targets), batch_start + batch_size)
        similarities = target_embeddings[batch_start:batch_end] @ reference_embeddings.T
        for local_index, target_index in enumerate(range(batch_start, batch_end)):
            target = targets[target_index]
            row = similarities[local_index]
            target_group = evidence_group(target, f"target-{target_index}")
            disallowed = by_content.get(target_group)
            if disallowed is not None:
                row[disallowed] = -np.inf
            if target.get("id") is not None:
                same_id = by_id.get(str(target["id"]))
                if same_id is not None:
                    row[same_id] = -np.inf

            ranked: list[tuple[str, float]] = []
            for person in people:
                values = row[by_person[person]]
                if values.size == 0:
                    continue
                score = float(np.max(values))
                if math.isfinite(score):
                    ranked.append((person, score))
            ranked.sort(key=lambda item: (-item[1], item[0]))
            if not ranked:
                continue

            top_person, top_score = ranked[0]
            second_score = ranked[1][1] if len(ranked) > 1 else None
            margin = top_score - second_score if second_score is not None else None
            truth = target.get("groundTruthLabel")
            true_rank = None
            if truth is not None:
                for rank, (person, _) in enumerate(ranked, start=1):
                    if person == truth:
                        true_rank = rank
                        break

            support_scores = top_independent_scores(
                row,
                by_person[top_person],
                references,
                limit=4,
            )
            true_reference_count = None
            if truth is not None and str(truth) in by_person:
                truth_values = row[by_person[str(truth)]]
                true_reference_count = int(np.isfinite(truth_values).sum())

            current_high = (
                top_score >= policy["highScoreThreshold"]
                and margin is not None
                and margin >= policy["highMarginThreshold"]
            )
            records.append({
                "target": target_index,
                "truth": truth,
                "topPerson": top_person,
                "topScore": top_score,
                "secondScore": second_score,
                "margin": margin,
                "correct": truth is not None and top_person == truth,
                "trueRank": true_rank,
                "currentHigh": current_high,
                "supportScores": support_scores,
                "split": split_name(target, target_index),
                "contentGroup": target_group,
                "detectorConfidence": target.get("detectorConfidence"),
                "faceAreaFraction": target.get("faceAreaFraction"),
                "trueReferenceCount": true_reference_count,
            })
    return records


def add_cluster_evidence(
    records: list[dict[str, Any]],
    targets: list[dict[str, Any]],
    target_embeddings: np.ndarray,
    policy: dict[str, Any],
    cluster_eps: float,
    cluster_min_samples: int,
) -> dict[str, Any]:
    clustering = DBSCAN(
        eps=cluster_eps,
        min_samples=cluster_min_samples,
        metric="cosine",
        algorithm="brute",
        n_jobs=-1,
    ).fit(target_embeddings)
    cluster_labels = [int(value) for value in clustering.labels_]
    core = {int(value) for value in clustering.core_sample_indices_}
    record_by_target = {int(record["target"]): record for record in records}
    members_by_cluster: dict[int, list[int]] = defaultdict(list)
    for target_index, cluster_id in enumerate(cluster_labels):
        if cluster_id >= 0 and target_index in record_by_target:
            members_by_cluster[cluster_id].append(target_index)

    strong_target_count = 0
    for cluster_id, members in members_by_cluster.items():
        core_share = sum(1 for member in members if member in core) / len(members)
        for target_index in members:
            target_record = record_by_target[target_index]
            target_group = target_record["contentGroup"]
            other_groups = {
                record_by_target[member]["contentGroup"]
                for member in members
                if member != target_index
                and record_by_target[member]["contentGroup"] != target_group
            }
            votes_by_group: dict[str, dict[str, Any]] = {}
            for member in members:
                if member == target_index:
                    continue
                member_record = record_by_target[member]
                if member_record["contentGroup"] == target_group:
                    continue
                if member_record["topScore"] < policy["mediumScoreThreshold"]:
                    continue
                group = member_record["contentGroup"]
                previous = votes_by_group.get(group)
                if previous is None or member_record["topScore"] > previous["topScore"]:
                    votes_by_group[group] = member_record

            votes = Counter(
                str(member_record["topPerson"])
                for member_record in votes_by_group.values()
            )
            ordered = sorted(votes, key=lambda person: (-votes[person], person))
            candidate = ordered[0] if ordered else None
            competitor = ordered[1] if len(ordered) > 1 else None
            denominator = len(other_groups)
            candidate_count = votes[candidate] if candidate is not None else 0
            competitor_count = votes[competitor] if competitor is not None else 0
            candidate_share = candidate_count / denominator if denominator else 0.0
            competitor_share = competitor_count / denominator if denominator else 0.0
            strong = (
                candidate is not None
                and candidate_count >= DEFAULT_MIN_CLUSTER_SUPPORT_COUNT
                and candidate_share >= DEFAULT_MIN_CLUSTER_SUPPORT_SHARE
                and core_share >= DEFAULT_MIN_CLUSTER_CORE_SHARE
                and competitor_count <= DEFAULT_MAX_CLUSTER_COMPETING_COUNT
                and competitor_share <= DEFAULT_MAX_CLUSTER_COMPETING_SHARE
            )
            target_record["clusterId"] = cluster_id
            target_record["clusterCandidate"] = candidate
            target_record["clusterSupportCount"] = candidate_count
            target_record["clusterSupportShare"] = candidate_share
            target_record["clusterCompetingCount"] = competitor_count
            target_record["clusterCompetingShare"] = competitor_share
            target_record["clusterCoreShare"] = core_share
            target_record["strongClusterEvidence"] = strong
            if strong:
                strong_target_count += 1

    for record in records:
        if "clusterId" not in record:
            record.update({
                "clusterId": None,
                "clusterCandidate": None,
                "clusterSupportCount": 0,
                "clusterSupportShare": 0.0,
                "clusterCompetingCount": 0,
                "clusterCompetingShare": 0.0,
                "clusterCoreShare": 0.0,
                "strongClusterEvidence": False,
            })

    return {
        "algorithm": "dbscan",
        "eps": cluster_eps,
        "minSamples": cluster_min_samples,
        "clusterCount": len(members_by_cluster),
        "noiseTargetCount": sum(1 for value in cluster_labels if value < 0),
        "strongTargetSpecificEvidenceCount": strong_target_count,
        "targetEvidenceExcludesTargetAndSameExactContent": True,
        "internalNotSameEvidenceIncludedInPrivateExport": False,
    }


def candidate_rules(policy: dict[str, Any]) -> list[CandidateRule]:
    rules: list[CandidateRule] = []
    for count in (2, 3, 4):
        for support_score in (0.50, 0.55, 0.60, 0.65):
            if support_score < policy["mediumScoreThreshold"]:
                continue
            for margin in (0.00, 0.05, 0.10):
                rules.append(CandidateRule(
                    name=f"multi-ref-{count}x-{support_score:.2f}-margin-{margin:.2f}",
                    family="multi-reference",
                    min_margin=margin,
                    support_count=count,
                    support_score=support_score,
                ))

    for margin in (0.00, 0.05, 0.10):
        rules.append(CandidateRule(
            name=f"cluster-strong-medium-margin-{margin:.2f}",
            family="cluster-corroborated",
            min_margin=margin,
            require_strong_cluster=True,
        ))

    for count in (2, 3):
        for support_score in (0.50, 0.55, 0.60):
            if support_score < policy["mediumScoreThreshold"]:
                continue
            for margin in (0.00, 0.05):
                rules.append(CandidateRule(
                    name=f"cluster-plus-{count}x-{support_score:.2f}-margin-{margin:.2f}",
                    family="cluster-plus-multi-reference",
                    min_margin=margin,
                    support_count=count,
                    support_score=support_score,
                    require_strong_cluster=True,
                ))
    return rules


def expansion_matches(
    record: dict[str, Any],
    rule: CandidateRule,
    policy: dict[str, Any],
) -> bool:
    if record["currentHigh"]:
        return False
    if record["topScore"] < policy["mediumScoreThreshold"]:
        return False
    margin = record.get("margin")
    if margin is None or margin < rule.min_margin:
        return False
    if rule.support_count:
        scores = record["supportScores"]
        if len(scores) < rule.support_count:
            return False
        if scores[rule.support_count - 1] < rule.support_score:
            return False
    if rule.require_strong_cluster:
        if not record["strongClusterEvidence"]:
            return False
        if record["clusterCandidate"] != record["topPerson"]:
            return False
    return True


def metric_counts(records: list[dict[str, Any]], assigned: list[dict[str, Any]]) -> dict[str, Any]:
    known_targets = sum(1 for record in records if record["truth"] is not None)
    unknown_targets = len(records) - known_targets
    known_assigned = [record for record in assigned if record["truth"] is not None]
    correct_known = sum(1 for record in known_assigned if record["correct"])
    wrong_known = len(known_assigned) - correct_known
    unknown_assigned = sum(1 for record in assigned if record["truth"] is None)
    false_count = wrong_known + unknown_assigned
    total = len(assigned)
    return {
        "targetCount": len(records),
        "knownTargetCount": known_targets,
        "unknownTargetCount": unknown_targets,
        "assignedCount": total,
        "knownAssignedCount": len(known_assigned),
        "correctKnownAssignments": correct_known,
        "wrongKnownAssignments": wrong_known,
        "unknownAssignments": unknown_assigned,
        "falseAutomaticAssignments": false_count,
        "falseAssignmentRate": false_count / total if total else 0.0,
        "conservativePrecision": correct_known / total if total else 0.0,
        "knownPrecision": correct_known / len(known_assigned) if known_assigned else 0.0,
        "knownCoverage": len(known_assigned) / known_targets if known_targets else 0.0,
        "unknownAssignmentRate": unknown_assigned / unknown_targets if unknown_targets else 0.0,
        "estimatedReviewActionsSaved": total,
    }


def evaluate_rule(
    all_records: list[dict[str, Any]],
    rule: CandidateRule | None,
    policy: dict[str, Any],
    split: str,
) -> dict[str, Any]:
    records = [record for record in all_records if split == "all" or record["split"] == split]
    baseline = [record for record in records if record["currentHigh"]]
    additions = [] if rule is None else [
        record for record in records if expansion_matches(record, rule, policy)
    ]
    assigned_by_target = {int(record["target"]): record for record in baseline}
    for record in additions:
        assigned_by_target[int(record["target"])] = record
    assigned = list(assigned_by_target.values())
    total_metrics = metric_counts(records, assigned)
    baseline_metrics = metric_counts(records, baseline)
    incremental_metrics = metric_counts(records, additions)
    return {
        **total_metrics,
        "incrementalAssignedCount": incremental_metrics["assignedCount"],
        "incrementalCorrectKnownAssignments": incremental_metrics["correctKnownAssignments"],
        "incrementalWrongKnownAssignments": incremental_metrics["wrongKnownAssignments"],
        "incrementalUnknownAssignments": incremental_metrics["unknownAssignments"],
        "incrementalConservativePrecision": incremental_metrics["conservativePrecision"],
        "incrementalFalseAssignmentRate": incremental_metrics["falseAssignmentRate"],
        "baselineAssignedCount": baseline_metrics["assignedCount"],
    }


def guardrail_class(candidate: dict[str, Any], baseline: dict[str, Any]) -> str:
    if candidate["incrementalAssignedCount"] <= 0:
        return "no-expansion"
    if (
        candidate["wrongKnownAssignments"] <= baseline["wrongKnownAssignments"]
        and candidate["unknownAssignments"] <= baseline["unknownAssignments"]
    ):
        return "strict-no-extra-false"
    tolerance = 1e-12
    if (
        candidate["knownPrecision"] + tolerance >= baseline["knownPrecision"]
        and candidate["conservativePrecision"] + tolerance >= baseline["conservativePrecision"]
        and candidate["unknownAssignmentRate"] <= baseline["unknownAssignmentRate"] + tolerance
    ):
        return "rate-preserving"
    return "exploratory"


def shortlist_rules(
    rules: list[CandidateRule],
    selection_results: dict[str, dict[str, Any]],
    baseline: dict[str, Any],
    limit: int,
) -> list[CandidateRule]:
    ranked: list[tuple[int, int, int, str, CandidateRule]] = []
    class_rank = {"strict-no-extra-false": 0, "rate-preserving": 1}
    for rule in rules:
        metrics = selection_results[rule.name]
        category = guardrail_class(metrics, baseline)
        if category not in class_rank:
            continue
        ranked.append((
            class_rank[category],
            -int(metrics["incrementalCorrectKnownAssignments"]),
            int(metrics["falseAutomaticAssignments"]),
            rule.name,
            rule,
        ))
    ranked.sort(key=lambda item: item[:4])
    return [item[4] for item in ranked[:limit]]


def segment_name(record: dict[str, Any]) -> list[str]:
    names = ["all"]
    confidence = record.get("detectorConfidence")
    area = record.get("faceAreaFraction")
    references = record.get("trueReferenceCount")
    if confidence is not None and float(confidence) < 0.70:
        names.append("detector-confidence-lt-0.70")
    if area is not None and float(area) < 0.005:
        names.append("face-area-lt-0.5pct")
    if record.get("truth") is not None and references is not None and int(references) < 10:
        names.append("true-identity-lt-10-references")
    return names


def quality_segments(
    records: list[dict[str, Any]],
    rule: CandidateRule | None,
    policy: dict[str, Any],
) -> dict[str, Any]:
    holdout = [record for record in records if record["split"] == "holdout"]
    buckets: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for record in holdout:
        for name in segment_name(record):
            buckets[name].append(record)
    result: dict[str, Any] = {}
    for name, selected in sorted(buckets.items()):
        baseline = [record for record in selected if record["currentHigh"]]
        additions = [] if rule is None else [
            record for record in selected if expansion_matches(record, rule, policy)
        ]
        assigned = {int(record["target"]): record for record in baseline}
        for record in additions:
            assigned[int(record["target"])] = record
        result[name] = metric_counts(selected, list(assigned.values()))
        result[name]["incrementalAssignedCount"] = len(additions)
    return result


def pct(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.3%}"


def render_markdown(report: dict[str, Any]) -> str:
    sample = report["sample"]
    baseline = report["baseline"]
    lines = [
        "# Private WI-0117 multi-evidence auto-assignment evaluation",
        "",
        "> PRIVATE: aggregate output derived from local biometric/review data. Do not commit the generated report.",
        "",
        f"- Exact model: `{sample['modelId']}` / `{sample['modelHash']}`",
        f"- Reviewed targets: {sample['targetFaceCount']}",
        f"- Confirmed production references: {sample['referenceFaceCount']}",
        f"- Reference identities: {sample['referenceLabelCount']}",
        f"- Selection / holdout targets: {sample['selectionTargetCount']} / {sample['holdoutTargetCount']}",
        "",
        "The deterministic split hashes exact-content groups, so an image and its exact copies stay in one partition. All ranking is duplicate-resistant: the target's exact-content group is removed from production references before policy evaluation.",
        "",
        "## Current High baseline",
        "",
        "| Split | Assigned | False assignments | Conservative precision | Known coverage | Unknown assignment |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for split in ("selection", "holdout", "all"):
        item = baseline[split]
        lines.append(
            f"| {split} | {item['assignedCount']} | {item['falseAutomaticAssignments']} | "
            f"{pct(item['conservativePrecision'])} | {pct(item['knownCoverage'])} | "
            f"{item['unknownAssignments']} ({pct(item['unknownAssignmentRate'])}) |"
        )

    lines.extend([
        "",
        "## Selection-split candidate screen",
        "",
        "Candidates expand the current High rule only for Medium-or-better targets. `strict-no-extra-false` means the selection split gained assignments without increasing either wrong-known or Unknown assignments versus current High. `rate-preserving` means precision and Unknown-assignment-rate guardrails were not worse.",
        "",
        "| Candidate | Family | Guardrail | Extra assignments | Extra correct known | Extra wrong known | Extra Unknown | Total precision |",
        "|---|---|---|---:|---:|---:|---:|---:|",
    ])
    for candidate in report["selectionScreen"][:15]:
        metrics = candidate["metrics"]
        lines.append(
            f"| {candidate['name']} | {candidate['family']} | {candidate['guardrailClass']} | "
            f"{metrics['incrementalAssignedCount']} | {metrics['incrementalCorrectKnownAssignments']} | "
            f"{metrics['incrementalWrongKnownAssignments']} | {metrics['incrementalUnknownAssignments']} | "
            f"{pct(metrics['conservativePrecision'])} |"
        )

    lines.extend([
        "",
        "## Holdout validation of automatically shortlisted candidates",
        "",
    ])
    if not report["shortlist"]:
        lines.append("No candidate preserved the selection-split guardrails, so no expansion candidate was promoted to holdout validation.")
    else:
        lines.extend([
            "| Candidate | Family | Assigned | Extra | False assignments | Conservative precision | Known coverage | Unknown assignment |",
            "|---|---|---:|---:|---:|---:|---:|---:|",
        ])
        for candidate in report["shortlist"]:
            metrics = candidate["holdout"]
            lines.append(
                f"| {candidate['name']} | {candidate['family']} | {metrics['assignedCount']} | "
                f"{metrics['incrementalAssignedCount']} | {metrics['falseAutomaticAssignments']} | "
                f"{pct(metrics['conservativePrecision'])} | {pct(metrics['knownCoverage'])} | "
                f"{metrics['unknownAssignments']} ({pct(metrics['unknownAssignmentRate'])}) |"
            )

    cluster = report["clusterEvidence"]
    lines.extend([
        "",
        "## Cluster evidence",
        "",
        f"- DBSCAN: eps `{cluster['eps']}`, min samples `{cluster['minSamples']}`; clusters **{cluster['clusterCount']}**, noise targets **{cluster['noiseTargetCount']}**.",
        f"- Targets with target-specific Strong cluster corroboration before requiring agreement with their own rank-1 Person: **{cluster['strongTargetSpecificEvidenceCount']}**.",
        "- Target-specific cluster votes exclude the target itself and every member from the same exact-content group.",
        "- The private export does not contain durable `not same` constraints. Production cluster evidence already fails closed on those constraints, so this evaluator is intentionally more permissive on that dimension rather than crediting unavailable negative evidence.",
    ])

    if report.get("primaryCandidate") is not None:
        primary = report["primaryCandidate"]
        lines.extend([
            "",
            f"## Holdout quality guardrails for selection winner `{primary['name']}`",
            "",
            "| Segment | Targets | Assigned | Extra | False assignments | Conservative precision | Unknown assignments |",
            "|---|---:|---:|---:|---:|---:|---:|",
        ])
        for name, metrics in primary["qualitySegments"].items():
            lines.append(
                f"| {name} | {metrics['targetCount']} | {metrics['assignedCount']} | "
                f"{metrics['incrementalAssignedCount']} | {metrics['falseAutomaticAssignments']} | "
                f"{pct(metrics['conservativePrecision'])} | {metrics['unknownAssignments']} |"
            )

    lines.extend([
        "",
        "## Interpretation constraints",
        "",
        "This is policy evaluation, not production enablement. The current High rule remains the production baseline. A candidate must be judged on holdout false assignments and Unknown rejection, not only additional coverage or aggregate top-1 accuracy.",
        "",
        "The evaluator deliberately does not test centroids or globally capped reference sets because WI-0081 already measured material regressions for those families. It also does not create, persist or backfill canonical assignments.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Evaluate WI-0117 multi-evidence auto-assignment candidates privately."
    )
    parser.add_argument("sample", type=Path)
    parser.add_argument("--report-json", type=Path, default=Path("private/cluster-evaluation/auto-assignment-report.json"))
    parser.add_argument("--report-md", type=Path, default=Path("private/cluster-evaluation/auto-assignment-report.md"))
    parser.add_argument("--cluster-eps", type=float, default=DEFAULT_CLUSTER_EPS)
    parser.add_argument("--cluster-min-samples", type=int, default=DEFAULT_CLUSTER_MIN_SAMPLES)
    parser.add_argument("--batch-size", type=int, default=256)
    parser.add_argument("--shortlist-limit", type=int, default=6)
    args = parser.parse_args()
    if not (0 < args.cluster_eps < 2):
        parser.error("--cluster-eps must be between 0 and 2")
    if args.cluster_min_samples < 2:
        parser.error("--cluster-min-samples must be at least 2")
    if args.batch_size < 1:
        parser.error("--batch-size must be at least 1")
    if args.shortlist_limit < 1:
        parser.error("--shortlist-limit must be at least 1")

    payload, targets, target_embeddings, references, reference_embeddings, policy = load_sample(args.sample)
    records = rank_targets(
        targets,
        target_embeddings,
        references,
        reference_embeddings,
        policy,
        args.batch_size,
    )
    if len(records) != len(targets):
        raise ValueError("one or more targets could not be ranked against the reference population")
    cluster = add_cluster_evidence(
        records,
        targets,
        target_embeddings,
        policy,
        args.cluster_eps,
        args.cluster_min_samples,
    )

    baseline = {
        split: evaluate_rule(records, None, policy, split)
        for split in ("selection", "holdout", "all")
    }
    rules = candidate_rules(policy)
    selection_results = {
        rule.name: evaluate_rule(records, rule, policy, "selection")
        for rule in rules
    }
    shortlist = shortlist_rules(
        rules,
        selection_results,
        baseline["selection"],
        args.shortlist_limit,
    )

    selection_screen = []
    for rule in rules:
        metrics = selection_results[rule.name]
        selection_screen.append({
            "name": rule.name,
            "family": rule.family,
            "guardrailClass": guardrail_class(metrics, baseline["selection"]),
            "metrics": metrics,
        })
    class_order = {
        "strict-no-extra-false": 0,
        "rate-preserving": 1,
        "exploratory": 2,
        "no-expansion": 3,
    }
    selection_screen.sort(key=lambda item: (
        class_order[item["guardrailClass"]],
        -int(item["metrics"]["incrementalCorrectKnownAssignments"]),
        int(item["metrics"]["falseAutomaticAssignments"]),
        item["name"],
    ))

    shortlist_report = []
    for rule in shortlist:
        shortlist_report.append({
            "name": rule.name,
            "family": rule.family,
            "selectionGuardrailClass": guardrail_class(
                selection_results[rule.name], baseline["selection"]
            ),
            "selection": selection_results[rule.name],
            "holdout": evaluate_rule(records, rule, policy, "holdout"),
            "all": evaluate_rule(records, rule, policy, "all"),
        })

    primary = None
    if shortlist:
        winner = shortlist[0]
        primary = {
            "name": winner.name,
            "family": winner.family,
            "qualitySegments": quality_segments(records, winner, policy),
        }

    selection_count = sum(1 for record in records if record["split"] == "selection")
    holdout_count = len(records) - selection_count
    reference_labels = len({str(reference["groundTruthLabel"]) for reference in references})
    report = {
        "schemaVersion": 1,
        "sample": {
            "modelId": payload.get("modelId"),
            "modelHash": payload.get("modelHash"),
            "targetFaceCount": len(targets),
            "referenceFaceCount": len(references),
            "referenceLabelCount": reference_labels,
            "selectionTargetCount": selection_count,
            "holdoutTargetCount": holdout_count,
            "splitProcedure": "sha256(exact-content-group) mod 5; buckets 0-2 selection, 3-4 holdout",
            "duplicateResistantRanking": True,
        },
        "productionSuggestionPolicy": policy,
        "baseline": baseline,
        "clusterEvidence": cluster,
        "candidateGrid": {
            "ruleCount": len(rules),
            "families": sorted({rule.family for rule in rules}),
            "selectionGuardrails": [
                "strict-no-extra-false: no increase in wrong-known or Unknown assignments",
                "rate-preserving: known precision, conservative precision and Unknown assignment rate not worse than current High",
            ],
        },
        "selectionScreen": selection_screen,
        "shortlist": shortlist_report,
        "primaryCandidate": primary,
        "notes": [
            "Current High semantics are evaluated with duplicate-resistant references to avoid exact-content leakage during policy selection.",
            "Candidate policies only add Medium-or-better targets beyond current High; they never remove current High assignments in the simulation.",
            "Multi-reference evidence counts independent exact-content reference groups and therefore cannot be inflated by exact source copies.",
            "Cluster corroboration is target-specific: votes from the target and its exact-content group are removed before evaluating support.",
            "Private export lacks durable not-same cluster constraints; production already fails closed on them, so the private simulation is more permissive on that dimension.",
            "Holdout metrics are emitted only for candidates selected by predeclared selection-split guardrails.",
            "No production thresholds, policy rows or canonical assignments are modified by this evaluator.",
        ],
    }

    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_md.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    markdown = render_markdown(report)
    args.report_md.write_text(markdown, encoding="utf-8")
    print(markdown)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
