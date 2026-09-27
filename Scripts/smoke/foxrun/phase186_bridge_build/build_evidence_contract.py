from __future__ import annotations
from .configuration_and_authority import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_build.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


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
        "standardCanonicalType": STANDARD_SCHEMA_TYPE,
        "standardSchemaDigest": STANDARD_SCHEMA_DIGEST,
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
    if (
        not isinstance(compiler, Mapping)
        or not isinstance(compiler.get("identity"), str)
        or not str(compiler.get("identity", "")).strip()
        or not isinstance(compiler.get("path"), str)
        or not str(compiler.get("path", "")).strip()
    ):
        raise BridgeBuildFailure("compiler identity is missing")
    for name in ("probeExecutable", "generatedDuplexProbe"):
        executable = value.get(name)
        if (
            not isinstance(executable, Mapping)
            or not isinstance(executable.get("sha256"), str)
            or _SHA256.fullmatch(str(executable.get("sha256"))) is None
        ):
            raise BridgeBuildFailure(
                name + " executable identity is missing"
            )


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


def _new_process_owner() -> tuple[
    process_support.WindowsKillOnCloseJob,
    process_support.OwnedProcessSet,
]:
    """Create one exact process-tree owner for a logged command."""

    job = process_support.WindowsKillOnCloseJob()
    return job, process_support.OwnedProcessSet(job)


__all__ = [name for name in globals() if not name.startswith("__")]
