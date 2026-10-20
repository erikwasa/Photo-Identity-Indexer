"""WI-0195 research-only shadow simulator and local reviewer tests."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import collection_shadow_simulator as shadow
from collection_shadow_review import (
    load_session, public_state, read_proxy, save_review,
)


def scores():
    return {
        "schemaVersion": 1,
        "benchmarkId": "research-benchmark-fixture",
        "inputKind": "review-proxy",
        "candidate": {"candidateId": "pyiqa-topiq_nr-0.1.16"},
        "higherIsBetter": True,
        "scores": [
            {"revisionId": k, "rawScore": v,
             "inferenceMilliseconds": 1.0, "errorCode": None, "diagnostics": []}
            for k, v in (("a", .10), ("b", .50), ("c", .31), ("d", .99),
                         ("e", .35), ("f", .88))
        ],
    }


def scenarios():
    return {"schemaVersion": 1, "protocol": shadow.SCENARIO_PROTOCOL,
            "scenarios": [{
                "scenarioId": "synthetic-creative",
                "kind": "creative",
                "targetCount": 2,
                "candidates": [
                    {"revisionId": "a", "rank": 0,
                     "relevanceScore": 100, "kind": "direct-anchor"},
                    {"revisionId": "b", "rank": 1,
                     "relevanceScore": 100, "kind": "direct-anchor"},
                    {"revisionId": "c", "rank": 2,
                     "relevanceScore": 100, "kind": "direct-anchor"},
                    {"revisionId": "d", "rank": 3,
                     "relevanceScore": 99, "kind": "direct-anchor"},
                    {"revisionId": "e", "rank": 4,
                     "relevanceScore": 105, "kind": "contextual-addition"},
                    {"revisionId": "f", "rank": 5,
                     "relevanceScore": 105, "kind": "contextual-addition"},
                ],
            }]}


class ShadowSimulationTests(unittest.TestCase):
    def test_frozen_research_preference_never_crosses_relevance_or_kind(self):
        result = shadow.simulate(scenarios(), scores())
        self.assertEqual(len(result), 1)
        row = result[0]
        self.assertEqual(row["baseline"], ["a", "b"])
        self.assertEqual(row["qualityShadow"], ["b", "c"])
        self.assertEqual(row["changedSelectionCount"], 1)
        self.assertEqual(row["targetCount"], len(row["qualityShadow"]))
        self.assertNotIn("d", row["qualityShadow"])
        self.assertNotIn("e", row["qualityShadow"])
        self.assertGreater(row["qualityPromotionCount"], 0)

    def test_gap_abstention_and_missing_score_coverage(self):
        empty = scores()
        empty["scores"][1]["rawScore"] = None
        row = shadow.simulate(scenarios(), empty)[0]
        self.assertGreater(row["missingScoreComparisons"], 0)
        self.assertEqual(row["targetCount"], len(row["qualityShadow"]))
        candidate = scenarios()
        candidate["scenarios"][0]["candidates"] = [
            candidate["scenarios"][0]["candidates"][0],
            candidate["scenarios"][0]["candidates"][2]]
        candidate["scenarios"][0]["candidates"][1]["rank"] = 1
        candidate["scenarios"][0]["targetCount"] = 1
        sc = scores()
        sc["scores"][2]["rawScore"] = .25
        self.assertEqual(shadow.simulate(candidate, sc)[0]["baseline"],
                         shadow.simulate(candidate, sc)[0]["qualityShadow"])

    def test_private_blind_does_not_disclose_model_arm_or_scores(self):
        blind, audit = shadow.build_outputs(scenarios(), scores())
        self.assertEqual(audit["blindSha256"], shadow.sha(blind))
        self.assertEqual(audit["frozenRawScoreGap"], shadow.FROZEN_GAP)
        self.assertEqual(
            audit["arms"]["redundancy-suppression"],
            "disabled-WI0194-no-go")
        self.assertEqual(
            audit["arms"]["very-low-exclusion"],
            "disabled-no-validated-absolute-tier")
        shown = json.dumps(blind)
        for value in ("topiq", "relevanceScore", "selectionScore",
                      "quality-shadow", "baseline", "candidateBucket",
                      "sourceKind"):
            self.assertNotIn(value, shown)
        self.assertEqual(blind["comparisons"][0]["leftRevisions"] !=
                         blind["comparisons"][0]["rightRevisions"], True)

    def test_source_provenance_rejects_human_labels_and_mixed_score_models(self):
        original = scores()
        for change in (
            lambda data: data.update({"qualityLevel": "high"}),
            lambda data: data["candidate"].update({"candidateId": "musiq"}),
            lambda data: data["scores"][0].update({"review": "high"}),
            lambda data: data["scores"][0].update({"rawScore": float("nan")}),
        ):
            modified = copy.deepcopy(original)
            change(modified)
            with self.assertRaises(ValueError):
                shadow.build_outputs(scenarios(), modified)
        changed = scenarios()
        changed["scenarios"][0]["candidates"][0]["review"] = "valuable"
        with self.assertRaises(ValueError):
            shadow.build_outputs(changed, original)

    def test_preview_import_actual_creative_kinds_and_source_shape(self):
        preview = {
            "collectionId": "collection-123",
            "selectedCandidates": [
                {"revisionId": "a", "kind": "direct-anchor", "selectionScore": 42},
                {"revisionId": "b", "kind": "direct-anchor", "selectionScore": 42},
                {"revisionId": "c", "kind": "contextual-addition", "selectionScore": 90},
            ],
        }
        case = shadow.scenarios_from_creative_preview(preview, target=2)
        manifest = {"schemaVersion": 1,
                    "protocol": shadow.SCENARIO_PROTOCOL, "scenarios": [case]}
        self.assertEqual(shadow.validate_scenarios(manifest)[0][2], 2)
        self.assertEqual(case["candidates"][2]["kind"], "contextual-addition")
        preview["selectedCandidates"][1]["kind"] = "unrecognized"
        with self.assertRaises(ValueError):
            shadow.scenarios_from_creative_preview(preview, target=2)

    def test_exclusive_private_outputs_and_aggregate_no_ids(self):
        blind, audit = shadow.build_outputs(scenarios(), scores())
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            a = root / "blind.json"
            b = root / "audit.json"
            shadow.save_new_only(a, b, blind, audit)
            with self.assertRaises(ValueError):
                shadow.save_new_only(a, b, blind, audit)
            self.assertEqual(shadow.load(a), blind)
            self.assertNotIn("synthetic-creative", json.dumps(shadow.summarize(audit)))
            self.assertFalse(shadow.summarize(audit)["automaticSuppressionAuthorized"])

    def test_blind_review_decodes_only_on_private_audit_side(self):
        blind, audit = shadow.build_outputs(scenarios(), scores())
        cid = blind["comparisons"][0]["comparisonId"]
        vote = {"schemaVersion": 1, "protocol": shadow.LABEL_PROTOCOL,
                "blindSha256": shadow.sha(blind),
                "reviews": [{"comparisonId": cid, "choice": "left"}]}
        report = shadow.review_summary(blind, audit, vote)
        self.assertEqual(report["reviewed"], 1)
        self.assertEqual(report["remaining"], 0)
        self.assertIn(audit["comparisons"][0]["blindLeftArm"],
                      report["preferences"])
        vote["blindSha256"] = "0"*64
        with self.assertRaises(ValueError):
            shadow.review_summary(blind, audit, vote)

    def test_proxy_validation_resume_and_verification(self):
        blind, audit = shadow.build_outputs(scenarios(), scores())
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            private, proxyroot = root / "private", root / "proxy"
            private.mkdir()
            proxyroot.mkdir()
            blind_path, audit_path, map_path, vote_path = (
                private / "blind.json", private / "audit.json",
                private / "proxy.json", private / "votes.json")
            blind_path.write_text(json.dumps(blind), encoding="utf-8")
            audit_path.write_text(json.dumps(audit), encoding="utf-8")
            unique = {rev for c in blind["comparisons"]
                      for side in ("leftRevisions", "rightRevisions")
                      for rev in c[side]}
            mapped = []
            for i, rev in enumerate(sorted(unique)):
                content = ("jpeg-test-" + rev).encode()
                name = str(i) + ".jpg"
                (proxyroot / name).write_bytes(content)
                mapped.append({"revisionId": rev, "relativePath": name,
                               "sha256": hashlib.sha256(content).hexdigest(),
                               "byteLength": len(content)})
            proxy_map = {
                "schemaVersion": 1, "protocol": "wi0195-private-shadow-proxies-v1",
                "blindFileSha256": hashlib.sha256(blind_path.read_bytes()).hexdigest(),
                "proxyRoot": str(proxyroot), "proxyProfile": "test",
                "photos": mapped, "catalogReadOnly": True,
                "noOriginalsHydrated": True}
            map_path.write_text(json.dumps(proxy_map), encoding="utf-8")
            state = load_session(blind_path, audit_path, map_path, vote_path)
            first = public_state(state, 0)
            photo = first["leftRevisions"][0]
            self.assertTrue(read_proxy(state, photo).startswith(b"jpeg-test-"))
            save_review(state, first["comparisonId"], "right")
            loaded = load_session(blind_path, audit_path, map_path, vote_path)
            self.assertEqual(public_state(loaded, 0)["reviewed"], 1)
            record = next(x for x in mapped if x["revisionId"] == photo)
            (proxyroot / record["relativePath"]).write_bytes(b"tampered")
            with self.assertRaises(ValueError):
                read_proxy(loaded, photo)


if __name__ == "__main__":
    unittest.main()
