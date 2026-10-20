#!/usr/bin/env python3
"""Executable entrypoint for decomposed regression tests."""

from __future__ import annotations

import unittest


def load_tests(loader, tests, pattern):
    """Discover split sections for direct and module execution."""
    return loader.discover(
        __file__.rsplit("\\", 1)[0] if "\\" in __file__ else __file__.rsplit("/", 1)[0],
        pattern="*_tests.py",
        top_level_dir=__file__.rsplit("\\", 5)[0] if "\\" in __file__ else __file__.rsplit("/", 5)[0],
    )

if __name__ == "__main__":
    raise SystemExit(unittest.main(module="__main__"))
