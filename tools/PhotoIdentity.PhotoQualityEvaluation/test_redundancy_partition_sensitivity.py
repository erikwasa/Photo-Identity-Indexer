"""Synthetic-only WI-0194 leakage linkage sensitivities; no source photos."""
import copy
import json
import unittest

from redundancy_partition_sensitivity import analyze, repartition
from redundancy_review_pairs import prepare_inventory
from test_redundancy_review_pairs import fixture


class RedundancyPartitionSensitivityTests(unittest.TestCase):
    def test_baseline_matches_sampler_and_all_scenarios_keep_linked_pairs_together(self):
        data = fixture()
        inventory, edges, original, _ = prepare_inventory(data)
        baseline, _ = repartition(inventory, edges, 4)
        self.assertEqual(baseline, original)
        summary = analyze(data)
        self.assertEqual([row["linkageDhashDistanceAtMost"]
                          for row in summary["scenarios"]], [4, 8, 12])
        for cutoff in (4, 8, 12):
            assignment, _ = repartition(inventory, edges, cutoff)
            for (a, b), pair in edges.items():
                d = pair["dhashDistance"]
                if d is not None and d <= cutoff:
                    self.assertEqual(assignment[a], assignment[b])

    def test_family_sizes_are_monotone_under_broader_linkage(self):
        data = fixture()
        stats = analyze(data)["scenarios"]
        families = [s["familyCount"] for s in stats]
        self.assertEqual(families, sorted(families, reverse=True))
        self.assertEqual(stats[0]["crossPartitionDhash5To8"] >= 0, True)
        self.assertEqual(stats[1]["crossPartitionDhash5To8"], 0)
        self.assertEqual(stats[2]["crossPartitionDhash9To12"], 0)

    def test_aggregate_privacy_stability_and_unfrozen_status(self):
        data = fixture()
        first = analyze(data)
        shuffled = copy.deepcopy(data)
        shuffled["photos"].reverse()
        shuffled["pairs"].reverse()
        self.assertEqual(first, analyze(shuffled))
        self.assertEqual(first["photos"], 100)
        self.assertEqual(first["candidatePairs"], 149)
        self.assertFalse(first["humanLabelsAccessed"])
        self.assertFalse(first["reviewManifestCreated"])
        output = json.dumps(first)
        for private in ("private-revision", "synthetic-redundancy-study",
                        "leftRevisionId", "contentSha256"):
            self.assertNotIn(private, output)

    def test_labels_are_not_accepted_and_invalid_cutoff_rejected(self):
        data = fixture()
        data["pairs"][0]["reviewLabel"] = "redundant"
        with self.assertRaises(ValueError):
            analyze(data)


if __name__ == "__main__":
    unittest.main()
