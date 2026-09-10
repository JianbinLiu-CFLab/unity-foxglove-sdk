from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Scripts/ros2forunity/interfaces/characterize_foxrun_custom_interface.py'

class H02SubstCleanupTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_cleanup_status_is_checked_without_masking_primary(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('primary_error: BaseException | None = None', t)
        self.assertIn('cleanup.returncode != 0', t)
        self.assertIn('if primary_error is None', t)
if __name__ == '__main__':
    unittest.main()
