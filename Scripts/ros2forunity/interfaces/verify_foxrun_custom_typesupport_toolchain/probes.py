#!/usr/bin/env python3
"""Non-mutating Phase181 Win64 custom-typesupport toolchain preflight.

The tool accepts explicit ROS2 and source roots, builds a minimal child
environment, and probes only the selected toolchain. It never downloads a
tool, sources a user shell profile, or writes below ``Packages/``. Successful
results are recorded as redacted provenance below ``build/phase181``.
"""

from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "verify_foxrun_custom_typesupport_toolchain.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import json
from pathlib import Path
from typing import Mapping

from .environment import _normalize_request, build_toolchain_environment
from .models import (
    REQUIRED_ROSIDL_MODULES,
    ProbeRunner,
    ToolchainPreflightError,
    ToolchainPreflightRequest,
    ToolchainPreflightResult,
)
from .provenance import _toolchain_input_identity, _write_provenance

def preflight_toolchain(
    request: ToolchainPreflightRequest,
    *,
    runner: ProbeRunner | None = None,
) -> ToolchainPreflightResult:
    """Probe the selected Windows toolchain and write redacted provenance.

    The only write is a successful provenance JSON below the caller-owned
    build root. Every probe uses explicit executable paths and ``shell=False``.
    """

    request = _normalize_request(request)
    runner = runner or _default_runner
    environment = build_toolchain_environment(request)
    requirement_rows: list[dict[str, str]] = []

    _require_directory(request.ros2_root, "provide-ros2-root", requirement_rows, "ros2-root")
    _require_source_root(request.ros2cs_source, "provide-ros2cs-source", requirement_rows, "ros2cs-source")
    _require_source_root(request.r2fu_source, "provide-r2fu-source", requirement_rows, "r2fu-source")

    python, colcon, cmake = _resolve_pixi_tools(request.ros2_root, requirement_rows)
    _require_pinned_openssl(request.ros2_root, requirement_rows)
    dotnet = _resolve_dotnet(request, requirement_rows)
    vswhere = _resolve_vswhere(request, requirement_rows)
    visual_studio_root = _probe_visual_studio(vswhere, runner, environment, requirement_rows)
    compiler, msbuild = _resolve_msvc_tools(visual_studio_root)
    _probe_compiler(compiler, msbuild, runner, environment, requirement_rows)
    _probe_cmake(cmake, request.generator, runner, environment, requirement_rows)
    _probe_colcon(colcon, runner, environment, requirement_rows)
    _probe_dotnet(dotnet, runner, environment, requirement_rows)
    _probe_rosidl_modules(python, runner, environment, requirement_rows)

    input_identity = _toolchain_input_identity(
        request,
        {
            "python": python,
            "colcon": colcon,
            "cmake": cmake,
            "dotnet": dotnet,
            "vswhere": vswhere,
            "compiler": compiler,
            "msbuild": msbuild,
            "visualStudio": visual_studio_root,
        },
    )
    result = ToolchainPreflightResult(
        ready=True,
        distro=request.distro,
        generator=request.generator,
        requirements=tuple(requirement_rows),
        input_identity=input_identity,
    )
    _write_provenance(request, result)
    return result

def _require_directory(path: Path, remediation: str, rows: list[dict[str, str]], label: str) -> None:
    """Implement the internal require directory step."""
    if not path.is_dir():
        raise ToolchainPreflightError(remediation)
    rows.append(_ready(label, "declared"))


def _require_source_root(path: Path, remediation: str, rows: list[dict[str, str]], label: str) -> None:
    """Implement the internal require source root step."""
    if not path.is_dir() or not (path / "src").is_dir():
        raise ToolchainPreflightError(remediation)
    rows.append(_ready(label, "declared-source"))


def _resolve_pixi_tools(ros2_root: Path, rows: list[dict[str, str]]) -> tuple[Path, Path, Path]:
    """Implement the internal resolve pixi tools step."""
    pixi = ros2_root / ".pixi" / "envs" / "default"
    python = pixi / "python.exe"
    colcon = pixi / "Scripts" / "colcon.exe"
    cmake = pixi / "Library" / "bin" / "cmake.exe"
    for path, label, remediation in (
        (python, "python", "repair-pinned-python"),
        (colcon, "colcon", "repair-pinned-colcon"),
        (cmake, "cmake", "repair-pinned-cmake"),
    ):
        if not path.is_file():
            raise ToolchainPreflightError(remediation)
        rows.append(_ready(label, "pinned"))
    return python, colcon, cmake


def _require_pinned_openssl(ros2_root: Path, rows: list[dict[str, str]]) -> None:
    """Require the selected ROS/Pixi OpenSSL prefix used by rosidl CMake."""

    library_root = ros2_root / ".pixi" / "envs" / "default" / "Library"
    required = (
        library_root / "include" / "openssl" / "ssl.h",
        library_root / "lib" / "libcrypto.lib",
    )
    if not all(path.is_file() for path in required):
        raise ToolchainPreflightError("repair-pinned-openssl")
    rows.append(_ready("openssl", "pinned"))


def _resolve_dotnet(request: ToolchainPreflightRequest, rows: list[dict[str, str]]) -> Path:
    """Implement the internal resolve dotnet step."""
    candidate = request.dotnet or Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet" / "dotnet.exe"
    if not candidate.is_file():
        raise ToolchainPreflightError("install-dotnet-sdk-for-ros2cs")
    rows.append(_ready("dotnet", "available"))
    return candidate


def _resolve_vswhere(request: ToolchainPreflightRequest, rows: list[dict[str, str]]) -> Path:
    """Implement the internal resolve vswhere step."""
    if request.vswhere:
        candidate = request.vswhere
    else:
        program_files_x86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
        candidate = Path(program_files_x86) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    if not candidate.is_file():
        raise ToolchainPreflightError("install-msvc-build-tools")
    rows.append(_ready("vswhere", "available"))
    return candidate


def _probe_visual_studio(
    vswhere: Path,
    runner: ProbeRunner,
    environment: Mapping[str, str],
    rows: list[dict[str, str]],
) -> Path:
    """Implement the internal probe visual studio step."""
    result = runner(
        (
            str(vswhere),
            "-latest",
            "-products",
            "*",
            "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property",
            "installationPath",
        ),
        environment,
    )
    if result.return_code != 0 or not result.stdout.strip():
        raise ToolchainPreflightError("install-msvc-build-tools")
    root = Path(result.stdout.strip().splitlines()[0].strip())
    if not (root / "Common7" / "Tools" / "VsDevCmd.bat").is_file():
        raise ToolchainPreflightError("repair-vsdevcmd")
    rows.append(_ready("msvc", "available"))
    return root


def _resolve_msvc_tools(visual_studio_root: Path) -> tuple[Path, Path]:
    """Implement the internal resolve msvc tools step."""
    tool_root = visual_studio_root / "VC" / "Tools" / "MSVC"
    versions = sorted((path for path in tool_root.iterdir() if path.is_dir()), key=lambda path: path.name)
    compiler = versions[-1] / "bin" / "Hostx64" / "x64" / "cl.exe" if versions else Path()
    msbuild = visual_studio_root / "MSBuild" / "Current" / "Bin" / "MSBuild.exe"
    if not compiler.is_file() or not msbuild.is_file():
        raise ToolchainPreflightError("repair-msvc-build-tools")
    return compiler, msbuild


def _probe_compiler(
    compiler: Path,
    msbuild: Path,
    runner: ProbeRunner,
    environment: Mapping[str, str],
    rows: list[dict[str, str]],
) -> None:
    """Verify explicit MSVC and MSBuild commands without sourcing a profile."""
    # MSVC's ``cl /Bv`` reports version data but returns 2 without a source
    # filename. Probe the explicit compiler path directly, then independently
    # require a successful explicit MSBuild version command. ``VsDevCmd`` is
    # still verified during discovery, but no batch profile is executed.
    compiler_result = runner((str(compiler), "/Bv"), environment)
    compiler_output = "\n".join(
        value for value in (compiler_result.stdout, compiler_result.stderr) if value
    )
    if compiler_result.return_code not in (0, 2) or "Compiler" not in compiler_output:
        raise ToolchainPreflightError("repair-msvc-command-prompt")
    msbuild_result = runner((str(msbuild), "-version"), environment)
    if msbuild_result.return_code != 0 or not msbuild_result.stdout.strip():
        raise ToolchainPreflightError("repair-msvc-command-prompt")
    rows.append(_ready("cl-msbuild", _first_line(compiler_output)))


def _probe_cmake(
    cmake: Path,
    generator: str,
    runner: ProbeRunner,
    environment: Mapping[str, str],
    rows: list[dict[str, str]],
) -> None:
    """Implement the internal probe cmake step."""
    version = runner((str(cmake), "--version"), environment)
    if version.return_code != 0 or not version.stdout.strip():
        raise ToolchainPreflightError("repair-pinned-cmake")
    rows.append(_ready("cmake-version", _first_line(version.stdout)))
    capabilities = runner((str(cmake), "-E", "capabilities"), environment)
    try:
        payload = json.loads(capabilities.stdout) if capabilities.return_code == 0 else {}
        available = {item.get("name") for item in payload.get("generators", []) if isinstance(item, dict)}
    except json.JSONDecodeError:
        available = set()
    if generator not in available:
        raise ToolchainPreflightError("select-supported-cmake-generator")
    rows.append(_ready("cmake-generator", generator))


def _probe_colcon(
    colcon: Path,
    runner: ProbeRunner,
    environment: Mapping[str, str],
    rows: list[dict[str, str]],
) -> None:
    """Implement the internal probe colcon step."""
    result = runner((str(colcon), "--help"), environment)
    if result.return_code != 0 or "usage:" not in result.stdout.lower():
        raise ToolchainPreflightError("repair-pinned-colcon")
    rows.append(_ready("colcon", "available"))


def _probe_dotnet(
    dotnet: Path,
    runner: ProbeRunner,
    environment: Mapping[str, str],
    rows: list[dict[str, str]],
) -> None:
    """Implement the internal probe dotnet step."""
    result = runner((str(dotnet), "--version"), environment)
    if result.return_code != 0 or not result.stdout.strip():
        raise ToolchainPreflightError("install-dotnet-sdk-for-ros2cs")
    rows.append(_ready("dotnet-sdk", _first_line(result.stdout)))


def _probe_rosidl_modules(
    python: Path,
    runner: ProbeRunner,
    environment: Mapping[str, str],
    rows: list[dict[str, str]],
) -> None:
    """Implement the internal probe rosidl modules step."""
    names = json.dumps(REQUIRED_ROSIDL_MODULES)
    command = (
        "import importlib.util,json; "
        + "names=" + names + "; "
        + "print(json.dumps({name: bool(importlib.util.find_spec(name)) for name in names}, sort_keys=True))"
    )
    result = runner((str(python), "-c", command), environment)
    try:
        modules = json.loads(result.stdout) if result.return_code == 0 else {}
    except json.JSONDecodeError:
        modules = {}
    if not all(modules.get(name) is True for name in REQUIRED_ROSIDL_MODULES):
        raise ToolchainPreflightError("repair-rosidl-python-modules")
    rows.append(_ready("rosidl-python", "available"))

__all__ = [name for name in globals() if not name.startswith("__")]
