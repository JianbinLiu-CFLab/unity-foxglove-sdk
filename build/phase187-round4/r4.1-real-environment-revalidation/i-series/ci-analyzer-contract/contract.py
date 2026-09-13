import importlib.util,sys
from pathlib import Path
spec=importlib.util.spec_from_file_location('v','Scripts/package/validate_source_generator_dll.py'); m=importlib.util.module_from_spec(spec); sys.modules['v']=m; spec.loader.exec_module(m)
print('contract_result='+str(m.validate_analyzer_contracts(('core',))))
