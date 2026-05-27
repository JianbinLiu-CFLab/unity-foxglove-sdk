#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Phase138B R2FU Jazzy Windows build toolchain orchestrator.

"""Run a deterministic R2FU Jazzy Windows build attempt.

This is intentionally a Python orchestrator, not a project-owned PowerShell
build wrapper. It may call upstream R2FU PowerShell scripts because those are
the upstream build entry points being evaluated.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import time
from dataclasses import dataclass
from typing import Iterable


R2FU_REPO_URL = "https://github.com/RobotecAI/ros2-for-unity.git"
R2FU_BRANCH = "feature/jazzy-support"
ROS2CS_REPO_URL = "https://github.com/RobotecAI/ros2cs.git"
ROS2CS_BRANCH = "feature/jazzy-support"
DEFAULT_BUILD_ROOT = pathlib.Path(r"D:\ros2unity\.build\r2fu-jazzy-win64")
DEFAULT_WORK_ROOT = DEFAULT_BUILD_ROOT / "work"
DEFAULT_TEMP_ROOT = DEFAULT_BUILD_ROOT / "tmp"
CHECKOUT_DIR_NAME = "r2u"
JAZZY_PIXI_RUNTIME_DLLS = ("yaml.dll", "spdlog.dll", "fmt.dll")

VERDICTS = (
    "BUILD_ORCHESTRATOR_GREEN",
    "BLOCKED_MSBUILD_QUERY",
    "BLOCKED_VSDEV_ENV",
    "BLOCKED_PYTHON_SELECTION",
    "BLOCKED_CL_TEMP_IL",
    "BLOCKED_CMAKE_GENERATOR",
    "BLOCKED_WINDOWS_PATH_LENGTH",
    "BLOCKED_R2FU_BUILD_SCRIPT",
    "BLOCKED_ROS2CS_BUILD",
    "BLOCKED_NATIVE_DEPENDENCY",
    "BLOCKED_UNKNOWN_TOOLCHAIN",
)


class Phase138BError(RuntimeError):
    """Build orchestration failure with a stable verdict label."""

    def __init__(self, verdict: str, message: str) -> None:
        """Create an error carrying the stable build verdict."""
        super().__init__(message)
        self.verdict = verdict


@dataclass
class CommandResult:
    """Captured subprocess result."""

    args: list[str]
    cwd: pathlib.Path
    exit_code: int
    output: str


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=str(find_repo_root(pathlib.Path.cwd())))
    parser.add_argument("--work-root", default=str(DEFAULT_WORK_ROOT))
    parser.add_argument("--temp-root", default=str(DEFAULT_TEMP_ROOT))
    parser.add_argument("--ros2-root", default=r"C:\ros2_jazzy\ros2-windows")
    parser.add_argument("--vs-dev-cmd", default="")
    parser.add_argument("--generator", choices=("auto", "visualstudio", "ninja"), default="auto")
    parser.add_argument("--clean", action="store_true")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--log-prefix", default="phase138b")
    parser.add_argument(
        "--evidence-path",
        default="",
        help="Optional evidence markdown output path. Relative paths resolve from --repo-root.",
    )
    return parser.parse_args(argv)


def find_repo_root(start: pathlib.Path) -> pathlib.Path:
    """Find the repository root from a starting path."""

    current = start.resolve()
    for candidate in (current, *current.parents):
        if (candidate / ".git").exists():
            return candidate
    raise Phase138BError("BLOCKED_UNKNOWN_TOOLCHAIN", f"Could not locate repo root from {start}")


def is_relative_to(path: pathlib.Path, parent: pathlib.Path) -> bool:
    """Return whether path is inside parent."""

    try:
        path.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def assert_safe_root(label: str, path: pathlib.Path, repo_root: pathlib.Path) -> None:
    """Reject roots inside the repo or synced BaiduSyncdisk workspace."""

    resolved = path.resolve()
    if is_relative_to(resolved, repo_root):
        raise Phase138BError(
            "BLOCKED_UNKNOWN_TOOLCHAIN",
            f"{label} must not be inside the repository: {resolved}",
        )
    if "baidusyncdisk" in str(resolved).lower() or "BaiduSyncdisk" in str(resolved):
        raise Phase138BError(
            "BLOCKED_UNKNOWN_TOOLCHAIN",
            f"{label} must not be inside D:\\BaiduSyncdisk: {resolved}",
        )


def timestamp() -> str:
    """Return a compact timestamp for log filenames."""

    return _dt.datetime.now().strftime("%Y%m%d_%H%M%S")


def ensure_dir(path: pathlib.Path) -> None:
    """Create a directory if missing."""

    path.mkdir(parents=True, exist_ok=True)


def remove_known_subdir(path: pathlib.Path, allowed_parent: pathlib.Path) -> None:
    """Delete only a verified child directory under an allowed parent."""

    resolved = path.resolve()
    parent = allowed_parent.resolve()
    if resolved == parent or not is_relative_to(resolved, parent):
        raise Phase138BError("BLOCKED_UNKNOWN_TOOLCHAIN", f"Refusing unsafe delete: {resolved}")
    if resolved.exists():
        def make_writable(function, item, _exc_info):
            """Make a readonly build artifact writable before retrying removal."""
            os.chmod(item, stat.S_IWRITE)
            function(item)

        shutil.rmtree(resolved, onerror=make_writable)


def run_command(
    args: Iterable[str],
    *,
    cwd: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    check: bool = False,
    timeout: int | None = None,
) -> CommandResult:
    """Run a command and append captured output to the log file."""

    command = list(args)
    header = f"\n\n$ {' '.join(command)}\n# cwd={cwd}\n"
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(header)

    timed_out = False
    try:
        process = subprocess.Popen(
            command,
            cwd=str(cwd),
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            errors="replace",
        )
        output_parts: list[str] = []
        deadline = time.monotonic() + timeout if timeout else None
        with log_file.open("a", encoding="utf-8", errors="replace") as log:
            assert process.stdout is not None
            while True:
                line = process.stdout.readline()
                if line:
                    output_parts.append(line)
                    log.write(line)
                    log.flush()
                if process.poll() is not None:
                    remainder = process.stdout.read()
                    if remainder:
                        output_parts.append(remainder)
                        log.write(remainder)
                        log.flush()
                    break
                if deadline is not None and time.monotonic() > deadline:
                    timed_out = True
                    kill_process_tree_windows(process.pid)
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        try:
                            process.wait(timeout=5)
                        except subprocess.TimeoutExpired:
                            pass
                    output_parts.append("\nCOMMAND_TIMEOUT\n")
                    log.write("\nCOMMAND_TIMEOUT\n")
                    log.flush()
                    break
        exit_code = 124 if timed_out else (process.returncode if process.returncode is not None else 124)
        output = "".join(output_parts)
    except FileNotFoundError as exc:
        exit_code = 127
        output = f"{exc}\n"
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(f"\n# exit={exit_code}\n")

    result = CommandResult(command, cwd, exit_code, output)
    if check and exit_code != 0:
        raise Phase138BError(classify_output(output), f"Command failed: {' '.join(command)}")
    return result


def kill_process_tree_windows(pid: int) -> None:
    """Terminate a Windows process tree without letting build children outlive timeout."""

    if os.name != "nt":
        return
    subprocess.run(
        ["taskkill", "/T", "/F", "/PID", str(pid)],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=False,
    )


def classify_output(output: str) -> str:
    """Map known build failure text to a stable verdict."""

    lower = output.lower()
    if "can't find third party dependency" in lower or "libssl-1_1" in lower or "libcrypto-1_1" in lower:
        return "BLOCKED_NATIVE_DEPENDENCY"
    if (
        "modulenotfounderror" in lower
        or "no module named 'em'" in lower
        or 'no module named "em"' in lower
        or "catkin_pkg" in lower
        or "ament_package" in lower
        or "anaconda3" in lower
        or re.search(r"python3[0-9]{2,}", lower) is not None
    ):
        return "BLOCKED_PYTHON_SELECTION"
    if "visualstudioversion is not set" in lower or "vsdevcmd" in lower:
        return "BLOCKED_VSDEV_ENV"
    if "winerror 2" in lower or "system cannot find the file" in lower or "system kann die angegebene datei nicht finden" in lower:
        return "BLOCKED_VSDEV_ENV"
    if "vctargetspath" in lower or "fileloadexception" in lower or ("msbuild" in lower and "invalid" in lower):
        return "BLOCKED_MSBUILD_QUERY"
    if "d8037" in lower or "cannot create temporary il file" in lower:
        return "BLOCKED_CL_TEMP_IL"
    if "msb3491" in lower or "msb4023" in lower or "260 zeichen" in lower or "maximale pfadlimit" in lower:
        return "BLOCKED_WINDOWS_PATH_LENGTH"
    if "python" in lower and "no module named" in lower:
        return "BLOCKED_PYTHON_SELECTION"
    if "cmake error" in lower and "generator" in lower:
        return "BLOCKED_CMAKE_GENERATOR"
    if "ros2cs" in lower and ("failed" in lower or "error" in lower):
        return "BLOCKED_ROS2CS_BUILD"
    if ".dll" in lower and ("not found" in lower or "missing" in lower):
        return "BLOCKED_NATIVE_DEPENDENCY"
    return "BLOCKED_R2FU_BUILD_SCRIPT"


def find_vswhere() -> pathlib.Path:
    """Find vswhere.exe."""

    candidates = [
        pathlib.Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"))
        / "Microsoft Visual Studio"
        / "Installer"
        / "vswhere.exe",
        pathlib.Path(r"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    raise Phase138BError("BLOCKED_VSDEV_ENV", "vswhere.exe was not found")


def resolve_vs_dev_cmd(explicit: str, env: dict[str, str], log_file: pathlib.Path) -> pathlib.Path:
    """Resolve VsDevCmd.bat using explicit input, vswhere, or common install paths."""

    if explicit:
        path = pathlib.Path(explicit)
        if path.exists():
            return path
        raise Phase138BError("BLOCKED_VSDEV_ENV", f"Explicit VsDevCmd.bat was not found: {path}")

    candidates: list[pathlib.Path] = [
        pathlib.Path(r"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"),
        pathlib.Path(r"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate

    vswhere = find_vswhere()
    result = run_command(
        [
            str(vswhere),
            "-latest",
            "-version",
            "[17.0,18.0)",
            "-products",
            "*",
            "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property",
            "installationPath",
        ],
        cwd=pathlib.Path.cwd(),
        env=env,
        log_file=log_file,
    )
    install_path = result.output.strip().splitlines()[-1] if result.output.strip() else ""
    if install_path:
        candidates.append(pathlib.Path(install_path) / "Common7" / "Tools" / "VsDevCmd.bat")
    for candidate in candidates:
        if candidate.exists():
            return candidate
    raise Phase138BError("BLOCKED_VSDEV_ENV", "VsDevCmd.bat was not found")


def capture_cmd_environment(vs_dev_cmd: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> dict[str, str]:
    """Import VsDevCmd.bat -arch=x64 -host_arch=x64 into a Python environment."""

    reject_cmd_shell_unsafe_path("VsDevCmd.bat", vs_dev_cmd)
    command = f'call "{vs_dev_cmd}" -arch=x64 -host_arch=x64 >nul && set'
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(f"\n\n$ {command}\n# cwd={vs_dev_cmd.parent}\n")
    completed = subprocess.run(
        command,
        cwd=str(vs_dev_cmd.parent),
        env=env,
        shell=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        errors="replace",
    )
    output = completed.stdout or ""
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(output)
        log.write(f"\n# exit={completed.returncode}\n")
    if completed.returncode != 0:
        raise Phase138BError(classify_output(output), f"Command failed: {command}")
    merged = dict(env)
    for line in output.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        merged[key] = value
    return merged


def reject_cmd_shell_unsafe_path(label: str, path: pathlib.Path) -> None:
    """Reject a path that cannot be safely embedded in a cmd.exe quoted string."""

    if '"' in str(path):
        raise Phase138BError("BLOCKED_VSDEV_ENV", f"{label} contains an unsupported quote: {path}")


def scrub_environment(env: dict[str, str], ros2_root: pathlib.Path, temp_root: pathlib.Path) -> dict[str, str]:
    """Remove common Python contamination and pin ROS2 Jazzy paths."""

    cleaned = dict(env)
    for key in list(cleaned):
        if key.upper().startswith("CONDA_") or key.upper() in {"PYTHONHOME", "PYTHONPATH"}:
            cleaned.pop(key, None)

    pixi = ros2_root / ".pixi" / "envs" / "default"
    python = pixi / "python.exe"
    path_entries = [
        ros2_root / "bin",
        ros2_root / "Scripts",
        pixi,
        pixi / "Library" / "bin",
        pixi / "Scripts",
    ]
    existing_path = cleaned.get("Path") or cleaned.get("PATH", "")
    filtered_existing = [
        entry
        for entry in existing_path.split(os.pathsep)
        if entry and not is_contaminating_python_path(entry)
    ]
    merged_path = os.pathsep.join([str(path) for path in path_entries] + filtered_existing)
    cleaned.pop("Path", None)
    cleaned["PATH"] = merged_path
    cleaned["COLCON_PYTHON_EXECUTABLE"] = str(python)
    cleaned["PYTHONUTF8"] = "1"
    cleaned["TEMP"] = str(temp_root)
    cleaned["TMP"] = str(temp_root)
    cleaned["ROS_DOMAIN_ID"] = "0"
    cleaned["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    cleaned["ROS_AUTOMATIC_DISCOVERY_RANGE"] = "SUBNET"
    cleaned.pop("ROS_LOCALHOST_ONLY", None)
    cleaned.pop("ROS_DISCOVERY_SERVER", None)
    return cleaned


def is_contaminating_python_path(entry: str) -> bool:
    """Return true for PATH entries likely to override the selected ROS2 pixi Python."""

    lower = entry.lower()
    blocked = (
        "anaconda",
        "miniconda",
        "conda",
        "mambaforge",
        "miniforge",
        "python27",
        "python36",
        "python37",
        "python38",
        "python39",
        "python310",
        "python311",
        "python312",
        "python313",
    )
    return any(token in lower for token in blocked) or re.search(r"python3[0-9]{2,}", lower) is not None


def capture_ros2_environment(ros2_root: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> dict[str, str]:
    """Import ROS2 local_setup.ps1 environment into the build process."""

    setup = ros2_root / "local_setup.ps1"
    if not setup.exists():
        raise Phase138BError("BLOCKED_PYTHON_SELECTION", f"ROS2 local_setup.ps1 was not found: {setup}")
    command = f"& '{setup}'; Get-ChildItem Env: | ForEach-Object {{ \"$($_.Name)=$($_.Value)\" }}"
    result = run_command(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
        cwd=ros2_root,
        env=env,
        log_file=log_file,
        check=True,
    )
    merged = dict(env)
    for line in result.output.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        merged[key] = value
    # Preserve the explicit isolation choices after local_setup has expanded ROS paths.
    merged["TEMP"] = env["TEMP"]
    merged["TMP"] = env["TMP"]
    merged["COLCON_PYTHON_EXECUTABLE"] = env["COLCON_PYTHON_EXECUTABLE"]
    merged["ROS_DOMAIN_ID"] = "0"
    merged["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    merged["ROS_AUTOMATIC_DISCOVERY_RANGE"] = "SUBNET"
    merged.pop("ROS_LOCALHOST_ONLY", None)
    merged.pop("ROS_DISCOVERY_SERVER", None)
    return merged


def record_where_diagnostics(env: dict[str, str], cwd: pathlib.Path, log_file: pathlib.Path) -> None:
    """Record where diagnostics for Python and native build tools."""

    where_commands = ["python", "pip", "colcon", "cmake", "ninja", "msbuild", "cl", "powershell"]
    for command in where_commands:
        run_command(["where", command], cwd=cwd, env=env, log_file=log_file)


def verify_python(ros2_root: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> pathlib.Path:
    """Verify the pinned ROS2 Jazzy pixi Python can import catkin_pkg."""

    python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    if not python.exists():
        raise Phase138BError("BLOCKED_PYTHON_SELECTION", f"Pinned Python is missing: {python}")
    run_command(
        [str(python), "-c", "import sys, catkin_pkg, ament_package; print(sys.executable); print(catkin_pkg.__file__); print(ament_package.__file__)"],
        cwd=ros2_root,
        env=env,
        log_file=log_file,
        check=True,
    )
    return python


def verify_cl_compile(temp_root: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> None:
    """Compile a tiny C file using cl.exe from the imported VS environment."""

    ensure_dir(temp_root)
    source = temp_root / "phase138b_cl_probe.c"
    obj = temp_root / "phase138b_cl_probe.obj"
    source.write_text("int main(void) { return 0; }\n", encoding="utf-8")
    cl_path = shutil.which("cl", path=env.get("Path") or env.get("PATH"))
    if not cl_path:
        raise Phase138BError("BLOCKED_VSDEV_ENV", "cl.exe was not found in the imported VS environment")
    try:
        result = run_command(
            [cl_path, "/nologo", "/c", str(source), f"/Fo{obj}"],
            cwd=temp_root,
            env=env,
            log_file=log_file,
        )
        if result.exit_code != 0:
            raise Phase138BError(classify_output(result.output), "cl.exe tiny compile failed")
    finally:
        for probe in (source, obj):
            try:
                probe.unlink()
            except FileNotFoundError:
                pass


def resolve_cmake_generator(requested: str, env: dict[str, str], log_file: pathlib.Path) -> str:
    """Resolve the effective CMake generator; auto should prefer Ninja."""

    path = env.get("Path") or env.get("PATH")
    ninja = shutil.which("ninja", path=path)
    if requested == "ninja" and not ninja:
        raise Phase138BError("BLOCKED_CMAKE_GENERATOR", "Ninja was requested but ninja.exe was not found")
    if requested == "visualstudio":
        effective = "visualstudio"
    elif requested == "ninja":
        effective = "ninja"
    else:
        # Prefer Ninja in auto mode because MSBuild expands generated ROSIDL target
        # names into paths that can exceed Windows' 260-character process limit.
        effective = "ninja" if ninja else "visualstudio"

    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(f"\n# EffectiveGenerator={effective}\n")
        if requested == "auto" and ninja:
            log.write(f"# auto selected ninja: {ninja}\n")
        elif requested == "auto":
            log.write("# auto selected visualstudio because ninja.exe was not found\n")
    return effective


def ensure_r2fu_checkout(work_root: pathlib.Path, env: dict[str, str], log_file: pathlib.Path, clean: bool) -> pathlib.Path:
    """Create or update a clean R2FU Jazzy-support checkout under WorkRoot."""

    ensure_dir(work_root)
    checkout = work_root / CHECKOUT_DIR_NAME
    if clean and checkout.exists():
        remove_known_subdir(checkout, work_root)
    if not checkout.exists():
        run_command(["git", "clone", R2FU_REPO_URL, str(checkout)], cwd=work_root, env=env, log_file=log_file, check=True)
    run_command(["git", "fetch", "origin"], cwd=checkout, env=env, log_file=log_file, check=True)
    run_command(
        ["git", "switch", "-C", R2FU_BRANCH, f"origin/{R2FU_BRANCH}"],
        cwd=checkout,
        env=env,
        log_file=log_file,
        check=True,
    )

    ros2cs = checkout / "src" / "ros2cs"
    if not ros2cs.exists():
        ensure_dir(checkout / "src")
        run_command(["git", "clone", ROS2CS_REPO_URL, str(ros2cs)], cwd=checkout / "src", env=env, log_file=log_file, check=True)
    run_command(["git", "fetch", "origin"], cwd=ros2cs, env=env, log_file=log_file, check=True)
    run_command(
        ["git", "switch", "-C", ROS2CS_BRANCH, f"origin/{ROS2CS_BRANCH}"],
        cwd=ros2cs,
        env=env,
        log_file=log_file,
        check=True,
    )
    return checkout


def patch_ros2cs_jazzy_windows_standalone(checkout: pathlib.Path, log_file: pathlib.Path) -> None:
    """Patch the checkout so Jazzy Windows standalone uses OpenSSL 3 DLL names."""

    cmake = checkout / "src" / "ros2cs" / "src" / "ros2cs" / "ros2cs_core" / "CMakeLists.txt"
    text = cmake.read_text(encoding="utf-8")
    new = """    set(third_party_standalone_libs
      msvcp140.dll
      vcruntime140.dll
      vcruntime140_1.dll
      tinyxml2.dll
    )
    if(ros2_distro STREQUAL "jazzy" OR ros2_distro STREQUAL "rolling")
      list(PREPEND third_party_standalone_libs
        libssl-3-x64.dll
        libcrypto-3-x64.dll
      )
    else()
      list(PREPEND third_party_standalone_libs
        libssl-1_1-x64.dll
        libcrypto-1_1-x64.dll
      )
    endif()
"""
    if "libssl-3-x64.dll" in text and "ros2_distro STREQUAL \"jazzy\"" in text:
        patched = False
    else:
        pattern = re.compile(
            r"(?ms)^[ \t]*set\(\s*third_party_standalone_libs\b.*?^[ \t]*\)\s*$"
        )
        matches = [
            match
            for match in pattern.finditer(text)
            if "libssl-1_1-x64.dll" in match.group(0)
            and "libcrypto-1_1-x64.dll" in match.group(0)
            and "tinyxml2.dll" in match.group(0)
        ]
        if len(matches) != 1:
            raise Phase138BError(
                "BLOCKED_ROS2CS_BUILD",
                f"Could not patch ros2cs_core OpenSSL standalone DLL list; matches={len(matches)}",
            )

        match = matches[0]
        cmake.write_text(text[: match.start()] + new + text[match.end() :], encoding="utf-8")
        patched = True

    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        state = "applied" if patched else "already-present"
        log.write(f"\n# patch_ros2cs_jazzy_windows_standalone={state}\n")


def run_jazzy_dependency_import(checkout: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> None:
    """Run the upstream Jazzy dependency import."""

    ros2cs = checkout / "src" / "ros2cs"
    get_repos = ros2cs / "get_repos.ps1"
    if not get_repos.exists():
        raise Phase138BError("BLOCKED_ROS2CS_BUILD", f"Missing upstream get_repos.ps1: {get_repos}")
    result = run_command(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(get_repos)],
        cwd=ros2cs,
        env=env,
        log_file=log_file,
    )
    if result.exit_code != 0:
        raise Phase138BError(classify_output(result.output), "Jazzy dependency import failed")


def clean_colcon_outputs(checkout: pathlib.Path) -> None:
    """Remove colcon output directories under the checkout."""

    for name in ("build", "install", "log"):
        remove_known_subdir(checkout / name, checkout)


def has_complete_asset(checkout: pathlib.Path) -> bool:
    """Return whether the R2FU Unity asset contains real Windows native libraries."""

    asset = checkout / "install" / "asset" / "Ros2ForUnity"
    metadata = asset / "metadata_ros2cs.xml"
    plugins = asset / "Plugins" / "Windows" / "x86_64"
    native_libraries = list(plugins.glob("*.dll")) if plugins.exists() else []
    required_runtime = all((plugins / name).exists() for name in JAZZY_PIXI_RUNTIME_DLLS)
    return asset.exists() and metadata.exists() and plugins.exists() and bool(native_libraries) and required_runtime


def copy_jazzy_pixi_runtime_closure(
    asset: pathlib.Path,
    ros2_root: pathlib.Path,
    log_file: pathlib.Path,
) -> None:
    """Copy pixi runtime DLLs missed by upstream standalone deployment."""

    pixi_bin = ros2_root / ".pixi" / "envs" / "default" / "Library" / "bin"
    plugin_dir = asset / "Plugins" / "Windows" / "x86_64"
    ensure_dir(plugin_dir)

    copied: list[str] = []
    for name in JAZZY_PIXI_RUNTIME_DLLS:
        source = pixi_bin / name
        if not source.exists():
            raise Phase138BError("BLOCKED_NATIVE_DEPENDENCY", f"Missing Jazzy pixi runtime DLL: {source}")
        shutil.copy2(source, plugin_dir / name)
        copied.append(name)

    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write("\n# copied Jazzy pixi runtime closure DLLs\n")
        for name in copied:
            log.write(f"# runtime={name}\n")


def deploy_asset_from_successful_colcon(
    checkout: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    ros2_root: pathlib.Path,
) -> str:
    """Deploy a successfully built ros2cs install into the Unity asset layout."""

    python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    metadata = checkout / "src" / "scripts" / "metadata_generator.py"
    result = run_command([str(python), str(metadata), "--standalone"], cwd=checkout, env=env, log_file=log_file)
    if result.exit_code != 0:
        return classify_output(result.output)

    asset_root = checkout / "install" / "asset"
    asset = asset_root / "Ros2ForUnity"
    if asset.exists():
        shutil.rmtree(asset, onerror=lambda function, item, _exc_info: (os.chmod(item, stat.S_IWRITE), function(item)))
    ensure_dir(asset_root)
    shutil.copytree(checkout / "src" / "Ros2ForUnity", asset, dirs_exist_ok=True)

    deploy = checkout / "deploy_unity_plugins.ps1"
    plugin_path = asset / "Plugins"
    result = run_command(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(deploy), str(plugin_path)],
        cwd=checkout,
        env=env,
        log_file=log_file,
    )
    if result.exit_code != 0:
        return classify_output(result.output)

    metadata_file = checkout / "src" / "Ros2ForUnity" / "metadata_ros2cs.xml"
    if metadata_file.exists():
        ensure_dir(asset / "Plugins" / "Windows" / "x86_64")
        shutil.copy2(metadata_file, asset / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml")
        shutil.copy2(metadata_file, asset / "Plugins" / "metadata_ros2cs.xml")

    copy_jazzy_pixi_runtime_closure(asset, ros2_root, log_file)

    return "BUILD_ORCHESTRATOR_GREEN" if has_complete_asset(checkout) else "BLOCKED_R2FU_BUILD_SCRIPT"


def run_direct_colcon_build(
    checkout: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    ros2_root: pathlib.Path,
    generator: str,
) -> str:
    """Run direct colcon with explicit Python CMake arguments."""

    clean_colcon_outputs(checkout)
    python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    cmake_args = [
        "-DSTANDALONE_BUILD:int=1",
        "-DCMAKE_BUILD_TYPE=Release",
        "-DBUILD_TESTING:int=0",
        f"-DPython3_EXECUTABLE={python}",
        f"-DPYTHON_EXECUTABLE={python}",
    ]
    if generator == "ninja":
        cmake_args.insert(0, "-G")
        cmake_args.insert(1, "Ninja")

    colcon_path = shutil.which("colcon", path=env.get("Path") or env.get("PATH"))
    if not colcon_path:
        raise Phase138BError("BLOCKED_PYTHON_SELECTION", "colcon was not found in the pinned ROS2 Jazzy environment")
    command = [
        colcon_path,
        "build",
        "--merge-install",
        "--event-handlers",
        "console_direct+",
        "--cmake-args",
        *cmake_args,
        "--no-warn-unused-cli",
    ]
    result = run_command(command, cwd=checkout, env=env, log_file=log_file)
    if result.exit_code != 0:
        return classify_output(result.output)

    lower = result.output.lower()
    if "failed   <<<" in lower or "not processed" in lower or "cmake error" in lower or "modulenotfounderror" in lower:
        return classify_output(result.output)

    return deploy_asset_from_successful_colcon(checkout, env, log_file, ros2_root)


def run_upstream_build(
    checkout: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    ros2_root: pathlib.Path,
    generator: str,
) -> str:
    """Run the upstream R2FU standalone build script and classify the result."""

    build_script = checkout / "build.ps1"
    if not build_script.exists():
        raise Phase138BError("BLOCKED_R2FU_BUILD_SCRIPT", f"Missing upstream build.ps1: {build_script}")
    result = run_command(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(build_script), "-standalone", "-clean_install"],
        cwd=checkout,
        env=env,
        log_file=log_file,
    )
    if result.exit_code != 0:
        return classify_output(result.output)

    lower = result.output.lower()
    if (
        "failed   <<<" in lower
        or "cmake error" in lower
        or "modulenotfounderror" in lower
        or "not processed" in lower
        or "0 packages finished" in lower
    ):
        verdict = classify_output(result.output)
        if verdict == "BLOCKED_PYTHON_SELECTION":
            return run_direct_colcon_build(checkout, env, log_file, ros2_root, generator)
        return verdict

    asset = checkout / "install" / "asset" / "Ros2ForUnity"
    if asset.exists():
        copy_jazzy_pixi_runtime_closure(asset, ros2_root, log_file)

    if has_complete_asset(checkout):
        return "BUILD_ORCHESTRATOR_GREEN"
    return "BLOCKED_R2FU_BUILD_SCRIPT"


def write_evidence(
    evidence_path: pathlib.Path,
    *,
    verdict: str,
    log_file: pathlib.Path,
    repo_root: pathlib.Path,
    work_root: pathlib.Path,
    temp_root: pathlib.Path,
    ros2_root: pathlib.Path,
    vs_dev_cmd: pathlib.Path | None,
    effective_generator: str,
    no_build: bool,
) -> None:
    """Write the local Phase138B evidence note."""

    lines = [
        "---",
        'title: "Phase138B R2FU Jazzy Windows Build Toolchain Closure Evidence"',
        "tags:",
        "  - developer",
        "  - phase138b",
        "  - ros2-for-unity",
        "  - jazzy",
        "  - windows",
        "status: blocked" if verdict != "BUILD_ORCHESTRATOR_GREEN" else "status: green",
        f"updated: {_dt.date.today().isoformat()}",
        "---",
        "",
        "# Phase138B R2FU Jazzy Windows Build Toolchain Closure Evidence",
        "",
        "## Verdict",
        "",
        "```text",
        verdict,
        "```",
        "",
        "Python orchestrator was used. No project-owned PowerShell wrapper was created.",
        "",
        "## Paths",
        "",
        "```text",
        f"RepoRoot: {repo_root}",
        f"WorkRoot: {work_root}",
        f"Checkout: {work_root / CHECKOUT_DIR_NAME}",
        f"TempRoot: {temp_root}",
        f"Ros2Root: {ros2_root}",
        f"VsDevCmd: {vs_dev_cmd if vs_dev_cmd else 'not resolved'}",
        f"EffectiveGenerator: {effective_generator}",
        f"Log: {log_file}",
        "NoBuild: " + ("true" if no_build else "false"),
        "```",
        "",
        "## Required Diagnostics",
        "",
        "The log records:",
        "",
        "```text",
        "where python",
        "where pip",
        "where colcon",
        "where cmake",
        "where ninja",
        "where msbuild",
        "where cl",
        "catkin_pkg import check",
        "cl tiny compile check",
        "```",
        "",
        "## Boundary",
        "",
        "Phase138B does not import any partial R2FU asset into Unity.",
        "",
        "Phase138 runtime acceptance remains Phase106-only after a complete asset exists.",
        "",
    ]
    evidence_path.parent.mkdir(parents=True, exist_ok=True)
    evidence_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main(argv: list[str]) -> int:
    """Program entry point."""

    args = parse_args(argv)
    repo_root = pathlib.Path(args.repo_root).resolve()
    work_root = pathlib.Path(args.work_root).resolve()
    temp_root = pathlib.Path(args.temp_root).resolve()
    ros2_root = pathlib.Path(args.ros2_root).resolve()
    log_dir = repo_root / "build" / "tmp"
    ensure_dir(log_dir)
    ensure_dir(temp_root)
    log_file = log_dir / f"{args.log_prefix}_{timestamp()}.log"
    evidence_path = (
        (repo_root / args.evidence_path).resolve()
        if args.evidence_path and not pathlib.Path(args.evidence_path).is_absolute()
        else pathlib.Path(args.evidence_path).resolve()
        if args.evidence_path
        else repo_root / "Developer" / "87 Phase138B R2FU Jazzy Windows Build Toolchain Closure Evidence.md"
    )
    vs_dev_cmd: pathlib.Path | None = None
    effective_generator = "unresolved"

    try:
        assert_safe_root("WorkRoot", work_root, repo_root)
        assert_safe_root("TempRoot", temp_root, repo_root)
        if args.clean:
            ensure_dir(work_root)
            ensure_dir(temp_root)
            remove_known_subdir(work_root / CHECKOUT_DIR_NAME, work_root)
            remove_known_subdir(work_root / "ros2-for-unity", work_root)

        env = dict(os.environ)
        vs_dev_cmd = resolve_vs_dev_cmd(args.vs_dev_cmd, env, log_file)
        env = capture_cmd_environment(vs_dev_cmd, env, log_file)
        env = scrub_environment(env, ros2_root, temp_root)
        env = capture_ros2_environment(ros2_root, env, log_file)
        record_where_diagnostics(env, repo_root, log_file)
        verify_python(ros2_root, env, log_file)
        verify_cl_compile(temp_root, env, log_file)
        effective_generator = resolve_cmake_generator(args.generator, env, log_file)

        verdict = "BLOCKED_UNKNOWN_TOOLCHAIN"
        if args.no_build:
            with log_file.open("a", encoding="utf-8", errors="replace") as log:
                log.write("\n# --no-build was set; build execution intentionally skipped.\n")
        else:
            checkout = ensure_r2fu_checkout(work_root, env, log_file, clean=False)
            patch_ros2cs_jazzy_windows_standalone(checkout, log_file)
            run_jazzy_dependency_import(checkout, env, log_file)
            verdict = run_upstream_build(checkout, env, log_file, ros2_root, effective_generator)

        write_evidence(
            evidence_path,
            verdict=verdict,
            log_file=log_file,
            repo_root=repo_root,
            work_root=work_root,
            temp_root=temp_root,
            ros2_root=ros2_root,
            vs_dev_cmd=vs_dev_cmd,
            effective_generator=effective_generator,
            no_build=args.no_build,
        )
        print(verdict)
        print(f"log={log_file}")
        print(f"evidence={evidence_path}")
        return 0 if verdict == "BUILD_ORCHESTRATOR_GREEN" or args.no_build else 2
    except Phase138BError as exc:
        verdict = exc.verdict if exc.verdict in VERDICTS else "BLOCKED_UNKNOWN_TOOLCHAIN"
        write_evidence(
            evidence_path,
            verdict=verdict,
            log_file=log_file,
            repo_root=repo_root,
            work_root=work_root,
            temp_root=temp_root,
            ros2_root=ros2_root,
            vs_dev_cmd=vs_dev_cmd,
            effective_generator=effective_generator,
            no_build=args.no_build,
        )
        print(verdict)
        print(str(exc), file=sys.stderr)
        print(f"log={log_file}")
        print(f"evidence={evidence_path}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
