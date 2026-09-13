import importlib.util, pathlib, subprocess, sys, time, os, signal
p=pathlib.Path('Scripts/smoke/foxrun/phase184_foxglove_cli_install.py'); spec=importlib.util.spec_from_file_location('sut',p); m=importlib.util.module_from_spec(spec); sys.modules['sut']=m; spec.loader.exec_module(m)
def run(escaped):
 code = "import os,time; os.setsid(); time.sleep(30)" if escaped else "import time; time.sleep(30)"
 parent_code=f"import subprocess,time,sys; c=subprocess.Popen([sys.executable,'-c',{code!r}]); print(c.pid,flush=True); time.sleep(30)"
 parent=subprocess.Popen([sys.executable,'-c',parent_code],stdout=subprocess.PIPE,text=True)
 child_pid=int(parent.stdout.readline().strip()); time.sleep(0.2); m._terminate_and_reap(parent); time.sleep(0.5)
 try: os.kill(child_pid,0); alive=True
 except OSError: alive=False
 if alive:
  try: os.kill(child_pid,signal.SIGKILL)
  except OSError: pass
 return alive
for e in (False,True):
 a=run(e); print(f'escaped={e} child_alive_after_reap={a}')
 assert not a
print('tree_cleanup=true')
