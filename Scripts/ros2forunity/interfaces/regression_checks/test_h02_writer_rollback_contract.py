from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[4]
WRITER = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2InterfacePackageWriter.cs"


class H02WriterRollbackContractTests(unittest.TestCase):
    """R4.1 deterministic source-level contract for rollback exception authority."""

    def test_restore_failure_is_aggregated_without_replacing_primary(self):
        """Keep the primary writer failure while reporting restore failures."""
        text = WRITER.read_text(encoding="utf-8")
        self.assertIn("catch (Exception primaryException)", text)
        self.assertIn("new AggregateException", text)
        self.assertIn("primaryException", text)


if __name__ == "__main__":
    unittest.main()
