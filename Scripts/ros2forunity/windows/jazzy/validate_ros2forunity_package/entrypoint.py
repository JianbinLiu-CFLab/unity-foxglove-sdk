from __future__ import annotations
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
del _Phase192Path

from .boundary_checks import *

def print_results(results: list[CheckResult]) -> None:
    """Print check results as aligned PASS/FAIL lines."""
    name_width = max(len(result.name) for result in results) if results else 0
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        print(f"[{status}] {result.name:<{name_width}}  {result.detail}")


def main() -> int:
    """Run optional package validation and return a process exit code."""
    JSON_CACHE.clear()
    results: list[CheckResult] = []
    check_package_metadata(results)
    check_required_files(results)
    check_manifest(results)
    check_runtime_inventory(results)
    check_text_boundaries(results)
    check_no_runtime_artifacts(results)
    check_sample_source_boundary(results)
    check_runtime_source_boundary(results)
    check_core_boundary(results)

    print_results(results)
    failed = [result for result in results if not result.ok]
    if failed:
        print(f"\nvalidate_ros2forunity_package: {len(failed)} check(s) failed.", file=sys.stderr)
        return EXIT_FAILURE

    print(f"\nvalidate_ros2forunity_package: {len(results)} check(s) passed.")
    return EXIT_SUCCESS


__all__ = [name for name in globals() if not name.startswith("__")]
