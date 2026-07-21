#!/usr/bin/env python3
"""Build and characterize the Phase181 static ROS2 interface out of tree.

This is deliberately a characterization gate, not package generation.  It
uses an explicitly selected ROS2 root, ros2cs overlay, and Visual Studio
installation to build the source-only package into ``build/phase181``.  The
only products are machine-local evidence and candidate files below that build
root; no Unity package is modified here.
"""

from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator, Mapping, Sequence

try:
    from verify_foxrun_custom_typesupport_toolchain import (
        ToolchainPreflightRequest,
        build_toolchain_environment,
        preflight_toolchain,
    )
except ModuleNotFoundError:  # pragma: no cover - package import test path
    from Scripts.ros2forunity.interfaces.verify_foxrun_custom_typesupport_toolchain import (
        ToolchainPreflightRequest,
        build_toolchain_environment,
        preflight_toolchain,
    )


ERROR_CODE = "FOXRUN_TOOLCHAIN002"
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
MANAGED_ASSEMBLY_NAME = ROS_PACKAGE_NAME + "_assembly"
ENVELOPE_TYPE_NAME = ROS_PACKAGE_NAME + ".msg.Phase181State48D288ED82F1Envelope"
ROS2_MESSAGE_IDENTITY = "ROS2.Message"
_LONGEST_NATIVE_TARGET = "unity2foxglove_foxrun_interfaces_v1__rosidl_typesupport_introspection_c__pyext.dir"
_LONGEST_NATIVE_OBJECT = "95441c87d059a3e1deffafe69425029c/_unity2foxglove_foxrun_interfaces_v1_s.ep.rosidl_typesupport_introspection_c.c.obj"
_WORKSPACE_DELETE_ATTEMPTS = 5
_WORKSPACE_DELETE_RETRY_SECONDS = 1.0


class CharacterizationError(RuntimeError):
    """A bounded, actionable custom-interface characterization failure."""

    def __init__(self, remediation: str):
        """Initialize this object."""
        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class CharacterizationRequest:
    """Represent CharacterizationRequest."""
    distro: str
    static_package: Path
    ros2_root: Path
    ros2cs_source: Path
    ros2cs_install: Path
    r2fu_source: Path
    build_root: Path
    generator: str = "Ninja"
    dotnet: Path | None = None
    workspace_name: str = "c"


@dataclass(frozen=True)
class CharacterizationResult:
    """Represent CharacterizationResult."""
    distro: str
    evidence_path: Path
    managed_assembly: Path
    native_libraries: tuple[Path, ...]


def _require_under(path: Path, root: Path) -> Path:
    """Implement the internal require under step."""
    resolved = path.resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError as exc:
        raise CharacterizationError("use-phase181-build-root") from exc
    return resolved


def characterization_root(request: CharacterizationRequest) -> Path:
    """Select the short, private build root for one characterization run."""
    # rosidl target names are deliberately descriptive and can otherwise push
    # MSVC object/PDB paths past the Windows limit. Keep this private build
    # layout short while retaining the public evidence boundary below
    # ``build/phase181/<distro>/``.
    workspace_name = request.workspace_name or ""
    if re.fullmatch(r"[a-z0-9][a-z0-9_-]*", workspace_name) is None:
        raise CharacterizationError("select-safe-phase181-workspace-name")
    return _characterization_parent(request) / workspace_name


def _characterization_parent(request: CharacterizationRequest) -> Path:
    """Implement the internal characterization parent step."""
    return request.build_root / "phase181" / request.distro


def requires_short_windows_build_alias(characterization_parent: Path) -> bool:
    """Return whether the deepest rosidl object would exceed safe MSVC space."""

    if os.name != "nt":
        return False
    projected = (
        characterization_parent
        / "b"
        / ROS_PACKAGE_NAME
        / "CMakeFiles"
        / _LONGEST_NATIVE_TARGET
        / _LONGEST_NATIVE_OBJECT
    )
    # CMake's Windows generator limit is 250 characters. Mapping the physical
    # workspace itself removes the complete ``build/phase181/<distro>/c``
    # prefix when needed.
    return len(str(projected)) > 250


def _remove_prior_workspace(root: Path) -> None:
    """Remove a build-owned workspace, tolerating a short-lived Ninja lock."""

    for attempt in range(_WORKSPACE_DELETE_ATTEMPTS):
        try:
            shutil.rmtree(root)
            return
        except PermissionError as exc:
            is_transient_windows_lock = getattr(exc, "winerror", None) == 32
            if not is_transient_windows_lock or attempt + 1 == _WORKSPACE_DELETE_ATTEMPTS:
                raise CharacterizationError("close-build-handles-and-retry-characterization") from exc
            time.sleep(_WORKSPACE_DELETE_RETRY_SECONDS)


def prepare_characterization_workspace(
    request: CharacterizationRequest,
    *,
    replace_existing: bool = False,
    _workspace_root: Path | None = None,
) -> Path:
    """Stage the static package into a controlled out-of-tree colcon source."""

    source = request.static_package / "Ros2Package~"
    if not (source / "package.xml").is_file() or not (source / "CMakeLists.txt").is_file():
        raise CharacterizationError("provide-locked-static-interface-source")
    root = _workspace_root or characterization_root(request)
    if _workspace_root is None:
        _require_under(root, request.build_root)
    if root.exists():
        if not replace_existing:
            raise CharacterizationError("remove-or-replace-prior-characterization")
        _remove_prior_workspace(root)
    workspace = root
    destination = workspace / "s" / ROS_PACKAGE_NAME
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, destination)
    return workspace


@contextmanager
def _temporary_short_workspace_root(request: CharacterizationRequest) -> Iterator[tuple[Path, Path]]:
    """Map only this verified workspace root when Windows path length demands it.

    The mapping is a temporary alias of the exact physical
    ``build/phase181/<distro>/c`` directory; it neither moves nor copies files.
    It is removed in ``finally`` and never replaces a pre-existing drive.
    """

    physical_workspace = characterization_root(request)
    _require_under(physical_workspace, request.build_root)
    physical_workspace.mkdir(parents=True, exist_ok=True)
    if not requires_short_windows_build_alias(physical_workspace):
        yield physical_workspace, physical_workspace
        return

    subst = Path(os.environ.get("SystemRoot", r"C:\\Windows")) / "System32" / "subst.exe"
    if not subst.is_file():
        raise CharacterizationError("provide-short-windows-build-root")

    mapped_drive: str | None = None
    for letter in "ZYXWVUTSRQPONMLKJIHGFED":
        candidate = Path(letter + ":\\")
        if candidate.exists():
            continue
        result = subprocess.run(
            (str(subst), letter + ":", str(physical_workspace)),
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            check=False,
        )
        if result.returncode == 0 and candidate.exists():
            mapped_drive = letter
            break
    if mapped_drive is None:
        raise CharacterizationError("provide-short-windows-build-root")

    mapped_workspace = Path(mapped_drive + ":\\")
    try:
        yield physical_workspace, mapped_workspace
    finally:
        subprocess.run(
            (str(subst), mapped_drive + ":", "/D"),
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            check=False,
        )


def build_colcon_command(
    request: CharacterizationRequest,
    workspace: Path,
    *,
    colcon: str = "colcon",
    python: str = "python",
) -> tuple[str, ...]:
    """Return the explicit out-of-tree rosidl build command without a shell."""

    cmake_python = python.replace("\\", "/")
    openssl_root = (request.ros2_root / ".pixi" / "envs" / "default" / "Library").as_posix()

    return (
        colcon,
        "--log-base",
        str(workspace / "l"),
        "build",
        "--merge-install",
        "--base-paths",
        str(workspace / "s"),
        "--build-base",
        str(workspace / "b"),
        "--install-base",
        str(workspace / "i"),
        "--event-handlers",
        "console_direct+",
        "--cmake-args",
        "-G",
        request.generator,
        "-DCMAKE_BUILD_TYPE=Release",
        "-DBUILD_TESTING=OFF",
        "-DSTANDALONE_BUILD:BOOL=ON",
        "-DPython3_EXECUTABLE=" + cmake_python,
        "-DPYTHON_EXECUTABLE=" + cmake_python,
        "-DOPENSSL_ROOT_DIR=" + openssl_root,
        "--no-warn-unused-cli",
    )


def inspect_managed_evidence(
    evidence_path: Path,
    expected_assembly: str,
    expected_envelope_type: str,
    expected_ros2_message_identity: str,
) -> dict[str, object]:
    """Validate only the identity facts that make custom output usable."""

    try:
        payload = json.loads(evidence_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise CharacterizationError("repair-managed-characterization-evidence") from exc
    assembly = payload.get("managedAssembly")
    messages = payload.get("messages")
    if not isinstance(assembly, dict) or assembly.get("name") != expected_assembly:
        raise CharacterizationError("repair-generated-managed-assembly-name")
    if payload.get("ros2MessageIdentity") != expected_ros2_message_identity:
        raise CharacterizationError("repair-ros2cs-message-identity")
    if not isinstance(messages, list):
        raise CharacterizationError("repair-managed-characterization-evidence")
    envelope = next(
        (item for item in messages if isinstance(item, dict) and item.get("fullName") == expected_envelope_type),
        None,
    )
    if not isinstance(envelope, dict):
        raise CharacterizationError("repair-generated-envelope-type")
    interfaces = envelope.get("interfaces")
    constructors = envelope.get("constructors")
    if not isinstance(interfaces, list) or expected_ros2_message_identity not in interfaces:
        raise CharacterizationError("repair-ros2cs-message-identity")
    if not isinstance(constructors, list) or ".ctor()" not in constructors:
        raise CharacterizationError("repair-generated-envelope-constructor")
    if envelope.get("disposable") is not True:
        raise CharacterizationError("repair-generated-envelope-dispose")
    return payload


def _capture_msvc_environment(base_environment: Mapping[str, str]) -> dict[str, str]:
    """Capture only the explicitly discovered Visual Studio x64 tool variables.

    ``VsDevCmd.bat`` is a Visual Studio tool activator found through ``vswhere``;
    it is not a user shell profile.  The captured result is whitelisted and
    folded into the caller-owned environment rather than inheriting arbitrary
    host environment state.
    """

    vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    if not vswhere.is_file():
        raise CharacterizationError("install-msvc-build-tools")
    discovered = subprocess.run(
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
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        env=dict(base_environment),
        check=False,
    )
    root_text = discovered.stdout.strip().splitlines()
    if discovered.returncode != 0 or not root_text:
        raise CharacterizationError("install-msvc-build-tools")
    visual_studio = Path(root_text[0].strip())
    command = visual_studio / "Common7" / "Tools" / "VsDevCmd.bat"
    if not command.is_file() or '"' in str(command) or "%" in str(command):
        raise CharacterizationError("repair-vsdevcmd")
    # On Windows, passing this as a process command line (not ``shell=True``)
    # preserves the one quoted batch path. Passing a list would cause Python's
    # list2cmdline to escape the nested quotes before cmd.exe sees them.
    command_line = (
        f'"{os.environ.get("ComSpec", r"C:\Windows\System32\cmd.exe")}" '
        f'/d /s /c call "{command}" -arch=x64 -host_arch=x64 >nul && set'
    )
    result = subprocess.run(
        command_line,
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        env=dict(base_environment),
        check=False,
    )
    if result.returncode != 0:
        raise CharacterizationError("repair-msvc-command-prompt")
    allowed_names = {
        "DevEnvDir",
        "Framework40Version",
        "FrameworkDir",
        "FrameworkDir32",
        "FrameworkDir64",
        "FrameworkVersion",
        "FrameworkVersion32",
        "FrameworkVersion64",
        "INCLUDE",
        "LIB",
        "LIBPATH",
        "NETFXSDKDir",
        "UCRTVersion",
        "VCIDEInstallDir",
        "VCINSTALLDIR",
        "VCToolsInstallDir",
        "VCToolsRedistDir",
        "VCToolsVersion",
        "VisualStudioVersion",
        "VSINSTALLDIR",
        "WindowsLibPath",
        "WindowsSdkBinPath",
        "WindowsSdkDir",
        "WindowsSDKLibVersion",
        "WindowsSDKVersion",
    }
    captured: dict[str, str] = {}
    visual_path: str | None = None
    for line in result.stdout.splitlines():
        if "=" not in line:
            continue
        name, value = line.split("=", 1)
        if name in allowed_names:
            captured[name] = value
        elif name.lower() == "path":
            visual_path = value
    if not captured.get("INCLUDE") or not captured.get("LIB") or not visual_path:
        raise CharacterizationError("repair-msvc-command-prompt")
    allowed_path_entries = [
        entry
        for entry in visual_path.split(os.pathsep)
        if "microsoft visual studio" in entry.lower() or "windows kits" in entry.lower()
    ]
    if not allowed_path_entries:
        raise CharacterizationError("repair-msvc-command-prompt")
    captured["PATH"] = os.pathsep.join(allowed_path_entries)
    return captured


def build_characterization_environment(request: CharacterizationRequest) -> dict[str, str]:
    """Construct a ROS/ros2cs/MSVC environment from declared roots only."""

    base = build_toolchain_environment(
        ToolchainPreflightRequest(
            distro=request.distro,
            ros2_root=request.ros2_root,
            ros2cs_source=request.ros2cs_source,
            r2fu_source=request.r2fu_source,
            build_root=request.build_root,
            generator=request.generator,
            dotnet=request.dotnet,
        )
    )
    if not (request.ros2cs_install / "share" / "rosidl_generator_cs").is_dir():
        raise CharacterizationError("provide-matching-ros2cs-install")
    environment = dict(base)
    toolchain_path = environment["PATH"]
    environment.update(_capture_msvc_environment(base))
    pixi = request.ros2_root / ".pixi" / "envs" / "default"
    path_entries = (
        environment["PATH"],
        toolchain_path,
        request.ros2cs_install / "bin",
        request.ros2cs_install / "lib",
        request.ros2_root / "bin",
        request.ros2_root / "Scripts",
        pixi / "Scripts",
        pixi / "Library" / "bin",
        Path(base["SystemRoot"]) / "System32",
        Path(base["SystemRoot"]) / "System32" / "WindowsPowerShell" / "v1.0",
    )
    environment["PATH"] = os.pathsep.join(str(entry) for entry in path_entries)
    # ros2cs is a complete overlay after its explicit install preflight: each
    # dependency exports native ROSIDL headers/targets *and* its managed
    # assembly.  Layer it before the base ROS2 root so custom generated C# and
    # native code resolve the same package revision.
    prefix_entries = (request.ros2cs_install, request.ros2_root)
    environment["AMENT_PREFIX_PATH"] = os.pathsep.join(str(entry) for entry in prefix_entries)
    environment["CMAKE_PREFIX_PATH"] = os.pathsep.join(str(entry) for entry in prefix_entries)
    environment["COLCON_PREFIX_PATH"] = os.pathsep.join(str(entry) for entry in prefix_entries)
    environment["PYTHONPATH"] = os.pathsep.join(
        str(entry)
        for entry in (request.ros2cs_install / "Lib" / "site-packages", request.ros2_root / "Lib" / "site-packages")
    )
    environment["COLCON_PYTHON_EXECUTABLE"] = str(pixi / "python.exe")
    environment["CMAKE_GENERATOR"] = request.generator
    environment["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    # rosidl templates include UTF-8 source text.  Windows' active code page
    # must not determine how the pinned Python opens those templates.
    environment["PYTHONUTF8"] = "1"
    return environment


def _run(command: Sequence[str], *, cwd: Path, environment: Mapping[str, str], log_path: Path) -> None:
    """Run colcon with live console progress and a durable combined log."""

    log_path.parent.mkdir(parents=True, exist_ok=True)
    command_line = "$ " + " ".join(command)
    print("[phase181-typesupport] native characterization started; live colcon output follows.", flush=True)
    print("[phase181-typesupport] log: " + str(log_path), flush=True)
    try:
        process = subprocess.Popen(
            tuple(command),
            shell=False,
            cwd=cwd,
            env=dict(environment),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            errors="replace",
            bufsize=1,
        )
    except OSError as exc:
        log_path.write_text(command_line + "\n# launch-error=" + str(exc) + "\n", encoding="utf-8")
        raise CharacterizationError("repair-custom-interface-colcon-build") from exc
    with log_path.open("w", encoding="utf-8", newline="\n") as log:
        log.write(command_line + "\n")
        output = process.stdout
        if output is not None:
            try:
                for line in output:
                    log.write(line)
                    log.flush()
                    sys.stdout.write(line)
                    sys.stdout.flush()
            finally:
                output.close()
        return_code = process.wait()
        log.write("\n# exit=" + str(return_code) + "\n")
    if return_code != 0:
        raise CharacterizationError("repair-custom-interface-colcon-build")


def _write_reflection_probe(workspace: Path, assembly: Path, ros2cs_dotnet: Path) -> Path:
    """Write an ephemeral reflection probe below the build root, never a package."""

    probe = workspace / "probe"
    probe.mkdir(parents=True, exist_ok=True)
    (probe / "Probe.csproj").write_text(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>",
        encoding="utf-8",
    )
    program = r'''using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
var assemblyPath = Path.GetFullPath(args[0]);
var dependencyRoot = Path.GetFullPath(args[1]);
var context = new AssemblyLoadContext("phase181-characterization", isCollectible: true);
context.Resolving += (_, name) => {
  var candidate = Path.Combine(dependencyRoot, name.Name + ".dll");
  return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
};
var assembly = context.LoadFromAssemblyPath(assemblyPath);
var messages = assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal).Select(type => new {
  fullName = type.FullName,
  interfaces = type.GetInterfaces().Select(item => item.FullName).Where(item => item != null).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
  constructors = type.GetConstructors().Select(ctor => ".ctor(" + string.Join(",", ctor.GetParameters().Select(parameter => parameter.ParameterType.FullName)) + ")").OrderBy(item => item, StringComparer.Ordinal).ToArray(),
  disposable = typeof(IDisposable).IsAssignableFrom(type),
}).ToArray();
var ros2Message = messages.SelectMany(message => assembly.GetType(message.fullName!)!.GetInterfaces()).FirstOrDefault(type => type.FullName == "ROS2.Message");
Console.Write(JsonSerializer.Serialize(new {
  managedAssembly = new { name = assembly.GetName().Name, version = assembly.GetName().Version?.ToString(), mvid = assembly.ManifestModule.ModuleVersionId.ToString("D") },
  ros2MessageIdentity = ros2Message?.FullName,
  ros2MessageAssembly = ros2Message?.Assembly.GetName().Name,
  ros2MessageAssemblyMvid = ros2Message?.Assembly.ManifestModule.ModuleVersionId.ToString("D"),
  messages,
}));
'''
    (probe / "Program.cs").write_text(program, encoding="utf-8")
    return probe


def _inspect_managed_assembly(workspace: Path, assembly: Path, ros2cs_dotnet: Path, environment: Mapping[str, str]) -> Path:
    """Implement the internal inspect managed assembly step."""
    dotnet = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet" / "dotnet.exe"
    if not dotnet.is_file():
        raise CharacterizationError("install-dotnet-sdk-for-ros2cs")
    probe = _write_reflection_probe(workspace, assembly, ros2cs_dotnet)
    result = subprocess.run(
        (str(dotnet), "run", "--project", str(probe / "Probe.csproj"), "--", str(assembly), str(ros2cs_dotnet)),
        shell=False,
        cwd=probe,
        env=dict(environment),
        capture_output=True,
        text=True,
        errors="replace",
        check=False,
    )
    evidence = workspace / "e" / "managed.json"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    if result.returncode != 0:
        (workspace / "e" / "managed-probe.log").write_text(result.stdout + "\n" + result.stderr, encoding="utf-8")
        raise CharacterizationError("repair-managed-characterization-probe")
    evidence.write_text(result.stdout.strip(), encoding="utf-8")
    return evidence


def _sha256(path: Path) -> str:
    """Implement the internal sha256 step."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _rva_offset(data: bytes, rva: int, section_offset: int, section_count: int, optional_size: int) -> int | None:
    """Implement the internal rva offset step."""
    for index in range(section_count):
        offset = section_offset + index * 40
        virtual_size = int.from_bytes(data[offset + 8:offset + 12], "little")
        virtual_address = int.from_bytes(data[offset + 12:offset + 16], "little")
        raw_size = int.from_bytes(data[offset + 16:offset + 20], "little")
        raw_pointer = int.from_bytes(data[offset + 20:offset + 24], "little")
        extent = max(virtual_size, raw_size)
        if virtual_address <= rva < virtual_address + extent:
            return raw_pointer + rva - virtual_address
    return None


def pe_imports(path: Path) -> tuple[str, ...]:
    """Return direct PE import names without depending on a host tool or PATH."""

    try:
        data = path.read_bytes()
        if data[:2] != b"MZ":
            return ()
        pe_offset = int.from_bytes(data[0x3C:0x40], "little")
        if data[pe_offset:pe_offset + 4] != b"PE\0\0":
            return ()
        coff = pe_offset + 4
        section_count = int.from_bytes(data[coff + 2:coff + 4], "little")
        optional_size = int.from_bytes(data[coff + 16:coff + 18], "little")
        optional = coff + 20
        magic = int.from_bytes(data[optional:optional + 2], "little")
        directory_offset = optional + (112 if magic == 0x20B else 96 if magic == 0x10B else -1)
        if directory_offset < optional:
            return ()
        import_rva = int.from_bytes(data[directory_offset + 8:directory_offset + 12], "little")
        if not import_rva:
            return ()
        section_offset = optional + optional_size
        descriptor = _rva_offset(data, import_rva, section_offset, section_count, optional_size)
        if descriptor is None:
            return ()
        names: list[str] = []
        while descriptor + 20 <= len(data):
            name_rva = int.from_bytes(data[descriptor + 12:descriptor + 16], "little")
            if not any(data[descriptor:descriptor + 20]):
                break
            name_offset = _rva_offset(data, name_rva, section_offset, section_count, optional_size)
            if name_offset is None:
                break
            end = data.find(b"\0", name_offset)
            if end < 0:
                break
            names.append(data[name_offset:end].decode("ascii", errors="replace").lower())
            descriptor += 20
        return tuple(sorted(set(names)))
    except OSError:
        return ()


def _native_evidence(workspace: Path) -> list[dict[str, object]]:
    """Implement the internal native evidence step."""
    native_root = workspace / "i"
    return [
        {
            "path": path.relative_to(native_root).as_posix(),
            "sha256": _sha256(path),
            "imports": list(pe_imports(path)),
        }
        for path in sorted(native_root.rglob("*.dll"), key=lambda item: item.as_posix().lower())
    ]


def characterize(request: CharacterizationRequest, *, replace_existing: bool = False) -> CharacterizationResult:
    """Run preflight, build once, inspect output, and emit machine-local evidence."""

    preflight_toolchain(
        ToolchainPreflightRequest(
            distro=request.distro,
            ros2_root=request.ros2_root,
            ros2cs_source=request.ros2cs_source,
            r2fu_source=request.r2fu_source,
            build_root=request.build_root,
            generator=request.generator,
            dotnet=request.dotnet,
        )
    )
    with _temporary_short_workspace_root(request) as (physical_workspace, active_workspace):
        workspace = prepare_characterization_workspace(
            request,
            replace_existing=replace_existing,
            _workspace_root=active_workspace,
        )
        environment = build_characterization_environment(request)
        pixi = request.ros2_root / ".pixi" / "envs" / "default"
        command = build_colcon_command(
            request,
            workspace,
            colcon=str(pixi / "Scripts" / "colcon.exe"),
            python=str(pixi / "python.exe"),
        )
        _run(command, cwd=workspace, environment=environment, log_path=workspace / "e" / "colcon.log")
        assembly = workspace / "i" / "lib" / "dotnet" / (MANAGED_ASSEMBLY_NAME + ".dll")
        if not assembly.is_file():
            raise CharacterizationError("repair-generated-managed-assembly")
        managed_evidence = _inspect_managed_assembly(workspace, assembly, request.ros2cs_install / "lib" / "dotnet", environment)
        managed = inspect_managed_evidence(managed_evidence, MANAGED_ASSEMBLY_NAME, ENVELOPE_TYPE_NAME, ROS2_MESSAGE_IDENTITY)
        evidence_path = workspace / "e" / "characterization.json"
        evidence_path.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "distro": request.distro,
                    "managed": managed,
                    "nativeLibraries": _native_evidence(workspace),
                },
                sort_keys=True,
                indent=2,
            ) + "\n",
            encoding="utf-8",
        )
        native = tuple(
            physical_workspace / "i" / item.relative_to(workspace / "i")
            for item in sorted((workspace / "i").rglob("*.dll"), key=lambda item: item.as_posix().lower())
        )
        result = CharacterizationResult(
            request.distro,
            physical_workspace / "e" / "characterization.json",
            physical_workspace / "i" / "lib" / "dotnet" / (MANAGED_ASSEMBLY_NAME + ".dll"),
            native,
        )
    return result


def _default_repo_root() -> Path:
    """Implement the internal default repo root step."""
    return Path(__file__).resolve().parents[3]


def parse_args(argv: Sequence[str]) -> tuple[CharacterizationRequest, bool]:
    """Parse command-line arguments."""
    root = _default_repo_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", required=True, choices=("humble", "jazzy", "lyrical"))
    parser.add_argument("--static-package", type=Path, default=root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces")
    parser.add_argument("--ros2-root", type=Path)
    parser.add_argument("--ros2cs-source", type=Path, required=True)
    parser.add_argument("--ros2cs-install", type=Path)
    parser.add_argument("--r2fu-source", type=Path, required=True)
    parser.add_argument("--build-root", type=Path, default=root / "build")
    parser.add_argument("--generator", default="Ninja")
    parser.add_argument("--dotnet", type=Path)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(argv)
    ros2_root = args.ros2_root or root / "ros2-windows" / ("ros2_" + args.distro)
    ros2cs_install = args.ros2cs_install or args.ros2cs_source / ("install-" + args.distro)
    return (
        CharacterizationRequest(
            distro=args.distro,
            static_package=args.static_package,
            ros2_root=ros2_root,
            ros2cs_source=args.ros2cs_source,
            ros2cs_install=ros2cs_install,
            r2fu_source=args.r2fu_source,
            build_root=args.build_root,
            generator=args.generator,
            dotnet=args.dotnet,
        ),
        args.replace,
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    request, replace_existing = parse_args(argv or sys.argv[1:])
    try:
        result = characterize(request, replace_existing=replace_existing)
    except CharacterizationError as error:
        print(str(error), file=sys.stderr)
        return 1
    print("PASS:", result.distro, result.evidence_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
