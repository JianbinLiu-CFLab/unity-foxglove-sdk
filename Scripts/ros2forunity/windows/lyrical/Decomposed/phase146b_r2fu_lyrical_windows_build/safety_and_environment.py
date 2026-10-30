from __future__ import annotations

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
    raise Phase146BError("BLOCKED_VSDEV_ENV", "vswhere.exe was not found")


def resolve_vs_dev_cmd(explicit: str, env: dict[str, str], log_file: pathlib.Path) -> pathlib.Path:
    """Resolve VsDevCmd.bat using explicit input, vswhere, or common install paths."""

    if explicit:
        path = pathlib.Path(explicit)
        if path.exists():
            return path
        raise Phase146BError("BLOCKED_VSDEV_ENV", f"Explicit VsDevCmd.bat was not found: {path}")

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
    raise Phase146BError("BLOCKED_VSDEV_ENV", "VsDevCmd.bat was not found")


def capture_cmd_environment(vs_dev_cmd: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> dict[str, str]:
    """Import VsDevCmd.bat -arch=x64 -host_arch=x64 into a Python environment."""

    reject_cmd_shell_unsafe_path("VsDevCmd.bat", vs_dev_cmd)
    command = f'call "{vs_dev_cmd}" -arch=x64 -host_arch=x64 >nul && set'
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(f"\n\n$ {command}\n# cwd={vs_dev_cmd.parent}\n")
    try:
        completed = subprocess.run(
            command,
            cwd=str(vs_dev_cmd.parent),
            env=env,
            shell=True,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            errors="replace",
            timeout=DEFAULT_COMMAND_TIMEOUT_SECONDS,
        )
        output = completed.stdout or ""
        exit_code = completed.returncode
    except subprocess.TimeoutExpired as exc:
        output = exc.stdout or ""
        if isinstance(output, bytes):
            output = output.decode(errors="replace")
        output += "\nCOMMAND_TIMEOUT\n"
        exit_code = 124
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(output)
        log.write(f"\n# exit={exit_code}\n")
    if exit_code != 0:
        raise Phase146BError(classify_output(output), f"Command failed: {command}")
    merged = dict(env)
    for line in output.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        merged[key] = value
    return merged


def reject_cmd_shell_unsafe_path(label: str, path: pathlib.Path) -> None:
    """Reject a path that cannot be safely embedded in a cmd.exe quoted string."""

    unsafe = next((character for character in '"%&|^<>\r\n' if character in str(path)), None)
    if unsafe is not None:
        raise Phase146BError(
            "BLOCKED_VSDEV_ENV",
            f"{label} contains unsupported cmd.exe metacharacter {unsafe!r}: {path}",
        )


def scrub_environment(env: dict[str, str], ros2_root: pathlib.Path, temp_root: pathlib.Path) -> dict[str, str]:
    """Remove common Python contamination and pin ROS2 Lyrical paths."""

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
        raise Phase146BError("BLOCKED_PYTHON_SELECTION", f"ROS2 local_setup.ps1 was not found: {setup}")
    command = f"& '{setup}'; Get-ChildItem Env: | ForEach-Object {{ \"$($_.Name)=$($_.Value)\" }}"
    result = run_command(
        [resolve_tool("powershell", env), "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
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
    """Verify the pinned ROS2 Lyrical pixi Python can import catkin_pkg."""

    python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    if not python.exists():
        raise Phase146BError("BLOCKED_PYTHON_SELECTION", f"Pinned Python is missing: {python}")
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
    source = temp_root / "phase146b_cl_probe.c"
    obj = temp_root / "phase146b_cl_probe.obj"
    source.write_text("int main(void) { return 0; }\n", encoding="utf-8")
    cl_path = shutil.which("cl", path=env.get("Path") or env.get("PATH"))
    if not cl_path:
        raise Phase146BError("BLOCKED_VSDEV_ENV", "cl.exe was not found in the imported VS environment")
    try:
        result = run_command(
            [cl_path, "/nologo", "/c", str(source), f"/Fo{obj}"],
            cwd=temp_root,
            env=env,
            log_file=log_file,
        )
        if result.exit_code != 0:
            raise Phase146BError(classify_output(result.output), "cl.exe tiny compile failed")
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
        raise Phase146BError("BLOCKED_CMAKE_GENERATOR", "Ninja was requested but ninja.exe was not found")
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
