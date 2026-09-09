from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
JAZZY=ROOT/'Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2Node.cs'
HUMBLE=ROOT/'Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/ROS2Node.cs'
class H04NodeLifetimeTests(unittest.TestCase):
    """R4.1 regression contract."""
 def test_jazzy_holds_mutex_during_native_action(self):
     """R4.1 regression contract."""
  text=JAZZY.read_text(encoding='utf-8'); self.assertIn('Hold the mutex through the native action',text); section=text[text.index('private TResult WithLiveNode'):text.index('/// <summary>',text.index('private TResult WithLiveNode'))]; self.assertIn('lock (mutex)',section); self.assertIn('return action(node);',section)
 def test_humble_control_also_holds_mutex(self):
     """R4.1 regression contract."""
  text=HUMBLE.read_text(encoding='utf-8'); section=text[text.index('private TResult WithLiveNode'):text.index('/// <summary>',text.index('private TResult WithLiveNode'))]; self.assertIn('lock (mutex)',section); self.assertIn('return action(node);',section)
if __name__=='__main__': unittest.main()
