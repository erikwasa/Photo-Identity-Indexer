"""Offline tests for WI-0193 reserved model-disagreement challenge flow."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from disagreement_challenges import (
    POOL_INPUT_KIND, POOL_POLICY, SELECTION_POLICY, score_pool, select,
    validate_pool,
)


class Scalar:
    def __init__(self, value):
        self.value = value

    def item(self):
        return self.value


class DisagreementTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.proxies = self.root / "proxies"
        self.proxies.mkdir()
        photos = []
        for i in range(24):
            content = f"synthetic-only-{i}".encode()
            (self.proxies / f"p{i}.jpg").write_bytes(content)
            photos.append({
                "revisionId": f"pool-{i}",
                "contentSha256": hashlib.sha256(content).hexdigest(),
                "proxyRelativePath": f"p{i}.jpg",
                "proxySha256": hashlib.sha256(content).hexdigest(),
                "sourceKey": f"private/source-{i}.jpg",
                "partition": "challenge",
                "sampleKind": "challenge",
                "review": {"qualityLevel": None,
                           "personallyImportant": None, "notes": None},
                "challengeReasons": ["rare-capture-era"],
            })
        self.pool = {
            "schemaVersion": 1, "benchmarkId": "frozen-benchmark",
            "selectionPolicy": POOL_POLICY, "photos": photos
        }
        self.benchmark = {
            "schemaVersion": 2, "benchmarkId": "frozen-benchmark",
            "proxyProfile": ("profile-id=jpeg-1600-q78\n"
                             "maximum-long-edge=1600\njpeg-quality=78"),
            "challengeReserve": 200, "challengeCount": 180,
            "photos": [{
                "revisionId": "original",
                "contentSha256": "9" * 64,
                "partition": "held-out",
                "review": {"qualityLevel": "very-low"}
            }]
        }

    def candidate_export(self, model, values):
        return {
            "schemaVersion": 1,
            "benchmarkId": "frozen-benchmark",
            "inputKind": POOL_INPUT_KIND,
            "higherIsBetter": True,
            "candidate": {"candidateId": model, "modelName": model},
            "photoCount": 24,
            "scores": [
                {"revisionId": f"pool-{i}", "rawScore": values[i],
                 "errorCode": None}
                for i in range(24)
            ],
        }

    def test_selection_is_deterministic_and_blind(self):
        topiq = self.candidate_export("topiq_nr", list(range(24)))
        musiq = self.candidate_export("musiq", list(reversed(range(24))))
        a = select(self.benchmark, self.pool, topiq, musiq)
        self.benchmark["photos"][0]["review"]["qualityLevel"] = "very-high"
        b = select(self.benchmark, self.pool, topiq, musiq)
        self.assertEqual(a, b)
        self.assertEqual(a["selectionPolicy"], SELECTION_POLICY)
        self.assertEqual(len(a["photos"]), 20)
        self.assertEqual(len({p["contentSha256"] for p in a["photos"]}), 20)
        self.assertTrue(all(p["review"]["qualityLevel"] is None
                            for p in a["photos"]))
        self.assertTrue(all("model-rank-disagreement-topiq-nr-musiq-v1"
                            in p["challengeReasons"] for p in a["photos"]))

    def test_reject_overlap_with_benchmark_by_original_content(self):
        self.pool["photos"][0]["contentSha256"] = "9" * 64
        with self.assertRaisesRegex(ValueError, "overlaps"):
            validate_pool(self.benchmark, self.pool)

    def test_reject_duplicate_pool_original_content(self):
        self.pool["photos"][1]["contentSha256"] = (
            self.pool["photos"][0]["contentSha256"])
        with self.assertRaisesRegex(ValueError, "duplicate"):
            validate_pool(self.benchmark, self.pool)

    def test_pool_scoring_uses_verified_pixels_and_no_labels(self):
        def fake_metric(_name, _device):
            return lambda _path: Scalar(0.8)

        export = score_pool(
            self.benchmark, self.pool, str(self.proxies), "topiq_nr",
            "cpu", str(self.root / "private" / "pool-scores.json"),
            metric_factory=fake_metric)
        self.assertEqual(export["inputKind"], POOL_INPUT_KIND)
        self.assertEqual(export["photoCount"], 24)
        raw = (self.root / "private" / "pool-scores.json").read_text()
        self.assertNotIn("sourceKey", raw)
        self.assertNotIn("private/source", raw)
        self.assertNotIn("very-low", raw)
        self.assertNotIn("held-out", raw)
        self.assertNotIn(str(self.proxies), raw)

    def test_score_export_for_existing_benchmark_cannot_be_used(self):
        bad = self.candidate_export("topiq_nr", list(range(24)))
        bad["inputKind"] = "review-proxy"
        with self.assertRaisesRegex(ValueError, "screening pool"):
            select(self.benchmark, self.pool, bad,
                   self.candidate_export("musiq", list(range(24))))

    def test_failed_inference_disallows_disagreement_selection(self):
        bad = self.candidate_export("musiq", list(range(24)))
        bad["scores"][0]["rawScore"] = None
        bad["scores"][0]["errorCode"] = "inference-failed"
        with self.assertRaisesRegex(ValueError, "invalid candidate score"):
            select(self.benchmark, self.pool,
                   self.candidate_export("topiq_nr", list(range(24))), bad)

    def test_cannot_consume_more_than_remaining_reserved_slots(self):
        with self.assertRaisesRegex(ValueError, "slots"):
            select(self.benchmark, self.pool,
                   self.candidate_export("topiq_nr", list(range(24))),
                   self.candidate_export("musiq", list(range(24))),
                   count=21)


if __name__ == "__main__":
    unittest.main()
