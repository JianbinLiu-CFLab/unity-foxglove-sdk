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


if __name__ == "__main__":
    unittest.main()
