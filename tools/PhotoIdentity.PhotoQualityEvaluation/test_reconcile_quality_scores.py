"""Offline verification of private quality-score extension; no learned model downloads."""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from reconcile_quality_scores import (
    POOL_INPUT_KIND, POOL_POLICY, PROTOCOL, SELECTED_INPUT_KIND,
    extend, score_selected, validate_expansion,
)


class FakeScalar:
    def item(self):
        return 0.75


class ReconcileTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.proxy_root = self.root / "proxies"
        self.proxy_root.mkdir()

        self.before = {
            "schemaVersion": 2, "benchmarkId": "frozen",
            "proxyProfile": "profile-id=jpeg-1600-q78\nmaximum-long-edge=1600\njpeg-quality=78",
            "selectionPolicy": "frozen-policy",
            "partitionPolicy": "calibration60-heldout40",
            "representativeCount": 2, "calibrationCount": 1, "heldOutCount": 1,
            "challengeReserve": 3, "challengeCount": 1,
            "challengeSelectionPolicies": ["initial"],
            "photos": [
                self.photo("cal", "calibration", "representative", 0),
                self.photo("held-out", "held-out", "representative", 1),
                self.photo("initial", "challenge", "challenge", 2),
            ],
        }
        self.selection = {
            "schemaVersion": 1,
            "benchmarkId": "frozen",
            "selectionPolicy": "m37-topiq-musiq-rank-disagreement-v1",
            "photos": [
                self.photo("new-0", "challenge", "challenge", 3),
                self.photo("new-1", "challenge", "challenge", 4),
            ],
        }
        self.expanded = deepcopy(self.before)
        self.expanded["challengeCount"] = 3
        self.expanded["challengeSelectionPolicies"] += [
            self.selection["selectionPolicy"]
        ]
        self.expanded["photos"] += deepcopy(self.selection["photos"])
        self.pool = {
            "schemaVersion": 1,
            "benchmarkId": "frozen",
            "selectionPolicy": POOL_POLICY,
            "photos": deepcopy(self.selection["photos"]) + [
                self.photo("unused-pool", "challenge", "challenge", 5)
            ],
        }

    def photo(self, revision, partition, sample, i):
        pixels = ("fake-bytes-" + revision).encode()
        digest = hashlib.sha256(pixels).hexdigest()
        path = revision + ".jpg"
        (self.proxy_root / path).write_bytes(pixels)
        return {
            "revisionId": revision,
            "contentSha256": digest,
            "proxySha256": digest,
            "proxyRelativePath": path,
            "partition": partition,
            "sampleKind": sample,
            "challengeReasons": ["rare-camera"],
            "review": {
                "qualityLevel": "high" if partition == "calibration" else None,
                "personallyImportant": None,
                "notes": None,
            },
        }

    @staticmethod
    def score(name, kind, photo_ids, n=0):
        candidate = {
            "candidateId": "pyiqa-" + name + "-0.1.16",
            "modelName": name,
            "modelVersion": "pyiqa-0.1.16; weights-not-yet-pinned",
            "executionProvider": "cpu",
            "framework": "pyiqa/PyTorch",
            "source": "test",
            "license": "not-verified",
            "preprocessing": "frozen-proxy",
            "scoreSemantics": "higher=better",
            "modelSizeBytes": None,
        }
        return {
            "schemaVersion": 1, "benchmarkId": "frozen",
            "generatedAtUtc": "2026-10-17T01:00:00Z",
            "candidate": candidate,
            "inputKind": kind, "higherIsBetter": True,
            "photoCount": len(photo_ids),
            "runtime": {"totalElapsedMilliseconds": 100,
                        "medianInferenceMilliseconds": 3,
                        "p95InferenceMilliseconds": 3},
            "scores": [
                {"revisionId": k, "rawScore": float(i + n),
                 "errorCode": None, "inferenceMilliseconds": float(i + 1),
                 "diagnostics": []}
                for i, k in enumerate(photo_ids)
            ],
        }

    def base(self, name="topiq_nr"):
        return self.score(name, "review-proxy",
                          [p["revisionId"] for p in self.before["photos"]])

    def supplementary_pool(self, name="topiq_nr"):
        return self.score(name, POOL_INPUT_KIND,
                          [p["revisionId"] for p in self.pool["photos"]], 100)

    def test_extend_uses_only_selected_subset_from_scored_pool(self):
        base = self.base()
        supplement = self.supplementary_pool()
        out = extend(self.before, self.expanded, self.selection,
                     base, supplement, self.pool)
        self.assertEqual(out["photoCount"], 5)
        self.assertEqual([p["revisionId"] for p in out["scores"]],
                         ["cal", "held-out", "initial", "new-0", "new-1"])
        self.assertEqual([p["rawScore"] for p in out["scores"]],
                         [0., 1., 2., 100., 101.])
        self.assertEqual(out["runtime"]["totalElapsedMilliseconds"], 9)
        self.assertEqual(out["reconciliation"]["protocol"], PROTOCOL)
        self.assertEqual(out["reconciliation"]["supplementInputKind"], POOL_INPUT_KIND)
        self.assertEqual(base["photoCount"], 3)
        self.assertEqual(len(supplement["scores"]), 3)

    def test_selected_only_arniqa_scoring_and_merge(self):
        output = str(self.root / "private" / "arniqa-selected.json")
        run = score_selected(self.before, self.expanded, self.selection,
                             str(self.proxy_root), "arniqa", "cpu", output,
                             metric_factory=lambda _name, _device:
                                 lambda _path: FakeScalar())
        self.assertEqual(run["inputKind"], SELECTED_INPUT_KIND)
        self.assertEqual(run["photoCount"], 2)
        raw = Path(output).read_text()
        self.assertNotIn("high", raw)
        self.assertNotIn(str(self.proxy_root), raw)
        base = self.base("arniqa")
        # The dependency-free fake model has an intentionally different
        # version descriptor; a production merge must reject it.
        with self.assertRaisesRegex(ValueError, "Model version"):
            extend(self.before, self.expanded, self.selection, base, run)
        run["candidate"] = deepcopy(base["candidate"])
        merged = extend(self.before, self.expanded, self.selection, base, run)
        self.assertEqual(merged["photoCount"], 5)

    def test_review_updates_never_affect_reconciliation(self):
        base, supplement = self.base(), self.supplementary_pool()
        first = extend(self.before, self.expanded, self.selection,
                       base, supplement, self.pool)
        self.expanded["photos"][0]["review"]["qualityLevel"] = "very-low"
        self.expanded["photos"][3]["review"]["qualityLevel"] = "high"
        after = extend(self.before, self.expanded, self.selection,
                       base, supplement, self.pool)
        self.assertEqual(first["scores"], after["scores"])

    def test_frozen_representative_changes_rejected(self):
        self.expanded["photos"][0]["proxySha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "existing benchmark photo"):
            validate_expansion(self.before, self.expanded, self.selection)

    def test_selected_not_imported_rejected(self):
        self.expanded["photos"].pop()
        with self.assertRaisesRegex(ValueError, "count"):
            validate_expansion(self.before, self.expanded, self.selection)

    def test_extra_foreign_or_missing_scores_rejected(self):
        bad = self.supplementary_pool()
        bad["scores"][1]["revisionId"] = "foreign"
        with self.assertRaisesRegex(ValueError, "foreign"):
            extend(self.before, self.expanded, self.selection,
                   self.base(), bad, self.pool)

    def test_wrong_pool_proxy_content_rejected(self):
        self.pool["photos"][0]["proxySha256"] = "f" * 64
        with self.assertRaisesRegex(ValueError, "differs"):
            extend(self.before, self.expanded, self.selection,
                   self.base(), self.supplementary_pool(), self.pool)

    def test_changes_to_model_version_rejected(self):
        bad = self.supplementary_pool()
        bad["candidate"]["modelVersion"] = "different-checkpoint"
        with self.assertRaisesRegex(ValueError, "Model version"):
            extend(self.before, self.expanded, self.selection,
                   self.base(), bad, self.pool)

    def test_failed_supplemental_inference_preserves_explicit_error(self):
        selected = self.score(
            "topiq_nr", SELECTED_INPUT_KIND, ["new-0", "new-1"])
        selected["scores"][0]["rawScore"] = None
        selected["scores"][0]["errorCode"] = "inference-failed"
        merged = extend(self.before, self.expanded, self.selection,
                        self.base(), selected)
        self.assertIsNone(merged["scores"][3]["rawScore"])
        self.assertEqual(merged["scores"][3]["errorCode"], "inference-failed")

    def test_no_duplicate_content_import(self):
        self.selection["photos"][0]["contentSha256"] = (
            self.before["photos"][0]["contentSha256"])
        self.expanded["photos"][3]["contentSha256"] = (
            self.before["photos"][0]["contentSha256"])
        with self.assertRaisesRegex(ValueError, "repeats"):
            validate_expansion(self.before, self.expanded, self.selection)


if __name__ == "__main__":
    unittest.main()
