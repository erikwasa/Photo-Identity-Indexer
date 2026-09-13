#!/usr/bin/env python3
"""Private WI-0113 face-clustering evaluator.

Input is the pseudonymized biometric export produced by PhotoIdentity.ClusterEvaluation.
The input and generated reports are private operator data and must not be committed.
"""

from __future__ import annotations

import argparse
import json
import math
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable

import numpy as np
from sklearn.cluster import DBSCAN, HDBSCAN
from sklearn.metrics import pairwise_distances


@dataclass(frozen=True)
class Metrics:
    cluster_count: int
    clustered_faces: int
    noise_faces: int
    coverage: float
    labeled_coverage: float
    false_merge_pairs: int
    comparable_cluster_pairs: int
    false_merge_rate: float
    false_split_pairs: int
    same_identity_pairs: int
    false_split_rate: float
    same_photo_conflicts: int
    singleton_clusters: int
    minimum_cluster_size: int
    median_cluster_size: float
    maximum_cluster_size: int


@dataclass(frozen=True)
class CandidateResult:
    algorithm: str
    parameters: dict[str, Any]
    metrics: Metrics


def parse_csv_numbers(value: str, cast: type) -> list[Any]:
    items = [cast(item.strip()) for item in value.split(",") if item.strip()]
    if not items:
        raise argparse.ArgumentTypeError("parameter grid cannot be empty")
    return items


def load_sample(path: Path) -> tuple[dict[str, Any], np.ndarray, list[str | None], list[str]]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if payload.get("schemaVersion") != 1:
        raise ValueError("unsupported cluster-evaluation export schema")
    faces = payload.get("faces") or []
    if len(faces) < 2:
        raise ValueError("at least two exported faces are required")

    embeddings = np.asarray([face["embedding"] for face in faces], dtype=np.float64)
    if embeddings.ndim != 2 or embeddings.shape[1] == 0:
        raise ValueError("embeddings must be a rectangular non-empty matrix")
    if not np.isfinite(embeddings).all():
        raise ValueError("embeddings contain non-finite values")
    norms = np.linalg.norm(embeddings, axis=1)
    if np.any(norms <= 0):
        raise ValueError("embeddings contain a zero vector")
    embeddings = embeddings / norms[:, None]

    labels = [face.get("groundTruthLabel") for face in faces]
    photo_groups = [str(face["photoGroup"]) for face in faces]
    return payload, embeddings, labels, photo_groups


def connected_components(adjacency: list[set[int]], minimum_cluster_size: int) -> np.ndarray:
    labels = np.full(len(adjacency), -1, dtype=int)
    visited: set[int] = set()
    cluster_id = 0
    for start in range(len(adjacency)):
        if start in visited:
            continue
        stack = [start]
        component: list[int] = []
        visited.add(start)
        while stack:
            node = stack.pop()
            component.append(node)
            for neighbor in sorted(adjacency[node]):
                if neighbor not in visited:
                    visited.add(neighbor)
                    stack.append(neighbor)
        if len(component) >= minimum_cluster_size:
            for node in component:
                labels[node] = cluster_id
            cluster_id += 1
    return labels


def mutual_neighbor_graph(
    distances: np.ndarray,
    neighbor_count: int,
    maximum_distance: float,
    minimum_shared_neighbors: int,
    minimum_cluster_size: int,
) -> np.ndarray:
    point_count = distances.shape[0]
    k = min(neighbor_count, point_count - 1)
    ordered = np.argsort(distances, axis=1, kind="stable")
    neighbors = [set(row[1 : k + 1].tolist()) for row in ordered]
    adjacency = [set() for _ in range(point_count)]

    for left in range(point_count):
        for right in sorted(neighbors[left]):
            if right <= left:
                continue
            if left not in neighbors[right]:
                continue
            if distances[left, right] > maximum_distance:
                continue
            shared = len(neighbors[left].intersection(neighbors[right]))
            if shared < minimum_shared_neighbors:
                continue
            adjacency[left].add(right)
            adjacency[right].add(left)

    return connected_components(adjacency, minimum_cluster_size)


def choose2(value: int) -> int:
    return value * (value - 1) // 2


def evaluate(labels: np.ndarray, truth: list[str | None], photo_groups: list[str]) -> Metrics:
    clustered = labels >= 0
    cluster_ids = sorted(int(value) for value in np.unique(labels[clustered]))
    cluster_sizes = [int(np.count_nonzero(labels == cluster_id)) for cluster_id in cluster_ids]

    labeled_indices = [index for index, value in enumerate(truth) if value is not None]
    clustered_labeled = sum(1 for index in labeled_indices if labels[index] >= 0)

    false_merge_pairs = 0
    comparable_cluster_pairs = 0
    same_photo_conflicts = 0
    for cluster_id in cluster_ids:
        members = [
            index
            for index in range(len(labels))
            if labels[index] == cluster_id and truth[index] is not None
        ]
        comparable_cluster_pairs += choose2(len(members))
        for left_pos, left in enumerate(members):
            for right in members[left_pos + 1 :]:
                if truth[left] != truth[right]:
                    false_merge_pairs += 1
                    if photo_groups[left] == photo_groups[right]:
                        same_photo_conflicts += 1

    by_identity: dict[str, list[int]] = {}
    for index, value in enumerate(truth):
        if value is not None:
            by_identity.setdefault(value, []).append(index)

    false_split_pairs = 0
    same_identity_pairs = 0
    for members in by_identity.values():
        same_identity_pairs += choose2(len(members))
        for left_pos, left in enumerate(members):
            for right in members[left_pos + 1 :]:
                same_cluster = labels[left] >= 0 and labels[left] == labels[right]
                if not same_cluster:
                    false_split_pairs += 1

    face_count = len(labels)
    clustered_faces = int(np.count_nonzero(clustered))
    noise_faces = face_count - clustered_faces
    return Metrics(
        cluster_count=len(cluster_ids),
        clustered_faces=clustered_faces,
        noise_faces=noise_faces,
        coverage=clustered_faces / face_count,
        labeled_coverage=(clustered_labeled / len(labeled_indices)) if labeled_indices else 0.0,
        false_merge_pairs=false_merge_pairs,
        comparable_cluster_pairs=comparable_cluster_pairs,
        false_merge_rate=(false_merge_pairs / comparable_cluster_pairs) if comparable_cluster_pairs else 0.0,
        false_split_pairs=false_split_pairs,
        same_identity_pairs=same_identity_pairs,
        false_split_rate=(false_split_pairs / same_identity_pairs) if same_identity_pairs else 0.0,
        same_photo_conflicts=same_photo_conflicts,
        singleton_clusters=sum(1 for size in cluster_sizes if size == 1),
        minimum_cluster_size=min(cluster_sizes) if cluster_sizes else 0,
        median_cluster_size=float(np.median(cluster_sizes)) if cluster_sizes else 0.0,
        maximum_cluster_size=max(cluster_sizes) if cluster_sizes else 0,
    )


def candidate_sort_key(candidate: CandidateResult) -> tuple[Any, ...]:
    metrics = candidate.metrics
    return (
        metrics.false_merge_pairs,
        metrics.false_merge_rate,
        -metrics.labeled_coverage,
        metrics.false_split_rate,
        metrics.noise_faces,
        candidate.algorithm,
        json.dumps(candidate.parameters, sort_keys=True),
    )


def render_markdown(report: dict[str, Any]) -> str:
    selected = report["selectedCandidate"]
    metrics = selected["metrics"]
    lines = [
        "# Private provisional face-cluster evaluation",
        "",
        "> PRIVATE: this report is derived from biometric evaluation data. Do not commit it.",
        "",
        f"- Exact model: `{report['sample']['modelId']}` / `{report['sample']['modelHash']}`",
        f"- Faces: {report['sample']['faceCount']}",
        f"- Assigned labels: {report['sample']['assignedLabelCount']}",
        f"- Unknown faces: {report['sample']['unknownFaceCount']}",
        f"- Pairwise cosine-distance calculation: {report['benchmark']['pairwiseDistanceSeconds']:.3f} s",
        "",
        "## Conservative candidate",
        "",
        f"- Algorithm: **{selected['algorithm']}**",
        f"- Parameters: `{json.dumps(selected['parameters'], sort_keys=True)}`",
        f"- False-merge pairs: **{metrics['false_merge_pairs']}** / {metrics['comparable_cluster_pairs']}",
        f"- False-merge rate: **{metrics['false_merge_rate']:.6f}**",
        f"- False-split rate: {metrics['false_split_rate']:.6f}",
        f"- Labeled coverage: {metrics['labeled_coverage']:.3f}",
        f"- Noise rate: {metrics['noise_faces'] / report['sample']['faceCount']:.3f}",
        f"- Same-photo conflicting merges: **{metrics['same_photo_conflicts']}**",
        "",
        "## Interpretation",
        "",
        "False merges are the primary selection risk. The evaluator therefore orders candidates by false-merge count/rate before coverage or split rate. The displayed candidate is a recommendation for maintainer inspection, not an automatic production-policy decision.",
        "",
        "Unknown faces participate as unlabeled points when present. They affect coverage/noise but are excluded from identity-labelled false-merge/false-split denominators.",
        "",
        "Age, pose, and image-quality stratification is not inferred from embeddings. Record those observations manually if the private reviewed sample contains enough known variation.",
        "",
        "## Candidate table",
        "",
        "| Algorithm | Parameters | False merges | False split rate | Labeled coverage | Noise | Clusters |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]
    for candidate in report["candidates"][:30]:
        cm = candidate["metrics"]
        lines.append(
            f"| {candidate['algorithm']} | `{json.dumps(candidate['parameters'], sort_keys=True)}` | "
            f"{cm['false_merge_pairs']} | {cm['false_split_rate']:.4f} | "
            f"{cm['labeled_coverage']:.3f} | {cm['noise_faces']} | {cm['cluster_count']} |"
        )
    lines.extend(
        [
            "",
            "## WI-0114 scaling decision",
            "",
            "The benchmark projection is diagnostic only. Record the maintainer decision after reviewing measured local timings: use PostgreSQL exact/vector-neighbour search if it meets the interactive/incremental budget; introduce ANN indexing only if measured exact-neighbour retrieval does not.",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate conservative provisional face clustering on a private reviewed sample.")
    parser.add_argument("sample", type=Path)
    parser.add_argument("--report-json", type=Path, default=Path("private/cluster-evaluation/report.json"))
    parser.add_argument("--report-md", type=Path, default=Path("private/cluster-evaluation/report.md"))
    parser.add_argument("--dbscan-eps", default="0.18,0.22,0.26,0.30,0.34")
    parser.add_argument("--hdbscan-min-cluster-size", default="2,3,4,5,8")
    parser.add_argument("--min-samples", default="2,3,4")
    parser.add_argument("--graph-k", default="3,5,8,12")
    parser.add_argument("--graph-max-distance", default="0.18,0.22,0.26,0.30")
    parser.add_argument("--graph-min-shared", default="0,1,2")
    parser.add_argument("--expected-face-count", type=int, default=10000)
    args = parser.parse_args()

    payload, embeddings, truth, photo_groups = load_sample(args.sample)

    start = time.perf_counter()
    distances = pairwise_distances(embeddings, metric="cosine", n_jobs=-1)
    distances = np.clip(distances, 0.0, 2.0)
    pairwise_seconds = time.perf_counter() - start

    candidates: list[CandidateResult] = []
    min_samples_values = parse_csv_numbers(args.min_samples, int)

    for eps in parse_csv_numbers(args.dbscan_eps, float):
        for min_samples in min_samples_values:
            labels = DBSCAN(eps=eps, min_samples=min_samples, metric="precomputed").fit_predict(distances)
            candidates.append(
                CandidateResult(
                    "dbscan",
                    {"eps": eps, "minSamples": min_samples},
                    evaluate(labels, truth, photo_groups),
                )
            )

    for minimum_cluster_size in parse_csv_numbers(args.hdbscan_min_cluster_size, int):
        for min_samples in min_samples_values:
            if min_samples > embeddings.shape[0]:
                continue
            labels = HDBSCAN(
                min_cluster_size=minimum_cluster_size,
                min_samples=min_samples,
                metric="precomputed",
                cluster_selection_method="eom",
                allow_single_cluster=False,
                copy=True,
            ).fit_predict(distances)
            candidates.append(
                CandidateResult(
                    "hdbscan",
                    {"minimumClusterSize": minimum_cluster_size, "minSamples": min_samples},
                    evaluate(labels, truth, photo_groups),
                )
            )

    for k in parse_csv_numbers(args.graph_k, int):
        for maximum_distance in parse_csv_numbers(args.graph_max_distance, float):
            for minimum_shared in parse_csv_numbers(args.graph_min_shared, int):
                for minimum_cluster_size in (2, 3, 4):
                    labels = mutual_neighbor_graph(
                        distances,
                        k,
                        maximum_distance,
                        minimum_shared,
                        minimum_cluster_size,
                    )
                    candidates.append(
                        CandidateResult(
                            "mutual-neighbor-graph",
                            {
                                "neighborCount": k,
                                "maximumDistance": maximum_distance,
                                "minimumSharedNeighbors": minimum_shared,
                                "minimumClusterSize": minimum_cluster_size,
                            },
                            evaluate(labels, truth, photo_groups),
                        )
                    )

    if not candidates:
        raise RuntimeError("no clustering candidates were evaluated")
    candidates.sort(key=candidate_sort_key)
    selected = candidates[0]

    face_count = embeddings.shape[0]
    measured_pairs = face_count * face_count
    expected_pairs = args.expected_face_count * args.expected_face_count
    projected_seconds = pairwise_seconds * (expected_pairs / measured_pairs) if measured_pairs else math.inf
    report = {
        "schemaVersion": 1,
        "sample": {
            "modelId": payload["modelId"],
            "modelHash": payload["modelHash"],
            "faceCount": payload["faceCount"],
            "assignedLabelCount": payload["assignedLabelCount"],
            "unknownFaceCount": payload["unknownFaceCount"],
        },
        "selectionRule": "minimize false-merge pairs/rate first, then maximize labeled coverage and minimize false splits/noise",
        "selectedCandidate": asdict(selected),
        "benchmark": {
            "pairwiseDistanceSeconds": pairwise_seconds,
            "measuredFaceCount": face_count,
            "expectedFaceCount": args.expected_face_count,
            "projectedPairwiseDistanceSecondsAtExpectedScale": projected_seconds,
            "annDecision": "maintainer-verification-required",
        },
        "variationAnalysis": {
            "age": "manual-private-sample-observation-required",
            "pose": "manual-private-sample-observation-required",
            "imageQuality": "manual-private-sample-observation-required",
        },
        "candidates": [asdict(candidate) for candidate in candidates],
    }

    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_md.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    args.report_md.write_text(render_markdown(report), encoding="utf-8")

    print(render_markdown(report))
    print(f"\nPrivate JSON report: {args.report_json}")
    print(f"Private Markdown report: {args.report_md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
