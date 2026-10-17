"""Offline contract tests for original-versus-proxy private study."""
from copy import deepcopy
import hashlib
from pathlib import Path
import tempfile
import unittest

from original_proxy_study import (
    PROTOCOL, safe_original, score_pairs, select_pairs, summarize,
    validate_selection,
)


class Scalar:
    def __init__(self, value):
        self.value = value

    def item(self):
        return self.value


class StudyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.original = self.root / "originals"
        self.proxy = self.root / "proxies"
        self.original.mkdir()
        self.proxy.mkdir()
        photos = []
        for i in range(81):
            source = f"original-{i}.jpg"
            relative = f"proxy-{i}.jpg"
            orig_content = f"original-pixels-{i}".encode()
            proxy_content = f"proxy-pixels-{i}".encode()
            (self.original / source).write_bytes(orig_content)
            (self.proxy / relative).write_bytes(proxy_content)
            photos.append({
                "revisionId": f"revision-{i}",
                "sourceKey": source,
                "proxyRelativePath": relative,
                "contentSha256": hashlib.sha256(orig_content).hexdigest(),
                "proxySha256": hashlib.sha256(proxy_content).hexdigest(),
                "partition": "held-out" if i == 80 else
                             "calibration" if i % 2 else "challenge",
                "sampleKind": "representative" if i % 2 else "challenge",
                "stratum": {"mediaFamily": ("jpeg", "heic", "png", "raw")[i % 4]},
                "review": {"qualityLevel": "very-low" if i == 80 else None},
            })
        self.benchmark = {
            "schemaVersion": 2, "benchmarkId": "frozen",
            "proxyProfile": "profile-id=jpeg-1600-q78\njpeg-quality=78",
            "photos": photos,
        }

    def test_selection_is_deterministic_blind_and_uses_all_four_families(self):
        a = select_pairs(self.benchmark, 80)
        self.benchmark["photos"][-1]["review"]["qualityLevel"] = "high"
        self.benchmark["photos"][0]["review"]["qualityLevel"] = "normal"
        b = select_pairs(self.benchmark, 80)
        self.assertEqual(a, b)
        self.assertEqual(len(a["photos"]), 80)
        self.assertTrue(all(p["revisionId"] != "revision-80" for p in a["photos"]))
        self.assertEqual(
            {p["mediaFamily"] for p in a["photos"]}, {"jpeg", "heic", "png", "raw"})
        self.assertNotIn("review", str(a))
        self.assertEqual(len(validate_selection(self.benchmark, a)), 80)

    def test_cannot_contaminate_with_heldout_revision(self):
        selected = select_pairs(self.benchmark)
        selected["photos"][0]["revisionId"] = "revision-80"
        with self.assertRaisesRegex(ValueError, "fingerprint"):
            validate_selection(self.benchmark, selected)

    def test_mutated_original_digest_is_rejected(self):
        selection = select_pairs(self.benchmark)
        self.benchmark["photos"][0]["contentSha256"] = "f" * 64
        with self.assertRaisesRegex(ValueError, "Selection changed"):
            validate_selection(self.benchmark, selection)

    def test_original_hash_mismatch_rejects_before_model_loading(self):
        selection = select_pairs(self.benchmark)
        source_name = selection["photos"][0]["sourceKey"]
        (self.original / source_name).write_bytes(b"altered")
        model_called = []
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            score_pairs(
                self.benchmark, selection, self.original, self.proxy,
                "musiq", "cpu", str(self.root / "private" / "bad.json"),
                metric_factory=lambda _name, _device:
                    model_called.append(True))
        self.assertEqual(model_called, [])
        self.assertFalse((self.root / "private" / "bad.json").exists())

    def test_pairs_scored_offline_without_paths_or_labels_in_export(self):
        selection = select_pairs(self.benchmark)
        def decode(original, family, dest):
            self.assertTrue(original.is_file())
            self.assertIn(family, ("jpeg", "heic", "raw", "png"))
            dest.write_bytes(b"lossless-decoded-pixels")

        def metric(_model, _device):
            def run(path):
                return Scalar(0.9 if "decoded-original" in path else 0.5)
            return run

        result = score_pairs(
            self.benchmark, selection, self.original, self.proxy,
            "topiq_nr", "cpu", str(self.root / "private" / "scores.json"),
            metric_factory=metric, decode_factory=decode)
        report = summarize(result)
        self.assertEqual(result["protocol"], PROTOCOL)
        self.assertEqual(report["scoredPairs"], 80)
        self.assertTrue(report["complete"])
        self.assertEqual(report["failures"], 0)
        self.assertAlmostEqual(report["medianProxyMinusOriginal"], -0.4)
        raw = (self.root / "private" / "scores.json").read_text()
        self.assertNotIn("sourceKey", raw)
        self.assertNotIn("qualityLevel", raw)
        self.assertNotIn(str(self.root), raw)
        self.assertNotIn("revision-80", raw)

    def test_original_path_traversal_rejected(self):
        with self.assertRaisesRegex(ValueError, "source key"):
            safe_original(self.original, "../secret.jpg", "x" * 64)
        with self.assertRaisesRegex(ValueError, "source key"):
            safe_original(self.original, "C:/secret.jpg", "x" * 64)

    def test_inference_failure_is_explicit_and_not_counted(self):
        selection = select_pairs(self.benchmark)
        def decode(_original, _family, dest):
            dest.write_bytes(b"fake")
        def metric(_name, _device):
            def run(path):
                if "decoded-original" in path:
                    raise RuntimeError("a private path must not surface")
                return Scalar(0.3)
            return run
        result = score_pairs(
            self.benchmark, selection, self.original, self.proxy,
            "musiq", "cpu", str(self.root / "private" / "failure.json"),
            metric_factory=metric, decode_factory=decode)
        summary = summarize(result)
        self.assertEqual(summary["failures"], 80)
        self.assertFalse(summary["complete"])
        self.assertNotIn("a private path", str(result))

    def test_select_only_supported_count_and_families(self):
        with self.assertRaisesRegex(ValueError, "50 to 100"):
            select_pairs(self.benchmark, 20)
        with self.assertRaisesRegex(ValueError, "media-family"):
            select_pairs(self.benchmark, 80, ("jpeg", "jpeg"))


if __name__ == "__main__":
    unittest.main()
