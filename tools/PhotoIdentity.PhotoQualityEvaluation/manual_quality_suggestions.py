#!/usr/bin/env python3
"""WI-0629: private, non-destructive technical photo suggestion workbench.

This intentionally makes SUGGESTIONS for a human, not automatic exclusions.
It consumes the existing read-only WI-0182 review-proxy evidence, never human
quality benchmark labels, and never modifies Photo Identity's catalogue.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import secrets

MANIFEST_PROTOCOL = "wi0629-manual-quality-suggestions-v1"
STATE_PROTOCOL = "wi0629-human-suggestion-decisions-v1"
INPUT_ALGORITHM = "proxy-technical-quality-v1"
EXPECTED_PROFILE = "jpeg-1600-q78"
SCOPES = ("archive-cleanup", "collection-curation", "slideshow-curation")
ACTIONS = ("keep", "dismiss", "consider-exclusion")
REASONS = {
    "candidate-severe-underexposure": ("Severe underexposure", 4),
    "candidate-severe-overexposure": ("Severe overexposure", 4),
    "candidate-low-sharpness": ("Possible blur or missed focus", 2),
    "candidate-shadow-clipping": ("Shadow clipping", 1),
    "candidate-highlight-clipping": ("Highlight clipping", 1),
    "candidate-low-contrast": ("Low contrast", 1),
    "candidate-low-effective-proxy-resolution": ("Low effective resolution", 2),
}
GUID = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
MAX_PHOTOS = 5000
MAX_SUGGESTIONS = 1500


def canonical(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":"),
                      ensure_ascii=True, allow_nan=False).encode("utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def private_path(path, *, must_exist):
    root = (Path.cwd() / "private").resolve()
    resolved = Path(path).resolve(strict=must_exist)
    if not resolved.is_relative_to(root) or resolved == root:
        raise ValueError("Only private/ paths are permitted")
    if must_exist and not resolved.is_file():
        raise ValueError("Missing private input")
    return resolved


def read_json(path):
    with path.open("r", encoding="utf-8") as src:
        return json.load(src)


def build(source, *, limit=MAX_SUGGESTIONS):
    if (not isinstance(source, dict) or source.get("schemaVersion") != 1
            or source.get("algorithmVersion") != INPUT_ALGORITHM
            or not isinstance(source.get("photos"), list)
            or len(source["photos"]) > MAX_PHOTOS
            or not 1 <= limit <= MAX_SUGGESTIONS
            or source.get("photoCount") != len(source["photos"])):
        raise ValueError("Invalid read-only technical evaluation export")
    seen = set()
    candidates = []
    reasons_seen = Counter()
    for photo in source["photos"]:
        if (not isinstance(photo, dict) or not isinstance(photo.get("revisionId"), str)
                or not GUID.fullmatch(photo["revisionId"])
                or not isinstance(photo.get("evidence"), dict)
                or not isinstance(photo.get("proxyRelativePath"), str)
                or not isinstance(photo.get("proxySha256"), str)):
            raise ValueError("Invalid technical candidate identity")
        revision = photo["revisionId"].lower()
        if revision in seen:
            raise ValueError("Repeated technical photo revision")
        seen.add(revision)
        # The source evaluator includes an empty draft; actual operator reviews
        # are never calibration or evidence for this suggestion generator.
        draft = photo.get("review")
        if draft is not None and (not isinstance(draft, dict) or any(
                value is not None and value != "" for value in draft.values())):
            raise ValueError("Source contains human review labels; not allowed")
        ev = photo["evidence"]
        codes = ev.get("reasons")
        if (not isinstance(codes, list)
                or not all(isinstance(code, str) for code in codes)
                or len(codes) != len(set(codes))):
            raise ValueError("Malformed technical evidence reasons")
        profile = ev.get("proxyProfileId")
        if (ev.get("algorithmVersion") != INPUT_ALGORITHM
                or profile != EXPECTED_PROFILE):
            raise ValueError("Unsupported technical source/proxy profile")
        if any(code not in REASONS for code in codes):
            # Do not silently turn unknown future signal definitions into
            # supposed image defects.
            raise ValueError("Unknown technical candidate reason")
        rel = photo["proxyRelativePath"]
        sha = photo["proxySha256"].lower()
        if (not rel or Path(rel).is_absolute()
                or ".." in rel.replace("\\", "/").split("/")
                or ":" in rel or "\x00" in rel
                or len(sha) != 64 or any(ch not in "0123456789abcdef" for ch in sha)):
            raise ValueError("Unsafe proxy locator or digest")
        if not codes:
            continue
        scores = [(code, REASONS[code][1]) for code in codes]
        priority = max(weight for _, weight in scores) * 10 + len(codes)
        for code in codes:
            reasons_seen[code] += 1
        candidates.append({
            "revisionId": revision,
            "proxyRelativePath": rel,
            "proxySha256": sha,
            "reasons": sorted(codes),
            "priority": priority,
        })
    # Deterministic, review-friendly order: strong *suggestion* reasons first,
    # then stable photo revision. This is NOT absolute image quality.
    candidates.sort(key=lambda row: (-row["priority"], row["revisionId"]))
    candidates = candidates[:limit]
    for code in list(reasons_seen):
        reasons_seen[code] = sum(code in row["reasons"] for row in candidates)
    manifest = {
        "schemaVersion": 1,
        "protocol": MANIFEST_PROTOCOL,
        "sourceProtocol": INPUT_ALGORITHM,
        "profile": EXPECTED_PROFILE,
        "sourceFingerprint": digest(canonical(source)),
        "scopeDefaults": {"archive-cleanup": True,
                          "collection-curation": False,
                          "slideshow-curation": False},
        "candidates": candidates,
        "actionsAreManual": True,
        "noAutomaticExclusion": True,
        "noCollectionChanges": True,
    }
    report = {
        "protocol": MANIFEST_PROTOCOL,
        "status": "suggestions-prepared-not-exclusions",
        "sourcePhotos": len(source["photos"]),
        "candidatePhotos": len(candidates),
        "reasons": dict(sorted(reasons_seen.items())),
        "onlyReviewProxyTechnicalEvidence": True,
        "noQualityTierOrAutomaticExclusion": True,
        "productionChangesMade": False,
    }
    return manifest, report


def validate_manifest(manifest):
    if (not isinstance(manifest, dict) or manifest.get("schemaVersion") != 1
            or manifest.get("protocol") != MANIFEST_PROTOCOL
            or manifest.get("profile") != EXPECTED_PROFILE
            or manifest.get("actionsAreManual") is not True
            or manifest.get("noAutomaticExclusion") is not True
            or manifest.get("noCollectionChanges") is not True
            or not isinstance(manifest.get("candidates"), list)
            or len(manifest["candidates"]) > MAX_SUGGESTIONS
            or manifest.get("scopeDefaults") != {
                "archive-cleanup": True, "collection-curation": False,
                "slideshow-curation": False}):
        raise ValueError("Unsafe or incompatible suggestion manifest")
    seen = set()
    for row in manifest["candidates"]:
        if (not isinstance(row, dict) or set(row) != {
                "revisionId", "proxyRelativePath", "proxySha256",
                "reasons", "priority"} or not GUID.fullmatch(row["revisionId"])
                or row["revisionId"] in seen
                or type(row["priority"]) is not int
                or not isinstance(row["reasons"], list)
                or not row["reasons"]
                or not set(row["reasons"]).issubset(REASONS)):
            raise ValueError("Invalid manifest candidate")
        seen.add(row["revisionId"])
        path, sha = row["proxyRelativePath"], row["proxySha256"]
        if (not isinstance(path, str) or not path or Path(path).is_absolute()
                or ".." in path.replace("\\", "/").split("/")
                or ":" in path or not isinstance(sha, str) or len(sha) != 64
                or any(c not in "0123456789abcdef" for c in sha)):
            raise ValueError("Unsafe reviewer proxy locator")
    return seen


def blank_state(manifest):
    return {
        "schemaVersion": 1,
        "protocol": STATE_PROTOCOL,
        "manifestFingerprint": digest(canonical(manifest)),
        "enabledScopes": dict(manifest["scopeDefaults"]),
        "decisions": {scope: {} for scope in SCOPES},
    }


def validate_state(state, manifest):
    allowed = validate_manifest(manifest)
    if (not isinstance(state, dict) or set(state) != {
                "schemaVersion", "protocol", "manifestFingerprint",
                "enabledScopes", "decisions"}
            or state["schemaVersion"] != 1 or state["protocol"] != STATE_PROTOCOL
            or state["manifestFingerprint"] != digest(canonical(manifest))
            or not isinstance(state["enabledScopes"], dict)
            or set(state["enabledScopes"]) != set(SCOPES)
            or any(type(x) is not bool for x in state["enabledScopes"].values())
            or not isinstance(state["decisions"], dict)
            or set(state["decisions"]) != set(SCOPES)):
        raise ValueError("State does not match the immutable suggestion manifest")
    for decisions in state["decisions"].values():
        if (not isinstance(decisions, dict) or
                any(key not in allowed or label not in ACTIONS
                    for key, label in decisions.items())):
            raise ValueError("Invalid human suggestion decisions")
    return state


def atomic_state(path, state, manifest):
    validate_state(state, manifest)
    if path.exists():
        validate_state(read_json(path), manifest)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp-" + secrets.token_hex(8))
    try:
        with temp.open("xb") as stream:
            stream.write(json.dumps(state, indent=2, allow_nan=False).encode("utf-8"))
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def read_proxy(root, row):
    # Reject symlink escapes and verify current bytes every request.
    prefix = root.resolve(strict=True)
    path = (prefix / row["proxyRelativePath"]).resolve(strict=True)
    if not path.is_relative_to(prefix) or not path.is_file():
        raise ValueError("Review proxy outside configured root")
    data = path.read_bytes()
    if not 1 <= len(data) <= 16 * 1024 * 1024:
        raise ValueError("Review proxy size outside bounds")
    if digest(data) != row["proxySha256"]:
        raise ValueError("Review proxy SHA mismatch")
    return data


def aggregate(manifest, state):
    validate_state(state, manifest)
    reasons = Counter(code for row in manifest["candidates"]
                      for code in row["reasons"])
    return {
        "protocol": "wi0629-manual-suggestion-review-report-v1",
        "suggested": len(manifest["candidates"]),
        "reasonCounts": dict(sorted(reasons.items())),
        "byScope": {
            scope: {
                "enabledForReview": state["enabledScopes"][scope],
                "reviewed": len(state["decisions"][scope]),
                "keep": sum(v == "keep" for v in state["decisions"][scope].values()),
                "dismiss": sum(v == "dismiss" for v in state["decisions"][scope].values()),
                "considerExclusion": sum(v == "consider-exclusion" for v in state["decisions"][scope].values()),
                "remaining": len(manifest["candidates"]) - len(state["decisions"][scope]),
            } for scope in SCOPES
        },
        "exclusionsPerformed": 0,
        "automaticPhotoOrCollectionChanges": False,
        "exclusionRequiresExistingSeparateNativeConfirmation": True,
        "limitations": (
            "Only existing technical proxy candidate reasons from a bounded "
            "deterministic archive sample, not a quality classifier or representative "
            "whole-archive scan. Context switches change ONLY this private suggestion "
            "review queue and never Photo Identity collection, slideshow or archive "
            "eligibility. Consider-exclusion records intent only, never purge."
        )
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest="mode", required=True)
    prep = subs.add_parser("prepare")
    prep.add_argument("--source", required=True)
    prep.add_argument("--output", required=True)
    prep.add_argument("--limit", type=int, default=500)
    report = subs.add_parser("report")
    report.add_argument("--manifest", required=True)
    report.add_argument("--state", required=True)
    args = parser.parse_args()
    try:
        if args.mode == "prepare":
            a = private_path(args.source, must_exist=True)
            b = private_path(args.output, must_exist=False)
            if a == b or b.exists():
                raise ValueError("Suggestion output must be a new private file")
            manifest, result = build(read_json(a), limit=args.limit)
            validate_manifest(manifest)
            b.parent.mkdir(parents=True, exist_ok=True)
            with b.open("xb") as stream:
                stream.write(json.dumps(manifest, indent=2).encode("utf-8"))
        else:
            manifest = read_json(private_path(args.manifest, must_exist=True))
            state = read_json(private_path(args.state, must_exist=True))
            result = aggregate(manifest, state)
        print(json.dumps(result, indent=2, allow_nan=False))
    except (OSError, ValueError, TypeError, KeyError) as ex:
        raise SystemExit("Suggestion processing failed (" + type(ex).__name__ + ")") from None


if __name__ == "__main__":
    main()
