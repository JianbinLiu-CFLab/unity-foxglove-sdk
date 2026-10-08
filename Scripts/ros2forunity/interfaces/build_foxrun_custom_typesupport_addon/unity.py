#!/usr/bin/env python3
"""Build one Phase181 custom ROS2 typesupport add-on below ``build/phase181``.

The script deliberately has two materialization boundaries.  rosidl output is
first characterized in an out-of-tree candidate workspace.  A Unity batch
importer then generates PluginImporter metadata for every managed and native
candidate DLL.  Only the sibling ``sync_...`` command may copy a fully
validated candidate to ``Packages/``.
"""


from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "build_foxrun_custom_typesupport_addon.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import os
import subprocess
from pathlib import Path

from .models import *

def _unity_executable(request: CandidateBuildRequest) -> Path:
    """Return the explicitly supplied Unity executable or the supported Unity 6000 location."""
    if request.unity_executable is not None:
        return Path(request.unity_executable)
    return Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "Unity" / "Hub" / "Editor" / "6000.3.14f1" / "Editor" / "Unity.exe"


def _unity_editor_process_is_running() -> bool:
    """Return whether any Unity Editor process is active without using a shell."""

    if os.name != "nt":
        return False
    result = subprocess.run(
        ("tasklist", "/FI", "IMAGENAME eq Unity.exe", "/FO", "CSV", "/NH"),
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        check=False,
    )
    return result.returncode == 0 and "Unity.exe" in result.stdout


def _unity_plugin_importer_arguments(
    input_path: Path,
    output_path: Path,
    *,
    input_is_directory: bool,
) -> tuple[str, ...]:
    """Build the auditable Batch Mode importer arguments for one candidate boundary."""

    input_text = Path(input_path).as_posix()
    output_text = Path(output_path).as_posix()
    if input_is_directory:
        return (
            "-phase181TypesupportPluginInputDirectory",
            input_text,
            "-phase181TypesupportPluginMetaOutputDirectory",
            output_text,
        )
    return (
        "-phase181TypesupportManagedInput",
        input_text,
        "-phase181TypesupportManagedMetaOutput",
        output_text,
    )


def _run_unity_plugin_importer(
    request: CandidateBuildRequest,
    input_path: Path,
    *,
    input_is_directory: bool,
) -> None:
    """Ask Unity to generate restricted importer metadata for an owned candidate boundary."""

    unity = _unity_executable(request)
    project = _repo_root(request) / "Unity2Foxglove"
    input_path = Path(input_path)
    output_path = input_path if input_is_directory else input_path.with_name(input_path.name + ".meta")
    expected_meta = (
        tuple(path.with_name(path.name + ".meta") for path in sorted(input_path.glob("*.dll"), key=lambda item: item.name.lower()))
        if input_is_directory
        else (output_path,)
    )
    if not unity.is_file() or not project.is_dir():
        raise CandidateBuildError("provide-unity-6000-plugin-importer")
    if input_is_directory:
        if not input_path.is_dir() or not expected_meta:
            raise CandidateBuildError("repair-generated-native-typesupport-closure")
    elif not input_path.is_file():
        raise CandidateBuildError("repair-generated-managed-assembly")
    # A stale lockfile can remain after a prior Editor crash or controlled exit.
    # Only an actually running Editor blocks our batch import; Unity itself owns
    # stale-lock recovery when it opens the project.
    if (project / "Temp" / "UnityLockfile").exists() and _unity_editor_process_is_running():
        raise CandidateBuildError("close-unity-editor-before-importing-plugin-metadata")
    command = (
        str(unity),
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        str(project),
        "-executeMethod",
        "Phase181TypesupportPluginImporterBuilder.Run",
        *_unity_plugin_importer_arguments(input_path, output_path, input_is_directory=input_is_directory),
        "-logFile",
        str(_phase181_distro_root(request) / "candidate" / "e" / "unity-plugin-importer.log"),
    )
    result = subprocess.run(command, shell=False, capture_output=True, text=True, errors="replace", check=False)
    if result.returncode != 0 or not all(path.is_file() for path in expected_meta):
        raise CandidateBuildError("repair-unity-plugin-importer-metadata")

__all__ = [name for name in globals() if not name.startswith("__")]
