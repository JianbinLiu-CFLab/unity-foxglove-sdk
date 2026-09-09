import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[4]
SLOT = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2OwnedLatestSlot.cs"

class H03StopAdmissionContractTests(unittest.TestCase):
    def test_stop_does_not_spin_behind_admitted_operations(self):
        text = SLOT.read_text(encoding="utf-8")
        self.assertIn("if (Volatile.Read(ref _activePublishers) != 0", text)
        self.assertIn("|| Volatile.Read(ref _activeAppliers) != 0)", text)
        self.assertIn("Leave the stop request pending", text)

    def test_publisher_finally_completes_deferred_stop(self):
        text = SLOT.read_text(encoding="utf-8")
        marker = "Interlocked.Decrement(ref _activePublishers);"
        self.assertIn(marker, text)
        self.assertIn("TryCompleteDeferredStop();", text)

if __name__ == "__main__":
    unittest.main()
