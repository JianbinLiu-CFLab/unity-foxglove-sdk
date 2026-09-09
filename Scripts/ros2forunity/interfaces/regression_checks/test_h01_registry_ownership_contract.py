from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
SRC=ROOT/'Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportProviderRegistry.cs'
class H01RegistryOwnershipTests(unittest.TestCase):
 def test_rejected_nonnull_session_is_disposed(self):
  t=SRC.read_text(encoding='utf-8'); self.assertIn('if (session != null)\n                                DisposeCaptured(new[] { session });',t)
 def test_post_acquisition_metadata_is_contained(self):
  t=SRC.read_text(encoding='utf-8'); self.assertIn('bool mismatched;',t); self.assertIn('contain hostile metadata getters',t)
if __name__=='__main__': unittest.main()
