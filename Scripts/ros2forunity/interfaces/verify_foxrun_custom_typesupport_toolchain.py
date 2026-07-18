"""Non-mutating Phase181 Win64 custom-typesupport toolchain preflight.

The tool accepts explicit ROS2 and source roots, builds a minimal child
environment, and probes only the selected toolchain. It never downloads a
tool, sources a user shell profile, or writes below ``Packages/``. Successful
results are recorded as redacted provenance below ``build/phase181``.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping, Sequence


SUPPORTED_DISTROS = ("humble", "jazzy", "lyrical")
DEFAULT_GENERATOR = "Visual Studio 17 2022"
REQUIRED_ROSIDL_MODULES = (
    "rosidl_adapter",
    "rosidl_generator_c",
    "rosidl_generator_cpp",
    "rosidl_typesupport_c",
)
ERROR_CODE = "FOXRUN_TOOLCHAIN001"


@dataclass(frozen=True)
class ProcessResult:
    """Captured output from one explicit, shell-free process probe."""

    return_code: int
    stdout: str
    stderr: str


@dataclass(frozen=True)
class ToolchainPreflightRequest:
    """All roots are explicit so no user profile or global ROS setup is read."""

    distro: str
    ros2_root: Path
    ros2cs_source: Path
    r2fu_source: Path
    build_root: Path
    generator: str = DEFAULT_GENERATOR
    vswhere: Path | None = None
    dotnet: Path | None = None


@dataclass(frozen=True)
class ToolchainPreflightResult:
    ready: bool
    distro: str
    generator: str
    requirements: tuple[dict[str, str], ...]


class ToolchainPreflightError(RuntimeError):
    """A bounded, operator-actionable preflight failure without local paths."""

    def __init__(self, remediation: str):
        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


ProbeRunner = Callable[[Sequence[str], Mapping[str, str]], ProcessResult]


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

    result = ToolchainPreflightResult(
        ready=True,
        distro=request.distro,
        generator=request.generator,
        requirements=tuple(requirement_rows),
    )
    _write_provenance(request, result)
    return result


def build_toolchain_environment(request: ToolchainPreflightRequest) -> dict[str, str]:
    """Return a controlled child environment for one ROS2 distribution.

    The host environment supplies only Windows process essentials. ROS paths,
    Python paths, compiler selection, and RMW choice are selected by this
    request; no setup script or arbitrary profile is sourced.
    """

    root = request.ros2_root
    pixi = root / ".pixi" / "envs" / "default"
    inherited_keys = (
        # dotnet/NuGet resolves its user config and SDK roots through these
        # Windows process variables. They are process essentials, not a shell
        # profile or ROS configuration.
        "APPDATA",
        "ComSpec",
        "HOMEDRIVE",
        "HOMEPATH",
        "NUMBER_OF_PROCESSORS",
        "PATHEXT",
        "ProgramFiles",
        "SystemDrive",
        "SystemRoot",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR",
    )
    environment = {key: os.environ[key] for key in inherited_keys if os.environ.get(key)}
    environment["ROS_DISTRO"] = request.distro
    environment["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    environment["AMENT_PREFIX_PATH"] = str(root)
    environment["CMAKE_PREFIX_PATH"] = str(root)
    environment["COLCON_PREFIX_PATH"] = str(root)
    environment["PYTHONPATH"] = str(root / "Lib" / "site-packages")
    path_entries = (
        pixi / "Scripts",
        pixi / "Library" / "bin",
        root / "Scripts",
        root / "bin",
        request.dotnet.parent if request.dotnet else Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "dotnet",
        Path(environment.get("SystemRoot", r"C:\Windows")) / "System32",
    )
    environment["PATH"] = os.pathsep.join(str(entry) for entry in path_entries)
    return environment


def _normalize_request(request: ToolchainPreflightRequest) -> ToolchainPreflightRequest:
    distro = (request.distro or "").strip().lower()
    if distro not in SUPPORTED_DISTROS:
        raise ToolchainPreflightError("select-supported-ros-distro")
    if not (request.generator or "").strip():
        raise ToolchainPreflightError("select-supported-cmake-generator")
    return ToolchainPreflightRequest(
        distro=distro,
        ros2_root=Path(request.ros2_root),
        ros2cs_source=Path(request.ros2cs_source),
        r2fu_source=Path(request.r2fu_source),
        build_root=Path(request.build_root),
        generator=request.generator.strip(),
        vswhere=Path(request.vswhere) if request.vswhere else None,
        dotnet=Path(request.dotnet) if request.dotnet else Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "dotnet" / "dotnet.exe",
    )


def _require_directory(path: Path, remediation: str, rows: list[dict[str, str]], label: str) -> None:
    if not path.is_dir():
        raise ToolchainPreflightError(remediation)
    rows.append(_ready(label, "declared"))


def _require_source_root(path: Path, remediation: str, rows: list[dict[str, str]], label: str) -> None:
    if not path.is_dir() or not (path / "src").is_dir():
        raise ToolchainPreflightError(remediation)
    rows.append(_ready(label, "declared-source"))


def _resolve_pixi_tools(ros2_root: Path, rows: list[dict[str, str]]) -> tuple[Path, Path, Path]:
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
    candidate = request.dotnet or Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet" / "dotnet.exe"
    if not candidate.is_file():
        raise ToolchainPreflightError("install-dotnet-sdk-for-ros2cs")
    rows.append(_ready("dotnet", "available"))
    return candidate


def _resolve_vswhere(request: ToolchainPreflightRequest, rows: list[dict[str, str]]) -> Path:
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


def _write_provenance(request: ToolchainPreflightRequest, result: ToolchainPreflightResult) -> None:
    target = request.build_root / "phase181" / request.distro / "provenance" / "toolchain.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "schemaVersion": 1,
        "distro": result.distro,
        "generator": result.generator,
        "requirements": list(result.requirements),
    }
    temporary = target.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    os.replace(temporary, target)


def _ready(label: str, detail: str) -> dict[str, str]:
    return {"label": label, "status": "ready", "detail": _bounded(detail)}


def _first_line(value: str) -> str:
    return _bounded((value or "").strip().splitlines()[0] if (value or "").strip() else "available")


def _bounded(value: str) -> str:
    return " ".join((value or "").split())[:160]


def _default_runner(argv: Sequence[str], environment: Mapping[str, str]) -> ProcessResult:
    completed = subprocess.run(
        tuple(str(value) for value in argv),
        capture_output=True,
        check=False,
        cwd=None,
        env=dict(environment),
        shell=False,
        text=True,
    )
    return ProcessResult(completed.returncode, completed.stdout or "", completed.stderr or "")


def _repository_root() -> Path:
    return Path(__file__).resolve().parents[3]


def parse_args(argv: Sequence[str] | None = None) -> ToolchainPreflightRequest:
    root = _repository_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", choices=SUPPORTED_DISTROS, required=True)
    parser.add_argument("--ros2-root", type=Path)
    parser.add_argument("--ros2cs-source", type=Path, required=True)
    parser.add_argument("--r2fu-source", type=Path, required=True)
    parser.add_argument("--build-root", type=Path, default=root / "build")
    parser.add_argument("--generator", default=DEFAULT_GENERATOR)
    parser.add_argument("--vswhere", type=Path)
    parser.add_argument("--dotnet", type=Path)
    args = parser.parse_args(argv)
    ros2_root = args.ros2_root or root / "ros2-windows" / ("ros2_" + args.distro)
    return ToolchainPreflightRequest(
        distro=args.distro,
        ros2_root=ros2_root,
        ros2cs_source=args.ros2cs_source,
        r2fu_source=args.r2fu_source,
        build_root=args.build_root,
        generator=args.generator,
        vswhere=args.vswhere,
        dotnet=args.dotnet,
    )


def main(argv: Sequence[str] | None = None) -> int:
    try:
        result = preflight_toolchain(parse_args(argv))
    except ToolchainPreflightError as error:
        print(error.code + ": " + error.remediation, file=sys.stderr)
        return 1
    print("PASS: " + result.distro + " / " + result.generator)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
