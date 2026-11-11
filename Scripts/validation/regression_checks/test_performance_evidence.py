"""Regression tests for Plan199 performance evidence auditing."""

from __future__ import annotations

import unittest

from Scripts.validation.plan_audit import performance_evidence


class PerformanceEvidenceTests(unittest.TestCase):
    """Verify the Plan199 audit rejects weakened evidence contracts."""

    def test_workflow_pin_rejects_removed_quick_mode(self) -> None:
        """Removing quick mode must fail the fixed-lane contract."""
        text = "workflow_dispatch:\npull_request:\nPackages/dev.unity2foxglove.sdk/Runtime/Components/Manager/**\nPackages/dev.unity2foxglove.sdk/Runtime/Core/**\nPackages/dev.unity2foxglove.sdk/Tests/Performance/**\nScripts/performance/**\n--quick --thresholds performance-thresholds.ci.json\npermissions:\ncontents: read\npersist-credentials: false\n"
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


if __name__ == "__main__":
    unittest.main()
