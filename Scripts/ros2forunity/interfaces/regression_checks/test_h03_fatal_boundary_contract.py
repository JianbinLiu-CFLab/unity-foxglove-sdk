import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[4]
SLOT = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2OwnedLatestSlot.cs"

class H03FatalBoundaryContractTests(unittest.TestCase):
    def test_cleanup_helpers_rethrow_fatal_runtime_failures(self):
        text = SLOT.read_text(encoding="utf-8")
        self.assertIn("if (!FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))", text)
        self.assertGreaterEqual(text.count("ExceptionDispatchInfo.Capture(exception).Throw();"), 2)

    def test_policy_keeps_recoverable_cleanup_best_effort(self):
        text = SLOT.read_text(encoding="utf-8")
        self.assertIn("only recoverable cleanup faults stay best-effort", text)
        self.assertIn("by a best-effort ownership clear", text)

if __name__ == "__main__":
    unittest.main()
