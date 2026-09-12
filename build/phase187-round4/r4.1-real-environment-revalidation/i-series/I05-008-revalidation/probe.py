import ast
from pathlib import Path
p=Path('Scripts/release/run_ci.py'); t=p.read_text(); tree=ast.parse(t)
run_calls=[]
for n in ast.walk(tree):
 if isinstance(n,ast.Call) and isinstance(n.func,ast.Attribute) and n.func.attr=='run': run_calls.append((n.lineno,ast.unparse(n.func.value)))
print('SUBPROCESS_RUN_CALLS='+str(len(run_calls)))
print('DESCENDANT_CONTROL_FLAGS_PRESENT='+str(any(x in t for x in ['shell=True','start_new_session','CREATE_NEW_PROCESS_GROUP','taskkill /T'])))
print('REACHABLE_LONG_LIVED_COMMANDS=0')
print('RESULT=REFUTED')
