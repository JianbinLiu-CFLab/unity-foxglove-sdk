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
        raise Phase160Error(
            "BLOCKED_UNKNOWN_TOOLCHAIN",
            f"{label} must not be inside the repository: {resolved}",
        )
    if "baidusyncdisk" in str(resolved).lower() or "BaiduSyncdisk" in str(resolved):
        raise Phase160Error(
            "BLOCKED_UNKNOWN_TOOLCHAIN",
            f"{label} must not be inside a synced workspace: {resolved}",
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
        raise Phase160Error("BLOCKED_UNKNOWN_TOOLCHAIN", f"Refusing unsafe delete: {resolved}")
    if resolved.exists():
        def make_writable(function, item, _exc_info):
            """Make a readonly build artifact writable before retrying removal."""
            os.chmod(item, stat.S_IWRITE)
            function(item)

        shutil.rmtree(resolved, onerror=make_writable)



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
    raise Phase160Error("BLOCKED_VSDEV_ENV", "vswhere.exe was not found")


def resolve_vs_dev_cmd(explicit: str, env: dict[str, str], log_file: pathlib.Path) -> pathlib.Path:
    """Resolve VsDevCmd.bat using explicit input, vswhere, or common install paths."""

    if explicit:
        path = pathlib.Path(explicit)
        if path.exists():
            return path
        raise Phase160Error("BLOCKED_VSDEV_ENV", f"Explicit VsDevCmd.bat was not found: {path}")

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
    raise Phase160Error("BLOCKED_VSDEV_ENV", "VsDevCmd.bat was not found")


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
        raise Phase160Error(classify_output(output), f"Command failed: {command}")
    merged = dict(env)
    for line in output.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        merged[key] = value
    return merged
