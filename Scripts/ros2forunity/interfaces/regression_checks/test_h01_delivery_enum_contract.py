from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportContracts.cs'

class H01DeliveryEnumTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_policy_rejects_unknown_delivery_axes(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertEqual(t.count('Enum.IsDefined(typeof(FoxRunDelivery'), 3)
if __name__ == '__main__':
    unittest.main()
