#!/usr/bin/env python3
'Executable entrypoint for decomposed Lyrical runtime-package tests.'

from __future__ import annotations

import sys
import unittest

from . import _PHASE192_SECTION_MODULES


if __name__ == "__main__":
    sys.argv[0] = "test_build_r2fu_runtime_package.py"
    suite = unittest.TestSuite(
        unittest.defaultTestLoader.loadTestsFromModule(section)
        for section in _PHASE192_SECTION_MODULES
    )
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(0 if result.wasSuccessful() else 1)
