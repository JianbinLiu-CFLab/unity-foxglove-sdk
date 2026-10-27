from __future__ import annotations

import importlib as _importlib

_foundation = _importlib.import_module(f"{__package__}.checks_foundation")
_runtime = _importlib.import_module(f"{__package__}.checks_runtime")

annotations = _foundation.annotations
json = _foundation.json
argparse = _foundation.argparse
os = _foundation.os
hashlib = _foundation.hashlib
re = _foundation.re
sys = _foundation.sys
subprocess = _foundation.subprocess
ElementTree = _foundation.ElementTree
dataclass = _foundation.dataclass
Path = _foundation.Path
PurePosixPath = _foundation.PurePosixPath
Iterable = _foundation.Iterable
REPO_ROOT_PARENT_DEPTH = _foundation.REPO_ROOT_PARENT_DEPTH
EXIT_SUCCESS = _foundation.EXIT_SUCCESS
EXIT_FAILURE = _foundation.EXIT_FAILURE
ROOT = _foundation.ROOT
PACKAGE_NAME = _foundation.PACKAGE_NAME
PACKAGE = _foundation.PACKAGE
ADAPTER_PACKAGE = _foundation.ADAPTER_PACKAGE
CORE_PACKAGE = _foundation.CORE_PACKAGE
RUNTIME_ROOT = _foundation.RUNTIME_ROOT
PLUGIN_ROOT = _foundation.PLUGIN_ROOT
MANIFEST = _foundation.MANIFEST
INVENTORY = _foundation.INVENTORY
ARTIFACT_NAME = _foundation.ARTIFACT_NAME
EXPECTED_ARTIFACT_SHA256 = _foundation.EXPECTED_ARTIFACT_SHA256
EXPECTED_RMW_IMPLEMENTATION = _foundation.EXPECTED_RMW_IMPLEMENTATION
CRITICAL_DLLS = _foundation.CRITICAL_DLLS
PHASE161_ADDED_DLLS = _foundation.PHASE161_ADDED_DLLS
PHASE161_SUPPLEMENTAL_RUNTIME_DLLS = _foundation.PHASE161_SUPPLEMENTAL_RUNTIME_DLLS
V083_EXCLUDED_TEST_TYPESUPPORT_DLLS = _foundation.V083_EXCLUDED_TEST_TYPESUPPORT_DLLS
PHASE161_ASSET_CRITICAL_BASELINE = _foundation.PHASE161_ASSET_CRITICAL_BASELINE
MODIFICATIONS_COPYRIGHT = _foundation.MODIFICATIONS_COPYRIGHT
LOCAL_PATCH_MARKER = _foundation.LOCAL_PATCH_MARKER
LEAKY_UPSTREAM_EXAMPLES = _foundation.LEAKY_UPSTREAM_EXAMPLES
PATCHED_VENDOR_FILES = _foundation.PATCHED_VENDOR_FILES
PUBLIC_DOCS = _foundation.PUBLIC_DOCS
INTERNAL_TOKENS = _foundation.INTERNAL_TOKENS
CheckResult = _foundation.CheckResult
rel = _foundation.rel
add = _foundation.add
iter_files = _foundation.iter_files
load_json = _foundation.load_json
read_optional_text = _foundation.read_optional_text
file_sha256 = _foundation.file_sha256
is_sha256 = _foundation.is_sha256
unity_editor_using_is_guarded = _foundation.unity_editor_using_is_guarded
check_package_metadata = _foundation.check_package_metadata
check_required_files = _foundation.check_required_files
check_ros2cs_metadata_portability = _foundation.check_ros2cs_metadata_portability
check_runtime_manifest = _foundation.check_runtime_manifest
inventory_category_counts_match = _foundation.inventory_category_counts_match
fresh_project_acceptance_status = _foundation.fresh_project_acceptance_status
published_runtime_is_not_prototype = _foundation.published_runtime_is_not_prototype
fresh_project_acceptance_passed = _foundation.fresh_project_acceptance_passed
_manifest_without_acceptance = _foundation._manifest_without_acceptance
acceptance_commit_is_ancestor_and_package_unchanged = _foundation.acceptance_commit_is_ancestor_and_package_unchanged
fresh_project_acceptance_binding_matches_manifest = _foundation.fresh_project_acceptance_binding_matches_manifest
fresh_project_acceptance_record_is_valid = _foundation.fresh_project_acceptance_record_is_valid
check_inventory = _foundation.check_inventory
check_generator_alignment = _runtime.check_generator_alignment
check_package_boundaries = _runtime.check_package_boundaries
check_package_path_patch = _runtime.check_package_path_patch
check_public_docs = _runtime.check_public_docs
check_runtime_asmdef = _runtime.check_runtime_asmdef
check_runtime_files = _runtime.check_runtime_files
check_runtime_source_patches = _runtime.check_runtime_source_patches
core_runtime_has_forbidden_tokens = _runtime.core_runtime_has_forbidden_tokens
main = _runtime.main
parse_args = _runtime.parse_args
print_results = _runtime.print_results
run_checks = _runtime.run_checks

_EXPORT_NAMES = ('ADAPTER_PACKAGE', 'ARTIFACT_NAME', 'CORE_PACKAGE', 'CRITICAL_DLLS', 'CheckResult', 'EXIT_FAILURE', 'EXIT_SUCCESS', 'EXPECTED_ARTIFACT_SHA256', 'EXPECTED_RMW_IMPLEMENTATION', 'ElementTree', 'INTERNAL_TOKENS', 'INVENTORY', 'Iterable', 'LEAKY_UPSTREAM_EXAMPLES', 'LOCAL_PATCH_MARKER', 'MANIFEST', 'MODIFICATIONS_COPYRIGHT', 'PACKAGE', 'PACKAGE_NAME', 'PATCHED_VENDOR_FILES', 'PHASE161_ADDED_DLLS', 'PHASE161_ASSET_CRITICAL_BASELINE', 'PHASE161_SUPPLEMENTAL_RUNTIME_DLLS', 'PLUGIN_ROOT', 'PUBLIC_DOCS', 'Path', 'PurePosixPath', 'REPO_ROOT_PARENT_DEPTH', 'ROOT', 'RUNTIME_ROOT', 'V083_EXCLUDED_TEST_TYPESUPPORT_DLLS', '_manifest_without_acceptance', 'acceptance_commit_is_ancestor_and_package_unchanged', 'add', 'annotations', 'argparse', 'check_generator_alignment', 'check_inventory', 'check_package_boundaries', 'check_package_metadata', 'check_package_path_patch', 'check_public_docs', 'check_required_files', 'check_ros2cs_metadata_portability', 'check_runtime_asmdef', 'check_runtime_files', 'check_runtime_manifest', 'check_runtime_source_patches', 'core_runtime_has_forbidden_tokens', 'dataclass', 'file_sha256', 'fresh_project_acceptance_binding_matches_manifest', 'fresh_project_acceptance_passed', 'fresh_project_acceptance_record_is_valid', 'fresh_project_acceptance_status', 'hashlib', 'inventory_category_counts_match', 'is_sha256', 'iter_files', 'json', 'load_json', 'main', 'os', 'parse_args', 'print_results', 'published_runtime_is_not_prototype', 're', 'read_optional_text', 'rel', 'run_checks', 'subprocess', 'sys', 'unity_editor_using_is_guarded')

_ORIGINAL_SECTION_VALUES = {
    _name: globals().get(_name)
    for _name in _EXPORT_NAMES
}

def sync(source: dict[str, object], wrappers: dict[str, object]) -> None:
    """Mirror facade state into both implementation sections."""
    global ADAPTER_PACKAGE, ARTIFACT_NAME, CORE_PACKAGE, CRITICAL_DLLS, CheckResult, EXIT_FAILURE, EXIT_SUCCESS, EXPECTED_ARTIFACT_SHA256, EXPECTED_RMW_IMPLEMENTATION, ElementTree, INTERNAL_TOKENS, INVENTORY, Iterable, LEAKY_UPSTREAM_EXAMPLES, LOCAL_PATCH_MARKER, MANIFEST, MODIFICATIONS_COPYRIGHT, PACKAGE, PACKAGE_NAME, PATCHED_VENDOR_FILES, PHASE161_ADDED_DLLS, PHASE161_ASSET_CRITICAL_BASELINE, PHASE161_SUPPLEMENTAL_RUNTIME_DLLS, PLUGIN_ROOT, PUBLIC_DOCS, Path, PurePosixPath, REPO_ROOT_PARENT_DEPTH, ROOT, RUNTIME_ROOT, V083_EXCLUDED_TEST_TYPESUPPORT_DLLS, _manifest_without_acceptance, acceptance_commit_is_ancestor_and_package_unchanged, add, annotations, argparse, check_generator_alignment, check_inventory, check_package_boundaries, check_package_metadata, check_package_path_patch, check_public_docs, check_required_files, check_ros2cs_metadata_portability, check_runtime_asmdef, check_runtime_files, check_runtime_manifest, check_runtime_source_patches, core_runtime_has_forbidden_tokens, dataclass, file_sha256, fresh_project_acceptance_binding_matches_manifest, fresh_project_acceptance_passed, fresh_project_acceptance_record_is_valid, fresh_project_acceptance_status, hashlib, inventory_category_counts_match, is_sha256, iter_files, json, load_json, main, os, parse_args, print_results, published_runtime_is_not_prototype, re, read_optional_text, rel, run_checks, subprocess, sys, unity_editor_using_is_guarded
    if "ADAPTER_PACKAGE" not in wrappers or source["ADAPTER_PACKAGE"] is not wrappers.get("ADAPTER_PACKAGE"):
        ADAPTER_PACKAGE = source["ADAPTER_PACKAGE"]
        _foundation.ADAPTER_PACKAGE = source["ADAPTER_PACKAGE"]
        _runtime.ADAPTER_PACKAGE = source["ADAPTER_PACKAGE"]
    if "ARTIFACT_NAME" not in wrappers or source["ARTIFACT_NAME"] is not wrappers.get("ARTIFACT_NAME"):
        ARTIFACT_NAME = source["ARTIFACT_NAME"]
        _foundation.ARTIFACT_NAME = source["ARTIFACT_NAME"]
        _runtime.ARTIFACT_NAME = source["ARTIFACT_NAME"]
    if "CORE_PACKAGE" not in wrappers or source["CORE_PACKAGE"] is not wrappers.get("CORE_PACKAGE"):
        CORE_PACKAGE = source["CORE_PACKAGE"]
        _foundation.CORE_PACKAGE = source["CORE_PACKAGE"]
        _runtime.CORE_PACKAGE = source["CORE_PACKAGE"]
    if "CRITICAL_DLLS" not in wrappers or source["CRITICAL_DLLS"] is not wrappers.get("CRITICAL_DLLS"):
        CRITICAL_DLLS = source["CRITICAL_DLLS"]
        _foundation.CRITICAL_DLLS = source["CRITICAL_DLLS"]
        _runtime.CRITICAL_DLLS = source["CRITICAL_DLLS"]
    if "CheckResult" not in wrappers or source["CheckResult"] is not wrappers.get("CheckResult"):
        CheckResult = source["CheckResult"]
        _foundation.CheckResult = source["CheckResult"]
        _runtime.CheckResult = source["CheckResult"]
    if "EXIT_FAILURE" not in wrappers or source["EXIT_FAILURE"] is not wrappers.get("EXIT_FAILURE"):
        EXIT_FAILURE = source["EXIT_FAILURE"]
        _foundation.EXIT_FAILURE = source["EXIT_FAILURE"]
        _runtime.EXIT_FAILURE = source["EXIT_FAILURE"]
    if "EXIT_SUCCESS" not in wrappers or source["EXIT_SUCCESS"] is not wrappers.get("EXIT_SUCCESS"):
        EXIT_SUCCESS = source["EXIT_SUCCESS"]
        _foundation.EXIT_SUCCESS = source["EXIT_SUCCESS"]
        _runtime.EXIT_SUCCESS = source["EXIT_SUCCESS"]
    if "EXPECTED_ARTIFACT_SHA256" not in wrappers or source["EXPECTED_ARTIFACT_SHA256"] is not wrappers.get("EXPECTED_ARTIFACT_SHA256"):
        EXPECTED_ARTIFACT_SHA256 = source["EXPECTED_ARTIFACT_SHA256"]
        _foundation.EXPECTED_ARTIFACT_SHA256 = source["EXPECTED_ARTIFACT_SHA256"]
        _runtime.EXPECTED_ARTIFACT_SHA256 = source["EXPECTED_ARTIFACT_SHA256"]
    if "EXPECTED_RMW_IMPLEMENTATION" not in wrappers or source["EXPECTED_RMW_IMPLEMENTATION"] is not wrappers.get("EXPECTED_RMW_IMPLEMENTATION"):
        EXPECTED_RMW_IMPLEMENTATION = source["EXPECTED_RMW_IMPLEMENTATION"]
        _foundation.EXPECTED_RMW_IMPLEMENTATION = source["EXPECTED_RMW_IMPLEMENTATION"]
        _runtime.EXPECTED_RMW_IMPLEMENTATION = source["EXPECTED_RMW_IMPLEMENTATION"]
    if "ElementTree" not in wrappers or source["ElementTree"] is not wrappers.get("ElementTree"):
        ElementTree = source["ElementTree"]
        _foundation.ElementTree = source["ElementTree"]
        _runtime.ElementTree = source["ElementTree"]
    if "INTERNAL_TOKENS" not in wrappers or source["INTERNAL_TOKENS"] is not wrappers.get("INTERNAL_TOKENS"):
        INTERNAL_TOKENS = source["INTERNAL_TOKENS"]
        _foundation.INTERNAL_TOKENS = source["INTERNAL_TOKENS"]
        _runtime.INTERNAL_TOKENS = source["INTERNAL_TOKENS"]
    if "INVENTORY" not in wrappers or source["INVENTORY"] is not wrappers.get("INVENTORY"):
        INVENTORY = source["INVENTORY"]
        _foundation.INVENTORY = source["INVENTORY"]
        _runtime.INVENTORY = source["INVENTORY"]
    if "Iterable" not in wrappers or source["Iterable"] is not wrappers.get("Iterable"):
        Iterable = source["Iterable"]
        _foundation.Iterable = source["Iterable"]
        _runtime.Iterable = source["Iterable"]
    if "LEAKY_UPSTREAM_EXAMPLES" not in wrappers or source["LEAKY_UPSTREAM_EXAMPLES"] is not wrappers.get("LEAKY_UPSTREAM_EXAMPLES"):
        LEAKY_UPSTREAM_EXAMPLES = source["LEAKY_UPSTREAM_EXAMPLES"]
        _foundation.LEAKY_UPSTREAM_EXAMPLES = source["LEAKY_UPSTREAM_EXAMPLES"]
        _runtime.LEAKY_UPSTREAM_EXAMPLES = source["LEAKY_UPSTREAM_EXAMPLES"]
    if "LOCAL_PATCH_MARKER" not in wrappers or source["LOCAL_PATCH_MARKER"] is not wrappers.get("LOCAL_PATCH_MARKER"):
        LOCAL_PATCH_MARKER = source["LOCAL_PATCH_MARKER"]
        _foundation.LOCAL_PATCH_MARKER = source["LOCAL_PATCH_MARKER"]
        _runtime.LOCAL_PATCH_MARKER = source["LOCAL_PATCH_MARKER"]
    if "MANIFEST" not in wrappers or source["MANIFEST"] is not wrappers.get("MANIFEST"):
        MANIFEST = source["MANIFEST"]
        _foundation.MANIFEST = source["MANIFEST"]
        _runtime.MANIFEST = source["MANIFEST"]
    if "MODIFICATIONS_COPYRIGHT" not in wrappers or source["MODIFICATIONS_COPYRIGHT"] is not wrappers.get("MODIFICATIONS_COPYRIGHT"):
        MODIFICATIONS_COPYRIGHT = source["MODIFICATIONS_COPYRIGHT"]
        _foundation.MODIFICATIONS_COPYRIGHT = source["MODIFICATIONS_COPYRIGHT"]
        _runtime.MODIFICATIONS_COPYRIGHT = source["MODIFICATIONS_COPYRIGHT"]
    if "PACKAGE" not in wrappers or source["PACKAGE"] is not wrappers.get("PACKAGE"):
        PACKAGE = source["PACKAGE"]
        _foundation.PACKAGE = source["PACKAGE"]
        _runtime.PACKAGE = source["PACKAGE"]
    if "PACKAGE_NAME" not in wrappers or source["PACKAGE_NAME"] is not wrappers.get("PACKAGE_NAME"):
        PACKAGE_NAME = source["PACKAGE_NAME"]
        _foundation.PACKAGE_NAME = source["PACKAGE_NAME"]
        _runtime.PACKAGE_NAME = source["PACKAGE_NAME"]
    if "PATCHED_VENDOR_FILES" not in wrappers or source["PATCHED_VENDOR_FILES"] is not wrappers.get("PATCHED_VENDOR_FILES"):
        PATCHED_VENDOR_FILES = source["PATCHED_VENDOR_FILES"]
        _foundation.PATCHED_VENDOR_FILES = source["PATCHED_VENDOR_FILES"]
        _runtime.PATCHED_VENDOR_FILES = source["PATCHED_VENDOR_FILES"]
    if "PHASE161_ADDED_DLLS" not in wrappers or source["PHASE161_ADDED_DLLS"] is not wrappers.get("PHASE161_ADDED_DLLS"):
        PHASE161_ADDED_DLLS = source["PHASE161_ADDED_DLLS"]
        _foundation.PHASE161_ADDED_DLLS = source["PHASE161_ADDED_DLLS"]
        _runtime.PHASE161_ADDED_DLLS = source["PHASE161_ADDED_DLLS"]
    if "PHASE161_ASSET_CRITICAL_BASELINE" not in wrappers or source["PHASE161_ASSET_CRITICAL_BASELINE"] is not wrappers.get("PHASE161_ASSET_CRITICAL_BASELINE"):
        PHASE161_ASSET_CRITICAL_BASELINE = source["PHASE161_ASSET_CRITICAL_BASELINE"]
        _foundation.PHASE161_ASSET_CRITICAL_BASELINE = source["PHASE161_ASSET_CRITICAL_BASELINE"]
        _runtime.PHASE161_ASSET_CRITICAL_BASELINE = source["PHASE161_ASSET_CRITICAL_BASELINE"]
    if "PHASE161_SUPPLEMENTAL_RUNTIME_DLLS" not in wrappers or source["PHASE161_SUPPLEMENTAL_RUNTIME_DLLS"] is not wrappers.get("PHASE161_SUPPLEMENTAL_RUNTIME_DLLS"):
        PHASE161_SUPPLEMENTAL_RUNTIME_DLLS = source["PHASE161_SUPPLEMENTAL_RUNTIME_DLLS"]
        _foundation.PHASE161_SUPPLEMENTAL_RUNTIME_DLLS = source["PHASE161_SUPPLEMENTAL_RUNTIME_DLLS"]
        _runtime.PHASE161_SUPPLEMENTAL_RUNTIME_DLLS = source["PHASE161_SUPPLEMENTAL_RUNTIME_DLLS"]
    if "PLUGIN_ROOT" not in wrappers or source["PLUGIN_ROOT"] is not wrappers.get("PLUGIN_ROOT"):
        PLUGIN_ROOT = source["PLUGIN_ROOT"]
        _foundation.PLUGIN_ROOT = source["PLUGIN_ROOT"]
        _runtime.PLUGIN_ROOT = source["PLUGIN_ROOT"]
    if "PUBLIC_DOCS" not in wrappers or source["PUBLIC_DOCS"] is not wrappers.get("PUBLIC_DOCS"):
        PUBLIC_DOCS = source["PUBLIC_DOCS"]
        _foundation.PUBLIC_DOCS = source["PUBLIC_DOCS"]
        _runtime.PUBLIC_DOCS = source["PUBLIC_DOCS"]
    if "Path" not in wrappers or source["Path"] is not wrappers.get("Path"):
        Path = source["Path"]
        _foundation.Path = source["Path"]
        _runtime.Path = source["Path"]
    if "PurePosixPath" not in wrappers or source["PurePosixPath"] is not wrappers.get("PurePosixPath"):
        PurePosixPath = source["PurePosixPath"]
        _foundation.PurePosixPath = source["PurePosixPath"]
        _runtime.PurePosixPath = source["PurePosixPath"]
    if "REPO_ROOT_PARENT_DEPTH" not in wrappers or source["REPO_ROOT_PARENT_DEPTH"] is not wrappers.get("REPO_ROOT_PARENT_DEPTH"):
        REPO_ROOT_PARENT_DEPTH = source["REPO_ROOT_PARENT_DEPTH"]
        _foundation.REPO_ROOT_PARENT_DEPTH = source["REPO_ROOT_PARENT_DEPTH"]
        _runtime.REPO_ROOT_PARENT_DEPTH = source["REPO_ROOT_PARENT_DEPTH"]
    if "ROOT" not in wrappers or source["ROOT"] is not wrappers.get("ROOT"):
        ROOT = source["ROOT"]
        _foundation.ROOT = source["ROOT"]
        _runtime.ROOT = source["ROOT"]
    if "RUNTIME_ROOT" not in wrappers or source["RUNTIME_ROOT"] is not wrappers.get("RUNTIME_ROOT"):
        RUNTIME_ROOT = source["RUNTIME_ROOT"]
        _foundation.RUNTIME_ROOT = source["RUNTIME_ROOT"]
        _runtime.RUNTIME_ROOT = source["RUNTIME_ROOT"]
    if "V083_EXCLUDED_TEST_TYPESUPPORT_DLLS" not in wrappers or source["V083_EXCLUDED_TEST_TYPESUPPORT_DLLS"] is not wrappers.get("V083_EXCLUDED_TEST_TYPESUPPORT_DLLS"):
        V083_EXCLUDED_TEST_TYPESUPPORT_DLLS = source["V083_EXCLUDED_TEST_TYPESUPPORT_DLLS"]
        _foundation.V083_EXCLUDED_TEST_TYPESUPPORT_DLLS = source["V083_EXCLUDED_TEST_TYPESUPPORT_DLLS"]
        _runtime.V083_EXCLUDED_TEST_TYPESUPPORT_DLLS = source["V083_EXCLUDED_TEST_TYPESUPPORT_DLLS"]
    if "_manifest_without_acceptance" not in wrappers or source["_manifest_without_acceptance"] is not wrappers.get("_manifest_without_acceptance"):
        _manifest_without_acceptance = source["_manifest_without_acceptance"]
        _foundation._manifest_without_acceptance = source["_manifest_without_acceptance"]
        _runtime._manifest_without_acceptance = source["_manifest_without_acceptance"]
    if "acceptance_commit_is_ancestor_and_package_unchanged" not in wrappers or source["acceptance_commit_is_ancestor_and_package_unchanged"] is not wrappers.get("acceptance_commit_is_ancestor_and_package_unchanged"):
        acceptance_commit_is_ancestor_and_package_unchanged = source["acceptance_commit_is_ancestor_and_package_unchanged"]
        _foundation.acceptance_commit_is_ancestor_and_package_unchanged = source["acceptance_commit_is_ancestor_and_package_unchanged"]
        _runtime.acceptance_commit_is_ancestor_and_package_unchanged = source["acceptance_commit_is_ancestor_and_package_unchanged"]
    if "add" not in wrappers or source["add"] is not wrappers.get("add"):
        add = source["add"]
        _foundation.add = source["add"]
        _runtime.add = source["add"]
    if "annotations" not in wrappers or source["annotations"] is not wrappers.get("annotations"):
        annotations = source["annotations"]
        _foundation.annotations = source["annotations"]
        _runtime.annotations = source["annotations"]
    if "argparse" not in wrappers or source["argparse"] is not wrappers.get("argparse"):
        argparse = source["argparse"]
        _foundation.argparse = source["argparse"]
        _runtime.argparse = source["argparse"]
    if "check_generator_alignment" not in wrappers or source["check_generator_alignment"] is not wrappers.get("check_generator_alignment"):
        check_generator_alignment = source["check_generator_alignment"]
        _foundation.check_generator_alignment = source["check_generator_alignment"]
        _runtime.check_generator_alignment = source["check_generator_alignment"]
    if "check_inventory" not in wrappers or source["check_inventory"] is not wrappers.get("check_inventory"):
        check_inventory = source["check_inventory"]
        _foundation.check_inventory = source["check_inventory"]
        _runtime.check_inventory = source["check_inventory"]
    if "check_package_boundaries" not in wrappers or source["check_package_boundaries"] is not wrappers.get("check_package_boundaries"):
        check_package_boundaries = source["check_package_boundaries"]
        _foundation.check_package_boundaries = source["check_package_boundaries"]
        _runtime.check_package_boundaries = source["check_package_boundaries"]
    if "check_package_metadata" not in wrappers or source["check_package_metadata"] is not wrappers.get("check_package_metadata"):
        check_package_metadata = source["check_package_metadata"]
        _foundation.check_package_metadata = source["check_package_metadata"]
        _runtime.check_package_metadata = source["check_package_metadata"]
    if "check_package_path_patch" not in wrappers or source["check_package_path_patch"] is not wrappers.get("check_package_path_patch"):
        check_package_path_patch = source["check_package_path_patch"]
        _foundation.check_package_path_patch = source["check_package_path_patch"]
        _runtime.check_package_path_patch = source["check_package_path_patch"]
    if "check_public_docs" not in wrappers or source["check_public_docs"] is not wrappers.get("check_public_docs"):
        check_public_docs = source["check_public_docs"]
        _foundation.check_public_docs = source["check_public_docs"]
        _runtime.check_public_docs = source["check_public_docs"]
    if "check_required_files" not in wrappers or source["check_required_files"] is not wrappers.get("check_required_files"):
        check_required_files = source["check_required_files"]
        _foundation.check_required_files = source["check_required_files"]
        _runtime.check_required_files = source["check_required_files"]
    if "check_ros2cs_metadata_portability" not in wrappers or source["check_ros2cs_metadata_portability"] is not wrappers.get("check_ros2cs_metadata_portability"):
        check_ros2cs_metadata_portability = source["check_ros2cs_metadata_portability"]
        _foundation.check_ros2cs_metadata_portability = source["check_ros2cs_metadata_portability"]
        _runtime.check_ros2cs_metadata_portability = source["check_ros2cs_metadata_portability"]
    if "check_runtime_asmdef" not in wrappers or source["check_runtime_asmdef"] is not wrappers.get("check_runtime_asmdef"):
        check_runtime_asmdef = source["check_runtime_asmdef"]
        _foundation.check_runtime_asmdef = source["check_runtime_asmdef"]
        _runtime.check_runtime_asmdef = source["check_runtime_asmdef"]
    if "check_runtime_files" not in wrappers or source["check_runtime_files"] is not wrappers.get("check_runtime_files"):
        check_runtime_files = source["check_runtime_files"]
        _foundation.check_runtime_files = source["check_runtime_files"]
        _runtime.check_runtime_files = source["check_runtime_files"]
    if "check_runtime_manifest" not in wrappers or source["check_runtime_manifest"] is not wrappers.get("check_runtime_manifest"):
        check_runtime_manifest = source["check_runtime_manifest"]
        _foundation.check_runtime_manifest = source["check_runtime_manifest"]
        _runtime.check_runtime_manifest = source["check_runtime_manifest"]
    if "check_runtime_source_patches" not in wrappers or source["check_runtime_source_patches"] is not wrappers.get("check_runtime_source_patches"):
        check_runtime_source_patches = source["check_runtime_source_patches"]
        _foundation.check_runtime_source_patches = source["check_runtime_source_patches"]
        _runtime.check_runtime_source_patches = source["check_runtime_source_patches"]
    if "core_runtime_has_forbidden_tokens" not in wrappers or source["core_runtime_has_forbidden_tokens"] is not wrappers.get("core_runtime_has_forbidden_tokens"):
        core_runtime_has_forbidden_tokens = source["core_runtime_has_forbidden_tokens"]
        _foundation.core_runtime_has_forbidden_tokens = source["core_runtime_has_forbidden_tokens"]
        _runtime.core_runtime_has_forbidden_tokens = source["core_runtime_has_forbidden_tokens"]
    if "dataclass" not in wrappers or source["dataclass"] is not wrappers.get("dataclass"):
        dataclass = source["dataclass"]
        _foundation.dataclass = source["dataclass"]
        _runtime.dataclass = source["dataclass"]
    if "file_sha256" not in wrappers or source["file_sha256"] is not wrappers.get("file_sha256"):
        file_sha256 = source["file_sha256"]
        _foundation.file_sha256 = source["file_sha256"]
        _runtime.file_sha256 = source["file_sha256"]
    if "fresh_project_acceptance_binding_matches_manifest" not in wrappers or source["fresh_project_acceptance_binding_matches_manifest"] is not wrappers.get("fresh_project_acceptance_binding_matches_manifest"):
        fresh_project_acceptance_binding_matches_manifest = source["fresh_project_acceptance_binding_matches_manifest"]
        _foundation.fresh_project_acceptance_binding_matches_manifest = source["fresh_project_acceptance_binding_matches_manifest"]
        _runtime.fresh_project_acceptance_binding_matches_manifest = source["fresh_project_acceptance_binding_matches_manifest"]
    if "fresh_project_acceptance_passed" not in wrappers or source["fresh_project_acceptance_passed"] is not wrappers.get("fresh_project_acceptance_passed"):
        fresh_project_acceptance_passed = source["fresh_project_acceptance_passed"]
        _foundation.fresh_project_acceptance_passed = source["fresh_project_acceptance_passed"]
        _runtime.fresh_project_acceptance_passed = source["fresh_project_acceptance_passed"]
    if "fresh_project_acceptance_record_is_valid" not in wrappers or source["fresh_project_acceptance_record_is_valid"] is not wrappers.get("fresh_project_acceptance_record_is_valid"):
        fresh_project_acceptance_record_is_valid = source["fresh_project_acceptance_record_is_valid"]
        _foundation.fresh_project_acceptance_record_is_valid = source["fresh_project_acceptance_record_is_valid"]
        _runtime.fresh_project_acceptance_record_is_valid = source["fresh_project_acceptance_record_is_valid"]
    if "fresh_project_acceptance_status" not in wrappers or source["fresh_project_acceptance_status"] is not wrappers.get("fresh_project_acceptance_status"):
        fresh_project_acceptance_status = source["fresh_project_acceptance_status"]
        _foundation.fresh_project_acceptance_status = source["fresh_project_acceptance_status"]
        _runtime.fresh_project_acceptance_status = source["fresh_project_acceptance_status"]
    if "hashlib" not in wrappers or source["hashlib"] is not wrappers.get("hashlib"):
        hashlib = source["hashlib"]
        _foundation.hashlib = source["hashlib"]
        _runtime.hashlib = source["hashlib"]
    if "inventory_category_counts_match" not in wrappers or source["inventory_category_counts_match"] is not wrappers.get("inventory_category_counts_match"):
        inventory_category_counts_match = source["inventory_category_counts_match"]
        _foundation.inventory_category_counts_match = source["inventory_category_counts_match"]
        _runtime.inventory_category_counts_match = source["inventory_category_counts_match"]
    if "is_sha256" not in wrappers or source["is_sha256"] is not wrappers.get("is_sha256"):
        is_sha256 = source["is_sha256"]
        _foundation.is_sha256 = source["is_sha256"]
        _runtime.is_sha256 = source["is_sha256"]
    if "iter_files" not in wrappers or source["iter_files"] is not wrappers.get("iter_files"):
        iter_files = source["iter_files"]
        _foundation.iter_files = source["iter_files"]
        _runtime.iter_files = source["iter_files"]
    if "json" not in wrappers or source["json"] is not wrappers.get("json"):
        json = source["json"]
        _foundation.json = source["json"]
        _runtime.json = source["json"]
    if "load_json" not in wrappers or source["load_json"] is not wrappers.get("load_json"):
        load_json = source["load_json"]
        _foundation.load_json = source["load_json"]
        _runtime.load_json = source["load_json"]
    if "main" not in wrappers or source["main"] is not wrappers.get("main"):
        main = source["main"]
        _foundation.main = source["main"]
        _runtime.main = source["main"]
    if "os" not in wrappers or source["os"] is not wrappers.get("os"):
        os = source["os"]
        _foundation.os = source["os"]
        _runtime.os = source["os"]
    if "parse_args" not in wrappers or source["parse_args"] is not wrappers.get("parse_args"):
        parse_args = source["parse_args"]
        _foundation.parse_args = source["parse_args"]
        _runtime.parse_args = source["parse_args"]
    if "print_results" not in wrappers or source["print_results"] is not wrappers.get("print_results"):
        print_results = source["print_results"]
        _foundation.print_results = source["print_results"]
        _runtime.print_results = source["print_results"]
    if "published_runtime_is_not_prototype" not in wrappers or source["published_runtime_is_not_prototype"] is not wrappers.get("published_runtime_is_not_prototype"):
        published_runtime_is_not_prototype = source["published_runtime_is_not_prototype"]
        _foundation.published_runtime_is_not_prototype = source["published_runtime_is_not_prototype"]
        _runtime.published_runtime_is_not_prototype = source["published_runtime_is_not_prototype"]
    if "re" not in wrappers or source["re"] is not wrappers.get("re"):
        re = source["re"]
        _foundation.re = source["re"]
        _runtime.re = source["re"]
    if "read_optional_text" not in wrappers or source["read_optional_text"] is not wrappers.get("read_optional_text"):
        read_optional_text = source["read_optional_text"]
        _foundation.read_optional_text = source["read_optional_text"]
        _runtime.read_optional_text = source["read_optional_text"]
    if "rel" not in wrappers or source["rel"] is not wrappers.get("rel"):
        rel = source["rel"]
        _foundation.rel = source["rel"]
        _runtime.rel = source["rel"]
    if "run_checks" not in wrappers or source["run_checks"] is not wrappers.get("run_checks"):
        run_checks = source["run_checks"]
        _foundation.run_checks = source["run_checks"]
        _runtime.run_checks = source["run_checks"]
    if "subprocess" not in wrappers or source["subprocess"] is not wrappers.get("subprocess"):
        subprocess = source["subprocess"]
        _foundation.subprocess = source["subprocess"]
        _runtime.subprocess = source["subprocess"]
    if "sys" not in wrappers or source["sys"] is not wrappers.get("sys"):
        sys = source["sys"]
        _foundation.sys = source["sys"]
        _runtime.sys = source["sys"]
    if "unity_editor_using_is_guarded" not in wrappers or source["unity_editor_using_is_guarded"] is not wrappers.get("unity_editor_using_is_guarded"):
        unity_editor_using_is_guarded = source["unity_editor_using_is_guarded"]
        _foundation.unity_editor_using_is_guarded = source["unity_editor_using_is_guarded"]
        _runtime.unity_editor_using_is_guarded = source["unity_editor_using_is_guarded"]

    for _name, _value in source.items():
        if _name in wrappers:
            _installed = (
                _ORIGINAL_SECTION_VALUES.get(_name)
                if _value is wrappers.get(_name)
                else _value
            )
            if _installed is not None:
                globals()[_name] = _installed
                _foundation.__dict__[_name] = _installed
                _runtime.__dict__[_name] = _installed
        else:
            globals()[_name] = _value

__all__ = ['ADAPTER_PACKAGE', 'ARTIFACT_NAME', 'CORE_PACKAGE', 'CRITICAL_DLLS', 'CheckResult', 'EXIT_FAILURE', 'EXIT_SUCCESS', 'EXPECTED_ARTIFACT_SHA256', 'EXPECTED_RMW_IMPLEMENTATION', 'ElementTree', 'INTERNAL_TOKENS', 'INVENTORY', 'Iterable', 'LEAKY_UPSTREAM_EXAMPLES', 'LOCAL_PATCH_MARKER', 'MANIFEST', 'MODIFICATIONS_COPYRIGHT', 'PACKAGE', 'PACKAGE_NAME', 'PATCHED_VENDOR_FILES', 'PHASE161_ADDED_DLLS', 'PHASE161_ASSET_CRITICAL_BASELINE', 'PHASE161_SUPPLEMENTAL_RUNTIME_DLLS', 'PLUGIN_ROOT', 'PUBLIC_DOCS', 'Path', 'PurePosixPath', 'REPO_ROOT_PARENT_DEPTH', 'ROOT', 'RUNTIME_ROOT', 'V083_EXCLUDED_TEST_TYPESUPPORT_DLLS', '_manifest_without_acceptance', 'acceptance_commit_is_ancestor_and_package_unchanged', 'add', 'annotations', 'argparse', 'check_generator_alignment', 'check_inventory', 'check_package_boundaries', 'check_package_metadata', 'check_package_path_patch', 'check_public_docs', 'check_required_files', 'check_ros2cs_metadata_portability', 'check_runtime_asmdef', 'check_runtime_files', 'check_runtime_manifest', 'check_runtime_source_patches', 'core_runtime_has_forbidden_tokens', 'dataclass', 'file_sha256', 'fresh_project_acceptance_binding_matches_manifest', 'fresh_project_acceptance_passed', 'fresh_project_acceptance_record_is_valid', 'fresh_project_acceptance_status', 'hashlib', 'inventory_category_counts_match', 'is_sha256', 'iter_files', 'json', 'load_json', 'main', 'os', 'parse_args', 'print_results', 'published_runtime_is_not_prototype', 're', 'read_optional_text', 'rel', 'run_checks', 'subprocess', 'sys', 'unity_editor_using_is_guarded']
