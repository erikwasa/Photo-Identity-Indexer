#!/usr/bin/env python3
"""Read-only triage of failed original/proxy pairs; NO model or pixel inference.

Read metadata headers and verify original SHA-256, without decoding pixels,
accessing human labels or exposing source keys. This cannot establish the
stage at which the previous combined decode/inference errors occurred.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
import math
from pathlib import Path
import statistics

from original_proxy_study import (
    PROTOCOL, SUPPORTED, load, safe_original, validate_selection,
)

PROVENANCE = "wi0193-original-proxy-header-triage-v1"


def validate_previous(benchmark, selection, previous):
    photos = validate_selection(benchmark, selection)
    if (previous.get("protocol") != PROTOCOL
            or previous.get("benchmarkId") != benchmark["benchmarkId"]
            or previous.get("selectionSha256") != selection["selectionSha256"]
            or previous.get("isProbe") is True
            or previous.get("maxOriginalLongEdge") is not None):
        raise ValueError("Expected the original complete-size study for this frozen selection")
    rows = previous.get("photos")
    if not isinstance(rows, list) or len(rows) != len(photos):
        raise ValueError("The earlier study must contain every selected photo")
    if previous.get("plannedPairs", len(photos)) != len(photos):
        raise ValueError("The earlier study did not cover the entire selection")
    validated = []
    for photo, row in zip(photos, rows):
        if (row.get("revisionId", "").casefold() != photo["revisionId"].casefold()
                or row.get("mediaFamily") != photo["mediaFamily"]):
            raise ValueError("Score rows do not match the selected benchmark order")
        error = row.get("errorCode")
        x, y = row.get("originalScore"), row.get("proxyScore")
        if error is None:
            if (type(x) not in (int, float) or type(y) not in (int, float)
                    or not math.isfinite(x) or not math.isfinite(y)):
                raise ValueError("Successful earlier row does not have two finite scores")
            status = "success"
        elif (isinstance(error, str) and error
              and x is None and y is None):
            status = "failed"
        else:
            raise ValueError("Malformed earlier per-photo error/score row")
        validated.append((photo, status))
    return validated


def dimensions(path, family):
    """Only open format headers; never load image pixels or run a model."""
    if family == "raw":
        import rawpy
        with rawpy.imread(str(path)) as source:
            width = int(source.sizes.width)
            height = int(source.sizes.height)
    else:
        if family == "heic":
            from pillow_heif import register_heif_opener
            register_heif_opener(thumbnails=False)
        from PIL import Image
        with Image.open(path) as source:
            width, height = source.size
    if width <= 0 or height <= 0:
        raise ValueError("Image dimensions invalid")
    return width, height


def triage(benchmark, selection, previous, original_root, scope="failed",
           dimension_reader=None):
    if scope not in ("failed", "all"):
        raise ValueError("Scope must be failed or all")
    verified = validate_previous(benchmark, selection, previous)
    root = Path(original_root).resolve(strict=True)
    if not root.is_dir():
        raise ValueError("Expected original source root directory")
    reader = dimensions if dimension_reader is None else dimension_reader
    results = []
    for photo, status in verified:
        if scope == "failed" and status != "failed":
            continue
        family = photo["mediaFamily"]
        if family not in SUPPORTED:
            raise ValueError("Unknown media family")
        try:
            original = safe_original(root, photo["sourceKey"], photo["contentSha256"])
        except Exception as exc:
            # Integrity checks are a hard stop; do not leak paths/exceptions.
            raise ValueError(
                "Original source identity/availability check failed; no triage result produced"
            ) from None
        bytes_size = original.stat().st_size
        try:
            width, height = reader(original, family)
            pixels = width * height
            if not 0 < pixels <= 500_000_000:
                raise ValueError("Implausible image dimension")
            result = {"family": family, "previousStatus": status,
                      "fileBytes": bytes_size, "pixels": pixels,
                      "headerStatus": "ok"}
        except Exception:
            result = {"family": family, "previousStatus": status,
                      "fileBytes": bytes_size, "pixels": None,
                      "headerStatus": "unavailable"}
        results.append(result)
    by_family = {}
    for family in SUPPORTED:
        group = [row for row in results if row["family"] == family]
        if not group:
            continue
        by_status = {}
        for status in ("failed", "success"):
            subset = [r for r in group if r["previousStatus"] == status]
            if not subset:
                continue
            valid = [r for r in subset if r["pixels"] is not None]
            by_status[status] = {
                "count": len(subset),
                "headerReadable": len(valid),
                "headerUnavailable": len(subset) - len(valid),
                "medianMegapixels": round(
                    statistics.median(r["pixels"] for r in valid) / 1e6, 2
                ) if valid else None,
                "maxMegapixels": round(max(r["pixels"] for r in valid) / 1e6, 2)
                if valid else None,
                "countAtLeast20MP": sum(r["pixels"] >= 20_000_000 for r in valid),
                "countAtLeast40MP": sum(r["pixels"] >= 40_000_000 for r in valid),
                "medianFileMB": round(
                    statistics.median(r["fileBytes"] for r in subset) / 1e6, 2),
            }
        by_family[family] = by_status
    return {
        "protocol": PROVENANCE,
        "sourceStudyProtocol": PROTOCOL,
        "scope": scope,
        "selectedPairs": len(verified),
        "previousFailures": sum(status == "failed" for _, status in verified),
        "metadataChecked": len(results),
        "metadataUnavailable": sum(r["headerStatus"] != "ok" for r in results),
        "byMediaFamilyAndPreviousStatus": by_family,
        "caution": (
            "Read-only metadata/header check, not full pixel decode or model inference. "
            "Header success cannot distinguish original decoding from model OOM/failure. "
            "Do not infer that JPEG/RAW pixel decoding was the cause of previous failures. "
            "No held-out or human review labels accessed."),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Frozen private benchmark")
    parser.add_argument("--selection", required=True, help="Original 80-photo private selection")
    parser.add_argument("--scores", required=True, help="Private TOPIQ original/proxy export")
    parser.add_argument("--original-root", required=True)
    parser.add_argument("--scope", choices=("failed", "all"), default="failed")
    args = parser.parse_args()
    report = triage(load(args.input), load(args.selection), load(args.scores),
                    args.original_root, scope=args.scope)
    print(json.dumps(report, indent=2, allow_nan=False))
    print("No image paths, photo IDs, reviews or per-photo scores included.")


if __name__ == "__main__":
    main()
