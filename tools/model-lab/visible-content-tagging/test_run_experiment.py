from __future__ import annotations

import unittest

import run_experiment


class SafeExperimentTests(unittest.TestCase):
    def test_tag_normalization_matches_production_shape(self):
        self.assertEqual(
            "watching television",
            run_experiment.normalize_tag("  Ｗatching\t  Television  "),
        )
        with self.assertRaises(ValueError):
            run_experiment.normalize_tag("tag" + chr(1) + "name")

    def test_recall_risk_flags_are_rejected(self):
        self.assertTrue(
            run_experiment.has_recall_risk(run_experiment.FILE_ATTRIBUTE_OFFLINE)
        )
        self.assertTrue(
            run_experiment.has_recall_risk(
                run_experiment.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
            )
        )
        self.assertFalse(run_experiment.has_recall_risk(0))


if __name__ == "__main__":
    unittest.main()
