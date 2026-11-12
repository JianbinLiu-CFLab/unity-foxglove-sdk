"""Regression tests for Plan199 performance evidence auditing."""

from __future__ import annotations

import unittest
import tempfile
from pathlib import Path
from unittest.mock import patch

from Scripts.validation.plan_audit import performance_evidence


class PerformanceEvidenceTests(unittest.TestCase):
    """Verify the Plan199 audit rejects weakened evidence contracts."""

    def test_workflow_pin_rejects_removed_quick_mode(self) -> None:
        """Removing quick mode must fail the fixed-lane contract."""
        text = "workflow_dispatch:\npull_request:\nPackages/dev.unity2foxglove.sdk/Runtime/Transport/**\nPackages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/**\nPackages/dev.unity2foxglove.sdk/Runtime/Components/Manager/**\nPackages/dev.unity2foxglove.sdk/Runtime/Core/**\nPackages/dev.unity2foxglove.sdk/Runtime/Protocol/**\nPackages/dev.unity2foxglove.sdk/Runtime/Schemas/**\nPackages/dev.unity2foxglove.sdk/Runtime/IO/**\nPackages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/**\nPackages/dev.unity2foxglove.sdk/Editor/SourceGenerators/**\nPackages/dev.unity2foxglove.sdk/Tests/Performance/**\nScripts/performance/**\n.github/workflows/performance.yml\n--quick --thresholds performance-thresholds.ci.json\npermissions:\ncontents: read\npersist-credentials: false\n"
        self.assertTrue(performance_evidence._workflow_contract(text))
        self.assertFalse(performance_evidence._workflow_contract(text.replace("--quick ", "", 1)))

    def test_thresholds_require_finite_large_payload_ceiling(self) -> None:
        """The large-payload gate must keep finite allocation and GC ceilings."""
        data = performance_evidence._load_thresholds()
        defaults = data["defaults"]
        large = data["scenarios"]["LargePayloadFanout"]
        quick_large = data["modes"]["quick"]["scenarios"]["LargePayloadFanout"]
        self.assertLess(defaults["maxAllocatedBytesTotal"], 10_000_000_000)
        self.assertGreater(large["maxAllocatedBytesPerMessage"], 0)
        self.assertLessEqual(large["maxAllocatedBytesPerMessage"], 4 * 1024 * 1024)
        self.assertGreater(large["maxAllocatedBytesTotal"], 0)
        self.assertLessEqual(large["maxAllocatedBytesTotal"], 16 * 1024 * 1024 * 1024)
        self.assertGreaterEqual(large["maxGen2Collections"], 0)
        self.assertLessEqual(large["maxGen2Collections"], 2000)
        for key in ("maxAllocatedBytesPerMessage", "maxAllocatedBytesTotal", "maxGen1Collections", "maxGen2Collections"):
            self.assertLessEqual(quick_large[key], large[key], key)

    def test_threshold_check_rejects_non_object_scenario_records(self) -> None:
        """Return a failed audit when a required threshold record is null."""
        data = performance_evidence._load_thresholds()
        data["scenarios"]["FoxRunStreamIngest"] = None
        performance_evidence.audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=performance_evidence.audit_lib.BUILD_ROOT) as directory:
            with patch.object(performance_evidence, "OUTPUT", Path(directory)):
                with patch.object(performance_evidence, "_load_thresholds", return_value=data):
                    result = performance_evidence._threshold_check()
        self.assertEqual("FAIL", result.status)

    def test_threshold_check_rejects_boolean_numeric_values(self) -> None:
        """Reject booleans even though Python treats them as integers."""
        data = performance_evidence._load_thresholds()
        data["defaults"]["maxAllocatedBytesTotal"] = True
        data["scenarios"]["LargePayloadFanout"]["maxGen2Collections"] = True
        performance_evidence.audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=performance_evidence.audit_lib.BUILD_ROOT) as directory:
            with patch.object(performance_evidence, "OUTPUT", Path(directory)):
                with patch.object(performance_evidence, "_load_thresholds", return_value=data):
                    result = performance_evidence._threshold_check()
        self.assertEqual("FAIL", result.status)

    def test_threshold_check_rejects_malformed_default_threshold_values(self) -> None:
        """Reject malformed defaults fields used during threshold resolution."""
        data = performance_evidence._load_thresholds()
        data["defaults"]["maxElapsedMs"] = True
        performance_evidence.audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=performance_evidence.audit_lib.BUILD_ROOT) as directory:
            with patch.object(performance_evidence, "OUTPUT", Path(directory)):
                with patch.object(performance_evidence, "_load_thresholds", return_value=data):
                    result = performance_evidence._threshold_check()
        self.assertEqual("FAIL", result.status)

    def test_threshold_check_rejects_invalid_mode_defaults_and_full_large_payload(self) -> None:
        """Reject invalid mode defaults and an over-broad full large-payload ceiling."""
        for mutation in (
            lambda data: data["modes"]["full"]["defaults"].__setitem__("maxGen2Collections", True),
            lambda data: data["modes"]["full"]["scenarios"]["LargePayloadFanout"].__setitem__(
                "maxAllocatedBytesTotal", 16 * 1024 * 1024 * 1024 + 1
            ),
        ):
            with self.subTest(mutation=mutation):
                data = performance_evidence._load_thresholds()
                mutation(data)
                performance_evidence.audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
                with tempfile.TemporaryDirectory(dir=performance_evidence.audit_lib.BUILD_ROOT) as directory:
                    with patch.object(performance_evidence, "OUTPUT", Path(directory)):
                        with patch.object(performance_evidence, "_load_thresholds", return_value=data):
                            result = performance_evidence._threshold_check()
                self.assertEqual("FAIL", result.status)

    def test_threshold_check_rejects_oversized_integer_values(self) -> None:
        """Fail cleanly when a threshold integer cannot convert to a float."""
        data = performance_evidence._load_thresholds()
        data["defaults"]["maxAllocatedBytesTotal"] = 10**400
        performance_evidence.audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=performance_evidence.audit_lib.BUILD_ROOT) as directory:
            with patch.object(performance_evidence, "OUTPUT", Path(directory)):
                with patch.object(performance_evidence, "_load_thresholds", return_value=data):
                    result = performance_evidence._threshold_check()
        self.assertEqual("FAIL", result.status)

    def test_threshold_check_rejects_non_object_top_level_records(self) -> None:
        """Fail cleanly when a required top-level threshold record is null."""
        data = performance_evidence._load_thresholds()
        data["modes"]["quick"]["scenarios"]["LargePayloadFanout"] = None
        performance_evidence.audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=performance_evidence.audit_lib.BUILD_ROOT) as directory:
            with patch.object(performance_evidence, "OUTPUT", Path(directory)):
                with patch.object(performance_evidence, "_load_thresholds", return_value=data):
                    result = performance_evidence._threshold_check()
        self.assertEqual("FAIL", result.status)


if __name__ == "__main__":
    unittest.main()
