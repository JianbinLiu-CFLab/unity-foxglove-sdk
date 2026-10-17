"""Command-line orchestration for Unity package validation."""

from __future__ import annotations

from .foundation import *
def print_results(results: list[CheckResult]) -> None:
    """Print check results as aligned PASS/FAIL lines."""
    name_width = max(len(r.name) for r in results) if results else EMPTY_RESULT_NAME_WIDTH
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        print(f"[{status}] {result.name:<{name_width}}  {result.detail}")


def main() -> int:
    """Run all release package checks and return a process exit code."""
    results: list[CheckResult] = []
    package_entries = list(PACKAGE.rglob("*")) if PACKAGE.exists() else []
    package_files = [path for path in package_entries if path.is_file()]
    samples_entries = [path for path in package_entries if path_is_relative_to(path, SAMPLES)]
    samples_files = [path for path in samples_entries if path.is_file()]
    docs_files = [path for path in package_files if path_is_relative_to(path, DOCS)]
    check_package_matrix(results)
    check_consumer_local_bindings(results)
    data = load_package_json(results)
    if data:
        check_package_identity(results, data)
        check_dependent_package_versions(results, data)
    check_ros2_bridge_package(results)
    check_optional_package_boundaries(results)
    check_required_files(results)
    bridge_files = iter_bridge_asset_files()
    check_sample_meta(results, samples_files, bridge_files)
    check_sdk_script_meta(results, package_files)
    check_sample_boundaries(results)
    check_forbidden_public_content(results, samples_files, docs_files)
    check_forbidden_sample_artifacts(results, samples_entries)
    check_package_build_artifacts(results, package_entries)
    check_manual_phase_service_guards(results)
    check_validation_naming(results, package_files)
    check_google_protobuf_collision(results)
    check_third_party_notices(results, package_entries)

    print_results(results)
    failed = [r for r in results if not r.ok]
    if failed:
        print(f"\nvalidate_unity_package: {len(failed)} check(s) failed.", file=sys.stderr)
        return EXIT_FAILURE

    print(f"\nvalidate_unity_package: {len(results)} check(s) passed.")
    return EXIT_SUCCESS


__all__ = sorted(name for name in globals() if not name.startswith("__"))
