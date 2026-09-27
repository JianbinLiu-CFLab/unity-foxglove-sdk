from __future__ import annotations
from .build_evidence_contract import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_build.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


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
    process: subprocess.Popen[str] | None = None
    job: process_support.WindowsKillOnCloseJob | None = None
    owner: process_support.OwnedProcessSet | None = None
    owner_closed = False
    try:
        with log_path.open("w", encoding="utf-8", newline="\n") as log:
            job, owner = _new_process_owner()
            try:
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
                owner.register("command", process)
                output, _ = process.communicate(timeout=timeout_seconds)
            except subprocess.TimeoutExpired:
                with contextlib.suppress(BaseException):
                    owner.close()
                owner_closed = True
                output = ""
                if process is not None:
                    with contextlib.suppress(OSError, subprocess.SubprocessError):
                        output, _ = process.communicate(timeout=10)
                log.write(output or "")
                raise BridgeBuildFailure(
                    "owned command exceeded its bounded timeout: "
                    + pathlib.Path(str(command[0])).name
                )
            except BaseException:
                with contextlib.suppress(BaseException):
                    owner.close()
                owner_closed = True
                output = ""
                if process is not None:
                    with contextlib.suppress(OSError, subprocess.SubprocessError):
                        output, _ = process.communicate(timeout=10)
                log.write(output or "")
                raise
            log.write(output or "")
    except OSError as exc:
        raise LivePrerequisiteMissing(
            "command unavailable: " + pathlib.Path(str(command[0])).name
        ) from exc
    finally:
        if owner is not None and not owner_closed:
            owner.close()
        elif job is not None:
            job.close()
    assert process is not None
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
    version = str(environment.get("VisualStudioVersion", "")).strip()
    identity = (
        "MSVC " + version
        if version and compiler
        else pathlib.PureWindowsPath(compiler).name
        if compiler
        else ""
    )
    return {"identity": identity, "path": compiler}


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


def cpp_runtime_paths(
    physical_row_root: pathlib.Path,
    runtime_row_root: pathlib.Path,
) -> tuple[pathlib.Path, pathlib.Path, pathlib.Path]:
    """Separate durable evidence paths from short Windows CMake/include paths."""

    physical = pathlib.Path(physical_row_root)
    runtime = pathlib.Path(runtime_row_root)
    return (
        physical / "cpp-build",
        runtime / "cpp-build",
        runtime / "peer-workspace" / "install",
    )


def reset_cmake_build_for_runtime_alias(
    physical_build: pathlib.Path,
    runtime_build: pathlib.Path,
) -> bool:
    """Discard an owned CMake tree when its temporary subst drive changed."""

    physical = pathlib.Path(physical_build)
    runtime = pathlib.Path(runtime_build)
    runtime_name = pathlib.PureWindowsPath(str(runtime)).name
    if physical.name.casefold() != "cpp-build" or runtime_name.casefold() != "cpp-build":
        raise BridgeBuildFailure("CMake cache reset target is not cpp-build")
    cache = physical / "CMakeCache.txt"
    if not cache.is_file():
        return False
    cached_directory: str | None = None
    for line in cache.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("CMAKE_CACHEFILE_DIR:INTERNAL="):
            cached_directory = line.split("=", 1)[1].strip()
            break
    if cached_directory and pathlib.PureWindowsPath(
        cached_directory
    ) == pathlib.PureWindowsPath(str(runtime)):
        return False
    if physical.is_symlink():
        raise BridgeBuildFailure("CMake cache reset target must not be a symlink")
    shutil.rmtree(physical)
    return True


__all__ = [name for name in globals() if not name.startswith("__")]
