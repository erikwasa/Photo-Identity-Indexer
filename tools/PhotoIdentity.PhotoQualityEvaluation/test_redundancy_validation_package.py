"""Synthetic-only held-out validation freeze, leakage and review integrity tests."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import redundancy_validation_package as validation
from redundancy_pilot_package import (
    LABELS_PROTOCOL as CALIB_LABELS_PROTOCOL, canonical_bytes, fingerprint,
    make_package,
)
from redundancy_pilot_review import load_session, public_state, read_verified_proxy, store_review
from test_redundancy_review_pairs import fixture
from redundancy_review_pairs import prepare_inventory


MINI_QUOTAS = {
    "strict-dhash-0-to-4": 1,
    "weak-dhash-5-to-8": 2,
    "weak-dhash-9-to-12": 2,
    "screened-random-control": 2,
    "other-candidate": 2,
}


def synthetic():
    src = fixture()
    with patch.dict(validation.QUOTAS, MINI_QUOTAS, clear=True):
        calib, _ = make_package(src)
    return src, calib


def generate(src, calib):
    with patch.dict(validation.QUOTAS, MINI_QUOTAS, clear=True), (
            patch.object(validation, "MIN_PAIRS", 1)):
        return validation.prepare(src, calib)


class IndependentHeldoutValidationTests(unittest.TestCase):
    def test_frozen_package_is_disjoint_blind_and_deterministic(self):
        src, calib = synthetic()
        blind, audit = generate(src, calib)
        self.assertEqual(audit["status"],
                         "heldout-validation-frozen-before-human-labels")
        self.assertFalse(audit["heldOutReviewLabelsRead"])
        self.assertFalse(audit["calibrationReviewLabelsRead"])
        self.assertFalse(audit["productionSuppressionApproved"])
        self.assertEqual(audit["pilotSha256"],
                         fingerprint(canonical_bytes(blind)))
        _, _, membership, _ = prepare_inventory(src)
        calib_revisions = {item[side].casefold()
                           for item in calib["pairs"]
                           for side in ("leftRevisionId", "rightRevisionId")}
        for item in blind["pairs"]:
            for side in ("leftRevisionId", "rightRevisionId"):
                self.assertEqual(membership[item[side].casefold()], "held-out")
                self.assertNotIn(item[side].casefold(), calib_revisions)
        shown = json.dumps(blind)
        for private in ("dhashDistance", "clipCosine", "candidateBucket",
                        "sourceKind", "calibration", "model", "held-out"):
            self.assertNotIn(private, shown)
        reordered = copy.deepcopy(src)
        reordered["photos"].reverse()
        reordered["pairs"].reverse()
        other, audit2 = generate(reordered, calib)
        self.assertEqual(blind, other)
        self.assertEqual(audit["pairEvidence"], audit2["pairEvidence"])

    def test_observed_calibration_cross_links_are_excluded_without_repartitioning(self):
        src, calib = synthetic()
        inv, rows, membership, _ = prepare_inventory(src)
        calibration_id = calib["pairs"][0]["leftRevisionId"].casefold()
        heldout = next((b for b in sorted(inv)
                        if membership[b] == "held-out"
                        and tuple(sorted((calibration_id, b))) not in rows))
        src["pairs"].append({
            "leftRevisionId": calibration_id,
            "rightRevisionId": heldout,
            "sourceKind": "retrieved",
            "dhashDistance": 7,
            "clipCosine": .50,
        })
        with patch.dict(validation.QUOTAS, MINI_QUOTAS, clear=True), (
                patch.object(validation, "MIN_PAIRS", 1)), (
                patch.object(validation, "MAX_QUARANTINED_FRACTION", 1.0)):
            blind, audit = validation.prepare(src, calib)
        self.assertGreaterEqual(
            audit["heldOutPhotosQuarantinedByObservedLinks"], 1)
        self.assertGreaterEqual(
            audit["observedCrossSplitRiskEdgesToCalibrationPilot"], 1)
        visible = {item[side].casefold() for item in blind["pairs"]
                   for side in ("leftRevisionId", "rightRevisionId")}
        self.assertNotIn(heldout, visible)

    def test_guard_failure_refuses_to_write_any_pilot_artifacts(self):
        src, calib = synthetic()
        with tempfile.TemporaryDirectory() as folder:
            private = Path(folder) / "private"
            private.mkdir()
            candidate = private / "candidate.json"
            calibration = private / "calibration.json"
            candidate.write_text(json.dumps(src), encoding="utf-8")
            calibration.write_text(json.dumps(calib), encoding="utf-8")
            blind = private / "holdout.json"
            audit = private / "audit.json"
            with patch.dict(validation.QUOTAS, MINI_QUOTAS, clear=True), (
                    patch.object(validation, "MIN_PAIRS", 99999)):
                with self.assertRaises(ValueError):
                    validation.create_files(candidate, calibration, blind, audit)
            self.assertFalse(blind.exists())
            self.assertFalse(audit.exists())

    def test_private_review_binds_frozen_blind_and_cannot_reuse_calibration_labels(self):
        src, calib = synthetic()
        blind, audit = generate(src, calib)
        with tempfile.TemporaryDirectory() as folder:
            private = Path(folder) / "private"
            proxies = Path(folder) / "proxies"
            private.mkdir()
            proxies.mkdir()
            blind_path = private / "holdout.json"
            audit_path = private / "audit.json"
            proxy_map_path = private / "map.json"
            labels = private / "labels.json"
            blind_path.write_bytes(canonical_bytes(blind))
            audit_path.write_bytes(canonical_bytes(audit))
            revisions = sorted({item[side] for item in blind["pairs"]
                                for side in ("leftRevisionId", "rightRevisionId")})
            mapping = {
                "schemaVersion": 1, "protocol": "wi0194-private-pilot-proxies-v1",
                "pilotSha256": fingerprint(blind_path.read_bytes()),
                "proxyRoot": str(proxies), "proxyProfile": "synthetic-test",
                "catalogReadOnly": True, "noOriginalsHydrated": True,
                "photos": []
            }
            for index, revision in enumerate(revisions):
                payload = ("synthetic-proxy-" + str(index)).encode()
                relative = str(index) + ".jpg"
                (proxies / relative).write_bytes(payload)
                mapping["photos"].append({
                    "revisionId": revision, "relativePath": relative,
                    "sha256": hashlib.sha256(payload).hexdigest(),
                    "byteLength": len(payload)
                })
            proxy_map_path.write_text(json.dumps(mapping), encoding="utf-8")
            session = load_session(blind_path, proxy_map_path, labels)
            self.assertTrue(session["heldout"])
            initial = public_state(session, 0)
            self.assertTrue(read_verified_proxy(session, initial["pairId"], "left"))
            self.assertEqual(store_review(session, initial["pairId"], "redundant", ""), 1)
            resumed = load_session(blind_path, proxy_map_path, labels)
            self.assertEqual(public_state(resumed, 0)["reviewed"], 1)
            with patch.dict(validation.QUOTAS, MINI_QUOTAS, clear=True):
                report = validation.report(blind_path, audit_path, labels)
            self.assertEqual(report["reviewedPairs"], 1)
            self.assertFalse(report["automaticSuppressionAuthorized"])
            self.assertNotIn(revisions[0], json.dumps(report))
            corrupted = json.loads(labels.read_text())
            corrupted["protocol"] = CALIB_LABELS_PROTOCOL
            labels.write_text(json.dumps(corrupted), encoding="utf-8")
            with self.assertRaises(ValueError):
                load_session(blind_path, proxy_map_path, labels)
            with self.assertRaises(ValueError):
                validation.report(blind_path, audit_path, labels)

    def test_calibration_input_labels_are_not_read(self):
        src, calib = synthetic()
        src["photos"][0]["review"] = {"label": "redundant"}
        with self.assertRaises(ValueError):
            generate(src, calib)


if __name__ == "__main__":
    unittest.main()
