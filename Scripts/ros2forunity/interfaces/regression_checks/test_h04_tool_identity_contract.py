from pathlib import Path
import unittest, importlib.util, sys
ROOT=Path(__file__).resolve().parents[4]
FILES=[ROOT/'Scripts/ros2forunity/windows/humble/phase160_r2fu_humble_windows_build.py',ROOT/'Scripts/ros2forunity/windows/jazzy/phase138b_r2fu_jazzy_windows_build.py',ROOT/'Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build.py']
class H04ToolIdentityTests(unittest.TestCase):
 def test_all_orchestrators_bind_tools_and_attest_source(self):
  for path in FILES:
   text=path.read_text(encoding='utf-8'); self.assertIn('def resolve_tool(',text,path.name); self.assertIn('resolve_tool("git", env)',text,path.name); self.assertIn('resolve_tool("powershell", env)',text,path.name); self.assertIn('record_checkout_identity(checkout, env, log_file)',text,path.name)
 def test_resolver_prefers_known_installations(self):
  spec=importlib.util.spec_from_file_location('humble_h04',FILES[0]); mod=importlib.util.module_from_spec(spec); sys.modules[spec.name]=mod; spec.loader.exec_module(mod)
  git=mod.resolve_tool('git',{'Path':r'C:\fake-shim'}).lower(); ps=mod.resolve_tool('powershell',{'Path':r'C:\fake-shim'}).lower()
  self.assertTrue(git.endswith(r'git\cmd\git.exe')); self.assertIn('windowspowershell',ps); self.assertTrue(ps.endswith('powershell.exe'))
if __name__=='__main__': unittest.main()
