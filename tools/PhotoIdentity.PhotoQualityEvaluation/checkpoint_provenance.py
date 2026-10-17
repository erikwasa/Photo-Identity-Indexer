#!/usr/bin/env python3
"""Hash explicitly selected third-party IQA checkpoints; no network or model loading.

Never infer a deployment license from package or checkpoint file names.
The JSON output is private evidence, not part of the public repository.
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


def inventory(pairs):
    found = {}
    for pair in pairs:
        if "=" not in pair:
            raise ValueError("Use --checkpoint metric=full-file-path")
        name, path = pair.split("=", 1)
        if name not in MODEL_NAMES or name in found or not path:
            raise ValueError("Invalid or duplicate candidate model name")
        original = Path(path).expanduser()
        if not original.is_file():
            raise ValueError("Selected checkpoint is not a regular file")
        size = original.stat().st_size
        if not 0 < size <= MAX_CHECKPOINT_BYTES:
            raise ValueError("Checkpoint size must be between 1 byte and 4 GiB")
        digest = hashlib.sha256()
        with original.open("rb") as handle:
            while block := handle.read(1024 * 1024):
                digest.update(block)
        found[name] = {
            "artifactName": original.name,
            "sha256": digest.hexdigest(),
            "sizeBytes": size,
            "licenseStatus": "not-verified",
            "modelToArtifactMapping": "maintainer-selected; verify loading provenance"
        }
    if not found:
        raise ValueError("Supply at least one --checkpoint")
    try:
        version = importlib.metadata.version("pyiqa")
    except importlib.metadata.PackageNotFoundError:
        version = None
    return {
        "schemaVersion": 1,
        "protocol": "wi0193-checkpoint-provenance-v1",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "pythonVersion": platform.python_version(),
        "pyiqaVersion": version,
        "checkpoints": found,
        "warning": ("File hashes alone do not prove which weights the model loaded, "
                    "license permissions, reproducibility or deployment safety.")
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", action="append", required=True,
                        help="Candidate and explicit local file, e.g. musiq=C:/cache/model.pth")
    parser.add_argument("--output", required=True,
                        help="Private JSON output, must not exist")
    args = parser.parse_args()
    dest = Path(args.output)
    if not dest.is_absolute() and (not dest.parts or dest.parts[0].lower() not in
                                   {"private", "staging", "data"}):
        parser.error("Relative outputs must be under private/, staging/, or data/")
    report = inventory(args.checkpoint)
    dest.parent.mkdir(parents=True, exist_ok=True)
    with dest.open("x", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print("Private checkpoint hashes recorded. Verify model-to-file mapping and license separately.")


if __name__ == "__main__":
    main()
