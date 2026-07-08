#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for native source documentation contracts.

from __future__ import annotations

import hashlib
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


class NativeSourceTests(unittest.TestCase):
    """Regression coverage for native source contracts."""

    def test_draco_native_documents_inverse_compression_speed_mapping(self) -> None:
        """Native Draco speed constants should document their inverse CLI mapping."""
        source = (ROOT / "Scripts/native/draco_native/Unity2FoxgloveDracoNative.cpp").read_text(encoding="utf-8")

        self.assertIn("Draco speed option 3 corresponds to CLI compression level 7", source)

    def test_openh264_header_provenance_hashes_match_committed_headers(self) -> None:
        """Committed OpenH264 headers should match the recorded provenance hashes."""
        provenance = ROOT / "Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/v2.6.0/HEADER_PROVENANCE.md"
        text = provenance.read_text(encoding="utf-8")
        entries = re.findall(r"\| `(include/wels/[^`]+)` \| `([0-9a-f]{64})` \|", text)

        self.assertGreater(len(entries), 0, "HEADER_PROVENANCE.md should list header hashes")
        for relative, expected in entries:
            header = provenance.parent / relative
            actual = hashlib.sha256(header.read_bytes()).hexdigest()
            self.assertEqual(expected, actual, relative)


if __name__ == "__main__":
    unittest.main()
