from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
SRC=ROOT/'Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs'
class H01SelectionErrorTests(unittest.TestCase):
 def test_invalid_publish_reports_publish_configuration(self):
  t=SRC.read_text(encoding='utf-8'); self.assertIn('var configuredText = _enableFoxRunInbound',t); self.assertIn('(_foxRunPublishTransportIds ?? Array.Empty<string>())',t)
if __name__=='__main__': unittest.main()
