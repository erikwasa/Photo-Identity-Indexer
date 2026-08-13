from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name("experiment.py")
SPEC = importlib.util.spec_from_file_location("visible_content_experiment", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
experiment = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = experiment
SPEC.loader.exec_module(experiment)


class VisibleContentExperimentTests(unittest.TestCase):
    def test_threshold_selection_and_proxy_original_agreement(self):
        samples = [
            experiment.ScoredSample(
                "one", "proxy", frozenset({"dog"}),
                {"dog": 0.31, "beach": 0.11, "indoors": 0.20}),
            experiment.ScoredSample(
                "one", "original", frozenset({"dog"}),
                {"dog": 0.32, "beach": 0.10, "indoors": 0.19}),
            experiment.ScoredSample(
                "two", "proxy", frozenset(),
                {"dog": 0.19, "beach": 0.18, "indoors": 0.17}),
        ]

        proxy = [sample for sample in samples if sample.input_kind == "proxy"]
        selected = experiment.choose_threshold(
            experiment.threshold_sweep(proxy, [0.18, 0.20, 0.30]))
        agreement = experiment.proxy_original_agreement(samples, 0.25, top_k=2)

        self.assertEqual(0.30, selected["threshold"])
        self.assertEqual(1.0, selected["precision"])
        self.assertEqual(1.0, selected["recall"])
        self.assertEqual(1, agreement["pairCount"])
        self.assertEqual(1.0, agreement["meanThresholdedJaccard"])
        self.assertEqual(1.0, agreement["meanTopKOverlap"])

    def test_manifest_digest_does_not_include_filesystem_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            manifest = Path(directory) / "manifest.json"
            marker = "local-fixture-marker"
            manifest.write_text(json.dumps({
                "schemaVersion": 1,
                "samples": [{
                    "id": "sample-01",
                    "proxyPath": f"{marker}/proxy.jpg",
                    "originalPath": f"{marker}/original.jpg",
                    "expectedTags": ["Dog"]
                }]
            }), encoding="utf-8")

            samples, digest = experiment.load_manifest(manifest)

            self.assertEqual("sample-01", samples[0].sample_id)
            self.assertEqual({"dog"}, set(samples[0].expected_tags))
            self.assertNotIn(marker, digest)
            self.assertEqual(64, len(digest))


if __name__ == "__main__":
    unittest.main()
