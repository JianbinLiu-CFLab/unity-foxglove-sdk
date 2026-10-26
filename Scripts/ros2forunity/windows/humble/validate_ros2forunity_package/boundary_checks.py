from __future__ import annotations
from .sample_checks import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def check_runtime_source_boundary(results: list[CheckResult]) -> None:
    """Allow facade-only Runtime source while rejecting upstream ROS2/R2FU dependencies."""
    runtime = PACKAGE / "Runtime"
    if not runtime.exists():
        add(results, "optional Runtime absent or facade-only", True, "Runtime absent")
        return

    invalid_files: list[str] = []
    token_hits: list[str] = []
    for path in iter_files(runtime):
        if path.suffix.lower() not in ALLOWED_RUNTIME_SUFFIXES:
            invalid_files.append(rel(path))
            continue
        if _is_runtime_token_exempt(path, runtime):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for token in FORBIDDEN_RUNTIME_TOKENS:
            if token in text:
                token_hits.append(f"{rel(path)} contains {token}")
                break

    add(
        results,
        "optional Runtime contains only facade source",
        not invalid_files,
        "; ".join(invalid_files[:MAX_REPORTED_OFFENDERS]) if invalid_files else "all Runtime files are source/asmdef/meta",
    )
    add(
        results,
        "optional Runtime has no upstream ROS2/R2FU API references",
        not token_hits,
        "; ".join(token_hits[:MAX_REPORTED_OFFENDERS]) if token_hits else "no forbidden Runtime tokens",
    )


def check_core_boundary(results: list[CheckResult]) -> None:
    """Confirm the core package does not depend on the optional R2FU package."""
    core_json = CORE_PACKAGE / "package.json"
    data = load_json(core_json, results, "core package.json parses") if core_json.exists() else {}
    dependencies = data.get("dependencies", {}) if isinstance(data, dict) else {}
    add(
        results,
        "core package does not depend on optional package",
        isinstance(dependencies, dict) and "dev.unity2foxglove.ros2forunity" not in dependencies,
        f"dependencies={dependencies!r}",
    )




__all__ = [name for name in globals() if not name.startswith("__")]
