"""Offline tests for checkpoint file-hash provenance, without model imports."""
import hashlib
from pathlib import Path
import tempfile
import unittest

from checkpoint_provenance import discover, inventory, candidate_hint


class CheckpointProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "weights.safetensors"
        self.path.write_bytes(b"synthetic weights")

    def test_reports_hash_and_byte_size_without_private_root(self):
        data = inventory(["musiq=" + str(self.path)])
        row = data["checkpoints"]["musiq"]
        self.assertEqual(row["sha256"], hashlib.sha256(b"synthetic weights").hexdigest())
        self.assertEqual(row["sizeBytes"], len(b"synthetic weights"))
        self.assertEqual(row["licenseStatus"], "not-verified")
        self.assertNotIn(str(self.path.parent), str(data))

    def test_rejects_duplicate_candidate(self):
        with self.assertRaises(ValueError):
            inventory(["arniqa=" + str(self.path), "arniqa=" + str(self.path)])

    def test_rejects_unknown_or_missing_files(self):
        with self.assertRaises(ValueError):
            inventory(["not-a-model=" + str(self.path)])
        with self.assertRaises(ValueError):
            inventory(["topiq_nr=" + str(self.path.with_name("absent.pth"))])

    def test_cache_scan_returns_hashes_without_paths_or_false_licensing(self):
        for name in (
            "cfanet_nr_koniq_res50-9a73138b.pth",
            "musiq_koniq_ckpt-e95806b9.pth",
            "arniqa_candidate.safetensors"):
            (self.path.parent / name).write_bytes(name.encode("ascii"))
        result = discover(self.path.parent)
        self.assertEqual(result["mode"], "cache-discovery-unverified")
        self.assertEqual(result["artifactCount"], 4)
        by_name = {row["artifactName"]: row for row in result["artifacts"]}
        self.assertEqual(
            by_name["cfanet_nr_koniq_res50-9a73138b.pth"]["candidateHints"],
            ["topiq_nr"])
        self.assertEqual(
            by_name["musiq_koniq_ckpt-e95806b9.pth"]["candidateHints"],
            ["musiq"])
        for row in result["artifacts"]:
            self.assertEqual(row["licenseStatus"], "not-verified")
            self.assertFalse(row["mappingVerified"])
            self.assertEqual(len(row["sha256"]), 64)
        self.assertNotIn(str(self.path.parent), str(result))
        self.assertIn("packageVersions", result)

    def test_cache_scan_requires_checkpoint_artifacts(self):
        empty = self.path.parent / "empty"
        empty.mkdir()
        with self.assertRaisesRegex(ValueError, "No checkpoint artifacts"):
            discover(empty)

    def test_unknown_filename_is_not_misidentified_as_candidate(self):
        self.assertEqual(candidate_hint("some_generic_model.pth"), [])

    def test_symlinks_not_included_if_os_supports_them(self):
        link = self.path.parent / "linked.pth"
        try:
            link.symlink_to(self.path)
        except (OSError, NotImplementedError):
            self.skipTest("Symlinks unavailable for this test environment")
        result = discover(self.path.parent)
        self.assertNotIn("linked.pth",
                         [row["artifactName"] for row in result["artifacts"]])

    def test_extra_cache_root_discovers_arniqa_encoder_without_loading_model(self):
        extra = self.path.parent / "checkpoints"
        extra.mkdir()
        (extra / "ARNIQA.pth").write_bytes(b"mock pretrained encoder")
        (extra / "resnet50-0676ba61.pth").write_bytes(b"mock resnet backbone")
        (self.path.parent / "regressor_koniq10k.pth").write_bytes(b"mock regressor")
        result = discover(self.path.parent, [extra])
        self.assertEqual(result["cacheRootCount"], 2)
        self.assertEqual(result["artifactCount"], 4)
        rows = {a["artifactName"]: a for a in result["artifacts"]}
        self.assertEqual(rows["regressor_koniq10k.pth"]["candidateHints"],
                         ["arniqa"])
        self.assertEqual(rows["regressor_koniq10k.pth"]["roleHint"],
                         "possible-arniqa-koniq-regressor")
        self.assertEqual(rows["ARNIQA.pth"]["roleHint"],
                         "possible-arniqa-encoder")
        self.assertEqual(rows["resnet50-0676ba61.pth"]["roleHint"],
                         "possible-torchvision-backbone-not-arniqa-proof")
        self.assertEqual(rows["ARNIQA.pth"]["sourceCacheIndex"], 2)
        self.assertFalse(rows["ARNIQA.pth"]["mappingVerified"])
        self.assertNotIn(str(extra), str(result))

    def test_nested_cache_root_deduplicates_same_file(self):
        nested = self.path.parent / "nested"
        nested.mkdir()
        (nested / "ARNIQA.pth").write_bytes(b"encoder")
        data = discover(self.path.parent, [nested])
        self.assertEqual(data["artifactCount"], 2)

    def test_duplicate_cache_roots_rejected(self):
        with self.assertRaisesRegex(ValueError, "Duplicate cache"):
            discover(self.path.parent, [self.path.parent])

    def test_empty_checkpoint_rejected(self):
        self.path.write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "size"):
            inventory(["topiq_nr=" + str(self.path)])


if __name__ == "__main__":
    unittest.main()
