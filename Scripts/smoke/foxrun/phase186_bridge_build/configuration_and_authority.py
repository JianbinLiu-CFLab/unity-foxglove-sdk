#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke/foxrun
# Purpose: Reproducible Windows-native Phase186 Bridge and Phase181 overlay build entry.

"""Build the Bridge and its exact Phase181 custom-interface overlay per matrix row.

This command owns only ``build/phase186/bridge/<row>``.  Missing live Windows,
ROS, or MSVC prerequisites produce a machine-readable ``NOT RUN`` result;
tooling checks or cached output are never promoted to a live PASS.
"""

from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_build.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

import argparse
import contextlib
import dataclasses
import datetime as _datetime
import hashlib
import json
import os
import pathlib
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import time
from collections.abc import Mapping, Sequence

try:
    from Scripts.smoke.foxrun import phase184_profile_acceptance as process_support
except ImportError:  # Direct script execution from outside the repository root.
    import phase184_profile_acceptance as process_support


INTERFACE_TYPE = (
    "unity2foxglove_foxrun_interfaces_v1/msg/"
    "Phase181State48D288ED82F1Envelope"
)
INTERFACE_DIGEST = (
    "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd"
)
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
STANDARD_ROS_PACKAGE_NAME = "foxglove_msgs"
STANDARD_SCHEMA_TYPE = "foxglove_msgs/msg/Log"
STANDARD_SCHEMA_DIGEST = (
    "13566915f24162eab241ef8df32ed199c1c8748c2252b359b7bf0253cd866e44"
)
SUMMARY_SCHEMA_VERSION = 1
_SHA256 = re.compile(r"^[0-9a-f]{64}$")


class BridgeBuildFailure(RuntimeError):
    """Stable fail-closed build error."""


class LivePrerequisiteMissing(BridgeBuildFailure):
    """A named live prerequisite is not provisioned."""


@dataclasses.dataclass(frozen=True)
class BridgeRow:
    """One immutable maintained Windows ROS/RMW build row."""

    row_id: str
    distro: str
    rmw: str


ROWS: dict[str, BridgeRow] = {
    "humble-fastrtps": BridgeRow(
        "humble-fastrtps", "humble", "rmw_fastrtps_cpp"
    ),
    "jazzy-fastrtps": BridgeRow(
        "jazzy-fastrtps", "jazzy", "rmw_fastrtps_cpp"
    ),
    "lyrical-fastrtps": BridgeRow(
        "lyrical-fastrtps", "lyrical", "rmw_fastrtps_cpp"
    ),
    "lyrical-zenoh": BridgeRow(
        "lyrical-zenoh", "lyrical", "rmw_zenoh_cpp"
    ),
}


def timestamp() -> str:
    """Return an ISO-8601 local timestamp with milliseconds."""

    return _datetime.datetime.now().astimezone().isoformat(timespec="milliseconds")


def repository_root() -> pathlib.Path:
    """Find the repository without following the local ROS junction tree."""

    start = pathlib.Path(__file__).resolve()
    for candidate in (start.parent, *start.parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    raise BridgeBuildFailure("repository root could not be located")


def require_row(row_id: str) -> BridgeRow:
    """Return one exact row; aliases are deliberately rejected."""

    row = ROWS.get(str(row_id))
    if row is None:
        raise BridgeBuildFailure(
            "unknown Phase186 row; expected exactly one of: " + ", ".join(ROWS)
        )
    return row


def _load_phase181_peer(repository: pathlib.Path):
    """Import the maintained Phase181 peer helper from the repository."""

    ros_scripts = repository / "Scripts" / "smoke" / "ros2"
    if str(ros_scripts) not in sys.path:
        sys.path.insert(0, str(ros_scripts))
    try:
        import phase181_custom_ros2_peer as peer
    except ImportError as exc:
        raise BridgeBuildFailure(
            "maintained Phase181 peer tooling could not be imported"
        ) from exc
    return peer


def load_interface_authority(repository: pathlib.Path) -> dict[str, object]:
    """Read and recompute the exact tracked Phase181 interface authority."""

    root = pathlib.Path(repository).resolve()
    peer = _load_phase181_peer(root)
    package = (
        root
        / "Packages"
        / "dev.unity2foxglove.foxrun.ros2.interfaces"
    )
    try:
        lock = peer.load_static_interface_lock(package)
    except Exception as exc:
        raise BridgeBuildFailure(
            "tracked Phase181 interface lock/source validation failed"
        ) from exc
    canonical_type = (
        lock.ros_package_name + "/msg/" + lock.envelope_message_name
    )
    if (
        lock.ros_package_name != ROS_PACKAGE_NAME
        or canonical_type != INTERFACE_TYPE
        or lock.interface_digest != INTERFACE_DIGEST
    ):
        raise BridgeBuildFailure(
            "tracked Phase181 interface identity differs from Phase186 authority"
        )
    return {
        "rosPackageName": lock.ros_package_name,
        "interfaceRevision": lock.interface_revision,
        "interfaceDigest": lock.interface_digest,
        "canonicalType": canonical_type,
        "sourceDigest": peer.compute_static_source_digest(package),
        "staticPackage": str(package),
        "_lock": lock,
    }


def load_standard_schema_authority(
    repository: pathlib.Path,
) -> dict[str, object]:
    """Lock the exact generated standard schema used by the live duplex probe."""

    root = pathlib.Path(repository).resolve()
    source = (
        root
        / "third-party"
        / "foxglove-sdk"
        / "schemas"
        / "ros2"
        / "Log.msg"
    )
    catalog = (
        root
        / "Packages"
        / "dev.unity2foxglove.ros2bridge"
        / "Runtime"
        / "Schemas"
        / "Ros2Msg"
        / "FoxgloveRos2MsgSchemaCatalog.cs"
    )
    try:
        source_bytes = canonical_schema_bytes(source.read_bytes())
        source_text = source_bytes.decode("utf-8")
        catalog_text = catalog.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as exc:
        raise BridgeBuildFailure(
            "tracked generated standard ROS schema authority is unavailable"
        ) from exc
    source_digest = hashlib.sha256(source_bytes).hexdigest()
    if source_digest != STANDARD_SCHEMA_DIGEST:
        raise BridgeBuildFailure(
            "tracked generated standard ROS schema digest differs from authority"
        )
    if (
        f'"{STANDARD_SCHEMA_TYPE}"' not in catalog_text
        or f'"{STANDARD_SCHEMA_DIGEST}"' not in catalog_text
    ):
        raise BridgeBuildFailure(
            "Bridge generated schema catalog differs from the live standard authority"
        )
    return {
        "rosPackageName": STANDARD_ROS_PACKAGE_NAME,
        "canonicalType": STANDARD_SCHEMA_TYPE,
        "sourceDigest": source_digest,
        "sourcePath": str(source),
        "sourceText": source_text,
        "sourceBytes": source_bytes,
    }


def canonical_schema_bytes(value: bytes) -> bytes:
    """Normalize generated ROS schema text before hashing and staging it."""

    return value.replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def build_overlay_colcon_command(
    colcon: pathlib.Path,
    python_executable: pathlib.Path,
) -> list[str]:
    """Build the Phase181 and generated-standard test packages together."""

    command = _load_phase181_peer(repository_root()).build_windows_colcon_command(
        colcon,
        ROS_PACKAGE_NAME,
        python_executable,
    )
    try:
        selected = command.index("--packages-select")
    except ValueError as exc:
        raise BridgeBuildFailure(
            "maintained colcon command lacks an explicit package selection"
        ) from exc
    if command[selected + 1] != ROS_PACKAGE_NAME:
        raise BridgeBuildFailure(
            "maintained colcon command selected the wrong Phase181 package"
        )
    command.insert(selected + 2, STANDARD_ROS_PACKAGE_NAME)
    return command


def overlay_build_cache_key(
    peer_cache_key: str,
    standard_source_digest: str,
) -> str:
    """Bind the reusable peer workspace to every staged schema source."""

    if (
        _SHA256.fullmatch(peer_cache_key) is None
        or _SHA256.fullmatch(standard_source_digest) is None
    ):
        raise BridgeBuildFailure(
            "overlay cache identity requires exact SHA-256 inputs"
        )
    payload = (
        "phase186-overlay-v1\0"
        + peer_cache_key
        + "\0"
        + standard_source_digest
    ).encode("ascii")
    return hashlib.sha256(payload).hexdigest()


def stage_standard_schema_package(
    repository: pathlib.Path,
    workspace: pathlib.Path,
) -> pathlib.Path:
    """Stage one exact test-only foxglove_msgs package into an owned workspace."""

    authority = load_standard_schema_authority(repository)
    destination = (
        pathlib.Path(workspace)
        / "src"
        / STANDARD_ROS_PACKAGE_NAME
    )
    if destination.exists():
        raise BridgeBuildFailure(
            "owned overlay already contains the generated standard package"
        )
    try:
        message_directory = destination / "msg"
        message_directory.mkdir(parents=True)
        (message_directory / "Log.msg").write_bytes(
            bytes(authority["sourceBytes"])
        )
        (destination / "package.xml").write_text(
            """<?xml version=\"1.0\"?>
<package format=\"3\">
  <name>foxglove_msgs</name>
  <version>0.0.0</version>
  <description>Phase186 generated-standard duplex certification fixture.</description>
  <maintainer email=\"noreply@example.invalid\">Unity2Foxglove Phase186</maintainer>
  <license>Apache-2.0</license>
  <buildtool_depend>ament_cmake</buildtool_depend>
  <build_depend>rosidl_default_generators</build_depend>
  <depend>builtin_interfaces</depend>
  <exec_depend>rosidl_default_runtime</exec_depend>
  <member_of_group>rosidl_interface_packages</member_of_group>
  <export><build_type>ament_cmake</build_type></export>
</package>
""",
            encoding="utf-8",
            newline="\n",
        )
        (destination / "CMakeLists.txt").write_text(
            """cmake_minimum_required(VERSION 3.12)
project(foxglove_msgs)
find_package(ament_cmake REQUIRED)
find_package(rosidl_default_generators REQUIRED)
find_package(builtin_interfaces REQUIRED)
rosidl_generate_interfaces(${PROJECT_NAME}
  \"msg/Log.msg\"
  DEPENDENCIES builtin_interfaces
)
ament_export_dependencies(rosidl_default_runtime)
ament_package()
""",
            encoding="utf-8",
            newline="\n",
        )
    except OSError as exc:
        raise BridgeBuildFailure(
            "generated standard ROS schema package could not be staged"
        ) from exc
    return destination


def validate_installed_standard_schema(
    install_prefix: pathlib.Path,
    expected_digest: str,
) -> None:
    """Reject absent or stale generated-standard outputs, including cache reuse."""

    install = pathlib.Path(install_prefix)
    package = install / "share" / STANDARD_ROS_PACKAGE_NAME
    message = package / "msg" / "Log.msg"
    if not (package / "package.xml").is_file() or not message.is_file():
        raise BridgeBuildFailure(
            "row overlay lacks the generated standard schema package"
        )
    if sha256_file(message) != expected_digest:
        raise BridgeBuildFailure(
            "row overlay generated standard schema bytes differ from authority"
        )


def sha256_file(path: pathlib.Path) -> str:
    """Hash one required file."""

    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def hash_source_tree(root: pathlib.Path) -> str:
    """Hash one source tree with normalized paths and exact bytes."""

    source = pathlib.Path(root)
    files = sorted(
        path
        for path in source.rglob("*")
        if path.is_file()
        and not any(part in {"build", "install", "log", ".git"} for part in path.parts)
    )
    if not files:
        raise BridgeBuildFailure("Bridge source tree is empty")
    digest = hashlib.sha256()
    for path in files:
        relative = path.relative_to(source).as_posix().encode("utf-8")
        content = path.read_bytes()
        digest.update(len(relative).to_bytes(8, "big"))
        digest.update(relative)
        digest.update(len(content).to_bytes(8, "big"))
        digest.update(content)
    return digest.hexdigest()


def expected_overlay_authority(
    row: BridgeRow,
    row_root: pathlib.Path,
    install_prefix: pathlib.Path,
    *,
    source_digest: str,
    standard_source_digest: str,
) -> dict[str, object]:
    """Create the exact row-scoped overlay authority record."""

    root = pathlib.Path(row_root).resolve()
    install = pathlib.Path(install_prefix).resolve()
    try:
        install.relative_to(root)
    except ValueError as exc:
        raise BridgeBuildFailure("overlay install prefix escaped its row root") from exc
    setup = install / "local_setup.bat"
    if not setup.is_file():
        raise BridgeBuildFailure("row overlay has no local_setup.bat")
    if source_digest != INTERFACE_DIGEST:
        raise BridgeBuildFailure("row overlay source digest does not match the lock")
    if standard_source_digest != STANDARD_SCHEMA_DIGEST:
        raise BridgeBuildFailure(
            "row overlay generated standard digest does not match the lock"
        )
    return {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "validated": True,
        "rowId": row.row_id,
        "distro": row.distro,
        "rmw": row.rmw,
        "rosPackageName": ROS_PACKAGE_NAME,
        "canonicalType": INTERFACE_TYPE,
        "interfaceDigest": INTERFACE_DIGEST,
        "sourceDigest": source_digest,
        "standardSchema": {
            "rosPackageName": STANDARD_ROS_PACKAGE_NAME,
            "canonicalType": STANDARD_SCHEMA_TYPE,
            "sourceDigest": standard_source_digest,
        },
        "installPrefix": str(install),
        "localSetupSha256": sha256_file(setup),
    }


def validate_overlay_authority(
    value: Mapping[str, object],
    row: BridgeRow,
    row_root: pathlib.Path,
) -> None:
    """Reject stale, cross-row, ambient, or digest-mismatched overlays."""

    if not isinstance(value, Mapping):
        raise BridgeBuildFailure("overlay authority is not an object")
    exact = {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "validated": True,
        "rowId": row.row_id,
        "distro": row.distro,
        "rmw": row.rmw,
        "rosPackageName": ROS_PACKAGE_NAME,
        "canonicalType": INTERFACE_TYPE,
        "interfaceDigest": INTERFACE_DIGEST,
        "sourceDigest": INTERFACE_DIGEST,
    }
    for key, expected in exact.items():
        if value.get(key) != expected:
            raise BridgeBuildFailure(
                "overlay authority mismatch for " + key
            )
    if value.get("standardSchema") != {
        "rosPackageName": STANDARD_ROS_PACKAGE_NAME,
        "canonicalType": STANDARD_SCHEMA_TYPE,
        "sourceDigest": STANDARD_SCHEMA_DIGEST,
    }:
        raise BridgeBuildFailure(
            "overlay generated standard schema authority mismatch"
        )
    install_text = value.get("installPrefix")
    setup_digest = value.get("localSetupSha256")
    if not isinstance(install_text, str) or not isinstance(setup_digest, str):
        raise BridgeBuildFailure("overlay authority lacks its install identity")
    install = pathlib.Path(install_text).resolve()
    try:
        install.relative_to(pathlib.Path(row_root).resolve())
    except ValueError as exc:
        raise BridgeBuildFailure("overlay install prefix is not row-scoped") from exc
    setup = install / "local_setup.bat"
    if not setup.is_file() or sha256_file(setup) != setup_digest:
        raise BridgeBuildFailure("overlay setup identity is stale or missing")


__all__ = [name for name in globals() if not name.startswith("__")]
