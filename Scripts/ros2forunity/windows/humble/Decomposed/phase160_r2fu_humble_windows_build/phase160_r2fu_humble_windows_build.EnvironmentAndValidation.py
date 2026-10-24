def scrub_environment(env: dict[str, str], ros2_root: pathlib.Path, temp_root: pathlib.Path) -> dict[str, str]:
    """Remove common Python contamination and pin ROS2 Humble paths."""

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
        raise Phase160Error("BLOCKED_PYTHON_SELECTION", f"ROS2 local_setup.ps1 was not found: {setup}")
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
    """Verify the pinned ROS2 Humble pixi Python can import catkin_pkg."""

    python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    if not python.exists():
        raise Phase160Error("BLOCKED_PYTHON_SELECTION", f"Pinned Python is missing: {python}")
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
    source = temp_root / "phase160_cl_probe.c"
    obj = temp_root / "phase160_cl_probe.obj"
    source.write_text("int main(void) { return 0; }\n", encoding="utf-8")
    cl_path = shutil.which("cl", path=env.get("Path") or env.get("PATH"))
    if not cl_path:
        raise Phase160Error("BLOCKED_VSDEV_ENV", "cl.exe was not found in the imported VS environment")
    try:
        result = run_command(
            [cl_path, "/nologo", "/c", str(source), f"/Fo{obj}"],
            cwd=temp_root,
            env=env,
            log_file=log_file,
        )
        if result.exit_code != 0:
            raise Phase160Error(classify_output(result.output), "cl.exe tiny compile failed")
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
        raise Phase160Error("BLOCKED_CMAKE_GENERATOR", "Ninja was requested but ninja.exe was not found")
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


