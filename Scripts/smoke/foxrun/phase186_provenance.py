#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Fail-closed Phase186 reference provenance and SDK ROS inventory validation."""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import pathlib
import re
import subprocess
import sys
from collections.abc import Iterable, Mapping, Sequence


_REFERENCE_REMOTE = "https://github.com/Unity-Technologies/ROS-TCP-Connector.git"
_CLASSIFICATIONS = {"original", "inspired", "materially_copied"}
_OVERLAP_LINE_COUNT = 4
_OVERLAP_MIN_CHARS = 120


def _normal_path(value: str) -> str:
    return value.replace("\\", "/").strip("/")


def path_inventory_digest(paths: Iterable[str]) -> str:
    """Hash one canonical sorted path inventory."""

    canonical = sorted({_normal_path(path) for path in paths if _normal_path(path)})
    payload = "".join(path + "\n" for path in canonical).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _subprocess_lines(command: Sequence[str], cwd: pathlib.Path) -> list[str]:
    completed = subprocess.run(
        list(command),
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout).strip()
        raise RuntimeError(detail or f"command failed with exit code {completed.returncode}")
    return [line.strip() for line in completed.stdout.splitlines() if line.strip()]


def _substantial_lines(source: str) -> list[str]:
    lines: list[str] = []
    for raw in source.splitlines():
        line = re.sub(r"\s+", " ", raw.strip())
        if (
            not line
            or line in {"{", "}", "};"}
            or line.startswith("//")
            or line.startswith("using ")
            or line.startswith("namespace ")
            or "SPDX-License-Identifier:" in line
            or "Copyright (c)" in line
        ):
            continue
        lines.append(line)
    return lines


def _distinctive_windows(source: str) -> set[tuple[str, ...]]:
    lines = _substantial_lines(source)
    windows: set[tuple[str, ...]] = set()
    for index in range(0, len(lines) - _OVERLAP_LINE_COUNT + 1):
        window = tuple(lines[index : index + _OVERLAP_LINE_COUNT])
        if sum(len(line) for line in window) >= _OVERLAP_MIN_CHARS:
            windows.add(window)
    return windows


def validate_ledger_payload(
    payload: Mapping[str, object],
    *,
    actual_revision: str,
    implementation_sources: Mapping[str, str],
    reference_sources: Mapping[str, str],
) -> list[str]:
    """Validate one already-loaded ledger and its bounded source corpus."""

    errors: list[str] = []
    if payload.get("schemaVersion") != 1:
        errors.append("provenance schemaVersion must be exactly 1")

    reference = payload.get("reference")
    if not isinstance(reference, Mapping):
        return errors + ["provenance reference must be an object"]
    expected_revision = reference.get("revision")
    if expected_revision != actual_revision:
        errors.append(
            "reference revision mismatch: "
            f"expected {expected_revision!r}, observed {actual_revision!r}"
        )
    if reference.get("repository") != _REFERENCE_REMOTE:
        errors.append("reference repository must be the official ROS-TCP-Connector URL")
    if reference.get("license") != "Apache-2.0":
        errors.append("reference license must be Apache-2.0")

    inspected = reference.get("inspectedFiles")
    if (
        not isinstance(inspected, list)
        or not inspected
        or any(not isinstance(path, str) or not path for path in inspected)
    ):
        errors.append("reference inspectedFiles must be a non-empty string list")
        inspected = []
    expected_reference_paths = {_normal_path(path) for path in inspected}
    observed_reference_paths = {
        _normal_path(path) for path in reference_sources.keys()
    }
    missing_reference = sorted(expected_reference_paths - observed_reference_paths)
    if missing_reference:
        errors.append(
            "missing inspected reference files: " + ", ".join(missing_reference)
        )

    implementations = payload.get("implementations")
    if not isinstance(implementations, list) or not implementations:
        return errors + ["provenance implementations must be a non-empty list"]

    records: dict[str, Mapping[str, object]] = {}
    for index, raw_record in enumerate(implementations):
        if not isinstance(raw_record, Mapping):
            errors.append(f"implementation record {index} must be an object")
            continue
        path_value = raw_record.get("path")
        if not isinstance(path_value, str) or not path_value:
            errors.append(f"implementation record {index} has no path")
            continue
        path = _normal_path(path_value)
        if path in records:
            errors.append(f"duplicate implementation provenance path: {path}")
            continue
        records[path] = raw_record
        classification = raw_record.get("classification")
        if classification not in _CLASSIFICATIONS:
            errors.append(f"{path}: unknown classification {classification!r}")
        influence = raw_record.get("influence")
        if not isinstance(influence, str) or not influence.strip():
            errors.append(f"{path}: influence must be non-empty")
        if classification in {"inspired", "materially_copied"}:
            references = raw_record.get("referenceFiles")
            if (
                not isinstance(references, list)
                or not references
                or any(_normal_path(str(item)) not in expected_reference_paths for item in references)
            ):
                errors.append(f"{path}: referenceFiles must name inspected upstream files")
        if classification == "materially_copied":
            notice = raw_record.get("licenseNotice")
            if not isinstance(notice, str) or not notice.strip():
                errors.append(f"{path}: materially_copied content requires licenseNotice")

    observed_implementation_paths = {
        _normal_path(path) for path in implementation_sources.keys()
    }
    missing_implementation = sorted(set(records) - observed_implementation_paths)
    if missing_implementation:
        errors.append(
            "missing implementation files: " + ", ".join(missing_implementation)
        )

    reference_windows: dict[tuple[str, ...], str] = {}
    for reference_path, source in reference_sources.items():
        for window in _distinctive_windows(source):
            reference_windows.setdefault(window, _normal_path(reference_path))

    for implementation_path, source in implementation_sources.items():
        path = _normal_path(implementation_path)
        record = records.get(path)
        if record is None:
            errors.append(f"implementation file has no provenance record: {path}")
            continue
        if record.get("classification") == "materially_copied":
            continue
        for window in _distinctive_windows(source):
            reference_path = reference_windows.get(window)
            if reference_path is not None:
                errors.append(
                    f"{path}: unexplained distinctive overlap with {reference_path}"
                )
                break

    return errors


def _read_json(path: pathlib.Path) -> Mapping[str, object]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError(f"{path} must contain one JSON object")
    return payload


def validate_repository_provenance(
    repository: pathlib.Path,
    reference_root: pathlib.Path,
    ledger_path: pathlib.Path,
) -> list[str]:
    """Validate the checked-out official reference and every ledgered implementation."""

    repository = repository.resolve()
    reference_root = reference_root.resolve()
    ledger_path = ledger_path.resolve()
    errors: list[str] = []
    try:
        payload = _read_json(ledger_path)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [f"could not read provenance ledger: {exc}"]

    try:
        revision_lines = _subprocess_lines(
            ["git", "rev-parse", "HEAD"],
            reference_root,
        )
        actual_revision = revision_lines[0]
        dirty = _subprocess_lines(
            ["git", "status", "--porcelain=v1", "--untracked-files=no"],
            reference_root,
        )
        if dirty:
            errors.append("reference checkout has tracked modifications")
        remotes = _subprocess_lines(
            ["git", "remote", "get-url", "origin"],
            reference_root,
        )
        if not remotes or remotes[0] != _REFERENCE_REMOTE:
            errors.append("reference origin is not the official ROS-TCP-Connector URL")
    except (OSError, RuntimeError, IndexError) as exc:
        return errors + [f"could not inspect reference checkout: {exc}"]

    license_path = reference_root / "LICENSE"
    try:
        license_text = license_path.read_text(encoding="utf-8")
        if "Apache License" not in license_text or "Version 2.0" not in license_text:
            errors.append("reference LICENSE is not the recorded Apache-2.0 text")
    except OSError as exc:
        errors.append(f"could not read reference LICENSE: {exc}")

    reference = payload.get("reference")
    inspected = reference.get("inspectedFiles", []) if isinstance(reference, Mapping) else []
    reference_sources: dict[str, str] = {}
    for relative in inspected if isinstance(inspected, list) else []:
        if not isinstance(relative, str):
            continue
        try:
            reference_sources[_normal_path(relative)] = (
                reference_root / pathlib.PurePosixPath(relative)
            ).read_text(encoding="utf-8")
        except OSError as exc:
            errors.append(f"could not read inspected reference file {relative}: {exc}")

    implementations = payload.get("implementations")
    implementation_sources: dict[str, str] = {}
    for record in implementations if isinstance(implementations, list) else []:
        if not isinstance(record, Mapping) or not isinstance(record.get("path"), str):
            continue
        relative = _normal_path(str(record["path"]))
        try:
            implementation_sources[relative] = (
                repository / pathlib.PurePosixPath(relative)
            ).read_text(encoding="utf-8")
        except OSError as exc:
            errors.append(f"could not read implementation file {relative}: {exc}")

    errors.extend(
        validate_ledger_payload(
            payload,
            actual_revision=actual_revision,
            implementation_sources=implementation_sources,
            reference_sources=reference_sources,
        )
    )
    return errors


def _tracked_paths(repository: pathlib.Path) -> list[str]:
    completed = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=repository,
        check=False,
        capture_output=True,
    )
    if completed.returncode != 0:
        detail = completed.stderr.decode("utf-8", errors="replace").strip()
        raise RuntimeError(detail or "git ls-files failed")
    return [
        _normal_path(raw.decode("utf-8", errors="strict"))
        for raw in completed.stdout.split(b"\0")
        if raw
    ]


def _scope_paths(all_paths: Sequence[str], scope: Mapping[str, object]) -> list[str]:
    prefixes = scope.get("prefixes", [])
    exact_paths = scope.get("exactPaths", [])
    globs = scope.get("globs", [])
    if not all(isinstance(value, list) for value in (prefixes, exact_paths, globs)):
        raise ValueError("inventory selectors must be arrays")
    normalized_prefixes = tuple(
        _normal_path(str(prefix)).rstrip("/") + "/" for prefix in prefixes
    )
    normalized_exact = {_normal_path(str(path)) for path in exact_paths}
    normalized_globs = [_normal_path(str(pattern)) for pattern in globs]
    return sorted(
        path
        for path in all_paths
        if path in normalized_exact
        or any(path.startswith(prefix) for prefix in normalized_prefixes)
        or any(fnmatch.fnmatchcase(path, pattern) for pattern in normalized_globs)
    )


def validate_pre_move_inventory(
    repository: pathlib.Path,
    inventory_path: pathlib.Path,
) -> list[str]:
    """Validate compact exact path counts and hashes for every pre-move ROS scope."""

    repository = repository.resolve()
    try:
        payload = _read_json(inventory_path.resolve())
        all_paths = _tracked_paths(repository)
    except (OSError, RuntimeError, ValueError, json.JSONDecodeError) as exc:
        return [f"could not read pre-move inventory: {exc}"]
    errors: list[str] = []
    if payload.get("schemaVersion") != 1:
        errors.append("pre-move inventory schemaVersion must be exactly 1")
    scopes = payload.get("scopes")
    if not isinstance(scopes, list) or not scopes:
        return errors + ["pre-move inventory scopes must be a non-empty list"]

    union: set[str] = set()
    for index, raw_scope in enumerate(scopes):
        if not isinstance(raw_scope, Mapping):
            errors.append(f"inventory scope {index} must be an object")
            continue
        scope_id = raw_scope.get("id")
        if not isinstance(scope_id, str) or not scope_id:
            errors.append(f"inventory scope {index} has no id")
            scope_id = str(index)
        try:
            paths = _scope_paths(all_paths, raw_scope)
        except ValueError as exc:
            errors.append(f"{scope_id}: {exc}")
            continue
        union.update(paths)
        actual_digest = path_inventory_digest(paths)
        actual_count = len(paths)
        if raw_scope.get("pathCount") != actual_count:
            errors.append(
                f"{scope_id}: pathCount mismatch; "
                f"expected {raw_scope.get('pathCount')!r}, observed {actual_count}"
            )
        if raw_scope.get("pathDigestSha256") != actual_digest:
            errors.append(
                f"{scope_id}: path digest mismatch; observed {actual_digest}"
            )
        if raw_scope.get("action") not in {
            "move_to_bridge",
            "move_to_r2fu",
            "split_between_providers",
            "delete_from_sdk",
        }:
            errors.append(f"{scope_id}: action is not a recognized extraction action")

    actual_union_digest = path_inventory_digest(union)
    if payload.get("totalPathCount") != len(union):
        errors.append(
            "totalPathCount mismatch; "
            f"expected {payload.get('totalPathCount')!r}, observed {len(union)}"
        )
    if payload.get("totalPathDigestSha256") != actual_union_digest:
        errors.append(
            "total path digest mismatch; " f"observed {actual_union_digest}"
        )
    return errors


def _default_paths(repository: pathlib.Path) -> tuple[pathlib.Path, pathlib.Path, pathlib.Path]:
    return (
        repository / "third-party" / "ROS-TCP-Connector",
        repository
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "PROVENANCE.json",
        repository
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Tests"
        / "Unit"
        / "Phase186"
        / "Fixtures"
        / "pre_move_sdk_ros_inventory.json",
    )


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=pathlib.Path, default=pathlib.Path.cwd())
    parser.add_argument("--reference-root", type=pathlib.Path)
    parser.add_argument("--ledger", type=pathlib.Path)
    parser.add_argument("--inventory", type=pathlib.Path)
    arguments = parser.parse_args(argv)
    repository = arguments.repository.resolve()
    default_reference, default_ledger, default_inventory = _default_paths(repository)
    errors = validate_repository_provenance(
        repository,
        arguments.reference_root or default_reference,
        arguments.ledger or default_ledger,
    )
    errors.extend(
        validate_pre_move_inventory(
            repository,
            arguments.inventory or default_inventory,
        )
    )
    if errors:
        for error in errors:
            print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("PASS: Phase186 provenance and pre-move SDK ROS inventory are exact.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
