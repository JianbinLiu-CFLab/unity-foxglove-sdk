from __future__ import annotations

try:
    from Scripts.ros2forunity.windows.humble.validate_r2fu_runtime_package_sections import checks as _checks
except ImportError:
    from validate_r2fu_runtime_package_sections import checks as _checks

json = _checks.json
argparse = _checks.argparse
os = _checks.os
hashlib = _checks.hashlib
re = _checks.re
sys = _checks.sys
subprocess = _checks.subprocess
ElementTree = _checks.ElementTree
dataclass = _checks.dataclass
Path = _checks.Path
PurePosixPath = _checks.PurePosixPath
Iterable = _checks.Iterable
REPO_ROOT_PARENT_DEPTH = _checks.REPO_ROOT_PARENT_DEPTH
EXIT_SUCCESS = _checks.EXIT_SUCCESS
EXIT_FAILURE = _checks.EXIT_FAILURE
ROOT = _checks.ROOT
PACKAGE_NAME = _checks.PACKAGE_NAME
PACKAGE = _checks.PACKAGE
ADAPTER_PACKAGE = _checks.ADAPTER_PACKAGE
CORE_PACKAGE = _checks.CORE_PACKAGE
RUNTIME_ROOT = _checks.RUNTIME_ROOT
PLUGIN_ROOT = _checks.PLUGIN_ROOT
MANIFEST = _checks.MANIFEST
INVENTORY = _checks.INVENTORY
ARTIFACT_NAME = _checks.ARTIFACT_NAME
EXPECTED_RMW_IMPLEMENTATION = _checks.EXPECTED_RMW_IMPLEMENTATION
CRITICAL_DLLS = _checks.CRITICAL_DLLS
BASELINE_MESSAGE_PACKAGES = _checks.BASELINE_MESSAGE_PACKAGES
HUMBLE_HANDOFF_FAMILIES = _checks.HUMBLE_HANDOFF_FAMILIES
HUMBLE_HANDOFF_DLLS = _checks.HUMBLE_HANDOFF_DLLS
MODIFICATIONS_COPYRIGHT = _checks.MODIFICATIONS_COPYRIGHT
LOCAL_PATCH_MARKER = _checks.LOCAL_PATCH_MARKER
LEAKY_UPSTREAM_EXAMPLES = _checks.LEAKY_UPSTREAM_EXAMPLES
PATCHED_VENDOR_FILES = _checks.PATCHED_VENDOR_FILES
PUBLIC_DOCS = _checks.PUBLIC_DOCS
INTERNAL_TOKENS = _checks.INTERNAL_TOKENS
CheckResult = _checks.CheckResult

def rel(path: Path) -> str:
    'Format a path relative to the repository root when possible.'
    _sync()
    return _checks.rel(path)

def add(results: list[CheckResult], name: str, ok: bool, detail: str='') -> None:
    'Append one check result to the accumulated report.'
    _sync()
    return _checks.add(results, name, ok, detail)

def iter_files(root: Path) -> Iterable[Path]:
    'Yield files below a root, returning an empty iterable when absent.'
    _sync()
    return _checks.iter_files(root)

def load_json(path: Path, results: list[CheckResult], name: str) -> dict:
    'Load JSON and record whether parsing succeeded.'
    _sync()
    return _checks.load_json(path, results, name)

def read_optional_text(path: Path) -> str:
    'Read UTF-8 text when present, returning an empty string for absent files.'
    _sync()
    return _checks.read_optional_text(path)

def file_sha256(path: Path) -> str:
    'Return the SHA-256 digest for a package payload file.'
    _sync()
    return _checks.file_sha256(path)

def critical_runtime_file_path(name: str) -> Path:
    'Resolve critical runtime files across managed and native plugin roots.'
    _sync()
    return _checks.critical_runtime_file_path(name)

def is_sha256(value: object) -> bool:
    'Return true for a complete lowercase SHA-256 hex string.'
    _sync()
    return _checks.is_sha256(value)

def unity_editor_using_is_guarded(text: str) -> bool:
    'Return true when every UnityEditor using is inside a UNITY_EDITOR block.'
    _sync()
    return _checks.unity_editor_using_is_guarded(text)

def check_package_metadata(results: list[CheckResult]) -> None:
    'Validate Unity package metadata.'
    _sync()
    return _checks.check_package_metadata(results)

def check_required_files(results: list[CheckResult]) -> None:
    'Validate files required by the runtime package contract.'
    _sync()
    return _checks.check_required_files(results)

def check_ros2cs_metadata_portability(results: list[CheckResult]) -> None:
    'Require package-relative roots in both shipped plugin inventories.'
    _sync()
    return _checks.check_ros2cs_metadata_portability(results)

def check_message_package_floor(results: list[CheckResult]) -> None:
    'Validate generated message packages required by packaged R2FU scripts.'
    _sync()
    return _checks.check_message_package_floor(results)

def check_runtime_manifest(results: list[CheckResult], data: dict) -> None:
    'Validate the runtime support manifest.'
    _sync()
    return _checks.check_runtime_manifest(results, data)

def inventory_category_counts_match(categories: object, files: object) -> bool:
    'Return whether declared inventory categories exactly match file rows.'
    _sync()
    return _checks.inventory_category_counts_match(categories, files)

def fresh_project_acceptance_status(manifest: dict) -> str:
    'Return the normalized fresh-project acceptance status.'
    _sync()
    return _checks.fresh_project_acceptance_status(manifest)

def published_runtime_is_not_prototype(manifest: dict) -> bool:
    'Return whether the manifest is not marked as a Prototype release.'
    _sync()
    return _checks.published_runtime_is_not_prototype(manifest)

def fresh_project_acceptance_passed(manifest: dict) -> bool:
    'Return whether fresh-project acceptance passed.'
    _sync()
    return _checks.fresh_project_acceptance_passed(manifest)

def _manifest_without_acceptance(commit_sha: str, manifest_relative: str) -> dict | None:
    'Read a committed runtime manifest without its mutable acceptance record.'
    _sync()
    return _checks._manifest_without_acceptance(commit_sha, manifest_relative)

def acceptance_commit_is_ancestor_and_package_unchanged(acceptance_sha: str, candidate_sha: str) -> bool:
    'Return whether an accepted commit is an ancestor with an unchanged runtime payload.'
    _sync()
    return _checks.acceptance_commit_is_ancestor_and_package_unchanged(acceptance_sha, candidate_sha)

def fresh_project_acceptance_binding_matches_manifest(manifest: dict) -> bool:
    'Return whether a passed acceptance record remains valid for this candidate.'
    _sync()
    return _checks.fresh_project_acceptance_binding_matches_manifest(manifest)

def fresh_project_acceptance_record_is_valid(manifest: dict) -> bool:
    'Return whether the manifest carries a deferred or bound acceptance record.'
    _sync()
    return _checks.fresh_project_acceptance_record_is_valid(manifest)

def check_inventory(results: list[CheckResult], manifest: dict, release_gate: bool=False, skip_dll_hash: bool=False) -> None:
    'Validate the copied runtime inventory.'
    _sync()
    return _checks.check_inventory(results, manifest, release_gate, skip_dll_hash)

def check_runtime_files(results: list[CheckResult]) -> None:
    'Validate critical runtime files and package layout.'
    _sync()
    return _checks.check_runtime_files(results)

def check_package_path_patch(results: list[CheckResult]) -> None:
    'Validate the ROS2ForUnity.cs package path patch.'
    _sync()
    return _checks.check_package_path_patch(results)

def check_runtime_asmdef(results: list[CheckResult]) -> None:
    'Validate the runtime assembly definition is safe for Editor and Player.'
    _sync()
    return _checks.check_runtime_asmdef(results)

def check_runtime_source_patches(results: list[CheckResult]) -> None:
    'Validate local lifecycle, time-source, and attribution patches on vendored R2FU sources.'
    _sync()
    return _checks.check_runtime_source_patches(results)

def check_generator_alignment(results: list[CheckResult]) -> None:
    'Validate the generator knows about the lifecycle-patched package shape.'
    _sync()
    return _checks.check_generator_alignment(results)

def check_public_docs(results: list[CheckResult], manifest: dict) -> None:
    'Validate public runtime docs avoid internal planning names.'
    _sync()
    return _checks.check_public_docs(results, manifest)

def check_package_boundaries(results: list[CheckResult]) -> None:
    'Validate the SDK and adapter package dependency boundaries.'
    _sync()
    return _checks.check_package_boundaries(results)

def core_runtime_has_forbidden_tokens() -> bool:
    'Return True when the core SDK Runtime contains R2FU-only tokens.'
    _sync()
    return _checks.core_runtime_has_forbidden_tokens()

def run_checks(release_gate: bool=False, skip_dll_hash: bool=False) -> list[CheckResult]:
    'Run all runtime package checks.'
    _sync()
    return _checks.run_checks(release_gate, skip_dll_hash)

def print_results(results: list[CheckResult]) -> None:
    'Print validation results in a compact PASS/FAIL format.'
    _sync()
    return _checks.print_results(results)

def parse_args(argv: list[str] | None=None) -> argparse.Namespace:
    'Parse validator command-line arguments.'
    _sync()
    return _checks.parse_args(argv)

def main(argv: list[str] | None=None) -> int:
    'Run validation and return a process exit code.'
    _sync()
    return _checks.main(argv)

_WRAPPERS = {
    'rel': rel,
    'add': add,
    'iter_files': iter_files,
    'load_json': load_json,
    'read_optional_text': read_optional_text,
    'file_sha256': file_sha256,
    'critical_runtime_file_path': critical_runtime_file_path,
    'is_sha256': is_sha256,
    'unity_editor_using_is_guarded': unity_editor_using_is_guarded,
    'check_package_metadata': check_package_metadata,
    'check_required_files': check_required_files,
    'check_ros2cs_metadata_portability': check_ros2cs_metadata_portability,
    'check_message_package_floor': check_message_package_floor,
    'check_runtime_manifest': check_runtime_manifest,
    'inventory_category_counts_match': inventory_category_counts_match,
    'fresh_project_acceptance_status': fresh_project_acceptance_status,
    'published_runtime_is_not_prototype': published_runtime_is_not_prototype,
    'fresh_project_acceptance_passed': fresh_project_acceptance_passed,
    '_manifest_without_acceptance': _manifest_without_acceptance,
    'acceptance_commit_is_ancestor_and_package_unchanged': acceptance_commit_is_ancestor_and_package_unchanged,
    'fresh_project_acceptance_binding_matches_manifest': fresh_project_acceptance_binding_matches_manifest,
    'fresh_project_acceptance_record_is_valid': fresh_project_acceptance_record_is_valid,
    'check_inventory': check_inventory,
    'check_runtime_files': check_runtime_files,
    'check_package_path_patch': check_package_path_patch,
    'check_runtime_asmdef': check_runtime_asmdef,
    'check_runtime_source_patches': check_runtime_source_patches,
    'check_generator_alignment': check_generator_alignment,
    'check_public_docs': check_public_docs,
    'check_package_boundaries': check_package_boundaries,
    'core_runtime_has_forbidden_tokens': core_runtime_has_forbidden_tokens,
    'run_checks': run_checks,
    'print_results': print_results,
    'parse_args': parse_args,
    'main': main,
}

def _sync() -> None:
    """Synchronize facade exports with implementation sections."""
    _checks.sync({
        'json': json,
        'argparse': argparse,
        'os': os,
        'hashlib': hashlib,
        're': re,
        'sys': sys,
        'subprocess': subprocess,
        'ElementTree': ElementTree,
        'dataclass': dataclass,
        'Path': Path,
        'PurePosixPath': PurePosixPath,
        'Iterable': Iterable,
        'REPO_ROOT_PARENT_DEPTH': REPO_ROOT_PARENT_DEPTH,
        'EXIT_SUCCESS': EXIT_SUCCESS,
        'EXIT_FAILURE': EXIT_FAILURE,
        'ROOT': ROOT,
        'PACKAGE_NAME': PACKAGE_NAME,
        'PACKAGE': PACKAGE,
        'ADAPTER_PACKAGE': ADAPTER_PACKAGE,
        'CORE_PACKAGE': CORE_PACKAGE,
        'RUNTIME_ROOT': RUNTIME_ROOT,
        'PLUGIN_ROOT': PLUGIN_ROOT,
        'MANIFEST': MANIFEST,
        'INVENTORY': INVENTORY,
        'ARTIFACT_NAME': ARTIFACT_NAME,
        'EXPECTED_RMW_IMPLEMENTATION': EXPECTED_RMW_IMPLEMENTATION,
        'CRITICAL_DLLS': CRITICAL_DLLS,
        'BASELINE_MESSAGE_PACKAGES': BASELINE_MESSAGE_PACKAGES,
        'HUMBLE_HANDOFF_FAMILIES': HUMBLE_HANDOFF_FAMILIES,
        'HUMBLE_HANDOFF_DLLS': HUMBLE_HANDOFF_DLLS,
        'MODIFICATIONS_COPYRIGHT': MODIFICATIONS_COPYRIGHT,
        'LOCAL_PATCH_MARKER': LOCAL_PATCH_MARKER,
        'LEAKY_UPSTREAM_EXAMPLES': LEAKY_UPSTREAM_EXAMPLES,
        'PATCHED_VENDOR_FILES': PATCHED_VENDOR_FILES,
        'PUBLIC_DOCS': PUBLIC_DOCS,
        'INTERNAL_TOKENS': INTERNAL_TOKENS,
        'CheckResult': CheckResult,
        'rel': rel,
        'add': add,
        'iter_files': iter_files,
        'load_json': load_json,
        'read_optional_text': read_optional_text,
        'file_sha256': file_sha256,
        'critical_runtime_file_path': critical_runtime_file_path,
        'is_sha256': is_sha256,
        'unity_editor_using_is_guarded': unity_editor_using_is_guarded,
        'check_package_metadata': check_package_metadata,
        'check_required_files': check_required_files,
        'check_ros2cs_metadata_portability': check_ros2cs_metadata_portability,
        'check_message_package_floor': check_message_package_floor,
        'check_runtime_manifest': check_runtime_manifest,
        'inventory_category_counts_match': inventory_category_counts_match,
        'fresh_project_acceptance_status': fresh_project_acceptance_status,
        'published_runtime_is_not_prototype': published_runtime_is_not_prototype,
        'fresh_project_acceptance_passed': fresh_project_acceptance_passed,
        '_manifest_without_acceptance': _manifest_without_acceptance,
        'acceptance_commit_is_ancestor_and_package_unchanged': acceptance_commit_is_ancestor_and_package_unchanged,
        'fresh_project_acceptance_binding_matches_manifest': fresh_project_acceptance_binding_matches_manifest,
        'fresh_project_acceptance_record_is_valid': fresh_project_acceptance_record_is_valid,
        'check_inventory': check_inventory,
        'check_runtime_files': check_runtime_files,
        'check_package_path_patch': check_package_path_patch,
        'check_runtime_asmdef': check_runtime_asmdef,
        'check_runtime_source_patches': check_runtime_source_patches,
        'check_generator_alignment': check_generator_alignment,
        'check_public_docs': check_public_docs,
        'check_package_boundaries': check_package_boundaries,
        'core_runtime_has_forbidden_tokens': core_runtime_has_forbidden_tokens,
        'run_checks': run_checks,
        'print_results': print_results,
        'parse_args': parse_args,
        'main': main,
    }, _WRAPPERS)

__all__ = ['json', 'argparse', 'os', 'hashlib', 're', 'sys', 'subprocess', 'ElementTree', 'dataclass', 'Path', 'PurePosixPath', 'Iterable', 'REPO_ROOT_PARENT_DEPTH', 'EXIT_SUCCESS', 'EXIT_FAILURE', 'ROOT', 'PACKAGE_NAME', 'PACKAGE', 'ADAPTER_PACKAGE', 'CORE_PACKAGE', 'RUNTIME_ROOT', 'PLUGIN_ROOT', 'MANIFEST', 'INVENTORY', 'ARTIFACT_NAME', 'EXPECTED_RMW_IMPLEMENTATION', 'CRITICAL_DLLS', 'BASELINE_MESSAGE_PACKAGES', 'HUMBLE_HANDOFF_FAMILIES', 'HUMBLE_HANDOFF_DLLS', 'MODIFICATIONS_COPYRIGHT', 'LOCAL_PATCH_MARKER', 'LEAKY_UPSTREAM_EXAMPLES', 'PATCHED_VENDOR_FILES', 'PUBLIC_DOCS', 'INTERNAL_TOKENS', 'CheckResult', 'rel', 'add', 'iter_files', 'load_json', 'read_optional_text', 'file_sha256', 'critical_runtime_file_path', 'is_sha256', 'unity_editor_using_is_guarded', 'check_package_metadata', 'check_required_files', 'check_ros2cs_metadata_portability', 'check_message_package_floor', 'check_runtime_manifest', 'inventory_category_counts_match', 'fresh_project_acceptance_status', 'published_runtime_is_not_prototype', 'fresh_project_acceptance_passed', '_manifest_without_acceptance', 'acceptance_commit_is_ancestor_and_package_unchanged', 'fresh_project_acceptance_binding_matches_manifest', 'fresh_project_acceptance_record_is_valid', 'check_inventory', 'check_runtime_files', 'check_package_path_patch', 'check_runtime_asmdef', 'check_runtime_source_patches', 'check_generator_alignment', 'check_public_docs', 'check_package_boundaries', 'core_runtime_has_forbidden_tokens', 'run_checks', 'print_results', 'parse_args', 'main']
