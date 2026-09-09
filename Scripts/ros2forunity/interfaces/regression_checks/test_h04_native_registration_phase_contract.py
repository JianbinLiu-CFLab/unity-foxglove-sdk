from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
J=ROOT/'Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs'; L=ROOT/'Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs';
class H04LateRegistrationTests(unittest.TestCase):
    """R4.1 regression contract."""
 def test_jazzy_and_lyrical_seal_before_init_and_reset_after_shutdown(self):
     """R4.1 regression contract."""
  for p in (J,L):
   t=p.read_text(encoding='utf-8'); self.assertIn('SealNativeLibraryRegistration();',t,p.name); self.assertIn('ResetNativeLibraryRegistration();',t,p.name); self.assertLess(t.index('SealNativeLibraryRegistration();'),t.index('Ros2cs.Init();'),p.name)
 def test_init_failure_resets_registration_phase(self):
     """R4.1 regression contract."""
  for p in (J,L):
   t=p.read_text(encoding='utf-8'); self.assertGreaterEqual(t.count('ResetNativeLibraryRegistration();'),2,p.name)
if __name__=='__main__': unittest.main()
