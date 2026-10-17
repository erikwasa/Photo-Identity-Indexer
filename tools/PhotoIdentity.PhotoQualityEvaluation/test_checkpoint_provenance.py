"""Offline tests for checkpoint file-hash provenance, without model imports."""
import hashlib
from pathlib import Path
import tempfile
import unittest

from checkpoint_provenance import inventory


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

    def test_empty_checkpoint_rejected(self):
        self.path.write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "size"):
            inventory(["topiq_nr=" + str(self.path)])


if __name__ == "__main__":
    unittest.main()
