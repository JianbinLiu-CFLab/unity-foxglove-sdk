import importlib.util,sys
from pathlib import Path
spec=importlib.util.spec_from_file_location('v','Scripts/package/validate_source_generator_dll.py'); m=importlib.util.module_from_spec(spec); sys.modules['v']=m; spec.loader.exec_module(m)
project=Path('Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj')
sources=m._project_sources(project); print('explicit_sources='+str(len(sources))); assert len(sources)==45, 'excluded Compile items incorrectly counted'
