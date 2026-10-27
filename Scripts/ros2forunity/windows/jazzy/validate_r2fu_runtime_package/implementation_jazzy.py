from __future__ import annotations

if __package__.startswith("Scripts."):
    from Scripts.ros2forunity.windows.jazzy.validate_r2fu_runtime_package.sections import checks as _checks
else:
    from validate_r2fu_runtime_package.sections import checks as _checks

ADAPTER_PACKAGE = _checks.ADAPTER_PACKAGE
ARTIFACT_NAME = _checks.ARTIFACT_NAME
CORE_PACKAGE = _checks.CORE_PACKAGE
CRITICAL_DLLS = _checks.CRITICAL_DLLS
CheckResult = _checks.CheckResult
EXIT_FAILURE = _checks.EXIT_FAILURE
EXIT_SUCCESS = _checks.EXIT_SUCCESS
EXPECTED_ARTIFACT_SHA256 = _checks.EXPECTED_ARTIFACT_SHA256
EXPECTED_RMW_IMPLEMENTATION = _checks.EXPECTED_RMW_IMPLEMENTATION
ElementTree = _checks.ElementTree
INTERNAL_TOKENS = _checks.INTERNAL_TOKENS
INVENTORY = _checks.INVENTORY
Iterable = _checks.Iterable
LEAKY_UPSTREAM_EXAMPLES = _checks.LEAKY_UPSTREAM_EXAMPLES
LOCAL_PATCH_MARKER = _checks.LOCAL_PATCH_MARKER
MANIFEST = _checks.MANIFEST
MODIFICATIONS_COPYRIGHT = _checks.MODIFICATIONS_COPYRIGHT
PACKAGE = _checks.PACKAGE
PACKAGE_NAME = _checks.PACKAGE_NAME
PATCHED_VENDOR_FILES = _checks.PATCHED_VENDOR_FILES
PHASE161_ADDED_DLLS = _checks.PHASE161_ADDED_DLLS
PHASE161_ASSET_CRITICAL_BASELINE = _checks.PHASE161_ASSET_CRITICAL_BASELINE
PHASE161_SUPPLEMENTAL_RUNTIME_DLLS = _checks.PHASE161_SUPPLEMENTAL_RUNTIME_DLLS
PLUGIN_ROOT = _checks.PLUGIN_ROOT
PUBLIC_DOCS = _checks.PUBLIC_DOCS
Path = _checks.Path
PurePosixPath = _checks.PurePosixPath
REPO_ROOT_PARENT_DEPTH = _checks.REPO_ROOT_PARENT_DEPTH
ROOT = _checks.ROOT
RUNTIME_ROOT = _checks.RUNTIME_ROOT
V083_EXCLUDED_TEST_TYPESUPPORT_DLLS = _checks.V083_EXCLUDED_TEST_TYPESUPPORT_DLLS
annotations = _checks.annotations
argparse = _checks.argparse
dataclass = _checks.dataclass
hashlib = _checks.hashlib
json = _checks.json
os = _checks.os
re = _checks.re
subprocess = _checks.subprocess
sys = _checks.sys
def _manifest_without_acceptance(commit_sha: str, manifest_relative: str) -> dict | None:
    """Read a committed runtime manifest without its mutable acceptance record."""
    _sync()
    return _checks._manifest_without_acceptance(commit_sha, manifest_relative)

def acceptance_commit_is_ancestor_and_package_unchanged(acceptance_sha: str, candidate_sha: str) -> bool:
    """Return whether an accepted commit is an ancestor with an unchanged runtime payload."""
    _sync()
    return _checks.acceptance_commit_is_ancestor_and_package_unchanged(acceptance_sha, candidate_sha)

def add(results: list[CheckResult], name: str, ok: bool, detail: str = "") -> None:
    """Append one check result to the accumulated report."""
    _sync()
    return _checks.add(results, name, ok, detail)

def check_generator_alignment(results: list[CheckResult]) -> None:
    """Validate the generator knows about the lifecycle-patched package shape."""
    _sync()
    return _checks.check_generator_alignment(results)

def check_inventory(results: list[CheckResult], manifest: dict, release_gate: bool = False, skip_dll_hash: bool = False) -> None:
    """Validate the copied runtime inventory."""
    _sync()
    return _checks.check_inventory(results, manifest, release_gate, skip_dll_hash)

def check_package_boundaries(results: list[CheckResult]) -> None:
    """Validate the SDK and adapter package dependency boundaries."""
    _sync()
    return _checks.check_package_boundaries(results)

def check_package_metadata(results: list[CheckResult]) -> None:
    """Validate Unity package metadata."""
    _sync()
    return _checks.check_package_metadata(results)

def check_package_path_patch(results: list[CheckResult]) -> None:
    """Validate the ROS2ForUnity.cs package path patch."""
    _sync()
    return _checks.check_package_path_patch(results)

def check_public_docs(results: list[CheckResult], manifest: dict) -> None:
    """Validate public runtime docs avoid internal planning names."""
    _sync()
    return _checks.check_public_docs(results, manifest)

def check_required_files(results: list[CheckResult]) -> None:
    """Validate files required by the runtime package contract."""
    _sync()
    return _checks.check_required_files(results)

def check_ros2cs_metadata_portability(results: list[CheckResult]) -> None:
    """Require package-relative roots in both shipped plugin inventories."""
    _sync()
    return _checks.check_ros2cs_metadata_portability(results)

def check_runtime_asmdef(results: list[CheckResult]) -> None:
    """Validate the runtime assembly definition is safe for Editor and Player."""
    _sync()
    return _checks.check_runtime_asmdef(results)

def check_runtime_files(results: list[CheckResult]) -> None:
    """Validate critical runtime files and package layout."""
    _sync()
    return _checks.check_runtime_files(results)

def check_runtime_manifest(results: list[CheckResult], data: dict) -> None:
    """Validate the runtime support manifest."""
    _sync()
    return _checks.check_runtime_manifest(results, data)

def check_runtime_source_patches(results: list[CheckResult]) -> None:
    """Validate local lifecycle, time-source, and attribution patches on vendored R2FU sources."""
    _sync()
    return _checks.check_runtime_source_patches(results)

def core_runtime_has_forbidden_tokens() -> bool:
    """Return True when the core SDK Runtime contains R2FU-only tokens."""
    _sync()
    return _checks.core_runtime_has_forbidden_tokens()

def file_sha256(path: Path) -> str:
    """Return the SHA-256 digest for a package payload file."""
    _sync()
    return _checks.file_sha256(path)

def fresh_project_acceptance_binding_matches_manifest(manifest: dict) -> bool:
    """Return whether a passed acceptance record remains valid for this candidate."""
    _sync()
    return _checks.fresh_project_acceptance_binding_matches_manifest(manifest)

def fresh_project_acceptance_passed(manifest: dict) -> bool:
    """Return whether fresh-project acceptance passed."""
    _sync()
    return _checks.fresh_project_acceptance_passed(manifest)

def fresh_project_acceptance_record_is_valid(manifest: dict) -> bool:
    """Return whether the manifest carries a deferred or bound acceptance record."""
    _sync()
    return _checks.fresh_project_acceptance_record_is_valid(manifest)

def fresh_project_acceptance_status(manifest: dict) -> str:
    """Return the normalized fresh-project acceptance status."""
    _sync()
    return _checks.fresh_project_acceptance_status(manifest)

def inventory_category_counts_match(categories: object, files: object) -> bool:
    """Return whether declared inventory categories exactly match file rows."""
    _sync()
    return _checks.inventory_category_counts_match(categories, files)

def is_sha256(value: object) -> bool:
    """Return true for a complete lowercase SHA-256 hex string."""
    _sync()
    return _checks.is_sha256(value)

def iter_files(root: Path) -> Iterable[Path]:
    """Yield files below a root, returning an empty iterable when absent."""
    _sync()
    return _checks.iter_files(root)

def load_json(path: Path, results: list[CheckResult], name: str) -> dict:
    """Load JSON and record whether parsing succeeded."""
    _sync()
    return _checks.load_json(path, results, name)

def main(argv: list[str] | None = None) -> int:
    """Run validation and return a process exit code."""
    _sync()
    return _checks.main(argv)

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse validator command-line arguments."""
    _sync()
    return _checks.parse_args(argv)

def print_results(results: list[CheckResult]) -> None:
    """Print validation results in a compact PASS/FAIL format."""
    _sync()
    return _checks.print_results(results)

def published_runtime_is_not_prototype(manifest: dict) -> bool:
    """Return whether the manifest is not marked as a Prototype release."""
    _sync()
    return _checks.published_runtime_is_not_prototype(manifest)

def read_optional_text(path: Path) -> str:
    """Read UTF-8 text when present, returning an empty string for absent files."""
    _sync()
    return _checks.read_optional_text(path)

def rel(path: Path) -> str:
    """Format a path relative to the repository root when possible."""
    _sync()
    return _checks.rel(path)

def run_checks(release_gate: bool = False, skip_dll_hash: bool = False) -> list[CheckResult]:
    """Run all runtime package checks."""
    _sync()
    return _checks.run_checks(release_gate, skip_dll_hash)

def unity_editor_using_is_guarded(text: str) -> bool:
    """Return true when every UnityEditor using is inside a UNITY_EDITOR block."""
    _sync()
    return _checks.unity_editor_using_is_guarded(text)

_WRAPPERS = {
    "_manifest_without_acceptance": _manifest_without_acceptance,
    "acceptance_commit_is_ancestor_and_package_unchanged": acceptance_commit_is_ancestor_and_package_unchanged,
    "add": add,
    "check_generator_alignment": check_generator_alignment,
    "check_inventory": check_inventory,
    "check_package_boundaries": check_package_boundaries,
    "check_package_metadata": check_package_metadata,
    "check_package_path_patch": check_package_path_patch,
    "check_public_docs": check_public_docs,
    "check_required_files": check_required_files,
    "check_ros2cs_metadata_portability": check_ros2cs_metadata_portability,
    "check_runtime_asmdef": check_runtime_asmdef,
    "check_runtime_files": check_runtime_files,
    "check_runtime_manifest": check_runtime_manifest,
    "check_runtime_source_patches": check_runtime_source_patches,
    "core_runtime_has_forbidden_tokens": core_runtime_has_forbidden_tokens,
    "file_sha256": file_sha256,
    "fresh_project_acceptance_binding_matches_manifest": fresh_project_acceptance_binding_matches_manifest,
    "fresh_project_acceptance_passed": fresh_project_acceptance_passed,
    "fresh_project_acceptance_record_is_valid": fresh_project_acceptance_record_is_valid,
    "fresh_project_acceptance_status": fresh_project_acceptance_status,
    "inventory_category_counts_match": inventory_category_counts_match,
    "is_sha256": is_sha256,
    "iter_files": iter_files,
    "load_json": load_json,
    "main": main,
    "parse_args": parse_args,
    "print_results": print_results,
    "published_runtime_is_not_prototype": published_runtime_is_not_prototype,
    "read_optional_text": read_optional_text,
    "rel": rel,
    "run_checks": run_checks,
    "unity_editor_using_is_guarded": unity_editor_using_is_guarded,
}

def _sync() -> None:
    """Synchronize facade exports with implementation sections."""
    _checks.sync({
        "ADAPTER_PACKAGE": ADAPTER_PACKAGE,
        "ARTIFACT_NAME": ARTIFACT_NAME,
        "CORE_PACKAGE": CORE_PACKAGE,
        "CRITICAL_DLLS": CRITICAL_DLLS,
        "CheckResult": CheckResult,
        "EXIT_FAILURE": EXIT_FAILURE,
        "EXIT_SUCCESS": EXIT_SUCCESS,
        "EXPECTED_ARTIFACT_SHA256": EXPECTED_ARTIFACT_SHA256,
        "EXPECTED_RMW_IMPLEMENTATION": EXPECTED_RMW_IMPLEMENTATION,
        "ElementTree": ElementTree,
        "INTERNAL_TOKENS": INTERNAL_TOKENS,
        "INVENTORY": INVENTORY,
        "Iterable": Iterable,
        "LEAKY_UPSTREAM_EXAMPLES": LEAKY_UPSTREAM_EXAMPLES,
        "LOCAL_PATCH_MARKER": LOCAL_PATCH_MARKER,
        "MANIFEST": MANIFEST,
        "MODIFICATIONS_COPYRIGHT": MODIFICATIONS_COPYRIGHT,
        "PACKAGE": PACKAGE,
        "PACKAGE_NAME": PACKAGE_NAME,
        "PATCHED_VENDOR_FILES": PATCHED_VENDOR_FILES,
        "PHASE161_ADDED_DLLS": PHASE161_ADDED_DLLS,
        "PHASE161_ASSET_CRITICAL_BASELINE": PHASE161_ASSET_CRITICAL_BASELINE,
        "PHASE161_SUPPLEMENTAL_RUNTIME_DLLS": PHASE161_SUPPLEMENTAL_RUNTIME_DLLS,
        "PLUGIN_ROOT": PLUGIN_ROOT,
        "PUBLIC_DOCS": PUBLIC_DOCS,
        "Path": Path,
        "PurePosixPath": PurePosixPath,
        "REPO_ROOT_PARENT_DEPTH": REPO_ROOT_PARENT_DEPTH,
        "ROOT": ROOT,
        "RUNTIME_ROOT": RUNTIME_ROOT,
        "V083_EXCLUDED_TEST_TYPESUPPORT_DLLS": V083_EXCLUDED_TEST_TYPESUPPORT_DLLS,
        "_manifest_without_acceptance": _manifest_without_acceptance,
        "acceptance_commit_is_ancestor_and_package_unchanged": acceptance_commit_is_ancestor_and_package_unchanged,
        "add": add,
        "annotations": annotations,
        "argparse": argparse,
        "check_generator_alignment": check_generator_alignment,
        "check_inventory": check_inventory,
        "check_package_boundaries": check_package_boundaries,
        "check_package_metadata": check_package_metadata,
        "check_package_path_patch": check_package_path_patch,
        "check_public_docs": check_public_docs,
        "check_required_files": check_required_files,
        "check_ros2cs_metadata_portability": check_ros2cs_metadata_portability,
        "check_runtime_asmdef": check_runtime_asmdef,
        "check_runtime_files": check_runtime_files,
        "check_runtime_manifest": check_runtime_manifest,
        "check_runtime_source_patches": check_runtime_source_patches,
        "core_runtime_has_forbidden_tokens": core_runtime_has_forbidden_tokens,
        "dataclass": dataclass,
        "file_sha256": file_sha256,
        "fresh_project_acceptance_binding_matches_manifest": fresh_project_acceptance_binding_matches_manifest,
        "fresh_project_acceptance_passed": fresh_project_acceptance_passed,
        "fresh_project_acceptance_record_is_valid": fresh_project_acceptance_record_is_valid,
        "fresh_project_acceptance_status": fresh_project_acceptance_status,
        "hashlib": hashlib,
        "inventory_category_counts_match": inventory_category_counts_match,
        "is_sha256": is_sha256,
        "iter_files": iter_files,
        "json": json,
        "load_json": load_json,
        "main": main,
        "os": os,
        "parse_args": parse_args,
        "print_results": print_results,
        "published_runtime_is_not_prototype": published_runtime_is_not_prototype,
        "re": re,
        "read_optional_text": read_optional_text,
        "rel": rel,
        "run_checks": run_checks,
        "subprocess": subprocess,
        "sys": sys,
        "unity_editor_using_is_guarded": unity_editor_using_is_guarded,
    }, _WRAPPERS)

__all__ = ['ADAPTER_PACKAGE', 'ARTIFACT_NAME', 'CORE_PACKAGE', 'CRITICAL_DLLS', 'CheckResult', 'EXIT_FAILURE', 'EXIT_SUCCESS', 'EXPECTED_ARTIFACT_SHA256', 'EXPECTED_RMW_IMPLEMENTATION', 'ElementTree', 'INTERNAL_TOKENS', 'INVENTORY', 'Iterable', 'LEAKY_UPSTREAM_EXAMPLES', 'LOCAL_PATCH_MARKER', 'MANIFEST', 'MODIFICATIONS_COPYRIGHT', 'PACKAGE', 'PACKAGE_NAME', 'PATCHED_VENDOR_FILES', 'PHASE161_ADDED_DLLS', 'PHASE161_ASSET_CRITICAL_BASELINE', 'PHASE161_SUPPLEMENTAL_RUNTIME_DLLS', 'PLUGIN_ROOT', 'PUBLIC_DOCS', 'Path', 'PurePosixPath', 'REPO_ROOT_PARENT_DEPTH', 'ROOT', 'RUNTIME_ROOT', 'V083_EXCLUDED_TEST_TYPESUPPORT_DLLS', '_manifest_without_acceptance', 'acceptance_commit_is_ancestor_and_package_unchanged', 'add', 'annotations', 'argparse', 'check_generator_alignment', 'check_inventory', 'check_package_boundaries', 'check_package_metadata', 'check_package_path_patch', 'check_public_docs', 'check_required_files', 'check_ros2cs_metadata_portability', 'check_runtime_asmdef', 'check_runtime_files', 'check_runtime_manifest', 'check_runtime_source_patches', 'core_runtime_has_forbidden_tokens', 'dataclass', 'file_sha256', 'fresh_project_acceptance_binding_matches_manifest', 'fresh_project_acceptance_passed', 'fresh_project_acceptance_record_is_valid', 'fresh_project_acceptance_status', 'hashlib', 'inventory_category_counts_match', 'is_sha256', 'iter_files', 'json', 'load_json', 'main', 'os', 'parse_args', 'print_results', 'published_runtime_is_not_prototype', 're', 'read_optional_text', 'rel', 'run_checks', 'subprocess', 'sys', 'unity_editor_using_is_guarded']
