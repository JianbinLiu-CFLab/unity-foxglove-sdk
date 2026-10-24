def ensure_r2fu_checkout(work_root: pathlib.Path, env: dict[str, str], log_file: pathlib.Path, clean: bool) -> pathlib.Path:
    """Create or update a clean R2FU Humble-support checkout under WorkRoot."""

    ensure_dir(work_root)
    checkout = work_root / CHECKOUT_DIR_NAME
    if clean and checkout.exists():
        remove_known_subdir(checkout, work_root)
    if not checkout.exists():
        run_command([resolve_tool("git", env), "clone", R2FU_REPO_URL, str(checkout)], cwd=work_root, env=env, log_file=log_file, check=True)
    run_command([resolve_tool("git", env), "fetch", "origin"], cwd=checkout, env=env, log_file=log_file, check=True)
    run_command(
        [resolve_tool("git", env), "switch", "-C", R2FU_BRANCH, f"origin/{R2FU_BRANCH}"],
        cwd=checkout,
        env=env,
        log_file=log_file,
        check=True,
    )

    ros2cs = checkout / "src" / "ros2cs"
    if not ros2cs.exists():
        ensure_dir(checkout / "src")
        run_command([resolve_tool("git", env), "clone", ROS2CS_REPO_URL, str(ros2cs)], cwd=checkout / "src", env=env, log_file=log_file, check=True)
    run_command([resolve_tool("git", env), "fetch", "origin"], cwd=ros2cs, env=env, log_file=log_file, check=True)
    run_command(
        [resolve_tool("git", env), "switch", "-C", ROS2CS_BRANCH, f"origin/{ROS2CS_BRANCH}"],
        cwd=ros2cs,
        env=env,
        log_file=log_file,
        check=True,
    )
    record_checkout_identity(checkout, env, log_file)
    return checkout


def patch_ros2cs_humble_windows_standalone(checkout: pathlib.Path, log_file: pathlib.Path) -> None:
    """Patch the checkout so Humble Windows standalone uses OpenSSL 3 DLL names."""

    cmake = checkout / "src" / "ros2cs" / "src" / "ros2cs" / "ros2cs_core" / "CMakeLists.txt"
    text = cmake.read_text(encoding="utf-8")
    new = """    set(third_party_standalone_libs
      msvcp140.dll
      vcruntime140.dll
      vcruntime140_1.dll
      tinyxml2.dll
    )
    if(ros2_distro STREQUAL "humble" OR ros2_distro STREQUAL "rolling")
      list(PREPEND third_party_standalone_libs
        libssl-3-x64.dll
        libcrypto-3-x64.dll
      )
    else()
      list(PREPEND third_party_standalone_libs
        libssl-1_1-x64.dll
        libcrypto-1_1-x64.dll
      )
    endif()
"""
    if "libssl-3-x64.dll" in text and "ros2_distro STREQUAL \"humble\"" in text:
        patched = False
    else:
        pattern = re.compile(
            r"(?ms)^[ \t]*set\(\s*third_party_standalone_libs\b.*?^[ \t]*\)\s*$"
        )
        matches = [
            match
            for match in pattern.finditer(text)
            if "libssl-1_1-x64.dll" in match.group(0)
            and "libcrypto-1_1-x64.dll" in match.group(0)
            and "tinyxml2.dll" in match.group(0)
        ]
        if len(matches) != 1:
            raise Phase160Error(
                "BLOCKED_ROS2CS_BUILD",
                f"Could not patch ros2cs_core OpenSSL standalone DLL list; matches={len(matches)}",
            )

        match = matches[0]
        cmake.write_text(text[: match.start()] + new + text[match.end() :], encoding="utf-8")
        patched = True

    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        state = "applied" if patched else "already-present"
        log.write(f"\n# patch_ros2cs_humble_windows_standalone={state}\n")


def run_humble_dependency_import(checkout: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> None:
    """Run the upstream Humble dependency import."""

    ros2cs = checkout / "src" / "ros2cs"
    get_repos = ros2cs / "get_repos.ps1"
    if not get_repos.exists():
        raise Phase160Error("BLOCKED_ROS2CS_BUILD", f"Missing upstream get_repos.ps1: {get_repos}")
    result = run_command(
        [resolve_tool("powershell", env), "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(get_repos)],
        cwd=ros2cs,
        env=env,
        log_file=log_file,
    )
    if result.exit_code != 0:
        raise Phase160Error(classify_output(result.output), "Humble dependency import failed")


def clean_colcon_outputs(checkout: pathlib.Path) -> None:
    """Remove colcon output directories under the checkout."""

    for name in ("build", "install", "log"):
        remove_known_subdir(checkout / name, checkout)


def has_complete_asset(checkout: pathlib.Path) -> bool:
    """Return whether the R2FU Unity asset contains real Windows native libraries."""

    asset = checkout / "install" / "asset" / "Ros2ForUnity"
    metadata = asset / "metadata_ros2cs.xml"
    plugins = asset / "Plugins" / "Windows" / "x86_64"
    native_libraries = list(plugins.glob("*.dll")) if plugins.exists() else []
    required_runtime = all((plugins / name).exists() for name in HUMBLE_PIXI_RUNTIME_DLLS)
    return asset.exists() and metadata.exists() and plugins.exists() and bool(native_libraries) and required_runtime


def copy_humble_pixi_runtime_closure(
    asset: pathlib.Path,
    ros2_root: pathlib.Path,
    log_file: pathlib.Path,
) -> None:
    """Copy pixi runtime DLLs missed by upstream standalone deployment."""

    pixi_bin = ros2_root / ".pixi" / "envs" / "default" / "Library" / "bin"
    plugin_dir = asset / "Plugins" / "Windows" / "x86_64"
    ensure_dir(plugin_dir)

    copied: list[str] = []
    for name in HUMBLE_PIXI_RUNTIME_DLLS:
        source = pixi_bin / name
        if not source.exists():
            raise Phase160Error("BLOCKED_NATIVE_DEPENDENCY", f"Missing Humble pixi runtime DLL: {source}")
        shutil.copy2(source, plugin_dir / name)
        copied.append(name)

    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write("\n# copied Humble pixi runtime closure DLLs\n")
        for name in copied:
            log.write(f"# runtime={name}\n")


def deploy_asset_from_successful_colcon(
    checkout: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    ros2_root: pathlib.Path,
) -> str:
    """Deploy a successfully built ros2cs install into the Unity asset layout."""

    python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    metadata = checkout / "src" / "scripts" / "metadata_generator.py"
    result = run_command([str(python), str(metadata), "--standalone"], cwd=checkout, env=env, log_file=log_file)
    if result.exit_code != 0:
        return classify_output(result.output)

    asset_root = checkout / "install" / "asset"
    asset = asset_root / "Ros2ForUnity"
    if asset.exists():
        shutil.rmtree(asset, onerror=lambda function, item, _exc_info: (os.chmod(item, stat.S_IWRITE), function(item)))
    ensure_dir(asset_root)
    shutil.copytree(checkout / "src" / "Ros2ForUnity", asset, dirs_exist_ok=True)

    deploy = checkout / "deploy_unity_plugins.ps1"
    plugin_path = asset / "Plugins"
    result = run_command(
        [resolve_tool("powershell", env), "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(deploy), str(plugin_path)],
        cwd=checkout,
        env=env,
        log_file=log_file,
    )
    if result.exit_code != 0:
        return classify_output(result.output)

    metadata_file = checkout / "src" / "Ros2ForUnity" / "metadata_ros2cs.xml"
    if metadata_file.exists():
        ensure_dir(asset / "Plugins" / "Windows" / "x86_64")
        shutil.copy2(metadata_file, asset / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml")
        shutil.copy2(metadata_file, asset / "Plugins" / "metadata_ros2cs.xml")

    copy_humble_pixi_runtime_closure(asset, ros2_root, log_file)

    return "BUILD_ORCHESTRATOR_GREEN" if has_complete_asset(checkout) else "BLOCKED_R2FU_BUILD_SCRIPT"


def run_direct_colcon_build(
    checkout: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    ros2_root: pathlib.Path,
    generator: str,
) -> str:
    """Run direct colcon with explicit Python CMake arguments."""

    clean_colcon_outputs(checkout)
    python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    cmake_args = [
        "-DSTANDALONE_BUILD:int=1",
        "-DCMAKE_BUILD_TYPE=Release",
        "-DBUILD_TESTING:int=0",
        f"-DPython3_EXECUTABLE={python}",
        f"-DPYTHON_EXECUTABLE={python}",
    ]
    if generator == "ninja":
        cmake_args.insert(0, "-G")
        cmake_args.insert(1, "Ninja")

    colcon_path = shutil.which("colcon", path=env.get("Path") or env.get("PATH"))
    if not colcon_path:
        raise Phase160Error("BLOCKED_PYTHON_SELECTION", "colcon was not found in the pinned ROS2 Humble environment")
    command = [
        colcon_path,
        "build",
        "--merge-install",
        "--event-handlers",
        "console_direct+",
        "--cmake-args",
        *cmake_args,
        "--no-warn-unused-cli",
    ]
    result = run_command(command, cwd=checkout, env=env, log_file=log_file)
    if result.exit_code != 0:
        return classify_output(result.output)

    lower = result.output.lower()
    if "failed   <<<" in lower or "not processed" in lower or "cmake error" in lower or "modulenotfounderror" in lower:
        return classify_output(result.output)

    return deploy_asset_from_successful_colcon(checkout, env, log_file, ros2_root)


def run_upstream_build(
    checkout: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    ros2_root: pathlib.Path,
    generator: str,
) -> str:
    """Run the upstream R2FU standalone build script and classify the result."""

    build_script = checkout / "build.ps1"
    if not build_script.exists():
        raise Phase160Error("BLOCKED_R2FU_BUILD_SCRIPT", f"Missing upstream build.ps1: {build_script}")
    result = run_command(
        [resolve_tool("powershell", env), "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(build_script), "-standalone", "-clean_install"],
        cwd=checkout,
        env=env,
        log_file=log_file,
    )
    if result.exit_code != 0:
        return classify_output(result.output)

    lower = result.output.lower()
    if (
        "failed   <<<" in lower
        or "cmake error" in lower
        or "modulenotfounderror" in lower
        or "not processed" in lower
        or "0 packages finished" in lower
    ):
        verdict = classify_output(result.output)
        if verdict == "BLOCKED_PYTHON_SELECTION":
            return run_direct_colcon_build(checkout, env, log_file, ros2_root, generator)
        return verdict

    asset = checkout / "install" / "asset" / "Ros2ForUnity"
    if asset.exists():
        copy_humble_pixi_runtime_closure(asset, ros2_root, log_file)

    if has_complete_asset(checkout):
        return "BUILD_ORCHESTRATOR_GREEN"
    return "BLOCKED_R2FU_BUILD_SCRIPT"


def write_evidence(
    evidence_path: pathlib.Path,
    *,
    verdict: str,
    log_file: pathlib.Path,
    repo_root: pathlib.Path,
    work_root: pathlib.Path,
    temp_root: pathlib.Path,
    ros2_root: pathlib.Path,
    vs_dev_cmd: pathlib.Path | None,
    effective_generator: str,
    no_build: bool,
) -> None:
    """Write the local Phase160 evidence note."""

    lines = [
        "---",
        'title: "Phase160 R2FU Humble Windows Build Toolchain Closure Evidence"',
        "tags:",
        "  - developer",
        "  - phase160",
        "  - ros2-for-unity",
        "  - humble",
        "  - windows",
        "status: blocked" if verdict != "BUILD_ORCHESTRATOR_GREEN" else "status: green",
        f"updated: {_dt.date.today().isoformat()}",
        "---",
        "",
        "# Phase160 R2FU Humble Windows Build Toolchain Closure Evidence",
        "",
        "## Verdict",
        "",
        "```text",
        verdict,
        "```",
        "",
        "Python orchestrator was used. No project-owned PowerShell wrapper was created.",
        "",
        "## Paths",
        "",
        "```text",
        f"RepoRoot: {repo_root}",
        f"WorkRoot: {work_root}",
        f"Checkout: {work_root / CHECKOUT_DIR_NAME}",
        f"TempRoot: {temp_root}",
        f"Ros2Root: {ros2_root}",
        f"VsDevCmd: {vs_dev_cmd if vs_dev_cmd else 'not resolved'}",
        f"EffectiveGenerator: {effective_generator}",
        f"Log: {log_file}",
        "NoBuild: " + ("true" if no_build else "false"),
        "```",
        "",
        "## Required Diagnostics",
        "",
        "The log records:",
        "",
        "```text",
        "where python",
        "where pip",
        "where colcon",
        "where cmake",
        "where ninja",
        "where msbuild",
        "where cl",
        "catkin_pkg import check",
        "cl tiny compile check",
        "```",
        "",
        "## Boundary",
        "",
        "Phase160 does not import any partial R2FU asset into Unity.",
        "",
        "Phase138 runtime acceptance remains Phase106-only after a complete asset exists.",
        "",
    ]
    evidence_path.parent.mkdir(parents=True, exist_ok=True)
    evidence_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


