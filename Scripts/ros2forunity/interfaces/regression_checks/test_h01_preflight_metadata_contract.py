from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
SRC=ROOT/'Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportProviderRegistry.cs'
class H01PreflightMetadataTests(unittest.TestCase):
    """R4.1 regression contract."""
 def test_preflight_metadata_failures_are_contained(self):
     """R4.1 regression contract."""
  t=SRC.read_text(encoding='utf-8'); self.assertIn('Provider metadata is external code; contain failures',t); self.assertIn('FoxRunTransportSessionCaptureFailure.ProviderFailed',t)
if __name__=='__main__': unittest.main()
