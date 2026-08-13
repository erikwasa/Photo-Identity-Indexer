from __future__ import annotations

import argparse
import hashlib
import json
import math
import platform
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence

SCHEMA_VERSION = 1
DEFAULT_MODEL_REPOSITORY = "laion/CLIP-ViT-B-32-laion2B-s34B-b79K"
DEFAULT_MODEL_REVISION = "1a25a44"
DEFAULT_PROMPT_TEMPLATES = ("a photo of {tag}",)
DEFAULT_THRESHOLDS = tuple(round(0.10 + (index * 0.01), 2) for index in range(31))


@dataclass(frozen=True)
class VocabularyEntry:
    name: str
    prompts: tuple[str, ...]


@dataclass(frozen=True)
class Sample:
    sample_id: str
    proxy_path: Path
    original_path: Path | None
    expected_tags: frozenset[str]


@dataclass(frozen=True)
class ScoredSample:
    sample_id: str
    input_kind: str
    expected_tags: frozenset[str]
    scores: dict[str, float]


def canonical_json_bytes(value: object) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load_json(path: Path) -> object:
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def load_vocabulary(path: Path) -> tuple[list[VocabularyEntry], str]:
    raw = load_json(path)
    if not isinstance(raw, dict) or raw.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError(f"Vocabulary must use schemaVersion {SCHEMA_VERSION}.")

    raw_entries = raw.get("tags")
    if not isinstance(raw_entries, list) or not raw_entries:
        raise ValueError("Vocabulary must contain at least one tag.")

    entries: list[VocabularyEntry] = []
    seen: set[str] = set()
    for index, raw_entry in enumerate(raw_entries):
        if not isinstance(raw_entry, dict):
            raise ValueError(f"Vocabulary tag {index} must be an object.")
        name = normalize_tag(raw_entry.get("name"))
        if name in seen:
            raise ValueError(f"Duplicate canonical vocabulary tag: {name}")
        seen.add(name)

        raw_prompts = raw_entry.get("prompts")
        if raw_prompts is None:
            prompts = tuple(template.format(tag=name) for template in DEFAULT_PROMPT_TEMPLATES)
        elif isinstance(raw_prompts, list) and raw_prompts:
            prompts = tuple(normalize_prompt(value, name) for value in raw_prompts)
        else:
            raise ValueError(f"Vocabulary tag '{name}' prompts must be a non-empty array.")

        entries.append(VocabularyEntry(name=name, prompts=prompts))

    digest_payload = {
        "schemaVersion": SCHEMA_VERSION,
        "tags": [{"name": entry.name, "prompts": list(entry.prompts)} for entry in entries],
    }
    return entries, sha256_hex(canonical_json_bytes(digest_payload))


def normalize_tag(value: object) -> str:
    if not isinstance(value, str):
        raise ValueError("Tag names must be strings.")
    normalized = " ".join(value.split()).casefold()
    if not normalized:
        raise ValueError("Tag names cannot be empty.")
    if len(normalized) > 80:
        raise ValueError("Tag names cannot exceed 80 characters.")
    return normalized


def normalize_prompt(value: object, tag: str) -> str:
    if not isinstance(value, str):
        raise ValueError(f"Prompts for '{tag}' must be strings.")
    normalized = " ".join(value.split())
    if not normalized:
        raise ValueError(f"Prompts for '{tag}' cannot be empty.")
    return normalized


def load_manifest(path: Path) -> tuple[list[Sample], str]:
    raw = load_json(path)
    if not isinstance(raw, dict) or raw.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError(f"Manifest must use schemaVersion {SCHEMA_VERSION}.")

    raw_samples = raw.get("samples")
    if not isinstance(raw_samples, list) or not raw_samples:
        raise ValueError("Manifest must contain at least one sample.")

    samples: list[Sample] = []
    seen_ids: set[str] = set()
    digest_samples: list[dict[str, object]] = []
    for index, raw_sample in enumerate(raw_samples):
        if not isinstance(raw_sample, dict):
            raise ValueError(f"Sample {index} must be an object.")

        sample_id = raw_sample.get("id")
        if not isinstance(sample_id, str) or not sample_id.strip():
            raise ValueError(f"Sample {index} must have a non-empty id.")
        sample_id = sample_id.strip()
        if sample_id in seen_ids:
            raise ValueError(f"Duplicate sample id: {sample_id}")
        seen_ids.add(sample_id)

        proxy_path = require_local_path(raw_sample.get("proxyPath"), sample_id, "proxyPath")
        original_value = raw_sample.get("originalPath")
        original_path = (
            require_local_path(original_value, sample_id, "originalPath")
            if original_value is not None
            else None
        )

        raw_expected = raw_sample.get("expectedTags", [])
        if not isinstance(raw_expected, list):
            raise ValueError(f"Sample '{sample_id}' expectedTags must be an array.")
        expected_tags = frozenset(normalize_tag(value) for value in raw_expected)

        samples.append(
            Sample(
                sample_id=sample_id,
                proxy_path=proxy_path,
                original_path=original_path,
                expected_tags=expected_tags,
            )
        )
        digest_samples.append(
            {
                "id": sample_id,
                "hasOriginal": original_path is not None,
                "expectedTags": sorted(expected_tags),
            }
        )

    # Deliberately exclude private filesystem paths from the reproducibility digest.
    digest_payload = {"schemaVersion": SCHEMA_VERSION, "samples": digest_samples}
    return samples, sha256_hex(canonical_json_bytes(digest_payload))


def require_local_path(value: object, sample_id: str, field: str) -> Path:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"Sample '{sample_id}' {field} must be a non-empty path.")
    return Path(value).expanduser()


def validate_expected_tags(samples: Sequence[Sample], vocabulary: Sequence[VocabularyEntry]) -> None:
    allowed = {entry.name for entry in vocabulary}
    unknown = sorted({tag for sample in samples for tag in sample.expected_tags if tag not in allowed})
    if unknown:
        raise ValueError(
            "Manifest expectedTags are missing from vocabulary: " + ", ".join(unknown)
        )


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def prepare_model(model_directory: Path, repository: str, revision: str) -> Path:
    try:
        from huggingface_hub import HfApi, snapshot_download
    except ImportError as exc:
        raise RuntimeError(
            "huggingface_hub is required. Install requirements.txt in an isolated Python environment."
        ) from exc

    model_directory.mkdir(parents=True, exist_ok=True)
    resolved_revision = HfApi().model_info(repository, revision=revision).sha
    if not resolved_revision:
        raise RuntimeError(f"Could not resolve model revision '{revision}'.")

    snapshot_download(
        repo_id=repository,
        revision=resolved_revision,
        local_dir=str(model_directory),
        allow_patterns=[
            "open_clip_config.json",
            "open_clip_model.safetensors",
            "tokenizer.json",
            "tokenizer_config.json",
            "vocab.json",
            "merges.txt",
            "special_tokens_map.json",
        ],
    )

    checkpoint = model_directory / "open_clip_model.safetensors"
    if not checkpoint.is_file():
        raise FileNotFoundError(
            "Pinned model snapshot did not contain open_clip_model.safetensors."
        )

    provenance = {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "requestedRevision": revision,
        "resolvedRevision": resolved_revision,
        "checkpoint": checkpoint.name,
        "checkpointSha256": sha256_file(checkpoint),
        "openClipConfigSha256": sha256_file(model_directory / "open_clip_config.json"),
    }
    write_json(model_directory / "photo-identity-model-snapshot.json", provenance)
    return model_directory


def load_model_provenance(model_directory: Path) -> dict[str, object]:
    provenance_path = model_directory / "photo-identity-model-snapshot.json"
    if not provenance_path.is_file():
        raise FileNotFoundError(
            "Model snapshot provenance is missing. Run prepare-model before inference."
        )
    raw = load_json(provenance_path)
    if not isinstance(raw, dict) or raw.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError("Model snapshot provenance has an unsupported schema.")
    checkpoint_name = raw.get("checkpoint")
    checkpoint_digest = raw.get("checkpointSha256")
    if not isinstance(checkpoint_name, str) or not isinstance(checkpoint_digest, str):
        raise ValueError("Model snapshot provenance is incomplete.")
    checkpoint = model_directory / checkpoint_name
    if not checkpoint.is_file():
        raise FileNotFoundError(f"Pinned model checkpoint is missing: {checkpoint}")
    actual_digest = sha256_file(checkpoint)
    if actual_digest != checkpoint_digest:
        raise RuntimeError(
            "Pinned model checkpoint digest changed after preparation; prepare a clean snapshot."
        )
    return raw


def load_openclip(model_directory: Path, device_name: str):
    try:
        import open_clip
        import torch
    except ImportError as exc:
        raise RuntimeError(
            "open_clip_torch and torch are required. Install requirements.txt in an isolated Python environment."
        ) from exc

    if not (model_directory / "open_clip_config.json").is_file():
        raise FileNotFoundError(
            f"Model directory does not contain open_clip_config.json: {model_directory}"
        )

    device = torch.device(device_name)
    model_name = f"local-dir:{model_directory.resolve()}"
    model, _, preprocess = open_clip.create_model_and_transforms(
        model_name,
        device=device,
    )
    tokenizer = open_clip.get_tokenizer(model_name)
    model.eval()
    return torch, model, preprocess, tokenizer, device


def build_text_features(torch, model, tokenizer, device, vocabulary: Sequence[VocabularyEntry]):
    feature_rows = []
    with torch.no_grad():
        for entry in vocabulary:
            tokens = tokenizer(list(entry.prompts)).to(device)
            prompt_features = model.encode_text(tokens)
            prompt_features = prompt_features / prompt_features.norm(dim=-1, keepdim=True)
            tag_feature = prompt_features.mean(dim=0)
            tag_feature = tag_feature / tag_feature.norm()
            feature_rows.append(tag_feature)
    return torch.stack(feature_rows)


def score_image(
    path: Path,
    *,
    torch,
    model,
    preprocess,
    device,
    text_features,
    vocabulary: Sequence[VocabularyEntry],
) -> dict[str, float]:
    try:
        from PIL import Image
    except ImportError as exc:
        raise RuntimeError("Pillow is required to decode experiment images.") from exc

    with Image.open(path) as image:
        tensor = preprocess(image.convert("RGB")).unsqueeze(0).to(device)

    with torch.no_grad():
        image_features = model.encode_image(tensor)
        image_features = image_features / image_features.norm(dim=-1, keepdim=True)
        similarities = (image_features @ text_features.T).squeeze(0).detach().cpu().tolist()

    return {
        entry.name: float(similarity)
        for entry, similarity in zip(vocabulary, similarities, strict=True)
    }


def run_inference(
    samples: Sequence[Sample],
    vocabulary: Sequence[VocabularyEntry],
    model_directory: Path,
    device_name: str,
) -> tuple[list[ScoredSample], dict[str, object]]:
    torch, model, preprocess, tokenizer, device = load_openclip(model_directory, device_name)
    text_features = build_text_features(torch, model, tokenizer, device, vocabulary)

    scored: list[ScoredSample] = []
    elapsed_by_kind = {"proxy": 0.0, "original": 0.0}
    count_by_kind = {"proxy": 0, "original": 0}

    for sample in samples:
        for input_kind, path in (("proxy", sample.proxy_path), ("original", sample.original_path)):
            if path is None:
                continue
            if not path.is_file():
                raise FileNotFoundError(f"Experiment input is not a local file: {path}")

            started = time.perf_counter()
            scores = score_image(
                path,
                torch=torch,
                model=model,
                preprocess=preprocess,
                device=device,
                text_features=text_features,
                vocabulary=vocabulary,
            )
            elapsed_by_kind[input_kind] += time.perf_counter() - started
            count_by_kind[input_kind] += 1
            scored.append(
                ScoredSample(
                    sample_id=sample.sample_id,
                    input_kind=input_kind,
                    expected_tags=sample.expected_tags,
                    scores=scores,
                )
            )

    runtime = {
        kind: {
            "imageCount": count_by_kind[kind],
            "elapsedSeconds": round(elapsed_by_kind[kind], 6),
            "imagesPerSecond": (
                round(count_by_kind[kind] / elapsed_by_kind[kind], 6)
                if elapsed_by_kind[kind] > 0
                else None
            ),
        }
        for kind in ("proxy", "original")
    }
    return scored, runtime


def classify(scores: dict[str, float], threshold: float) -> frozenset[str]:
    return frozenset(tag for tag, score in scores.items() if score >= threshold)


def metric_counts(
    samples: Iterable[ScoredSample],
    threshold: float,
) -> tuple[int, int, int]:
    true_positive = false_positive = false_negative = 0
    for sample in samples:
        predicted = classify(sample.scores, threshold)
        true_positive += len(predicted & sample.expected_tags)
        false_positive += len(predicted - sample.expected_tags)
        false_negative += len(sample.expected_tags - predicted)
    return true_positive, false_positive, false_negative


def precision_recall_f1(tp: int, fp: int, fn: int) -> tuple[float, float, float]:
    precision = tp / (tp + fp) if tp + fp else 1.0
    recall = tp / (tp + fn) if tp + fn else 1.0
    f1 = (
        2.0 * precision * recall / (precision + recall)
        if precision + recall
        else 0.0
    )
    return precision, recall, f1


def threshold_sweep(
    samples: Sequence[ScoredSample],
    thresholds: Sequence[float],
) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for threshold in thresholds:
        tp, fp, fn = metric_counts(samples, threshold)
        precision, recall, f1 = precision_recall_f1(tp, fp, fn)
        rows.append(
            {
                "threshold": threshold,
                "truePositive": tp,
                "falsePositive": fp,
                "falseNegative": fn,
                "precision": round(precision, 6),
                "recall": round(recall, 6),
                "f1": round(f1, 6),
            }
        )
    return rows


def choose_threshold(rows: Sequence[dict[str, object]]) -> dict[str, object]:
    if not rows:
        raise ValueError("Threshold sweep cannot be empty.")
    # Prefer F1, then precision, then the higher threshold. False tag pollution is
    # costly in library discovery, so precision wins an otherwise equal tie.
    return max(
        rows,
        key=lambda row: (
            float(row["f1"]),
            float(row["precision"]),
            float(row["threshold"]),
        ),
    )


def jaccard(left: frozenset[str], right: frozenset[str]) -> float:
    union = left | right
    return len(left & right) / len(union) if union else 1.0


def proxy_original_agreement(
    samples: Sequence[ScoredSample],
    threshold: float,
    top_k: int = 5,
) -> dict[str, object]:
    by_id: dict[str, dict[str, ScoredSample]] = {}
    for sample in samples:
        by_id.setdefault(sample.sample_id, {})[sample.input_kind] = sample

    pairs = [
        pair
        for pair in by_id.values()
        if "proxy" in pair and "original" in pair
    ]
    if not pairs:
        return {
            "pairCount": 0,
            "meanThresholdedJaccard": None,
            "meanTopKOverlap": None,
            "meanAbsoluteScoreDelta": None,
        }

    jaccards: list[float] = []
    top_k_overlaps: list[float] = []
    score_deltas: list[float] = []
    for pair in pairs:
        proxy = pair["proxy"]
        original = pair["original"]
        jaccards.append(
            jaccard(classify(proxy.scores, threshold), classify(original.scores, threshold))
        )

        effective_k = min(top_k, len(proxy.scores))
        proxy_top = {
            tag
            for tag, _ in sorted(proxy.scores.items(), key=lambda item: item[1], reverse=True)[
                :effective_k
            ]
        }
        original_top = {
            tag
            for tag, _ in sorted(
                original.scores.items(), key=lambda item: item[1], reverse=True
            )[:effective_k]
        }
        top_k_overlaps.append(
            len(proxy_top & original_top) / effective_k if effective_k else 1.0
        )
        for tag in proxy.scores:
            score_deltas.append(abs(proxy.scores[tag] - original.scores[tag]))

    return {
        "pairCount": len(pairs),
        "meanThresholdedJaccard": round(sum(jaccards) / len(jaccards), 6),
        "meanTopKOverlap": round(sum(top_k_overlaps) / len(top_k_overlaps), 6),
        "meanAbsoluteScoreDelta": round(sum(score_deltas) / len(score_deltas), 6),
    }


def score_rows_for_output(samples: Sequence[ScoredSample]) -> list[dict[str, object]]:
    # This private-detail output contains sample IDs, expected labels and raw scores,
    # but never local filesystem paths.
    return [
        {
            "sampleId": sample.sample_id,
            "inputKind": sample.input_kind,
            "expectedTags": sorted(sample.expected_tags),
            "scores": {
                tag: round(score, 8)
                for tag, score in sorted(sample.scores.items())
            },
        }
        for sample in samples
    ]


def parse_thresholds(values: Sequence[str] | None) -> tuple[float, ...]:
    if not values:
        return DEFAULT_THRESHOLDS
    thresholds = sorted({float(value) for value in values})
    if any(not math.isfinite(value) or value < -1.0 or value > 1.0 for value in thresholds):
        raise ValueError("Thresholds must be finite cosine-similarity values from -1 through 1.")
    return tuple(thresholds)


def environment_metadata() -> dict[str, object]:
    from importlib import metadata as package_metadata

    packages: dict[str, str | None] = {}
    for package in (
        "open_clip_torch",
        "torch",
        "torchvision",
        "Pillow",
        "huggingface_hub",
        "safetensors",
        "timm",
    ):
        try:
            packages[package] = package_metadata.version(package)
        except package_metadata.PackageNotFoundError:
            packages[package] = None

    cuda_available = None
    try:
        import torch

        cuda_available = bool(torch.cuda.is_available())
    except ImportError:
        pass

    return {
        "python": platform.python_version(),
        "platform": platform.platform(),
        "packages": packages,
        "cudaAvailable": cuda_available,
    }


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
        stream.write("\n")


def command_prepare(args: argparse.Namespace) -> int:
    directory = prepare_model(Path(args.model_dir), args.model_repository, args.model_revision)
    print(f"Prepared exact model snapshot in {directory}")
    return 0


def command_run(args: argparse.Namespace) -> int:
    manifest_path = Path(args.manifest)
    vocabulary_path = Path(args.vocabulary)
    samples, manifest_digest = load_manifest(manifest_path)
    vocabulary, vocabulary_digest = load_vocabulary(vocabulary_path)
    validate_expected_tags(samples, vocabulary)
    thresholds = parse_thresholds(args.threshold)

    model_directory = Path(args.model_dir)
    model_provenance = load_model_provenance(model_directory)
    if model_provenance.get("repository") != args.model_repository:
        raise ValueError("Prepared model repository does not match --model-repository.")
    requested_revision = str(model_provenance.get("requestedRevision", ""))
    resolved_revision = str(model_provenance.get("resolvedRevision", ""))
    if args.model_revision not in (requested_revision, resolved_revision):
        raise ValueError("Prepared model revision does not match --model-revision.")

    scored, runtime = run_inference(
        samples,
        vocabulary,
        model_directory,
        args.device,
    )
    proxy_samples = [sample for sample in scored if sample.input_kind == "proxy"]
    original_samples = [sample for sample in scored if sample.input_kind == "original"]

    proxy_sweep = threshold_sweep(proxy_samples, thresholds)
    selected = choose_threshold(proxy_sweep)
    selected_threshold = float(selected["threshold"])

    aggregate = {
        "schemaVersion": SCHEMA_VERSION,
        "experiment": "visible-content-tagging-openclip-v1",
        "model": {
            **model_provenance,
            "loader": "open_clip local-dir",
        },
        "inputs": {
            "manifestDigest": manifest_digest,
            "vocabularyDigest": vocabulary_digest,
            "sampleCount": len(samples),
            "proxyCount": len(proxy_samples),
            "originalCount": len(original_samples),
        },
        "pipeline": {
            "score": "cosine_similarity_of_normalized_image_and_prompt_ensemble_features",
            "promptEnsemble": "normalized prompt features -> arithmetic mean -> renormalize",
            "thresholdSelection": "maximize proxy micro F1; ties prefer precision then higher threshold",
            "softmaxUsedAsConfidence": False,
        },
        "selectedProxyThreshold": selected,
        "proxyThresholdSweep": proxy_sweep,
        "originalAtSelectedThreshold": (
            threshold_sweep(original_samples, [selected_threshold])[0]
            if original_samples
            else None
        ),
        "proxyOriginalAgreement": proxy_original_agreement(
            scored,
            selected_threshold,
            top_k=args.top_k,
        ),
        "runtime": runtime,
        "environment": environment_metadata(),
    }

    output = Path(args.output)
    write_json(output, aggregate)
    if args.details_output:
        write_json(
            Path(args.details_output),
            {
                "schemaVersion": SCHEMA_VERSION,
                "manifestDigest": manifest_digest,
                "vocabularyDigest": vocabulary_digest,
                "scores": score_rows_for_output(scored),
            },
        )

    print(f"Wrote aggregate experiment report to {output}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Private local visible-content tagging experiment for WI-0049."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    prepare = subparsers.add_parser(
        "prepare-model",
        help="Download the exact pinned OpenCLIP model snapshot into a local ignored directory.",
    )
    add_model_arguments(prepare)
    prepare.set_defaults(func=command_prepare)

    run = subparsers.add_parser(
        "run",
        help="Score a private proxy/original manifest and write privacy-safe aggregate metrics.",
    )
    add_model_arguments(run)
    run.add_argument("--manifest", required=True)
    run.add_argument("--vocabulary", required=True)
    run.add_argument("--output", required=True)
    run.add_argument(
        "--details-output",
        help="Optional private score report. Keep this below an ignored/private directory.",
    )
    run.add_argument(
        "--device",
        default="cpu",
        help="PyTorch device string such as cpu or cuda. Default: cpu.",
    )
    run.add_argument(
        "--threshold",
        action="append",
        help="Cosine threshold to test; repeat to replace the default 0.10..0.40 sweep.",
    )
    run.add_argument("--top-k", type=int, default=5)
    run.set_defaults(func=command_run)
    return parser


def add_model_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--model-repository", default=DEFAULT_MODEL_REPOSITORY)
    parser.add_argument("--model-revision", default=DEFAULT_MODEL_REVISION)


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if getattr(args, "top_k", 1) <= 0:
        parser.error("--top-k must be greater than zero.")
    try:
        return int(args.func(args))
    except (ValueError, FileNotFoundError, RuntimeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
