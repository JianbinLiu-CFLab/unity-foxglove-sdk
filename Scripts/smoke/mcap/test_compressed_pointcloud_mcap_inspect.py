"""Regression tests for strict MCAP record-boundary handling."""

import struct
import unittest

from Scripts.smoke.mcap import compressed_pointcloud_mcap_inspect as module


class CompressedPointCloudMcapInspectTests(unittest.TestCase):
    """Ensure truncated MCAP records fail closed instead of being ignored."""

    def test_truncated_record_payload_is_rejected(self) -> None:
        """Reject a record whose declared payload exceeds available bytes."""
        data = bytes([module.OP_SCHEMA]) + struct.pack("<Q", 5) + b"ab"
        with self.assertRaisesRegex(ValueError, "truncated MCAP record payload"):
            module.read_records(data)

    def test_trailing_partial_header_is_rejected(self) -> None:
        """Reject trailing bytes that cannot form a complete record header."""
        with self.assertRaisesRegex(ValueError, "truncated MCAP record header"):
            module.read_records(b"\x01\x02")



    def test_oversized_input_is_rejected_before_read(self) -> None:
        """Reject files above the bounded evidence input budget before read_bytes."""
        import tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "oversized.mcap"
            with path.open("wb") as handle:
                handle.truncate(module.MAX_INPUT_BYTES + 1)
            with self.assertRaisesRegex(ValueError, "MCAP input exceeds maximum size"):
                module.inspect_input_file(path)

if __name__ == "__main__":
    unittest.main()

    def test_oversized_input_is_rejected_before_read(self) -> None:
        """Reject files above the bounded evidence input budget before read_bytes."""
        import tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "oversized.mcap"
            with path.open("wb") as handle:
                handle.truncate(module.MAX_INPUT_BYTES + 1)
            with self.assertRaisesRegex(ValueError, "MCAP input exceeds maximum size"):
                module.inspect_input_file(path)