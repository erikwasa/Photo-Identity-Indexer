from __future__ import annotations

import os
import unicodedata
from pathlib import Path
from typing import Sequence

import experiment as core

FILE_ATTRIBUTE_OFFLINE = 0x00001000
FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000
RECALL_RISK_MASK = FILE_ATTRIBUTE_OFFLINE | FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS


def normalize_tag(value: object) -> str:
    if not isinstance(value, str):
        raise ValueError("Tag names must be strings.")

    compatibility_normalized = unicodedata.normalize("NFKC", value).strip()
    display: list[str] = []
    pending_space = False
    for character in compatibility_normalized:
        if character.isspace():
            pending_space = bool(display)
            continue
        if unicodedata.category(character) == "Cc":
            raise ValueError("Tag names cannot contain control characters.")
        if pending_space:
            display.append(" ")
            pending_space = False
        display.append(character)

    display_name = "".join(display)
    if not display_name:
        raise ValueError("Tag names cannot be empty.")
    if len(display_name.encode("utf-16-le")) // 2 > 80:
        raise ValueError("Tag names cannot exceed 80 UTF-16 code units.")
    return display_name.lower()


def has_recall_risk(file_attributes: int) -> bool:
    return bool(file_attributes & RECALL_RISK_MASK)


def ensure_original_is_fully_local(path: Path) -> None:
    if os.name != "nt":
        return

    try:
        metadata = os.stat(path, follow_symlinks=False)
    except OSError as exc:
        raise RuntimeError(f"Original availability could not be checked: {path}") from exc

    attributes = getattr(metadata, "st_file_attributes", None)
    if attributes is None:
        raise RuntimeError(
            "Windows file attributes are unavailable; refusing original inference because "
            "local presence cannot be verified without risking implicit hydration."
        )
    if has_recall_risk(int(attributes)):
        raise RuntimeError(
            "Original is offline or recall-on-data-access. Hydrate a bounded evaluation "
            "subset explicitly before running original-image inference."
        )


def run_inference_safe(
    samples: Sequence[core.Sample],
    vocabulary: Sequence[core.VocabularyEntry],
    model_directory: Path,
    device_name: str,
):
    for sample in samples:
        if sample.original_path is not None:
            ensure_original_is_fully_local(sample.original_path)
    return _core_run_inference(samples, vocabulary, model_directory, device_name)


_core_run_inference = core.run_inference
core.normalize_tag = normalize_tag
core.run_inference = run_inference_safe


def main(argv: Sequence[str] | None = None) -> int:
    return core.main(argv)


if __name__ == "__main__":
    raise SystemExit(main())
