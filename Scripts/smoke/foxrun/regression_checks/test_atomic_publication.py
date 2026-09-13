import tempfile
import unittest
from unittest import mock
from pathlib import Path

import Scripts.smoke.atomic_output as atomic_output
from Scripts.smoke.atomic_output import atomic_write_bytes, atomic_write_text


class AtomicPublicationTests(unittest.TestCase):
    def test_text_replaces_complete_payload(self):
        with tempfile.TemporaryDirectory() as td:
            target = Path(td) / "nested" / "evidence.json"
            atomic_write_text(target, "{\"status\":\"PASS\"}\n")
            self.assertEqual(target.read_text(encoding="utf-8"), "{\"status\":\"PASS\"}\n")
            self.assertEqual(list(target.parent.glob(".evidence.json.*.tmp")), [])

    def test_bytes_replaces_complete_payload(self):
        with tempfile.TemporaryDirectory() as td:
            target = Path(td) / "evidence.bin"
            atomic_write_bytes(target, b"complete")
            self.assertEqual(target.read_bytes(), b"complete")

    def test_failure_does_not_publish_partial_payload(self):
        with tempfile.TemporaryDirectory() as td:
            target = Path(td) / "evidence.json"
            target.write_text("old\n", encoding="utf-8")
            with self.assertRaises(RuntimeError):
                atomic_write_text(target, "new\n", inject_failure=True)
            self.assertEqual(target.read_text(encoding="utf-8"), "old\n")

    def test_replace_failure_preserves_previous_payload(self):
        with tempfile.TemporaryDirectory() as td:
            target = Path(td) / "evidence.json"
            target.write_text("old\n", encoding="utf-8")
            with mock.patch.object(atomic_output.os, "replace", side_effect=OSError("replace failed")) as replace:
                with self.assertRaises(OSError):
                    atomic_write_text(target, "new\n")
            replace.assert_called_once()
            self.assertEqual(target.read_text(encoding="utf-8"), "old\n")
            self.assertEqual(list(target.parent.glob(".evidence.json.*.tmp")), [])


if __name__ == "__main__":
    unittest.main()
