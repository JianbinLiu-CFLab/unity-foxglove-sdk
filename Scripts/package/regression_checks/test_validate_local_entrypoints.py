#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for local-entrypoint validation diagnostics.

from __future__ import annotations

import importlib.util
import re
import sys
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
VALIDATOR_PATH = ROOT / "Scripts/package/validate_local_entrypoints.py"


def load_module(name: str, path: Path):
    """Load one validation script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class LocalEntrypointValidationTests(unittest.TestCase):
    """Lock deterministic failures for missing validation dependencies."""

    def test_missing_git_reports_an_actionable_error(self) -> None:
        """A missing git executable should not escape as a raw traceback."""
        validator = load_module(
            "validate_local_entrypoints_under_test",
            VALIDATOR_PATH,
        )
        with mock.patch.object(
            validator.subprocess,
            "Popen",
            side_effect=FileNotFoundError("git"),
        ):
            with self.assertRaisesRegex(RuntimeError, "git executable"):
                validator.git_grep_failures("label", re.compile("pattern"))

    def test_pipe_retention_is_bounded_and_owned_tree_is_terminated(self) -> None:
        """A descendant-held pipe must become a bounded validation failure."""
        validator = load_module(
            "validate_local_entrypoints_timeout_under_test",
            VALIDATOR_PATH,
        )

        class HangingProcess:
            """Model a child whose descendant retains the redirected pipes."""

            pid = 4242
            returncode = -9

            def __init__(self) -> None:
                self.communicate_calls: list[object] = []

            def communicate(self, input=None, timeout=None):
                self.communicate_calls.append(timeout)
                if len(self.communicate_calls) == 1:
                    raise validator.subprocess.TimeoutExpired(
                        ["git", "grep"], timeout, output="partial", stderr="held"
                    )
                return "", ""

            def wait(self, timeout=None):
                return self.returncode

            def poll(self):
                return self.returncode

            def kill(self):
                self.returncode = -9

            def __enter__(self):
                return self

            def __exit__(self, _type, _value, _traceback):
                return False

        process = HangingProcess()
        with mock.patch.object(
            validator.subprocess, "Popen", return_value=process
        ), mock.patch.object(
            validator, "_terminate_owned_process", create=True
        ) as terminate:
            with self.assertRaisesRegex(RuntimeError, "timed out"):
                validator.git_grep_failures(
                    "label", re.compile("__ROOT_I03_009_NEVER_MATCH__")
                )

        self.assertEqual(
            process.communicate_calls[0],
            getattr(validator, "GIT_GREP_TIMEOUT_SECONDS", 900),
        )
        terminate.assert_called_once_with(process)


if __name__ == "__main__":
    unittest.main()
