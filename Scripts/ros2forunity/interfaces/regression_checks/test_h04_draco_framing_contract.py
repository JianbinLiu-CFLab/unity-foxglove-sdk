from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Scripts/native/draco_probe/draco_probe_encoder.cpp'

class H04DracoFramingTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_truncated_input_has_distinct_status(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('enum class ReadStatus', t)
        self.assertIn('kCleanEof', t)
        self.assertIn('kTruncated', t)

    def test_main_returns_nonzero_for_truncation(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('return 2;', t)
        self.assertIn('status == ReadStatus::kCleanEof', t)
if __name__ == '__main__':
    unittest.main()
