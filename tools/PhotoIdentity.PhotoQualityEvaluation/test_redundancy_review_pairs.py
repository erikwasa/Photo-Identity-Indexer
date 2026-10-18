"""Synthetic-only WI-0194 review-pair foundation tests; no photos/labels/models."""
import copy
import io
import json
from pathlib import Path
import tempfile
import unittest
from contextlib import redirect_stdout
from unittest.mock import patch

from redundancy_review_pairs import (
    BUCKETS, INPUT_PROTOCOL, QUOTAS, _stratum, make_pair_sets,
    prepare_inventory, main,
)


def fixture():
    photos = [
        {
            "revisionId": f"private-revision-{i:03}",
            "contentSha256": f"{i + 1:064x}",
        }
        for i in range(100)
    ]
    # The final photo is an exact-content copy of photo 0.
    photos[99]["contentSha256"] = photos[0]["contentSha256"]
    photos[2]["leakageGroup"] = "group-of-near-photos"
    photos[3]["leakageGroup"] = "group-of-near-photos"
    pairs = []
    for i in range(98):
        pairs.append({
            "leftRevisionId": photos[i]["revisionId"],
            "rightRevisionId": photos[i + 1]["revisionId"],
            "sourceKind": "retrieved",
            "dhashDistance": 4 if i == 5 else 5 + (i * 5) % 59,
            "clipCosine": round(0.42 + (i % 12) * 0.047, 5),
        })
    # Separate exact content control, not treated as a human redundancy truth.
    pairs.append({
        "leftRevisionId": photos[0]["revisionId"],
        "rightRevisionId": photos[99]["revisionId"],
        "sourceKind": "retrieved",
        "dhashDistance": 0,
        "clipCosine": .99,
    })
    for i in range(50):
        pairs.append({
            "leftRevisionId": photos[i]["revisionId"],
            "rightRevisionId": photos[i + 50]["revisionId"],
            "sourceKind": "random-negative",
        })
    return {
        "schemaVersion": 1,
        "protocol": INPUT_PROTOCOL,
        "sampleId": "synthetic-redundancy-study",
        "photos": photos,
        "pairs": pairs,
    }


class RedundancyPairSamplerTests(unittest.TestCase):
    def setUp(self):
        self.data = fixture()

    def test_partition_is_photo_disjoint_and_content_family_safe(self):
        inventory, pairs, membership, groups = prepare_inventory(self.data)
        self.assertEqual(len(inventory), 100)
        self.assertEqual(len(pairs), 149)
        self.assertGreater(groups, 2)
        self.assertEqual(membership["private-revision-000"],
                         membership["private-revision-099"])
        self.assertEqual(membership["private-revision-002"],
                         membership["private-revision-003"])
        # dHash <=4 link is co-partitioned before any sampling.
        self.assertEqual(membership["private-revision-005"],
                         membership["private-revision-006"])
        review, audit = make_pair_sets(self.data)
        selected = {row["pairId"]: row for row in audit["selectedPairs"]}
        self.assertEqual(len(selected), len(review["pairs"]))
        for row in review["pairs"]:
            a, b = row["leftRevisionId"].casefold(), row["rightRevisionId"].casefold()
            self.assertEqual(membership[a], membership[b])
            self.assertEqual(selected[row["pairId"]]["partition"], membership[a])

    def test_order_invariance_and_blinded_output(self):
        left_review, left_audit = make_pair_sets(self.data)
        changed = copy.deepcopy(self.data)
        changed["photos"].reverse()
        changed["pairs"].reverse()
        right_review, right_audit = make_pair_sets(changed)
        self.assertEqual(left_review, right_review)
        # Audit fingerprints the supplied manifest, including row ordering;
        # the sampled pairs and opaque identifiers are still identical.
        self.assertEqual(left_audit["selectedPairs"], right_audit["selectedPairs"])
        blinded = json.dumps(left_review)
        for secret in (
            "dhashDistance", "clipCosine", "sourceKind", "candidateBucket",
            "partition", "calibration", "held-out", "leakageGroup",
            "contentSha256", "model", "\"qualityLevel\":",
        ):
            self.assertNotIn(secret, blinded)
        self.assertEqual(
            left_review["labels"],
            ["redundant", "similar-keep-both", "not-similar", "unsure"],
        )
        self.assertFalse(left_audit["heldOutReviewsAccessed"])
        self.assertFalse(left_audit["productionSuppressionApproved"])
        for partition in ("calibration", "held-out"):
            for bucket in BUCKETS:
                self.assertLessEqual(
                    left_audit["selectedBucketCounts"][partition][bucket],
                    QUOTAS[bucket],
                )

    def test_sampling_strata_do_not_claim_redundancy_truth(self):
        row = lambda source="retrieved", distance=None, clip=None: {
            "sourceKind": source,
            "dhashDistance": distance,
            "clipCosine": clip,
        }
        self.assertEqual(_stratum(row(distance=0, clip=.99), True),
                         "exact-copy-control")
        self.assertEqual(_stratum(row("random-negative"), False),
                         "random-negative")
        self.assertEqual(_stratum(row(distance=4, clip=.91), False), "near-copy")
        self.assertEqual(_stratum(row(distance=5, clip=.95), False), "high")
        self.assertEqual(_stratum(row(distance=18, clip=.7), False), "medium")
        self.assertEqual(_stratum(row(distance=32, clip=.69), False), "borderline")
        self.assertEqual(_stratum(row(distance=3, clip=.2), False), "disagreement")
        self.assertEqual(_stratum(row(distance=27, clip=.95), False), "disagreement")

    def test_no_labels_or_paths_allowed_in_input(self):
        for row, field, value in [
            (self.data["photos"][0], "review", {"qualityLevel": "high"}),
            (self.data["photos"][0], "sourcePath", "private/source"),
            (self.data["pairs"][0], "humanLabel", "redundant"),
        ]:
            with self.subTest(field=field):
                modified = copy.deepcopy(self.data)
                collection = ("photos" if row is self.data["photos"][0] else "pairs")
                modified[collection][0][field] = value
                with self.assertRaisesRegex(ValueError, "no human labels or paths"):
                    make_pair_sets(modified)

    def test_paths_cannot_masquerade_as_revision_ids(self):
        modified = copy.deepcopy(self.data)
        modified["photos"][0]["revisionId"] = "private/source/path.jpg"
        with self.assertRaisesRegex(ValueError, "Invalid revisionId"):
            prepare_inventory(modified)

    def test_malformed_or_duplicate_pairs_are_rejected(self):
        modifications = [
            {"dhashDistance": -1},
            {"dhashDistance": True},
            {"dhashDistance": 65},
            {"clipCosine": float("nan")},
            {"clipCosine": 1.0001},
            {"sourceKind": "human-rated"},
            {"leftRevisionId": "missing-photo"},
        ]
        for delta in modifications:
            with self.subTest(delta=delta):
                modified = copy.deepcopy(self.data)
                modified["pairs"][0].update(delta)
                with self.assertRaises(ValueError):
                    prepare_inventory(modified)
        modified = copy.deepcopy(self.data)
        duplicate = dict(modified["pairs"][0])
        duplicate["leftRevisionId"], duplicate["rightRevisionId"] = (
            duplicate["rightRevisionId"], duplicate["leftRevisionId"])
        modified["pairs"].append(duplicate)
        with self.assertRaisesRegex(ValueError, "Duplicate/reversed"):
            prepare_inventory(modified)

    def test_private_outputs_are_exclusive_and_console_aggregate_only(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            manifest = root / "candidate.json"
            blind = root / "blind.json"
            audit = root / "audit.json"
            manifest.write_text(json.dumps(self.data), encoding="utf-8")
            args = [
                "redundancy_review_pairs.py",
                "--input", str(manifest),
                "--blind-output", str(blind),
                "--audit-output", str(audit),
            ]
            out = io.StringIO()
            with patch("sys.argv", args), redirect_stdout(out):
                main()
            self.assertTrue(blind.is_file())
            self.assertTrue(audit.is_file())
            summary = json.loads(out.getvalue())
            self.assertFalse(summary["heldOutReviewsAccessed"])
            self.assertFalse(summary["productionSuppressionApproved"])
            self.assertNotIn("private-revision-", out.getvalue())
            self.assertNotIn("synthetic-redundancy-study", out.getvalue())
            with patch("sys.argv", args), self.assertRaisesRegex(ValueError, "Refusing"):
                main()


if __name__ == "__main__":
    unittest.main()
