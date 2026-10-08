#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "characterize_foxrun_custom_interface.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import hashlib
import os
import subprocess
import sys
from pathlib import Path
from typing import Mapping, Sequence

from .foundation import CharacterizationError

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

__all__ = [name for name in globals() if not name.startswith("__")]
