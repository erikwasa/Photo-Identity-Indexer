"""Synthetic, offline WI-0195 bounded research-score gate tests."""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import collection_shadow_batch_score as batch
import collection_shadow_simulator as shadow


IDS = [f"00000000-0000-0000-0000-{i:012d}" for i in range(1, 7)]


def scenarios():
    return {
        "schemaVersion": 1, "protocol": shadow.SCENARIO_PROTOCOL,
        "scenarios": [
            {"kind": "creative", "scenarioId": "batch-creative",
             "targetCount": 2, "candidates": [
                 {"revisionId": IDS[i], "rank": i,
                  "relevanceScore": 100, "kind": "direct-anchor"}
                 for i in range(4)]},
            {"kind": "search", "scenarioId": "batch-search",
             "targetCount": 1, "candidates": [
                 {"revisionId": IDS[i + 4], "rank": i,
                  "relevanceScore": 80, "kind": "direct-anchor"}
                 for i in range(2)]},
        ],
    }


def original_scores():
    return {
        "schemaVersion": 1, "inputKind": "review-proxy",
        "higherIsBetter": True,
        "candidate": {"candidateId": shadow.FROZEN_CANDIDATE},
        "scores": [
            {"revisionId": IDS[0], "rawScore": .15,
             "errorCode": None, "diagnostics": [],
             "inferenceMilliseconds": 2.0},
        ],
    }


def materialize(folder):
    root = Path(folder)
    private = root / "private"
    proxies = root / "proxies"
    private.mkdir()
    proxies.mkdir()
    case_file, map_file, score_file = (
        private / "cases.json", private / "map.json",
        private / "old-scores.json")
    case_file.write_text(json.dumps(scenarios()), encoding="utf-8")
    photos = []
    for i, revision in enumerate(IDS):
        body = (f"fake-local-proxy-{i}").encode("utf-8")
        name = str(i) + ".jpg"
        (proxies / name).write_bytes(body)
        photos.append({
            "revisionId": revision, "relativePath": name,
            "sha256": hashlib.sha256(body).hexdigest(),
            "byteLength": len(body),
        })
    mapping = {
        "schemaVersion": 1, "protocol": batch.MAP_PROTOCOL,
        "scenarioFileSha256": batch.source_fingerprint(case_file),
        "proxyRoot": str(proxies),
        "proxyProfile": batch.PROFILE,
        "photos": photos,
        "catalogReadOnly": True,
        "noOriginalsHydrated": True,
        "inferencePerformed": False,
    }
    map_file.write_text(json.dumps(mapping), encoding="utf-8")
    score_file.write_text(json.dumps(original_scores()), encoding="utf-8")
    return (case_file, map_file, score_file), private / "batch-scores.json", proxies


class MetricResult:
    def __init__(self, value):
        self.value = value
    def item(self):
        return self.value


class SyntheticMetric:
    def __call__(self, _):
        return MetricResult(.9)


class BoundedResearchScoreTests(unittest.TestCase):
    def test_planning_uses_only_provenance_and_no_inference(self):
        with tempfile.TemporaryDirectory() as folder:
            paths, output, proxies = materialize(folder)
            inputs = batch.checked_inputs(*paths)
            result = batch.summary(inputs)
            self.assertEqual(result["scenarioCount"], 2)
            self.assertEqual(result["distinctScenarioCandidates"], 6)
            self.assertEqual(result["reusedKnownTOPIQScores"], 1)
            self.assertEqual(result["additionalCandidatePhotosNeedingScores"], 5)
            self.assertFalse(result["inferencePerformed"])
            self.assertTrue(result["plannedBatchFitsHardCap"])
            self.assertFalse(output.exists())

    def test_scoring_requires_explicit_permission_and_is_one_shot(self):
        with tempfile.TemporaryDirectory() as folder:
            paths, output, proxies = materialize(folder)
            inputs = batch.checked_inputs(*paths)
            with self.assertRaises(ValueError):
                batch.score(inputs, output, metric_factory=SyntheticMetric)
            self.assertFalse(output.exists())
            report = batch.score(
                inputs, output, accept_weights=True,
                metric_factory=SyntheticMetric)
            self.assertEqual(report["newlyInferred"], 5)
            self.assertEqual(report["reusedExistingScores"], 1)
            self.assertFalse(report["productionRankingApproved"])
            self.assertTrue(output.exists())
            recorded = json.loads(output.read_text())
            self.assertFalse(recorded["noHumanLabelsUsed"] is False)
            self.assertEqual(recorded["scores"][0]["rawScore"], .15)
            self.assertEqual(recorded["frozenRawScoreGap"], shadow.FROZEN_GAP)
            self.assertEqual(
                shadow.read_scores(recorded)[IDS[1]], .9)
            with self.assertRaises(ValueError):
                batch.score(inputs, output, accept_weights=True,
                            metric_factory=SyntheticMetric)

    def test_bad_mapping_binding_and_originals_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            paths, output, proxies = materialize(folder)
            mapping = json.loads(paths[1].read_text())
            for change in (
                lambda m: m.update({"scenarioFileSha256": "f"*64}),
                lambda m: m.update({"noOriginalsHydrated": False}),
                lambda m: m["photos"].pop(),
                lambda m: m["photos"][1].update(
                    {"relativePath": "../outside.jpg"}),
            ):
                bad = copy.deepcopy(mapping)
                change(bad)
                paths[1].write_text(json.dumps(bad), encoding="utf-8")
                if bad["photos"] and "../outside.jpg" in json.dumps(bad):
                    # Path traversal is rejected on attempted read.
                    inputs = batch.checked_inputs(*paths)
                    with self.assertRaises(ValueError):
                        batch.score(inputs, output, accept_weights=True,
                                    metric_factory=SyntheticMetric)
                else:
                    with self.assertRaises(ValueError):
                        batch.checked_inputs(*paths)
            self.assertFalse(output.exists())

    def test_immutable_proxy_tamper_rejected_before_metric_init(self):
        with tempfile.TemporaryDirectory() as folder:
            paths, output, proxies = materialize(folder)
            inputs = batch.checked_inputs(*paths)
            (proxies / "1.jpg").write_bytes(b"tampered")
            called = []
            with self.assertRaises(ValueError):
                batch.score(inputs, output, accept_weights=True,
                            metric_factory=lambda: called.append(True))
            self.assertFalse(called)
            self.assertFalse(output.exists())

    def test_respects_hard_batch_limit_without_inference(self):
        with tempfile.TemporaryDirectory() as folder:
            paths, output, proxies = materialize(folder)
            inputs = batch.checked_inputs(*paths)
            with patch.object(batch, "MAX_NEW", 1):
                self.assertFalse(batch.summary(inputs)["plannedBatchFitsHardCap"])
                with self.assertRaises(ValueError):
                    batch.score(inputs, output, accept_weights=True,
                                metric_factory=SyntheticMetric)
            self.assertFalse(output.exists())

    def test_raw_scores_do_not_import_human_labels(self):
        with tempfile.TemporaryDirectory() as folder:
            paths, output, proxies = materialize(folder)
            score = original_scores()
            score["qualityLevel"] = "very-low"
            paths[2].write_text(json.dumps(score), encoding="utf-8")
            with self.assertRaises(ValueError):
                batch.checked_inputs(*paths)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
