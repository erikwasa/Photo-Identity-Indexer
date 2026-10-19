"""WI-0194 pilot packaging/review: synthetic-only, offline and no held-out labels."""
import copy
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import redundancy_pilot_package as package
from redundancy_pilot_review import (
    load_session, public_state, read_verified_proxy, store_review,
)
from redundancy_review_pairs import prepare_inventory
from test_redundancy_review_pairs import fixture


SMALL_QUOTAS = {
    "strict-dhash-0-to-4": 1,
    "weak-dhash-5-to-8": 2,
    "weak-dhash-9-to-12": 2,
    "screened-random-control": 2,
    "other-candidate": 2,
}


def generated():
    with patch.dict(package.QUOTAS, SMALL_QUOTAS, clear=True):
        return package.make_package(fixture())


def prepare_private(root):
    private = root / "private"
    private.mkdir()
    original = root / "proxies"
    original.mkdir()
    with patch.dict(package.QUOTAS, SMALL_QUOTAS, clear=True):
        blind, audit = package.make_package(fixture())
    blind_path = private / "blind.json"
    audit_path = private / "audit.json"
    blind_path.write_bytes(package.canonical_bytes(blind))
    audit_path.write_bytes(package.canonical_bytes(audit))
    revisions = sorted({
        row[side] for row in blind["pairs"]
        for side in ("leftRevisionId", "rightRevisionId")
    })
    mapping = {
        "schemaVersion": 1,
        "protocol": "wi0194-private-pilot-proxies-v1",
        "pilotSha256": package.fingerprint(blind_path.read_bytes()),
        "proxyRoot": str(original),
        "proxyProfile": "synthetic-local-test",
        "catalogReadOnly": True,
        "noOriginalsHydrated": True,
        "photos": [],
    }
    for i, revision in enumerate(revisions):
        contents = ("fake-jpeg-" + str(i)).encode()
        relative = str(i) + ".jpg"
        (original / relative).write_bytes(contents)
        mapping["photos"].append({
            "revisionId": revision,
            "relativePath": relative,
            "sha256": hashlib.sha256(contents).hexdigest(),
            "byteLength": len(contents),
        })
    mapping_path = private / "proxies.json"
    mapping_path.write_text(json.dumps(mapping), encoding="utf-8")
    return blind_path, audit_path, mapping_path, private / "labels.json"


class IntegratedCalibrationPilotTests(unittest.TestCase):
    def test_blind_package_contains_only_calibration_pairs_and_no_scores(self):
        blind, audit = generated()
        self.assertEqual(blind["protocol"], package.BLIND_PROTOCOL)
        self.assertEqual(audit["pilotSha256"],
                         package.fingerprint(package.canonical_bytes(blind)))
        manifest = fixture()
        _, _, membership, _ = prepare_inventory(manifest)
        for pair in blind["pairs"]:
            self.assertEqual(membership[pair["leftRevisionId"].casefold()],
                             "calibration")
            self.assertEqual(membership[pair["rightRevisionId"].casefold()],
                             "calibration")
        shown = json.dumps(blind)
        for forbidden in ("dhashDistance", "clipCosine", "candidateBucket",
                          "partition", "sourceKind", "held-out", "contentSha256",
                          "qualityLevel", "humanReview"):
            self.assertNotIn(forbidden, shown)
        self.assertEqual(audit["exactCopyControlQuota"], 0)
        self.assertFalse(audit["heldOutPairEvidenceIncluded"])

    def test_sampling_stable_with_permuted_manifest(self):
        baseline = fixture()
        with patch.dict(package.QUOTAS, SMALL_QUOTAS, clear=True):
            first, _ = package.make_package(baseline)
            permuted = copy.deepcopy(baseline)
            permuted["photos"].reverse()
            permuted["pairs"].reverse()
            second, _ = package.make_package(permuted)
        self.assertEqual(first, second)

    def test_package_output_new_only_and_calibration_report_bound(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            private = root / "private"
            private.mkdir()
            cand = private / "candidate.json"
            cand.write_text(json.dumps(fixture()), encoding="utf-8")
            blind = private / "blind.json"
            audit = private / "audit.json"
            with patch.dict(package.QUOTAS, SMALL_QUOTAS, clear=True):
                summary = package.create_files(cand, blind, audit)
                self.assertEqual(summary["heldOutPhotosIncluded"], 0)
                with self.assertRaises(ValueError):
                    package.create_files(cand, blind, audit)
            selected = json.loads(blind.read_text())
            labels = private / "labels.json"
            reviews = {
                "schemaVersion": 1, "protocol": package.LABELS_PROTOCOL,
                "pilotSha256": package.fingerprint(blind.read_bytes()),
                "reviews": [
                    {"pairId": selected["pairs"][0]["pairId"],
                     "label": "similar-keep-both", "notes": ""},
                    {"pairId": selected["pairs"][1]["pairId"],
                     "label": "redundant", "notes": ""},
                ]
            }
            labels.write_text(json.dumps(reviews), encoding="utf-8")
            with patch.dict(package.QUOTAS, SMALL_QUOTAS, clear=True):
                report = package.report(blind, audit, labels)
            self.assertEqual(report["reviewedPairs"], 2)
            self.assertFalse(report["heldOutReviewsAccessed"])
            self.assertFalse(report["productionSuppressionApproved"])
            self.assertNotIn("private-revision-", json.dumps(report))
            reviews["pilotSha256"] = "f"*64
            labels.write_text(json.dumps(reviews), encoding="utf-8")
            with self.assertRaises(ValueError):
                package.report(blind, audit, labels)

    def test_review_proxy_mapping_sha_verification_and_resumable_labels(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            blind, audit, mapping, labels = prepare_private(root)
            session = load_session(blind, mapping, labels)
            first = public_state(session, 0)
            self.assertEqual(first["remaining"],
                             len(session["blind"]["pairs"]))
            image = read_verified_proxy(session, first["pairId"], "left")
            self.assertTrue(image.startswith(b"fake-jpeg-"))
            self.assertEqual(
                store_review(session, first["pairId"], "similar-keep-both",
                             "Both meaningful"), 1)
            self.assertEqual(public_state(session, 0)["reviewed"], 1)
            resumed = load_session(blind, mapping, labels)
            self.assertEqual(public_state(resumed, 0)["notes"],
                             "Both meaningful")
            self.assertEqual(
                store_review(resumed, first["pairId"], "not-similar", "changed"), 1)
            self.assertEqual(public_state(resumed, 0)["label"], "not-similar")
            photo_id = resumed["blind"]["pairs"][0]["leftRevisionId"].casefold()
            proxy_file = resumed["photos"][photo_id][0]
            proxy_file.write_bytes(b"tampered-jpeg")
            with self.assertRaises(ValueError):
                read_verified_proxy(resumed, first["pairId"], "left")

    def test_rejects_bad_map_pilot_bindings_and_path_escape(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            blind, audit, mapping, labels = prepare_private(root)
            bad = json.loads(mapping.read_text())
            bad["pilotSha256"] = "0"*64
            mapping.write_text(json.dumps(bad), encoding="utf-8")
            with self.assertRaises(ValueError):
                load_session(blind, mapping, labels)
            bad["pilotSha256"] = package.fingerprint(blind.read_bytes())
            bad["photos"][0]["relativePath"] = "../outside.jpg"
            mapping.write_text(json.dumps(bad), encoding="utf-8")
            with self.assertRaises(ValueError):
                load_session(blind, mapping, labels)

    def test_user_labels_not_accepted_in_source_manifest(self):
        data = fixture()
        data["pairs"][0]["reviewLabel"] = "redundant"
        with self.assertRaises(ValueError):
            package.make_package(data)


if __name__ == "__main__":
    unittest.main()
