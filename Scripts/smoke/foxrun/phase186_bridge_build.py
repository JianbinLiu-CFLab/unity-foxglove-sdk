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


INTERFACE_TYPE = (
    "unity2foxglove_foxrun_interfaces_v1/msg/"
    "Phase181State48D288ED82F1Envelope"
)
INTERFACE_DIGEST = (
    "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd"
)
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
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


def not_run_summary(row: BridgeRow, prerequisite: str) -> dict[str, object]:
    """Create an honest terminal non-completion record."""

    return {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "rowId": row.row_id,
        "distro": row.distro,
        "requestedRmw": row.rmw,
        "verdict": "NOT RUN",
        "platform": platform.system(),
        "missingPrerequisite": str(prerequisite),
        "canonicalType": INTERFACE_TYPE,
        "interfaceDigest": INTERFACE_DIGEST,
        "startedAt": timestamp(),
        "finishedAt": timestamp(),
    }


def verdict_exit_code(verdict: object) -> int:
    """Map terminal result to a stable process exit code."""

    if verdict == "PASS":
        return 0
    if verdict == "NOT RUN":
        return 2
    return 1


def validate_build_summary(value: Mapping[str, object], row: BridgeRow) -> None:
    """Require real successful build, test, compiler, and executable evidence."""

    if not isinstance(value, Mapping):
        raise BridgeBuildFailure("build summary is not an object")
    expected = {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "rowId": row.row_id,
        "distro": row.distro,
        "requestedRmw": row.rmw,
        "selectedRmw": row.rmw,
        "verdict": "PASS",
        "platform": "Windows",
        "interfaceDigest": INTERFACE_DIGEST,
        "canonicalType": INTERFACE_TYPE,
    }
    for key, expected_value in expected.items():
        if value.get(key) != expected_value:
            raise BridgeBuildFailure("build summary mismatch for " + key)
    overlay = value.get("overlayAuthority")
    if not isinstance(overlay, Mapping) or overlay.get("validated") is not True:
        raise BridgeBuildFailure("build summary lacks validated overlay authority")
    commands = value.get("commands")
    if not isinstance(commands, Mapping):
        raise BridgeBuildFailure("build summary lacks command evidence")
    for name in ("colcon", "cmakeConfigure", "cmakeBuild", "ctest"):
        command = commands.get(name)
        if (
            not isinstance(command, Mapping)
            or command.get("exitCode") != 0
            or not isinstance(command.get("log"), str)
            or not command.get("log")
        ):
            raise BridgeBuildFailure("build command did not pass: " + name)
    ctest = value.get("ctest")
    if (
        not isinstance(ctest, Mapping)
        or not isinstance(ctest.get("tests"), int)
        or ctest.get("tests", 0) <= 0
        or ctest.get("passed") != ctest.get("tests")
    ):
        raise BridgeBuildFailure("ctest evidence is missing or incomplete")
    compiler = value.get("compiler")
    executable = value.get("probeExecutable")
    if not isinstance(compiler, Mapping) or not compiler.get("identity"):
        raise BridgeBuildFailure("compiler identity is missing")
    if (
        not isinstance(executable, Mapping)
        or not isinstance(executable.get("sha256"), str)
        or _SHA256.fullmatch(str(executable.get("sha256"))) is None
    ):
        raise BridgeBuildFailure("probe executable identity is missing")


def _write_json_atomic(path: pathlib.Path, value: Mapping[str, object]) -> None:
    """Write one JSON evidence object by atomic file replacement."""

    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=path.parent,
        prefix=path.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, path)


def _process_group_options() -> dict[str, object]:
    """Return platform-specific options for an owned process group."""

    if os.name == "nt":
        return {
            "creationflags": int(
                getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
            )
        }
    return {"start_new_session": True}


def run_logged(
    command: Sequence[str],
    *,
    cwd: pathlib.Path,
    env: Mapping[str, str],
    log_path: pathlib.Path,
    timeout_seconds: float,
) -> dict[str, object]:
    """Run one owned command, tee bounded output, and retain exact exit status."""

    if not command or timeout_seconds <= 0:
        raise BridgeBuildFailure("owned command contract is invalid")
    started = timestamp()
    start_clock = time.monotonic()
    log_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with log_path.open("w", encoding="utf-8", newline="\n") as log:
            process = subprocess.Popen(
                [str(part) for part in command],
                cwd=str(cwd),
                env=dict(env),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                errors="replace",
                bufsize=1,
                shell=False,
                **_process_group_options(),
            )
            try:
                output, _ = process.communicate(timeout=timeout_seconds)
            except subprocess.TimeoutExpired:
                process.kill()
                output, _ = process.communicate(timeout=10)
                log.write(output or "")
                raise BridgeBuildFailure(
                    "owned command exceeded its bounded timeout: "
                    + pathlib.Path(str(command[0])).name
                )
            log.write(output or "")
    except OSError as exc:
        raise LivePrerequisiteMissing(
            "command unavailable: " + pathlib.Path(str(command[0])).name
        ) from exc
    finished = timestamp()
    result = {
        "exitCode": process.returncode,
        "startedAt": started,
        "finishedAt": finished,
        "durationSeconds": round(time.monotonic() - start_clock, 3),
        "log": str(log_path),
        "executable": str(command[0]),
    }
    if process.returncode != 0:
        raise BridgeBuildFailure(
            "owned command failed; see " + str(log_path)
        )
    return result


def _find_tool(name: str, env: Mapping[str, str]) -> pathlib.Path:
    """Resolve one required executable from the supplied environment."""

    found = shutil.which(name, path=env.get("PATH"))
    if not found:
        raise LivePrerequisiteMissing(name + " is not available")
    return pathlib.Path(found).resolve()


def _ctest_counts(log_path: pathlib.Path) -> tuple[int, int]:
    """Extract passed and total test counts from a complete CTest log."""

    text = pathlib.Path(log_path).read_text(encoding="utf-8", errors="replace")
    match = re.search(
        r"(\d+)% tests passed,\s+(\d+) tests failed out of (\d+)",
        text,
    )
    if not match:
        raise BridgeBuildFailure("ctest log has no complete test-count summary")
    passed = int(match.group(3)) - int(match.group(2))
    return int(match.group(3)), passed


def _compiler_identity(cache_path: pathlib.Path, environment: Mapping[str, str]) -> dict[str, object]:
    """Describe the configured MSVC compiler using cache and environment evidence."""

    compiler = ""
    if cache_path.is_file():
        for line in cache_path.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("CMAKE_CXX_COMPILER:FILEPATH="):
                compiler = line.split("=", 1)[1]
                break
    return {
        "identity": "MSVC " + str(environment.get("VisualStudioVersion", "")).strip(),
        "path": compiler,
    }


def _build_cpp_environment(
    build_environment: Mapping[str, str],
    ros2_root: pathlib.Path,
    install_prefix: pathlib.Path,
    temporary_directory: pathlib.Path,
) -> dict[str, str]:
    """Build the isolated C++ environment for one Bridge matrix row."""

    env = dict(build_environment)
    pixi_library = ros2_root / ".pixi" / "envs" / "default" / "Library"
    prefixes = [str(install_prefix), str(ros2_root), str(pixi_library)]
    env["AMENT_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["CMAKE_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["COLCON_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["PATH"] = os.pathsep.join(
        [
            str(install_prefix / "bin"),
            str(install_prefix / "Lib"),
            str(ros2_root / "bin"),
            str(pixi_library / "bin"),
            env.get("PATH", ""),
        ]
    )
    temporary_directory.mkdir(parents=True, exist_ok=True)
    env["TMP"] = str(temporary_directory.resolve())
    env["TEMP"] = str(temporary_directory.resolve())
    env["PYTHONUTF8"] = "1"
    return env


def run_row(
    repository: pathlib.Path,
    row: BridgeRow,
    output_root: pathlib.Path,
    *,
    run_tests: bool,
) -> dict[str, object]:
    """Build one exact row and persist its terminal result."""

    repository = pathlib.Path(repository).resolve()
    output_root = pathlib.Path(output_root).resolve()
    row_root = output_root / row.row_id
    row_root.mkdir(parents=True, exist_ok=True)
    summary_path = row_root / "build-summary.json"
    started = timestamp()
    base: dict[str, object] = {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "rowId": row.row_id,
        "distro": row.distro,
        "requestedRmw": row.rmw,
        "selectedRmw": row.rmw,
        "platform": platform.system(),
        "canonicalType": INTERFACE_TYPE,
        "interfaceDigest": INTERFACE_DIGEST,
        "startedAt": started,
    }
    try:
        if os.name != "nt" or platform.system() != "Windows":
            raise LivePrerequisiteMissing("Windows-native execution")
        authority = load_interface_authority(repository)
        peer = _load_phase181_peer(repository)
        ros2_root = pathlib.Path(
            os.environ.get(
                "PHASE186_ROS2_" + row.distro.upper() + "_ROOT",
                str(repository / "ros2-windows" / ("ros2_" + row.distro)),
            )
        )
        try:
            toolchain = peer.resolve_windows_peer_toolchain(ros2_root)
        except Exception as exc:
            raise LivePrerequisiteMissing(
                row.distro + " Windows ROS2 root"
            ) from exc
        ros_environment = peer.ros2env.build_ros_env(
            toolchain.ros2_root,
            row.rmw,
            "LOCALHOST",
            "0",
            row.distro,
        )
        try:
            msvc_environment = peer.capture_windows_msvc_environment(
                ros_environment
            )
            build_environment = peer.merge_windows_peer_build_environment(
                ros_environment,
                msvc_environment,
            )
        except Exception as exc:
            raise LivePrerequisiteMissing(
                "Visual Studio C++ x64 toolchain"
            ) from exc

        colcon_command = peer.build_windows_colcon_command(
            toolchain.colcon_executable,
            ROS_PACKAGE_NAME,
            toolchain.python_executable,
        )
        cache_key = peer.peer_build_cache_key(
            authority["_lock"],
            row.row_id,
            row.distro,
            row.rmw,
            toolchain,
            colcon_command,
        )
        workspace, reused = peer.prepare_peer_build_workspace(
            output_root,
            row.row_id,
            cache_key,
            ROS_PACKAGE_NAME,
        )
        command_results: dict[str, object] = {}
        with contextlib.ExitStack() as stack:
            physical_workspace, runtime_workspace = stack.enter_context(
                peer.temporary_short_windows_peer_workspace(workspace)
            )
            if reused:
                command_results["colcon"] = {
                    "exitCode": 0,
                    "startedAt": started,
                    "finishedAt": timestamp(),
                    "durationSeconds": 0.0,
                    "log": str(workspace / "colcon-build.log"),
                    "reused": True,
                }
            else:
                peer.stage_locked_ros_source(
                    pathlib.Path(str(authority["staticPackage"])),
                    runtime_workspace,
                    ROS_PACKAGE_NAME,
                )
                command_results["colcon"] = run_logged(
                    colcon_command,
                    cwd=runtime_workspace,
                    env=build_environment,
                    log_path=physical_workspace / "colcon-build.log",
                    timeout_seconds=1800,
                )
                peer.seal_peer_build_workspace(
                    physical_workspace,
                    cache_key,
                    ROS_PACKAGE_NAME,
                )

        install_prefix = workspace / "install"
        overlay = expected_overlay_authority(
            row,
            row_root,
            install_prefix,
            source_digest=str(authority["sourceDigest"]),
        )
        validate_overlay_authority(overlay, row, row_root)
        _write_json_atomic(row_root / "overlay-authority.json", overlay)

        source_root = (
            repository
            / "Tools"
            / "ros2_bridge"
            / "unity2foxglove_ros2_bridge"
        )
        cpp_build = row_root / "cpp-build"
        cpp_temp = row_root / "tmp"
        cpp_environment = _build_cpp_environment(
            build_environment,
            toolchain.ros2_root,
            install_prefix,
            cpp_temp,
        )
        cmake = _find_tool("cmake.exe", cpp_environment)
        ctest = _find_tool("ctest.exe", cpp_environment)
        ninja = _find_tool("ninja.exe", cpp_environment)
        library = toolchain.ros2_root / ".pixi" / "envs" / "default" / "Library"
        nlohmann_directories = (
            library / "share" / "cmake" / "nlohmann_json",
            library / "lib" / "cmake" / "nlohmann_json",
            pathlib.Path(sys.prefix)
            / "Library"
            / "share"
            / "cmake"
            / "nlohmann_json",
        )
        nlohmann_directory = next(
            (
                candidate
                for candidate in nlohmann_directories
                if (candidate / "nlohmann_jsonConfig.cmake").is_file()
            ),
            None,
        )
        if nlohmann_directory is None:
            raise LivePrerequisiteMissing("nlohmann_json CMake package")
        configure_command = [
            str(cmake),
            "-S",
            str(source_root),
            "-B",
            str(cpp_build),
            "-G",
            "Ninja",
            "-DBUILD_TESTING=ON",
            "-DCMAKE_BUILD_TYPE=Release",
            "-DCMAKE_MAKE_PROGRAM=" + str(ninja).replace("\\", "/"),
            "-DPython3_EXECUTABLE="
            + str(toolchain.python_executable).replace("\\", "/"),
            "-DPYTHON_EXECUTABLE="
            + str(toolchain.python_executable).replace("\\", "/"),
            "-DOPENSSL_ROOT_DIR=" + str(library).replace("\\", "/"),
            "-Dnlohmann_json_DIR="
            + str(nlohmann_directory).replace("\\", "/"),
            "-Dtinyxml2_DIR="
            + str(library / "lib" / "cmake" / "tinyxml2").replace("\\", "/"),
        ]
        command_results["cmakeConfigure"] = run_logged(
            configure_command,
            cwd=row_root,
            env=cpp_environment,
            log_path=row_root / "cmake-configure.log",
            timeout_seconds=300,
        )
        command_results["cmakeBuild"] = run_logged(
            [str(cmake), "--build", str(cpp_build)],
            cwd=row_root,
            env=cpp_environment,
            log_path=row_root / "cmake-build.log",
            timeout_seconds=900,
        )

        if not run_tests:
            result = {
                **base,
                "verdict": "BUILD ONLY",
                "finishedAt": timestamp(),
                "overlayAuthority": overlay,
                "commands": command_results,
                "bridgeSourceDigest": hash_source_tree(source_root),
            }
            _write_json_atomic(summary_path, result)
            return result

        command_results["ctest"] = run_logged(
            [
                str(ctest),
                "--test-dir",
                str(cpp_build),
                "--output-on-failure",
                "-C",
                "Release",
            ],
            cwd=row_root,
            env=cpp_environment,
            log_path=row_root / "ctest.log",
            timeout_seconds=300,
        )
        test_count, passed = _ctest_counts(row_root / "ctest.log")
        probe_executable = cpp_build / "phase186_origin_suppression_probe.exe"
        if not probe_executable.is_file():
            raise BridgeBuildFailure(
                "origin-suppression probe executable was not built"
            )
        result = {
            **base,
            "verdict": "PASS",
            "finishedAt": timestamp(),
            "ros2Root": str(toolchain.ros2_root.resolve()),
            "overlayAuthority": overlay,
            "overlayReused": reused,
            "commands": command_results,
            "ctest": {"tests": test_count, "passed": passed},
            "compiler": _compiler_identity(
                cpp_build / "CMakeCache.txt",
                cpp_environment,
            ),
            "bridgeSourceDigest": hash_source_tree(source_root),
            "probeExecutable": {
                "path": str(probe_executable.resolve()),
                "sha256": sha256_file(probe_executable),
            },
        }
        validate_build_summary(result, row)
        _write_json_atomic(summary_path, result)
        return result
    except LivePrerequisiteMissing as exc:
        result = {
            **not_run_summary(row, str(exc)),
            "startedAt": started,
            "finishedAt": timestamp(),
        }
        _write_json_atomic(summary_path, result)
        return result
    except Exception as exc:
        result = {
            **base,
            "verdict": "FAIL",
            "finishedAt": timestamp(),
            "failure": str(exc),
        }
        _write_json_atomic(summary_path, result)
        return result


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse Bridge build-matrix command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--row", choices=tuple(ROWS))
    selection.add_argument("--all-supported-rows", action="store_true")
    parser.add_argument("--run-tests", action="store_true")
    parser.add_argument(
        "--output-root",
        type=pathlib.Path,
        default=None,
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the selected Bridge build rows and write their evidence."""

    args = parse_args(argv)
    repository = repository_root()
    output_root = (
        args.output_root
        if args.output_root is not None
        else repository / "build" / "phase186" / "bridge"
    )
    selected = tuple(ROWS) if args.all_supported_rows else (args.row,)
    exit_code = 0
    for row_id in selected:
        row = require_row(row_id)
        print("[phase186-build] starting " + row.row_id, flush=True)
        result = run_row(
            repository,
            row,
            output_root,
            run_tests=args.run_tests,
        )
        print(
            "[phase186-build] "
            + row.row_id
            + " => "
            + str(result.get("verdict")),
            flush=True,
        )
        exit_code = max(exit_code, verdict_exit_code(result.get("verdict")))
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
