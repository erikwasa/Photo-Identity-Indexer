"""Offline contract tests. No PyTorch or model downloads required."""
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest

import learned_iqa


class FakeScalar:
    def __init__(self, value):
        self.value = value

    def item(self):
        return self.value


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.proxies = self.root / "proxies"
        self.proxies.mkdir()
        content = b"fake jpeg bytes for contract-only fake metric"
        (self.proxies / "photo.jpg").write_bytes(content)
        digest = hashlib.sha256(content).hexdigest()
        self.manifest = self.root / "benchmark.json"
        self.manifest.write_text(json.dumps({
            "schemaVersion": 2,
            "benchmarkId": "frozen-benchmark",
            "proxyProfile": "profile-id=jpeg-1600-q78\nmaximum-long-edge=1600\njpeg-quality=78",
            "photos": [{
                "revisionId": "test-photo",
                "proxyRelativePath": "photo.jpg",
                "proxySha256": digest,
                "partition": "held-out",
                "review": {"qualityLevel": "very-low"}
            }]
        }), encoding="utf-8")

    def args(self, output="private/topiq.json"):
        return SimpleNamespace(
            input=str(self.manifest),
            proxy_root=str(self.proxies),
            output=str(self.root / output),
            metric="topiq_nr",
            device="cpu",
            limit=None
        )

    def test_score_contract_does_not_export_human_labels_or_paths(self):
        result = learned_iqa.run(
            self.args(), lambda metric, device: lambda path: FakeScalar(0.75))
        self.assertEqual(result["benchmarkId"], "frozen-benchmark")
        self.assertEqual(result["scores"][0]["rawScore"], 0.75)
        self.assertEqual(result["photoCount"], 1)
        raw = (self.root / "private/topiq.json").read_text()
        self.assertNotIn("very-low", raw)
        self.assertNotIn(str(self.proxies), raw)
        self.assertNotIn("proxyRelativePath", raw)

    def test_tampered_proxy_refuses_export_before_initializing_model(self):
        manifest = json.loads(self.manifest.read_text())
        manifest["photos"][0]["proxySha256"] = "0" * 64
        self.manifest.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "digest"):
            learned_iqa.run(
                self.args(),
                lambda metric, device: self.fail("metric initialized before SHA validation"))
        self.assertFalse((self.root / "private/topiq.json").exists())

    def test_traversal_rejected(self):
        with self.assertRaises(ValueError):
            learned_iqa.verified_proxy(
                self.proxies.resolve(),
                "../secret.jpg",
                "0" * 64)

    def test_failed_inference_is_explicit(self):
        def fail(_):
            raise RuntimeError("sensitive source path must never appear")
        result = learned_iqa.run(
            self.args(), lambda metric, device: fail)
        self.assertIsNone(result["scores"][0]["rawScore"])
        self.assertEqual(result["scores"][0]["errorCode"], "inference-failed")
        self.assertNotIn("sensitive source", json.dumps(result))

    def test_overwrite_refused(self):
        learned_iqa.run(
            self.args(), lambda metric, device: lambda path: FakeScalar(1.0))
        with self.assertRaisesRegex(ValueError, "overwrite"):
            learned_iqa.run(
                self.args(), lambda metric, device: lambda path: FakeScalar(1.0))


if __name__ == "__main__":
    unittest.main()
