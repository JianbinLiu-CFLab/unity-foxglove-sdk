from __future__ import annotations

def check_runtime_files(results: list[CheckResult]) -> None:
    """Validate critical runtime files and package layout."""
    for dll in CRITICAL_PLUGIN_DLLS:
        path = PLUGIN_ROOT / dll
        add(results, f"critical DLL present: {dll}", path.exists(), rel(path))
    add(
        results,
        "critical managed assembly present: rosgraph_msgs_assembly.dll",
        (RUNTIME_ROOT / "Plugins" / "rosgraph_msgs_assembly.dll").exists(),
        rel(RUNTIME_ROOT / "Plugins" / "rosgraph_msgs_assembly.dll"),
    )
    for path in ZENOH_CONFIG_FILES:
        add(results, f"Zenoh config present: {path.name}", path.exists(), rel(path))
    for plugin_config, streaming_assets_config in ZENOH_CONFIG_MIRRORS:
        add(
            results,
            f"Zenoh config mirror matches StreamingAssets: {plugin_config.name}",
            plugin_config.exists()
            and streaming_assets_config.exists()
            and plugin_config.read_bytes() == streaming_assets_config.read_bytes(),
            f"{rel(plugin_config)} <-> {rel(streaming_assets_config)}",
        )

    for path in [item for item in ZENOH_CONFIG_FILES if item.name == "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"]:
        text = path.read_text(encoding="utf-8") if path.exists() else ""
        add(
            results,
            f"Zenoh session listen failure is non-fatal: {path.parent.parent.name}/{path.name}",
            "exit_on_failure: false" in text,
            rel(path),
        )
        add(
            results,
            f"Zenoh session RX defragmentation buffer is bounded: {path.parent.parent.name}/{path.name}",
            "max_message_size: 134217728" in text and "max_message_size: 1073741824" not in text,
            rel(path),
        )
        adminspace_index = text.find("adminspace:")
        adminspace = text[adminspace_index:] if adminspace_index >= 0 else ""
        add(
            results,
            f"Zenoh session adminspace disabled by default: {path.parent.parent.name}/{path.name}",
            "enabled: false" in adminspace and "read: false" in adminspace,
            rel(path),
        )

    for path in [item for item in ZENOH_CONFIG_FILES if item.name == "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"]:
        text = path.read_text(encoding="utf-8") if path.exists() else ""
        add(
            results,
            f"Zenoh router open-listen profile is documented: {path.parent.parent.name}/{path.name}",
            "tcp/[::]:7447" in text
            and "without authentication or ACLs" in text
            and "localhost-only or ACL-protected deployment profile" in text,
            rel(path),
        )
        add(
            results,
            f"Zenoh router high connection limits are documented: {path.parent.parent.name}/{path.name}",
            "accept_pending: 10000" in text
            and "max_sessions: 10000" in text
            and "high development default is unsuitable" in text,
            rel(path),
        )

    dlls = list(PLUGIN_ROOT.glob("*.dll")) if PLUGIN_ROOT.exists() else []
    managed_dlls = list((RUNTIME_ROOT / "Plugins").glob("*.dll")) if (RUNTIME_ROOT / "Plugins").exists() else []
    plugin_dlls = sorted({*dlls, *managed_dlls}, key=lambda path: path.as_posix())
    add(results, "Windows x86_64 DLL payload", len(dlls) >= 700, f"dll_count={len(dlls)}")
    plugin_meta_failures = []
    for dll in plugin_dlls:
        meta = dll.with_name(dll.name + ".meta")
        if "PluginImporter:" not in read_optional_text(meta):
            plugin_meta_failures.append(rel(meta))
    add(
        results,
        "Runtime DLL metas use PluginImporter",
        len(dlls) >= 700 and not plugin_meta_failures,
        ", ".join(plugin_meta_failures[:8]),
    )

    for seed in RMW_DEPENDENCY_CLOSURE_SEEDS:
        seed_path = PLUGIN_ROOT / seed
        missing = missing_package_dll_imports(seed_path)
        detail = "; ".join(
            f"{parent} -> {', '.join(children)}"
            for parent, children in sorted(missing.items())
        )
        add(
            results,
            f"native DLL dependency closure: {seed}",
            not missing,
            detail or rel(seed_path),
        )

    add(results, "no root zip sidecar copied", not any(PACKAGE.glob("*.zip")) and not any(PACKAGE.glob("*.sha256")), rel(PACKAGE))

    copied_paths = [path.relative_to(PACKAGE).as_posix() for path in iter_files(PACKAGE)]
    sample_hits = [path for path in copied_paths if "Phase110Ros2ForUnity" in path or "External Adapter" in path]
    add(results, "runtime package does not duplicate adapter samples", not sample_hits, ", ".join(sample_hits[:8]))

    unexpected_platforms = [
        path
        for path in copied_paths
        if path.startswith("Runtime/Ros2ForUnity/Plugins/")
        and ("/Linux/" in path or "/Mac" in path or "/macOS/" in path)
    ]
    add(results, "runtime plugin payload limited to Windows", not unexpected_platforms, ", ".join(unexpected_platforms[:8]))

    scripts = RUNTIME_ROOT / "Scripts"
    leaky_examples = [name for name in LEAKY_UPSTREAM_EXAMPLES if (scripts / name).exists()]
    add(results, "leaky upstream examples pruned", not leaky_examples, ", ".join(leaky_examples))


def check_managed_deps_consistency(results: list[CheckResult]) -> None:
    """Validate known managed .deps.json dependency corrections."""
    managed_plugin_root = RUNTIME_ROOT / "Plugins"
    for name in ("stereo_msgs_assembly", "visualization_msgs_assembly"):
        path = managed_plugin_root / f"{name}.deps.json"
        data = load_json(path, results, f"{name}.deps.json parses")
        target = data.get("targets", {}).get(".NETStandard,Version=v2.0/", {})
        entry = target.get(f"{name}/1.0.0", {})
        dependencies = entry.get("dependencies", {})
        libraries = data.get("libraries", {})
        add(
            results,
            f"{name}.deps.json does not declare spurious service_msgs dependency",
            "service_msgs_assembly" not in dependencies
            and "service_msgs_assembly/0.0.0.0" not in target
            and "service_msgs_assembly/0.0.0.0" not in libraries,
            rel(path),
        )


def check_package_path_patch(results: list[CheckResult]) -> None:
    """Validate the ROS2ForUnity.cs package path patch."""
    source = RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs"
    text = source.read_text(encoding="utf-8", errors="replace") if source.exists() else ""
    required = [
        "Unity2Foxglove package path support",
        PACKAGE_NAME,
        "PackageInfo.FindForAssetPath",
        "resolvedPath",
        "unity2FoxgloveRuntimePackageAssetPath",
        'Path.Combine(',
        '"Packages"',
        '"Runtime"',
        "Directory.Exists(packagePath)",
        "return assetPath;",
    ]
    for token in required:
        add(results, f"ROS2ForUnity.cs contains {token}", token in text, token)
    add(
        results,
        "UnityEditor using guarded",
        guarded_unity_editor_using(text),
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "PackageManager lookup guarded",
        re.search(
            r"#if\s+UNITY_EDITOR[\s\S]{0,1200}UnityEditor\.PackageManager\.PackageInfo\.FindForAssetPath[\s\S]{0,1200}#endif",
            text,
        ) is not None,
        "ROS2ForUnity.cs",
    )


def check_runtime_asmdef(results: list[CheckResult]) -> None:
    """Validate the runtime assembly definition is safe for Editor and Player."""
    path = RUNTIME_ROOT / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.LyricalWin64.asmdef"
    data = load_json(path, results, "runtime asmdef parses")
    add(results, "runtime asmdef name", data.get("name") == "Unity2Foxglove.Ros2ForUnity.Runtime", f"name={data.get('name')!r}")
    add(
        results,
        "runtime asmdef targets Windows runtime and editor",
        data.get("includePlatforms") == ["Editor", "WindowsStandalone64"],
        f"includePlatforms={data.get('includePlatforms')!r}",
    )
    add(results, "runtime asmdef auto-referenced", data.get("autoReferenced") is True, f"autoReferenced={data.get('autoReferenced')!r}")
    add(
        results,
        "runtime asmdef has Windows define gate",
        data.get("defineConstraints") == ["UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN"],
        f"defineConstraints={data.get('defineConstraints')!r}",
    )


def check_runtime_source_patches(results: list[CheckResult]) -> None:
    """Validate local lifecycle, time-source, and attribution patches on vendored R2FU sources."""
    scripts = RUNTIME_ROOT / "Scripts"
    for relative in PATCHED_VENDOR_FILES:
        path = scripts / relative
        text = path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""
        add(results, f"patched vendored file exists: {relative}", path.exists(), rel(path))
        add(results, f"patched vendored attribution: {relative}", MODIFICATIONS_COPYRIGHT in text, relative)

    node = read_optional_text(scripts / "ROS2Node.cs")
    add(results, "ROS2Node implements IDisposable", "class ROS2Node : IDisposable" in node and "public void Dispose()" in node, "ROS2Node.cs")
    add(results, "ROS2Node avoids finalizer native cleanup", "~ROS2Node" not in node, "ROS2Node.cs")
    add(results, "ROS2Node removed UnityEditor using", "using UnityEditor;" not in node, "ROS2Node.cs")

    component = read_optional_text(scripts / "ROS2UnityComponent.cs")
    component_join = "threadToJoin.Join(1000)" in component or "threadToJoin.Join(TimeSpan.FromSeconds(2))" in component
    for token in (
        "private volatile bool quitting",
        "OnDestroy()",
        "OnApplicationQuit()",
        "node.TryDispose()",
        "StopExecutor()",
        "private int shutdownInProgress = 0",
        "Interlocked.CompareExchange(ref shutdownInProgress, 1, 0)",
        "Volatile.Write(ref shutdownInProgress, 0)",
        "MarkRuntimeShutdownPendingExecutor()",
    ):
        add(results, f"ROS2UnityComponent lifecycle token: {token}", token in component, token)
    add(
        results,
        "ROS2UnityComponent uses snapshot spin path",
        "nodesSnapshot" in component
        and "actionsSnapshot" in component
        and "collectionVersion" in component
        and "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)" in component,
        "ROS2UnityComponent.cs",
    )
    add(
        results,
        "ROS2UnityComponent disposes nodes before runtime release",
        "DisposeNodes()" in component and "instance.DestroyROS2ForUnity()" in component,
        "ROS2UnityComponent.cs",
    )
    add(
        results,
        "ROS2UnityComponent executor thread",
        "private Thread spinThread" in component or "private Thread executorThread" in component,
        "ROS2UnityComponent.cs",
    )
    add(results, "ROS2UnityComponent bounded join", component_join, "ROS2UnityComponent.cs")
    add(results, "ROS2UnityComponent does not shutdown on ordinary disable", "OnDisable()" not in component, "ROS2UnityComponent.cs")

    core = read_optional_text(scripts / "ROS2UnityCore.cs")
    core_join = "threadToJoin.Join(1000)" in core or "threadToJoin.Join(TimeSpan.FromSeconds(2))" in core
    for token in (
        "IDisposable",
        "private volatile bool quitting",
        "public void Dispose()",
        "StopExecutor()",
    ):
        add(results, f"ROS2UnityCore lifecycle token: {token}", token in core, token)
    add(
        results,
        "ROS2UnityCore uses snapshot spin path",
        "nodesSnapshot" in core
        and "actionsSnapshot" in core
        and "collectionVersion" in core
        and "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)" in core,
        "ROS2UnityCore.cs",
    )
    add(
        results,
        "ROS2UnityCore disposes nodes before runtime release",
        "DisposeNodes()" in core and "instance.DestroyROS2ForUnity()" in core,
        "ROS2UnityCore.cs",
    )
    add(
        results,
        "ROS2UnityCore executor thread",
        "private Thread spinThread" in core or "private Thread executorThread" in core,
        "ROS2UnityCore.cs",
    )
    add(results, "ROS2UnityCore bounded join", core_join, "ROS2UnityCore.cs")

    runtime = read_optional_text(scripts / "ROS2ForUnity.cs")
    old_tokens = ("ownerCount", "ownsLifecycle", "lifecycleGate", "UnregisterCallbacks()", "editorCallbacksRegistered")
    current_tokens = ("referenceCount", "ownsReference", "initMutex", "ShutdownShared()", "editorHandlersRegistered")
    old_lifecycle = all(token in runtime for token in old_tokens)
    current_lifecycle = all(token in runtime for token in current_tokens)
    mixed_partial = (
        old_lifecycle
        and any(token in runtime for token in ("referenceCount", "ownsReference", "editorHandlersRegistered"))
        and not current_lifecycle
    )
    add(
        results,
        "ROS2ForUnity deterministic lifecycle",
        (old_lifecycle or current_lifecycle) and not mixed_partial,
        f"old_lifecycle={old_lifecycle} current_lifecycle={current_lifecycle} mixed_partial={mixed_partial}",
    )
    add(results, "ROS2ForUnity avoids finalizer shutdown", "~ROS2ForUnity" not in runtime, "ROS2ForUnity.cs")
    add(
        results,
        "ROS2ForUnity uses non-obsolete ros2cs logger callback API",
        "Ros2csLogger.SetCallback" in runtime and "Ros2csLogger.setCallback" not in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity enforces supported RMW implementations",
        "defaultRmwImplementation" in runtime
        and "zenohRmwImplementation" in runtime
        and "supportedRmwImplementationsDescription" in runtime
        and "ValidateRmwImplementation" in runtime
        and "IsSupportedRmwImplementation" in runtime
        and DEFAULT_RMW_IMPLEMENTATION in runtime
        and ZENOH_RMW_IMPLEMENTATION in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity standalone isolates sourced ROS2 environment",
        "standalone runtime must not inherit a sourced ROS2 workspace" in runtime
        and "standalone runtime owns its RMW selection while allowing Lyrical Zenoh" in runtime
        and "selectedRmwImplementation" in runtime
        and "standalone runtime owns ROS_DISTRO" in runtime
        and "WarnIfStandaloneRosDistroOverride" in runtime
        and "sourcedRosDistroBeforeStandalonePatch" in runtime
        and "CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)" in runtime
        and "packagedRos2Version = GetMetadataValue" in runtime
        and "ROS2 version in standalone process environment does not match this runtime package" not in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity Windows CRT environment import is Windows-symbol guarded",
        (
            (
                runtime.count("#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN") >= 2
                or runtime.count("#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN") >= 2
            )
            and "[DllImport(\"ucrtbase.dll\"" in runtime
            and "_wputenv_s" in runtime
        )
        or (
            "Ros2ForUnityProcessEnvironmentLease.Set(" in runtime
            and "Ros2ForUnityProcessEnvironmentLease.Restore(" in runtime
            and (
                runtime.count("#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN")
                + runtime.count("#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN")
                >= 2
            )
            and "[DllImport(\"ucrtbase.dll\"" in runtime
            and "_wputenv_s" in runtime
        ),
        "ROS2ForUnity.cs",
    )
    constructor = runtime[runtime.find("internal ROS2ForUnity()") :]
    windows_block = constructor[constructor.find("if (GetOS() == Platform.Windows)") : constructor.find("} else {")]
    add(
        results,
        "ROS2ForUnity standalone environment setup is not repeated in the Windows PATH block",
        "SetStandaloneRosDistro(currentRos2Version)" not in windows_block
        and "SetStandalonePrefixPath();" not in windows_block
        and "SetStandaloneRmwImplementation();" not in windows_block
        and "SetStandaloneRcutilsConsoleMode();" not in windows_block,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2UnityComponent prevents restart during shared ROS shutdown",
        "runtimeShutdownRequested" in component
        and "MarkRuntimeShutdown()" in component
        and (
            "component.MarkRuntimeShutdown();" in component
            or (
                "private bool StopForRosShutdown()" in component
                and "TryDetachRuntimeState(true, out instance)" in component
                and "MarkRuntimeShutdownPendingExecutor()" in component
            )
        )
        and "throw new ObjectDisposedException(nameof(ROS2UnityComponent))" in component
        and "ros2forUnity == null" in component,
        "ROS2UnityComponent.cs",
    )
    add(
        results,
        "ROS2UnityComponent prewarms Unity path Lazy values on the main thread",
        "private void Awake()" in component
        and "ROS2ForUnity.PrewarmUnityPaths();" in component
        and "            runtimeShutdownRequested = false;" not in component,
        "ROS2UnityComponent.cs",
    )

    dotnet_time = read_optional_text(scripts / "Time" / "DotnetTimeSource.cs")
    add(
        results,
        "DotnetTimeSource converts Stopwatch duration to seconds",
        ("Stopwatch.Frequency" in dotnet_time and "ElapsedTicks" in dotnet_time)
        or "stopwatch.Elapsed.TotalSeconds" in dotnet_time,
        "DotnetTimeSource.cs",
    )
    add(
        results,
        "DotnetTimeSource clamps backward wall-clock corrections",
        "lastEmittedSeconds" in dotnet_time
        and "wall-clock corrections cannot move time backward" in dotnet_time,
        "DotnetTimeSource.cs",
    )
    itime_source = read_optional_text(scripts / "Time" / "ITimeSource.cs")
    add(
        results,
        "ITimeSource summary uses stable wording",
        "Interface for acquiring time" in itime_source,
        "ITimeSource.cs",
    )
    unity_time = read_optional_text(scripts / "Time" / "UnityTimeSource.cs")
    add(
        results,
        "UnityTimeSource reports off-main-thread construction clearly",
        "must be constructed on the Unity main thread" in unity_time,
        "UnityTimeSource.cs",
    )

    for relative in ("Time/ROS2TimeSource.cs", "Time/ROS2ScalableTimeSource.cs"):
        source = read_optional_text(scripts / relative)
        add(
            results,
            f"{relative} implements bool ITimeSource.GetTime",
            "public bool GetTime(out int seconds, out uint nanoseconds)" in source
            and "public void GetTime(out int seconds, out uint nanoseconds)" not in source,
            relative,
        )
        add(
            results,
            f"{relative} reports unavailable ROS time",
            "return false;" in source and "return true;" in source,
            relative,
        )
        add(
            results,
            f"{relative} bool contract patch is marked",
            "bool-returning ITimeSource contract" in source,
            relative,
        )

    time_utils = read_optional_text(scripts / "Time" / "TimeUtils.cs")
    add(
        results,
        "TimeUtils normalizes nanoseconds",
        "Math.Floor(secondsIn)" in time_utils
        and (
            "normalizedNanoseconds < 0" in time_utils
            or "wholeNanoseconds >= 1000000000" in time_utils
            or "wholeNanoseconds >= NanosecondsPerSecond" in time_utils
        ),
        "TimeUtils.cs",
    )
    add(results, "TimeUtils does not cast modulo directly", "(uint)(nanosec % 1e9)" not in time_utils and "(uint)(nanosec % 1000000000)" not in time_utils, "TimeUtils.cs")

    sensor = read_optional_text(scripts / "Sensor.cs")
    sensor_publisher_guard = (
        "publisher != null && publishing" in sensor
        or "publisherOwnership != null && publishing" in sensor
        or (
            "rosParticipantsDisposed" in sensor
            and "publisherOwnership == null" in sensor
            and "!publishing" in sensor
            and "CompletePublisherCall" in sensor
        )
    )
    add(results, "Sensor uses short-circuit publisher guard", sensor_publisher_guard, "Sensor.cs")
    readings_guard_index = sensor.find("if (readings != null)")
    readings_deref_index = sensor.find("readings.SetHeaderFrame")
    sensor_null_guard = (
        (readings_guard_index >= 0 and readings_deref_index >= 0 and readings_guard_index < readings_deref_index)
        or ("if (acquiredReading == null)" in sensor and "acquiredReading.SetHeaderFrame" in sensor)
    )
    add(results, "Sensor checks readings before dereference", sensor_null_guard, "Sensor.cs")
    add(results, "Sensor unregisters executable action", "UnregisterExecutable" in sensor, "Sensor.cs")


def check_generator_alignment(results: list[CheckResult]) -> None:
    """Validate the generator knows about the lifecycle-patched package shape."""
    generator_path = ROOT / "Scripts" / "ros2forunity" / "windows" / "lyrical" / "build_r2fu_runtime_package.py"
    try:
        generator = generator_path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        add(results, "runtime package generator script readable", False, f"{rel(generator_path)}: {exc}")
        return

    add(results, "runtime package generator script readable", True, rel(generator_path))
    generator_sources = [generator_path]
    section_dir = generator_path.with_name("Decomposed") / generator_path.stem
    manifest_path = section_dir / "sections.manifest"
    if manifest_path.is_file():
        section_names = tuple(
            line.strip()
            for line in manifest_path.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        )
        for section_name in section_names:
            section_path = section_dir / section_name
            try:
                generator_sources.append(section_path)
                section_path.read_text(encoding="utf-8", errors="replace")
            except OSError as exc:
                add(results, "runtime package generator section readable", False, f"{rel(section_path)}: {exc}")
    generator = "\n".join(
        source.read_text(encoding="utf-8", errors="replace")
        for source in generator_sources
        if source.is_file()
    )
    required = (
        "collect_local_patch_overlays",
        "apply_local_patch_overlays",
        "collect_meta_overlays",
        "apply_meta_overlays",
        "LOCAL_PATCH_OVERLAY_FILES",
        "patch_ros_time_source_contract",
        "LEAKY_UPSTREAM_EXAMPLES",
        "runtime_asmdef",
        "validate_ros2cs_metadata_descriptions",
        "make_writable",
        "windows_long_path",
        "PackageInfo.FindForAssetPath",
        "UNITY_EDITOR",
        "Application.streamingAssetsPath",
    )
    for token in required:
        add(results, f"runtime package generator token: {token}", token in generator, token)

__all__ = []
