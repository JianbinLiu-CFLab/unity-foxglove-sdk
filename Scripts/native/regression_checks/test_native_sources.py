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
        declared_rows = [
            line for line in text.splitlines()
            if line.lstrip().startswith("| `include/wels/")
        ]

        self.assertGreater(len(entries), 0, "HEADER_PROVENANCE.md should list header hashes")
        self.assertEqual(
            len(declared_rows),
            len(entries),
            "every OpenH264 provenance row must parse as path plus SHA-256",
        )
        for relative, expected in entries:
            header = provenance.parent / relative
            actual = hashlib.sha256(header.read_bytes()).hexdigest()
            self.assertEqual(expected, actual, relative)

    def test_openh264_probe_bounds_frames_and_converts_broken_pipes_to_exit_codes(self) -> None:
        """Malformed geometry and a closed stdout consumer must remain defined failures."""
        script = ROOT / "Scripts/native/openh264_probe/openh264_probe_encoder.cpp"
        package = ROOT / "Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/openh264_probe_encoder.cpp"
        source = script.read_text(encoding="utf-8")

        self.assertEqual(script.read_bytes(), package.read_bytes())
        self.assertIn("constexpr int MaxDimension = 8192;", source)
        self.assertIn("options.width <= MaxDimension", source)
        self.assertIn("options.height <= MaxDimension", source)
        self.assertIn("OpenH264 returned an invalid frame.", source)
        self.assertIn("std::signal(SIGPIPE, SIG_IGN)", source)
        self.assertIn("OpenH264 stdout write failed.", source)

    def test_draco_probe_zero_point_success_is_not_reported_as_a_warning(self) -> None:
        """The documented empty response is successful and should leave stderr clean."""
        source = (ROOT / "Scripts/native/draco_probe/draco_probe_encoder.cpp").read_text(encoding="utf-8")

        self.assertNotIn("warning: zero-point frame", source)


if __name__ == "__main__":
    unittest.main()
