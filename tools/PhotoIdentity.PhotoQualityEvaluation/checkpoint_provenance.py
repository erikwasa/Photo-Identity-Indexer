#!/usr/bin/env python3
"""Inspect local IQA weights without PyTorch/model inference or network access.

Local cache scans are *candidate hints*, never proof of which checkpoint a
pyiqa model loaded or evidence of license permission to deploy.
Outputs must remain private. No original/proxy photos or ratings are read.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.metadata
import json
from pathlib import Path
import platform

MODEL_NAMES = {"topiq_nr", "musiq", "arniqa"}
MAX_CHECKPOINT_BYTES = 4 * 1024 * 1024 * 1024
SUFFIXES = {".pth", ".pt", ".ckpt", ".safetensors"}
PROTOCOL = "wi0193-checkpoint-provenance-v2"


def package_versions():
    versions = {}
    for name in ("pyiqa", "torch", "torchvision", "Pillow", "rawpy", "pillow-heif"):
        try:
            versions[name] = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            versions[name] = None
    return versions


def digest_checkpoint(original):
    if not original.is_file() or original.is_symlink() or original.is_junction():
        raise ValueError("Selected checkpoint is not a regular local file")
    size = original.stat().st_size
    if not 0 < size <= MAX_CHECKPOINT_BYTES:
        raise ValueError("Checkpoint size must be between 1 byte and 4 GiB")
    digest = hashlib.sha256()
    with original.open("rb") as handle:
        while block := handle.read(1024 * 1024):
            digest.update(block)
    return {
        "artifactName": original.name,
        "sha256": digest.hexdigest(),
        "sizeBytes": size,
        "licenseStatus": "not-verified",
        "modelToArtifactMapping": "maintainer-selected; verify actual model loading",
    }


def envelope():
    versions = package_versions()
    return {
        "schemaVersion": 2,
        "protocol": PROTOCOL,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "pythonVersion": platform.python_version(),
        "pyiqaVersion": versions["pyiqa"],
        "packageVersions": versions,
        "warning": (
            "File names and hashes do not prove loaded weight identity or license rights. "
            "Verify checkpoint-to-model mapping and independent checkpoint license "
            "before approving or deploying a model."),
    }


def inventory(pairs):
    found = {}
    for pair in pairs:
        if "=" not in pair:
            raise ValueError("Use --checkpoint metric=full-file-path")
        name, path = pair.split("=", 1)
        if name not in MODEL_NAMES or name in found or not path:
            raise ValueError("Invalid or duplicate candidate model name")
        original = Path(path).expanduser()
        found[name] = digest_checkpoint(original)
    if not found:
        raise ValueError("Supply at least one --checkpoint")
    result = envelope()
    result["mode"] = "explicit-candidate-checkpoints"
    result["checkpoints"] = found
    return result


def candidate_hint(name):
    stem = name.casefold()
    # Hints are intentionally not trusted for model/checkpoint matching.
    hints = []
    if "cfanet" in stem or "topiq" in stem:
        hints.append("topiq_nr")
    if "musiq" in stem:
        hints.append("musiq")
    if "arniqa" in stem or stem == "regressor_koniq10k.pth":
        hints.append("arniqa")
    return hints


def role_hint(name):
    stem = name.casefold()
    if stem == "arniqa.pth":
        return "possible-arniqa-encoder"
    if stem == "regressor_koniq10k.pth":
        return "possible-arniqa-koniq-regressor"
    if stem.startswith("resnet50-"):
        return "possible-torchvision-backbone-not-arniqa-proof"
    return "unverified-weight-artifact"


def discover(cache_root, additional_cache_roots=()):
    """Inventory multiple explicit cache roots without model import or network."""
    root_inputs = (cache_root, *additional_cache_roots)
    if not root_inputs:
        raise ValueError("Supply a primary local cache root")
    roots = []
    for item in root_inputs:
        root = Path(item).expanduser().resolve(strict=True)
        if not root.is_dir():
            raise ValueError("Checkpoint cache root must be a directory")
        if root in roots:
            raise ValueError("Duplicate cache directory")
        roots.append(root)

    # Aggregate only basenames/size/hash/hints, never filesystem root paths.
    paths = []
    seen = set()
    for index, root in enumerate(roots):
        for path in root.rglob("*"):
            if path.is_symlink() or path.is_junction():
                continue
            if path.suffix.casefold() not in SUFFIXES:
                continue
            resolved = path.resolve()
            if not resolved.is_relative_to(root):
                raise ValueError("Candidate weight path outside cache")
            if resolved in seen:
                # Nested explicit roots must not count the same artifact twice.
                continue
            if path.is_file():
                seen.add(resolved)
                paths.append((index, root, path))
    if not paths:
        raise ValueError("No checkpoint artifacts found in cache")
    candidates = []
    for index, root, path in sorted(
        paths, key=lambda triple: (
            triple[0], triple[2].relative_to(triple[1]).as_posix().casefold())):
        evidence = digest_checkpoint(path)
        evidence["candidateHints"] = candidate_hint(path.name)
        evidence["roleHint"] = role_hint(path.name)
        evidence["sourceCacheIndex"] = index + 1
        evidence["mappingVerified"] = False
        candidates.append(evidence)
    result = envelope()
    result["mode"] = "cache-discovery-unverified"
    result["cacheRootCount"] = len(roots)
    result["artifactCount"] = len(candidates)
    result["artifacts"] = candidates
    result["warning"] += (
        " Cache filename/role hints are not proof of loaded model-to-file "
        "mapping or weight license. ARNIQA's KonIQ regressor alone is not "
        "a complete model: locate encoder/backbone dependencies separately.")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--checkpoint", action="append",
                       help="Exact selected metric=full-file-path; may repeat")
    group.add_argument("--cache-root",
                       help="Read-only scan of downloaded local pyIQA weights")
    parser.add_argument("--additional-cache-root", action="append", default=[],
                        help="Extra explicit local cache to scan (e.g. torch/hub/checkpoints); "
                             "requires --cache-root; may repeat")
    parser.add_argument("--output", required=True, help="Private JSON; must not exist")
    args = parser.parse_args()
    dest = Path(args.output)
    if not dest.is_absolute() and (not dest.parts or dest.parts[0].lower() not in
                                   {"private", "staging", "data"}):
        parser.error("Relative outputs must be under private/, staging/, or data/")
    if dest.exists():
        parser.error("Refusing to overwrite existing private checkpoint inventory")
    if args.checkpoint and args.additional_cache_root:
        parser.error("--additional-cache-root requires --cache-root")
    result = (inventory(args.checkpoint) if args.checkpoint
              else discover(args.cache_root, args.additional_cache_root))
    dest.parent.mkdir(parents=True, exist_ok=True)
    with dest.open("x", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2, allow_nan=False)
    print(json.dumps({
        "protocol": result["protocol"], "mode": result["mode"],
        "artifactCount": result.get("artifactCount", len(result.get("checkpoints", {}))),
        "cacheRootCount": result.get("cacheRootCount"),
        "packageVersions": result["packageVersions"],
        "warning": result["warning"]
    }, indent=2))
    print("Private checkpoint evidence written. No model weights or photo data uploaded.")


if __name__ == "__main__":
    main()
