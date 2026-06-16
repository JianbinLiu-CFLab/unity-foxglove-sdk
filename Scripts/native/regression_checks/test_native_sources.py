#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for native source documentation contracts.

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


class NativeSourceTests(unittest.TestCase):
    """Regression coverage for native source contracts."""

    def test_draco_native_documents_inverse_compression_speed_mapping(self) -> None:
        """Native Draco speed constants should document their inverse CLI mapping."""
        source = (ROOT / "Scripts/native/draco_native/Unity2FoxgloveDracoNative.cpp").read_text(encoding="utf-8")

        self.assertIn("Draco speed option 3 corresponds to CLI compression level 7", source)


if __name__ == "__main__":
    unittest.main()
