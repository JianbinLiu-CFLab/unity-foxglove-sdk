from __future__ import annotations

import importlib.util
import sys
from pathlib import Path


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    if path == Path(__file__).resolve().parents[1] / "run_ci.py":
        for module_name in tuple(sys.modules):
            if module_name == "run_ci" or module_name.startswith("run_ci."):
                sys.modules.pop(module_name, None)
    sys.modules.pop(spec.name, None)
    sys.modules[spec.name] = module
    try:
        spec.loader.exec_module(module)
    except Exception:
        sys.modules.pop(spec.name, None)
        raise
    return module


def module_registry():
    """Return the interpreter module registry for compatibility assertions."""
    return sys.modules
