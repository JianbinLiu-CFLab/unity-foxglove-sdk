"""Regression coverage for the decomposed U2R2 protocol limits source."""

from __future__ import annotations

import unittest
from pathlib import Path


class U2R2ProtocolLimitsSplitTests(unittest.TestCase):
    """Verify the U2R2 protocol limits source split contract."""

    def test_sections_stay_below_source_split_threshold(self) -> None:
        """The facade and each decomposed section remain below 500 lines."""
        root = Path(__file__).resolve().parents[3]
        facade = root / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/U2R2ProtocolLimits.cs"
        sections = sorted((facade.parent / "Decomposed" / facade.stem).glob("*.cs"))
        self.assertLess(len(facade.read_text(encoding="utf-8").splitlines()), 500)
        self.assertTrue(sections)
        self.assertTrue(all(len(path.read_text(encoding="utf-8").splitlines()) < 500 for path in sections))
