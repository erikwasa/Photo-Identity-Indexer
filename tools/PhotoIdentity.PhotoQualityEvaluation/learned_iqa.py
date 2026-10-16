#!/usr/bin/env python3
"""Private WI-0193 learned NR-IQA experiment. Not part of production inference."""
from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import math
import os
from pathlib import Path
import statistics
import sys
import time
from datetime import datetime, timezone

CANDIDATES = ("topiq_nr", "musiq", "arniqa")
PROFILE = "jpeg-1600-q78"
MAX_PROXY_BYTES = 16 * 1024 * 1024


def private_output(path: str) -> Path:
    raw = Path(path)
    if not raw.is_absolute() and (not raw.parts or raw.parts[0].lower() not in
                                  {"private", "data", "staging"}):
        raise ValueError("Relative outputs must live under private/, data/, or staging/.")
    target = raw.resolve()
    if target.exists():
        raise ValueError("Refusing to overwrite existing private score export.")
    return target


def verified_proxy(root: Path, relative: str, expected_hash: str) -> Path:
    rel = Path(relative.replace("\\", "/"))
    if rel.is_absolute() or ".." in rel.parts or not relative or ":" in relative:
        raise ValueError("Invalid proxy relative path.")
    candidate = root.joinpath(rel)
    if not candidate.resolve().is_relative_to(root):
        raise ValueError("Proxy escapes configured root.")
    # Refuse symbolic links and Windows junctions/reparse points in path components.
    for component in (candidate, *candidate.parents):
        if component == root.parent:
            break
        if component.is_symlink() or component.is_junction():
            raise ValueError("Proxy path contains a link or junction.")
        if component == root:
            break
    if not candidate.is_file() or candidate.stat().st_size > MAX_PROXY_BYTES:
        raise ValueError("Proxy absent or larger than 16 MiB.")
    actual = hashlib.sha256(candidate.read_bytes()).hexdigest()
    if actual.lower() != expected_hash.lower():
        raise ValueError("Proxy digest does not match frozen benchmark.")
    return candidate


def p95(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    values = sorted(values)
    return values[max(0, math.ceil(fraction * len(values)) - 1)]


def run(args: argparse.Namespace, metric_factory=None) -> dict:
    manifest = json.loads(Path(args.input).read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") not in (1, 2):
        raise ValueError("Unsupported benchmark schema.")
    photos = manifest.get("photos")
    if not manifest.get("benchmarkId") or not isinstance(photos, list) or not photos:
        raise ValueError("Invalid benchmark manifest.")
    ids = [photo["revisionId"] for photo in photos]
    if len(ids) != len({revision.lower() for revision in ids}):
        raise ValueError("Duplicate benchmark revisions.")
    profile = manifest.get("proxyProfile", "")
    if "profile-id=" + PROFILE not in profile.splitlines():
        raise ValueError("Only jpeg-1600-q78 benchmark proxies are supported.")
    root = Path(args.proxy_root).resolve(strict=True)
    output = private_output(args.output)
    if args.metric not in CANDIDATES:
        raise ValueError("Unsupported model.")
    if args.limit is not None and not 1 <= args.limit <= len(photos):
        raise ValueError("Invalid --limit.")
    if args.device != "cpu" and not args.device.startswith("cuda"):
        raise ValueError("Only cpu or cuda devices are supported.")

    # Fail before metric initialization on missing or tampered pixels.
    chosen = photos if args.limit is None else photos[:args.limit]
    paths = {p["revisionId"]: verified_proxy(
        root, p["proxyRelativePath"], p["proxySha256"]) for p in chosen}

    if metric_factory is None:
        import pyiqa  # installed separately, never by the .NET app
        metric = pyiqa.create_metric(args.metric, device=args.device)
        version = importlib.metadata.version("pyiqa")
    else:
        metric = metric_factory(args.metric, args.device)
        version = "test-double"

    scores = []
    elapsed = []
    start = time.perf_counter()
    for index, photo in enumerate(chosen, 1):
        begin = time.perf_counter()
        try:
            output_score = metric(str(paths[photo["revisionId"]]))
            raw = float(output_score.item())
            if not math.isfinite(raw):
                raise ArithmeticError("non-finite quality score")
            failure = None
        except Exception:
            raw = None
            failure = "inference-failed"  # No private filesystem paths in output.
        duration = (time.perf_counter() - begin) * 1000
        elapsed.append(duration)
        scores.append({
            "revisionId": photo["revisionId"],
            "rawScore": raw,
            "inferenceMilliseconds": duration,
            "errorCode": failure,
            "diagnostics": []
        })
        if index % 50 == 0:
            print(f"Processed {index}/{len(chosen)}", file=sys.stderr)

    # Partial scoring is a local smoke test, NOT a calibration-compatible export.
    if len(chosen) != len(photos):
        print("PARTIAL SMOKE TEST: cannot use with calibration-report.", file=sys.stderr)
    result = {
        "schemaVersion": 1,
        "benchmarkId": manifest["benchmarkId"],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "candidate": {
            "candidateId": f"pyiqa-{args.metric}-{version}",
            "modelName": args.metric,
            "modelVersion": f"pyiqa-{version}; weights-not-yet-pinned",
            "framework": "pyiqa / PyTorch",
            "license": "third-party framework and weights: verify separately before deployment",
            "source": "https://github.com/chaofengc/IQA-PyTorch",
            "preprocessing": "pyiqa default preprocessing for this model; immutable jpeg-1600-q78 input",
            "scoreSemantics": "model raw score; higher is better; no archive relative scaling",
            "modelSizeBytes": None,
            "executionProvider": args.device
        },
        "inputKind": "review-proxy",
        "higherIsBetter": True,
        "photoCount": len(scores),
        "runtime": {
            "totalElapsedMilliseconds": (time.perf_counter() - start) * 1000,
            "medianInferenceMilliseconds": statistics.median(elapsed),
            "p95InferenceMilliseconds": p95(elapsed, 0.95)
        },
        "scores": scores
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation: no accidental overwrites or partial replacement.
    with output.open("x", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2, allow_nan=False)
    print(f"Private scores written for {len(scores)} proxies; do not commit the export.")
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True)
    parser.add_argument("--proxy-root", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--metric", choices=CANDIDATES, required=True)
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--limit", type=int, default=None, help="Smoke-test subset, never calibration ready")
    parser.add_argument("--accept-third-party-weights", action="store_true",
                        help="Required: pyiqa may download third-party pretrained weights")
    args = parser.parse_args()
    if not args.accept_third_party_weights:
        parser.error("Explicit --accept-third-party-weights required; pyiqa can download weights.")
    try:
        run(args)
        return 0
    except Exception as ex:
        print(f"Learned IQA run failed: {type(ex).__name__}: {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
