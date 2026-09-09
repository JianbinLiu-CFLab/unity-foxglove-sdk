from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
SRC=ROOT/'Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeGeneratedSubscriptionRuntime.cs'
class H07PayloadOwnershipTests(unittest.TestCase):
 def test_manual_callbacks_receive_owned_copy(self):
  t=SRC.read_text(encoding='utf-8'); self.assertIn('var ownedPayload = apply.Frame.Payload.ToArray();',t); self.assertIn('subscribers[index].Route.OnPayload(\n                                ownedPayload,',t)
 def test_no_direct_pooled_payload_dispatch(self):
  t=SRC.read_text(encoding='utf-8'); self.assertNotIn('Route.OnPayload(\n                                apply.Frame.Payload,',t)
if __name__=='__main__': unittest.main()
