from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
SRC=ROOT/'Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportProviderRegistry.cs'
class H01ProviderFreezeTests(unittest.TestCase):
    """R4.1 regression contract."""
 def test_registration_freezes_id_and_capabilities(self):
     """R4.1 regression contract."""
  t=SRC.read_text(encoding='utf-8'); self.assertIn('RegisteredProvider',t); self.assertIn('private readonly Dictionary<IFoxRunTransportProvider, RegisteredProvider>',t); self.assertIn('public FoxRunTransportId Id { get; }',t)
if __name__=='__main__': unittest.main()
