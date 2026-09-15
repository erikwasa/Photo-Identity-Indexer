#!/usr/bin/env python3
"""Audit WI-0081 source-content duplication without confusing group photos for duplicates.

Consumes the pseudonymized private export produced by PhotoIdentity.ClusterEvaluation.
The input contains biometric embeddings and must remain local/private. Output contains
aggregate counts only and does not modify production catalogue state.
"""

from __future__ import annotations

import argparse
import json
import math
from collections import defaultdict
from pathlib import Path
from statistics import median
from typing import Any

import numpy as np

NEAR_DUPLICATE_SIMILARITY = 0.95


def load_sample(path: Path) -> tuple[dict[str, Any], list[dict[str, Any]], list[dict[str, Any]]]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if payload.get("schemaVersion") != 1:
        raise ValueError("unsupported cluster-evaluation export schema")

    targets = payload.get("faces") or []
    references = payload.get("referenceFaces") or []
    if not references:
        raise ValueError("referenceFaces are required; re-export the WI-0081 sample from current main")
    if any(reference.get("groundTruthLabel") is None for reference in references):
        raise ValueError("all production reference rows must have a ground-truth Person label")
    return payload, targets, references


def normalized_vector(row: dict[str, Any]) -> np.ndarray:
    vector = np.asarray(row.get("embedding"), dtype=np.float64)
    if vector.ndim != 1 or vector.size == 0 or not np.isfinite(vector).all():
        raise ValueError("embeddings must be finite one-dimensional vectors")
    norm = float(np.linalg.norm(vector))
    if norm <= 0 or not math.isfinite(norm):
        raise ValueError("embeddings contain a zero vector")
    return vector / norm


def groups_spanning_multiple_photos(rows: list[dict[str, Any]]) -> dict[str, list[int]]:
    by_content: dict[str, list[int]] = defaultdict(list)
    for index, row in enumerate(rows):
        content = row.get("contentGroup")
        photo = row.get("photoGroup")
        if content is None or photo is None:
            continue
        by_content[str(content)].append(index)
    return {
        content: indices
        for content, indices in by_content.items()
        if len({str(rows[index]["photoGroup"]) for index in indices}) > 1
    }


def audit_references(references: list[dict[str, Any]]) -> dict[str, Any]:
    by_content: dict[str, list[int]] = defaultdict(list)
    by_person: dict[str, list[int]] = defaultdict(list)
    by_person_content: dict[tuple[str, str], list[int]] = defaultdict(list)

    for index, reference in enumerate(references):
        content = str(reference.get("contentGroup") or f"missing-content-{index}")
        person = str(reference["groundTruthLabel"])
        by_content[content].append(index)
        by_person[person].append(index)
        by_person_content[(person, content)].append(index)

    repeated_content = {
        content: indices
        for content, indices in by_content.items()
        if len({str(references[index].get("photoGroup")) for index in indices}) > 1
    }
    repeated_person_content = {
        key: indices
        for key, indices in by_person_content.items()
        if len({str(references[index].get("photoGroup")) for index in indices}) > 1
    }

    near_duplicate_cross_label_pairs = 0
    maximum_cross_label_similarity: float | None = None
    for indices in repeated_content.values():
        vectors = {index: normalized_vector(references[index]) for index in indices}
        for position, left_index in enumerate(indices):
            left = references[left_index]
            for right_index in indices[position + 1:]:
                right = references[right_index]
                if left.get("photoGroup") == right.get("photoGroup"):
                    continue
                if left.get("groundTruthLabel") == right.get("groundTruthLabel"):
                    continue
                similarity = float(vectors[left_index] @ vectors[right_index])
                maximum_cross_label_similarity = (
                    similarity
                    if maximum_cross_label_similarity is None
                    else max(maximum_cross_label_similarity, similarity)
                )
                if similarity >= NEAR_DUPLICATE_SIMILARITY:
                    near_duplicate_cross_label_pairs += 1

    sizes = sorted(len(indices) for indices in by_person.values())
    total = sum(sizes)
    top_count = max(1, math.ceil(len(sizes) * 0.10)) if sizes else 0
    top_share = sum(sorted(sizes, reverse=True)[:top_count]) / total if total else 0.0
    merge_labels = {
        str(reference["groundTruthLabel"])
        for reference in references
        if reference.get(
            "reviewedPersonHasMergeHistory",
            reference.get("reviewedPersonWasMerged", False),
        )
    }

    return {
        "referenceContentGroups": len(by_content),
        "contentGroupsWithMultiplePhotoRevisions": len(repeated_content),
        "photoRevisionsInRepeatedContentGroups": sum(
            len({str(references[index].get("photoGroup")) for index in indices})
            for indices in repeated_content.values()
        ),
        "referenceFacesInRepeatedContentGroups": sum(
            len(indices) for indices in repeated_content.values()
        ),
        "personContentGroupsRepeatedAcrossPhotoRevisions": len(repeated_person_content),
        "referenceFacesInRepeatedPersonContentGroups": sum(
            len(indices) for indices in repeated_person_content.values()
        ),
        "crossLabelNearDuplicateFacePairCandidatesAt095": near_duplicate_cross_label_pairs,
        "maximumCrossLabelSimilarityAcrossRepeatedContent": maximum_cross_label_similarity,
        "identitiesWithMergeHistory": len(merge_labels),
        "referenceFacesForIdentitiesWithMergeHistory": sum(
            1
            for reference in references
            if str(reference["groundTruthLabel"]) in merge_labels
        ),
        "identityCount": len(sizes),
        "referenceCountMedian": median(sizes) if sizes else 0,
        "referenceCountMaximum": max(sizes) if sizes else 0,
        "topTenPercentIdentityReferenceShare": top_share,
    }


def audit_targets(targets: list[dict[str, Any]]) -> dict[str, Any]:
    repeated_content = groups_spanning_multiple_photos(targets)
    return {
        "targetContentGroupsWithMultiplePhotoRevisions": len(repeated_content),
        "targetFacesInRepeatedContentGroups": sum(len(indices) for indices in repeated_content.values()),
    }


def evaluate(path: Path) -> dict[str, Any]:
    payload, targets, references = load_sample(path)
    return {
        "schemaVersion": 1,
        "sample": {
            "modelId": payload.get("modelId"),
            "modelHash": payload.get("modelHash"),
            "targetFaceCount": len(targets),
            "referenceFaceCount": len(references),
        },
        "referenceAudit": audit_references(references),
        "targetAudit": audit_targets(targets),
        "notes": [
            "contentGroup is an exact source-image content hash; multiple faces from one group photo share one contentGroup and one photoGroup.",
            "A repeated source-content group therefore requires the same contentGroup to span more than one photoGroup.",
            "Repeated person-content groups measure same-identity reference evidence repeated across exact-content source copies.",
            "Cross-label near-duplicate pairs are conservative review candidates, not proof of a bad label; they require different photoGroups, different labels, the same exact source content and cosine similarity >= 0.95.",
            "Reference concentration and merge-history counts are descriptive audit evidence and do not change production matching.",
        ],
    }


def pct(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.3%}"


def number(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.4f}"


def render_markdown(report: dict[str, Any]) -> str:
    sample = report["sample"]
    audit = report["referenceAudit"]
    targets = report["targetAudit"]
    lines = [
        "# Private WI-0081 source-content audit",
        "",
        "> PRIVATE: aggregate output derived from local biometric/review data. Do not commit the generated report.",
        "",
        f"- Exact model: `{sample['modelId']}` / `{sample['modelHash']}`",
        f"- Reviewed targets: {sample['targetFaceCount']}",
        f"- Confirmed production references: {sample['referenceFaceCount']}",
        "",
        "## Exact-content source-copy audit",
        "",
        f"- Reference content groups spanning multiple photo revisions: **{audit['contentGroupsWithMultiplePhotoRevisions']}**",
        f"- Photo revisions in those repeated-content groups: **{audit['photoRevisionsInRepeatedContentGroups']}**",
        f"- Reference faces in repeated-content groups: **{audit['referenceFacesInRepeatedContentGroups']}**",
        f"- Same-identity content groups repeated across photo revisions: **{audit['personContentGroupsRepeatedAcrossPhotoRevisions']}**",
        f"- Reference faces in repeated same-identity content groups: **{audit['referenceFacesInRepeatedPersonContentGroups']}**",
        f"- Cross-label near-duplicate face-pair candidates (cosine >= {NEAR_DUPLICATE_SIMILARITY:.2f}): **{audit['crossLabelNearDuplicateFacePairCandidatesAt095']}**",
        f"- Maximum cross-label similarity across repeated content: **{number(audit['maximumCrossLabelSimilarityAcrossRepeatedContent'])}**",
        f"- Target content groups spanning multiple photo revisions: **{targets['targetContentGroupsWithMultiplePhotoRevisions']}** ({targets['targetFacesInRepeatedContentGroups']} target faces)",
        "",
        "## Reference population",
        "",
        f"- Identities with merge history: **{audit['identitiesWithMergeHistory']}** ({audit['referenceFacesForIdentitiesWithMergeHistory']} references)",
        f"- Median / maximum references per identity: **{audit['referenceCountMedian']} / {audit['referenceCountMaximum']}**",
        f"- Reference share held by largest 10% of identities: **{pct(audit['topTenPercentIdentityReferenceShare'])}**",
        "",
        "## Interpretation constraint",
        "",
        "Multiple faces in one group photo are not duplicates. This audit only calls exact content repeated when the same content hash occurs in more than one pseudonymized photo revision. Cross-label near-duplicate pairs are candidates for review, not automatic evidence that either assignment is wrong.",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Audit exact-content source-copy duplication for the private WI-0081 sample."
    )
    parser.add_argument("sample", type=Path)
    parser.add_argument(
        "--report-json",
        type=Path,
        default=Path("private/cluster-evaluation/suggestion-content-audit.json"),
    )
    parser.add_argument(
        "--report-md",
        type=Path,
        default=Path("private/cluster-evaluation/suggestion-content-audit.md"),
    )
    args = parser.parse_args()

    report = evaluate(args.sample)
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_md.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    markdown = render_markdown(report)
    args.report_md.write_text(markdown, encoding="utf-8")
    print(markdown)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
