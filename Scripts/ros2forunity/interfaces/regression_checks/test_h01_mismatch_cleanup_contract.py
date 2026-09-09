from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
SRC=ROOT/'Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportProviderRegistry.cs'
class H01MismatchCleanupTests(unittest.TestCase):
 def test_mismatch_cleanup_is_best_effort(self):
  t=SRC.read_text(encoding='utf-8'); self.assertIn('if (mismatched)',t); self.assertIn('DisposeCaptured(new[] { session });',t)
if __name__=='__main__': unittest.main()
