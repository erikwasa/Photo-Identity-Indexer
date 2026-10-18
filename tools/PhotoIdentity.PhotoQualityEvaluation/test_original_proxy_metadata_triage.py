"""Offline, no-decoding tests for private original/proxy failure metadata triage."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from original_proxy_metadata_triage import (
    PROVENANCE, triage, validate_previous,
)
from original_proxy_study import PROTOCOL, select_pairs


class OriginalMetadataTriageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.original = self.root / "originals"
        self.original.mkdir()
        photos = []
        for i in range(81):
            source = f"src-{i}.dng"
            content = f"original-private-{i}".encode("utf-8")
            (self.original / source).write_bytes(content)
            family = ("jpeg", "heic", "png", "raw")[i % 4]
            photos.append({
                "revisionId": f"rev-{i}", "sourceKey": source,
                "contentSha256": hashlib.sha256(content).hexdigest(),
                "proxyRelativePath": f"proxy/{i}.jpg",
                "proxySha256": "0" * 64,
                "stratum": {"mediaFamily": family},
                "partition": "held-out" if i == 80 else
                             "calibration" if i % 2 else "challenge",
                "sampleKind": "representative" if i % 2 else "challenge",
                "review": {"qualityLevel": "very-low"},
            })
        self.benchmark = {
            "schemaVersion": 2,
            "benchmarkId": "synthetic",
            "proxyProfile": "profile-id=jpeg-1600-q78\njpeg-quality=78",
            "photos": photos,
        }
        self.selection = select_pairs(self.benchmark, 80)
        failures = {"jpeg": 4, "raw": 13}
        rows = []
        for photo in self.selection["photos"]:
            failed = failures.get(photo["mediaFamily"], 0) > 0
            if failed:
                failures[photo["mediaFamily"]] -= 1
            rows.append({
                "revisionId": photo["revisionId"],
                "mediaFamily": photo["mediaFamily"],
                "originalScore": None if failed else 0.7,
                "proxyScore": None if failed else 0.6,
                "errorCode": "decode-or-inference-failed" if failed else None,
            })
        self.scores = {
            "protocol": PROTOCOL,
            "benchmarkId": "synthetic",
            "selectionSha256": self.selection["selectionSha256"],
            "modelName": "topiq_nr",
            "plannedPairs": 80,
            "maxOriginalLongEdge": None,
            "isProbe": False,
            "photos": rows,
        }

    def fake_header(self, path, family):
        return (8000, 6000) if family == "raw" else (4000, 3000)

    def test_failed_only_header_report_private_and_risk_sizes(self):
        report = triage(
            self.benchmark, self.selection, self.scores, self.original,
            dimension_reader=self.fake_header)
        self.assertEqual(report["protocol"], PROVENANCE)
        self.assertEqual(report["selectedPairs"], 80)
        self.assertEqual(report["previousFailures"], 17)
        self.assertEqual(report["metadataChecked"], 17)
        raw = report["byMediaFamilyAndPreviousStatus"]["raw"]["failed"]
        jpeg = report["byMediaFamilyAndPreviousStatus"]["jpeg"]["failed"]
        self.assertEqual(raw["count"], 13)
        self.assertEqual(raw["medianMegapixels"], 48)
        self.assertEqual(raw["countAtLeast40MP"], 13)
        self.assertEqual(jpeg["count"], 4)
        self.assertEqual(jpeg["medianMegapixels"], 12)
        self.assertNotIn("rev-", json.dumps(report))
        self.assertNotIn("sourceKey", json.dumps(report))
        self.assertNotIn("private-", json.dumps(report))
        self.assertNotIn("very-low", json.dumps(report))
        self.assertNotIn(str(self.original), json.dumps(report))

    def test_all_scope_can_compare_previous_successes_without_labels(self):
        report = triage(self.benchmark, self.selection, self.scores,
                        self.original, scope="all",
                        dimension_reader=self.fake_header)
        self.assertEqual(report["metadataChecked"], 80)
        groups = report["byMediaFamilyAndPreviousStatus"]
        self.assertEqual(groups["raw"]["success"]["count"], 7)
        self.assertEqual(groups["jpeg"]["success"]["count"], 16)
        self.assertEqual(report["previousFailures"], 17)

    def test_heldout_mutations_cannot_change_metadata_report(self):
        first = triage(self.benchmark, self.selection, self.scores, self.original,
                       dimension_reader=self.fake_header)
        self.benchmark["photos"][-1]["review"]["qualityLevel"] = "very-high"
        self.benchmark["photos"][0]["review"]["qualityLevel"] = "unsure"
        second = triage(self.benchmark, self.selection, self.scores, self.original,
                        dimension_reader=self.fake_header)
        self.assertEqual(first, second)

    def test_header_unavailable_aggregates_without_details(self):
        def reader(path, family):
            if family == "raw":
                raise RuntimeError("sensitive source path")
            return (3000, 2000)

        report = triage(self.benchmark, self.selection, self.scores, self.original,
                        dimension_reader=reader)
        self.assertEqual(report["metadataUnavailable"], 13)
        self.assertEqual(
            report["byMediaFamilyAndPreviousStatus"]["raw"]["failed"]["headerUnavailable"], 13)
        self.assertNotIn("sensitive", json.dumps(report))

    def test_score_export_must_match_frozen_selected_order(self):
        self.scores["photos"][0]["revisionId"] = "foreign"
        with self.assertRaisesRegex(ValueError, "selected benchmark order"):
            validate_previous(self.benchmark, self.selection, self.scores)

    def test_mutated_original_is_hard_stop(self):
        failed = next(p for p, row in zip(self.selection["photos"], self.scores["photos"])
                      if row["errorCode"] is not None)
        (self.original / failed["sourceKey"]).write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "identity/availability"):
            triage(self.benchmark, self.selection, self.scores, self.original,
                   dimension_reader=self.fake_header)

    def test_rejects_partial_and_capped_score_exports(self):
        self.scores["photos"].pop()
        with self.assertRaises(ValueError):
            validate_previous(self.benchmark, self.selection, self.scores)
        self.scores["photos"].append({
            "revisionId": "foreign", "mediaFamily": "raw",
            "originalScore": None, "proxyScore": None,
            "errorCode": "decode-or-inference-failed"})
        self.scores["maxOriginalLongEdge"] = 2560
        with self.assertRaisesRegex(ValueError, "original complete-size"):
            validate_previous(self.benchmark, self.selection, self.scores)


if __name__ == "__main__":
    unittest.main()
