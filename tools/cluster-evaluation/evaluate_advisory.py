#!/usr/bin/env python3
"""Private WI-0116 cluster-assisted known-person advisory evaluator.

Consumes the pseudonymized exact-model export produced by PhotoIdentity.ClusterEvaluation.
Input and reports contain biometric-derived data and must remain outside the repository.
"""

from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median
from typing import Any

import numpy as np
from sklearn.cluster import DBSCAN
from sklearn.metrics import pairwise_distances


def load_sample(path: Path) -> tuple[dict[str, Any], np.ndarray, list[str | None]]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if payload.get("schemaVersion") != 1:
        raise ValueError("unsupported cluster-evaluation export schema")
    faces = payload.get("faces") or []
    if len(faces) < 3:
        raise ValueError("at least three exported faces are required")
    embeddings = np.asarray([face["embedding"] for face in faces], dtype=np.float64)
    if embeddings.ndim != 2 or embeddings.shape[1] == 0 or not np.isfinite(embeddings).all():
        raise ValueError("embeddings must be a finite rectangular matrix")
    norms = np.linalg.norm(embeddings, axis=1)
    if np.any(norms <= 0):
        raise ValueError("embeddings contain a zero vector")
    embeddings = embeddings / norms[:, None]
    truth = [face.get("groundTruthLabel") for face in faces]
    return payload, embeddings, truth


def top_known_person_evidence(
    similarities: np.ndarray,
    truth: list[str | None],
) -> list[tuple[str, float, float | None] | None]:
    labelled_by_person: dict[str, list[int]] = defaultdict(list)
    for index, label in enumerate(truth):
        if label is not None:
            labelled_by_person[label].append(index)

    evidence: list[tuple[str, float, float | None] | None] = []
    for target in range(len(truth)):
        candidates: list[tuple[str, float]] = []
        for person, exemplar_indices in labelled_by_person.items():
            usable = [index for index in exemplar_indices if index != target]
            if not usable:
                continue
            candidates.append((person, max(float(similarities[target, index]) for index in usable)))
        candidates.sort(key=lambda item: (-item[1], item[0]))
        if not candidates:
            evidence.append(None)
            continue
        margin = candidates[0][1] - candidates[1][1] if len(candidates) > 1 else None
        evidence.append((candidates[0][0], candidates[0][1], margin))
    return evidence


def classify_cluster(
    members: list[int],
    core_indices: set[int],
    evidence: list[tuple[str, float, float | None] | None],
    *,
    medium_score: float,
    minimum_support_count: int,
    minimum_support_share: float,
    minimum_core_share: float,
    maximum_competing_count: int,
    maximum_competing_share: float,
) -> dict[str, Any]:
    qualifying = [
        item for index in members
        if (item := evidence[index]) is not None and item[1] >= medium_score
    ]
    votes = Counter(item[0] for item in qualifying)
    ordered_people = sorted(votes, key=lambda person: (-votes[person], person))
    candidate = ordered_people[0] if ordered_people else None
    competitor = ordered_people[1] if len(ordered_people) > 1 else None
    candidate_count = votes[candidate] if candidate else 0
    competitor_count = votes[competitor] if competitor else 0
    candidate_share = candidate_count / len(members)
    competitor_share = competitor_count / len(members)
    core_share = sum(1 for index in members if index in core_indices) / len(members)

    if candidate is None:
        status, reason = "insufficient", "no Medium-or-better rank-1 evidence"
    elif candidate_count < minimum_support_count:
        status, reason = "insufficient", "too few independent supporting members"
    elif candidate_share < minimum_support_share:
        status, reason = "insufficient", "candidate support share below policy"
    elif core_share < minimum_core_share:
        status, reason = "ambiguous", "cluster Core share below policy"
    elif competitor is not None and (
        competitor_count > maximum_competing_count or competitor_share > maximum_competing_share
    ):
        status, reason = "ambiguous", "material competing-person support"
    else:
        status, reason = "strong", "multiple independent members agree without material competition"

    candidate_scores = sorted(item[1] for item in qualifying if item[0] == candidate) if candidate else []
    return {
        "status": status,
        "reason": reason,
        "candidate": candidate,
        "candidateSupportCount": candidate_count,
        "candidateSupportShare": candidate_share,
        "candidateMedianScore": median(candidate_scores) if candidate_scores else None,
        "competitor": competitor,
        "competitorSupportCount": competitor_count,
        "competitorSupportShare": competitor_share,
        "coreShare": core_share,
        "rankedEvidenceCount": sum(1 for index in members if evidence[index] is not None),
        "qualifyingEvidenceCount": len(qualifying),
    }


def evaluate(args: argparse.Namespace) -> dict[str, Any]:
    payload, embeddings, truth = load_sample(args.sample)
    distances = np.clip(pairwise_distances(embeddings, metric="cosine", n_jobs=-1), 0.0, 2.0)
    similarities = 1.0 - distances
    dbscan = DBSCAN(
        eps=args.cluster_eps,
        min_samples=args.cluster_min_samples,
        metric="precomputed",
    ).fit(distances)
    labels = dbscan.labels_
    core_indices = set(int(value) for value in dbscan.core_sample_indices_)
    evidence = top_known_person_evidence(similarities, truth)

    cluster_ids = sorted(int(value) for value in np.unique(labels) if value >= 0)
    statuses = Counter()
    strong_total = 0
    correct_strong = 0
    false_person_proposals = 0
    evaluable_strong = 0
    opportunity_clusters = 0
    correct_opportunity_clusters = 0
    baseline_face_reviews = 0
    cluster_review_tasks = 0
    mixed_clusters = 0
    mixed_strong = 0

    for cluster_id in cluster_ids:
        members = [index for index, label in enumerate(labels) if int(label) == cluster_id]
        result = classify_cluster(
            members,
            core_indices,
            evidence,
            medium_score=args.medium_score_threshold,
            minimum_support_count=args.minimum_support_count,
            minimum_support_share=args.minimum_support_share,
            minimum_core_share=args.minimum_core_share,
            maximum_competing_count=args.maximum_competing_support_count,
            maximum_competing_share=args.maximum_competing_support_share,
        )
        statuses[result["status"]] += 1
        if result["status"] == "strong":
            strong_total += 1

        labelled_members = [truth[index] for index in members if truth[index] is not None]
        distinct_truth = sorted(set(labelled_members))
        mixed = len(distinct_truth) > 1
        if mixed:
            mixed_clusters += 1
            if result["status"] == "strong":
                mixed_strong += 1

        if labelled_members and result["status"] == "strong":
            evaluable_strong += 1
            correct = len(distinct_truth) == 1 and result["candidate"] == distinct_truth[0]
            if correct:
                correct_strong += 1
            else:
                false_person_proposals += 1

        # A conservative recall opportunity requires at least the policy's minimum number
        # of reviewed members and one unambiguous reviewed identity in the discovered cluster.
        if len(labelled_members) >= args.minimum_support_count and len(distinct_truth) == 1:
            opportunity_clusters += 1
            baseline_face_reviews += len(labelled_members)
            if result["status"] == "strong" and result["candidate"] == distinct_truth[0]:
                correct_opportunity_clusters += 1
                cluster_review_tasks += 1
            else:
                cluster_review_tasks += len(labelled_members)

    precision = correct_strong / evaluable_strong if evaluable_strong else 0.0
    recall = correct_opportunity_clusters / opportunity_clusters if opportunity_clusters else 0.0
    saved = baseline_face_reviews - cluster_review_tasks
    compression = baseline_face_reviews / cluster_review_tasks if cluster_review_tasks else 0.0

    return {
        "schemaVersion": 1,
        "sample": {
            "modelId": payload["modelId"],
            "modelHash": payload["modelHash"],
            "faceCount": payload["faceCount"],
            "assignedLabelCount": payload["assignedLabelCount"],
            "unknownFaceCount": payload["unknownFaceCount"],
        },
        "clusterPolicy": {
            "algorithm": "dbscan",
            "eps": args.cluster_eps,
            "minSamples": args.cluster_min_samples,
        },
        "advisoryPolicy": {
            "version": "m25-cluster-known-person-v1",
            "ordinaryMediumScoreThreshold": args.medium_score_threshold,
            "minimumSupportCount": args.minimum_support_count,
            "minimumSupportShare": args.minimum_support_share,
            "minimumCoreShare": args.minimum_core_share,
            "maximumCompetingSupportCount": args.maximum_competing_support_count,
            "maximumCompetingSupportShare": args.maximum_competing_support_share,
        },
        "results": {
            "clusterCount": len(cluster_ids),
            "strongClusters": statuses["strong"],
            "ambiguousClusters": statuses["ambiguous"],
            "insufficientClusters": statuses["insufficient"],
            "evaluableStrongProposals": evaluable_strong,
            "correctStrongProposals": correct_strong,
            "falsePersonProposals": false_person_proposals,
            "proposalPrecision": precision,
            "pureReviewedOpportunityClusters": opportunity_clusters,
            "correctStrongOpportunityClusters": correct_opportunity_clusters,
            "opportunityRecall": recall,
            "mixedReviewedClusters": mixed_clusters,
            "mixedClustersClassifiedStrong": mixed_strong,
            "baselineIndividualFaceReviews": baseline_face_reviews,
            "estimatedClusterAssistedReviewTasks": cluster_review_tasks,
            "estimatedReviewActionsSaved": saved,
            "estimatedReviewCompression": compression,
        },
        "notes": [
            "Ground truth uses only pseudonymized reviewed Person labels; Unknown faces remain unlabeled.",
            "A strong proposal is counted correct only when every reviewed member in that cluster has one identity and the proposal matches it.",
            "Per-face known-person evidence is simulated by best similarity to another reviewed exemplar of each Person; the target face itself is excluded.",
            "Production not-same constraints are not present in the WI-0113 export; their fail-closed behavior is covered by automated production tests.",
            "Review-effort numbers are comparative estimates, not observed operator timing.",
        ],
    }


def render_markdown(report: dict[str, Any]) -> str:
    r = report["results"]
    p = report["advisoryPolicy"]
    c = report["clusterPolicy"]
    return "\n".join([
        "# Private WI-0116 cluster-assisted known-person evaluation",
        "",
        "> PRIVATE: derived from local biometric/review data. Do not commit this report.",
        "",
        f"- Exact model: `{report['sample']['modelId']}` / `{report['sample']['modelHash']}`",
        f"- Faces: {report['sample']['faceCount']}",
        f"- Production cluster policy: DBSCAN eps={c['eps']}, min_samples={c['minSamples']}",
        f"- Advisory policy: `{p['version']}`; Medium threshold={p['ordinaryMediumScoreThreshold']:.2f}, minimum support={p['minimumSupportCount']} / {p['minimumSupportShare']:.0%}",
        "",
        "## Advisory outcome",
        "",
        f"- Strong / ambiguous / insufficient clusters: **{r['strongClusters']} / {r['ambiguousClusters']} / {r['insufficientClusters']}**",
        f"- Evaluable strong proposals: {r['evaluableStrongProposals']}",
        f"- Correct strong proposals: **{r['correctStrongProposals']}**",
        f"- False-person proposals: **{r['falsePersonProposals']}**",
        f"- Proposal precision: **{r['proposalPrecision']:.3%}**",
        f"- Pure reviewed opportunity recall: **{r['opportunityRecall']:.3%}** ({r['correctStrongOpportunityClusters']} / {r['pureReviewedOpportunityClusters']})",
        f"- Mixed reviewed clusters classified Strong: **{r['mixedClustersClassifiedStrong']}** / {r['mixedReviewedClusters']}",
        "",
        "## Estimated review effort",
        "",
        f"- Baseline individual reviewed-face actions in opportunities: {r['baselineIndividualFaceReviews']}",
        f"- Estimated cluster-assisted tasks: {r['estimatedClusterAssistedReviewTasks']}",
        f"- Estimated actions saved: **{r['estimatedReviewActionsSaved']}**",
        f"- Estimated compression: **{r['estimatedReviewCompression']:.2f}x**",
        "",
        "A strong proposal is considered correct only when all reviewed members of the discovered cluster share one identity and the advisory candidate matches it. Mixed-cluster strong proposals therefore count as false-person proposals.",
        "",
        "The evaluator does not contain production not-same constraints; that fail-closed path is verified in automated tests. Review-effort numbers are comparative estimates rather than observed operator timing.",
        "",
    ])


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate WI-0116 advisory identity evidence on a private reviewed sample.")
    parser.add_argument("sample", type=Path)
    parser.add_argument("--report-json", type=Path, default=Path("private/cluster-evaluation/advisory-report.json"))
    parser.add_argument("--report-md", type=Path, default=Path("private/cluster-evaluation/advisory-report.md"))
    parser.add_argument("--cluster-eps", type=float, default=0.30)
    parser.add_argument("--cluster-min-samples", type=int, default=3)
    parser.add_argument("--medium-score-threshold", type=float, default=0.50)
    parser.add_argument("--minimum-support-count", type=int, default=3)
    parser.add_argument("--minimum-support-share", type=float, default=0.60)
    parser.add_argument("--minimum-core-share", type=float, default=0.60)
    parser.add_argument("--maximum-competing-support-count", type=int, default=1)
    parser.add_argument("--maximum-competing-support-share", type=float, default=0.20)
    args = parser.parse_args()

    report = evaluate(args)
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_md.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    args.report_md.write_text(render_markdown(report), encoding="utf-8")
    print(render_markdown(report))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
