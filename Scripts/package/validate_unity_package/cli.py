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


__all__ = [
    'CONSUMER_LOCAL_DEPENDENCIES',
    'CONSUMER_LOCAL_DEPENDENCY_PREFIXES',
    'CheckResult',
    'DOCS',
    'EMPTY_RESULT_NAME_WIDTH',
    'EXIT_FAILURE',
    'EXIT_SUCCESS',
    'EXPECTED_SAMPLES',
    'EXPECTED_SAMPLE_COUNT',
    'FORBIDDEN_PUBLIC_LABELS_BY_GROUP',
    'FORBIDDEN_PUBLIC_PATTERNS',
    'FORBIDDEN_PUBLIC_PATTERN_GROUPS',
    'FORBIDDEN_PUBLIC_SCAN_PATTERN',
    'FORBIDDEN_SAMPLE_NAMES',
    'FORBIDDEN_SAMPLE_NAME_PATTERNS',
    'FORBIDDEN_SAMPLE_PARTS',
    'Iterable',
    'LEGACY_VALIDATION_FILENAME_CUTOFF_INDEX',
    'LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE',
    'MAX_REPORTED_MISSING_META',
    'MAX_REPORTED_OFFENDERS',
    'META_GUID_RE',
    'PACKAGE',
    'PACKAGE_BUILD_DIRECTORY_NAMES',
    'Path',
    'REMOTE_GATEWAY_PACKAGE',
    'REPO_ROOT_PARENT_DEPTH',
    'ROOT',
    'ROS2_BRIDGE_PACKAGE',
    'ROS2_FOR_UNITY_PACKAGE',
    'ROS2_RUNTIME_PACKAGES',
    'ROS2_TYPESUPPORT_PACKAGES',
    'SAMPLES',
    'THIRD_PARTY_NOTICES',
    'THIRD_PARTY_NOTICE_REQUIREMENTS',
    'UNITY_DEMO_ASSETS',
    'UNITY_DEMO_SCRIPTS',
    'UNITY_META_EXTENSIONS',
    'VALIDATION_PHASE_FILENAME_INDEX_RE',
    'VALIDATION_PHASE_FILENAME_RE',
    'VERSION_RE',
    'add',
    'annotations',
    'dataclass',
    'iter_bridge_asset_files',
    'iter_files',
    'json',
    'load_package_json',
    'main',
    'path_is_relative_to',
    'print_results',
    're',
    'rel',
    'sys',
]
