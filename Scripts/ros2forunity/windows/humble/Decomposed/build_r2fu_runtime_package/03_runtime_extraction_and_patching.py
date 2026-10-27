def extract_runtime(paths: BuildPaths) -> None:
    """Extract the Ros2ForUnity asset folder into the runtime package layout."""
    runtime_root = paths.package / "Runtime" / "Ros2ForUnity"
    runtime_root.mkdir(parents=True, exist_ok=True)
    runtime_root_resolved = runtime_root.resolve()
    with zipfile.ZipFile(paths.artifact) as archive:
        for info in archive.infolist():
            name = info.filename
            if info.is_dir() or not name.startswith("Ros2ForUnity/"):
                continue
            relative = safe_runtime_zip_relative_path(name)
            target = (runtime_root / relative).resolve()
            try:
                target.relative_to(runtime_root_resolved)
            except ValueError as exc:
                raise ValueError(f"Rejected runtime zip entry outside package root: {name}") from exc
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(info) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)


def normalize_ros2cs_plugin_roots(package: Path) -> None:
    """Replace artifact-producer plugin roots with package-relative metadata."""
    metadata_files = (
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "metadata_ros2cs.xml",
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml",
    )
    for path in metadata_files:
        text = path.read_text(encoding="utf-8", errors="strict")
        try:
            root = ElementTree.fromstring(text)
        except ElementTree.ParseError as error:
            raise ValueError(f"Invalid ros2cs metadata XML: {path}") from error
        plugins = root.find("plugins") if root.tag == "ros2cs" else None
        if plugins is None or plugins.get("root") is None:
            raise ValueError(f"Missing ros2cs plugin root: {path}")
        normalized, replacements = re.subn(
            r'(<plugins\b[^>]*\broot=")[^"]*(")',
            r"\g<1>.\2",
            text,
            count=1,
        )
        if replacements != 1:
            raise ValueError(f"Ambiguous ros2cs plugin root: {path}")
        write_text(path, normalized)


def safe_runtime_zip_relative_path(name: str) -> Path:
    """Return the path under Runtime/Ros2ForUnity for a trusted zip entry name."""
    zip_path = PurePosixPath(name)
    if zip_path.is_absolute():
        raise ValueError(f"Rejected absolute runtime zip entry: {name}")

    parts = zip_path.parts
    if len(parts) < 2 or parts[0] != "Ros2ForUnity":
        raise ValueError(f"Rejected unexpected runtime zip entry: {name}")
    if any(part in ("", ".", "..") for part in parts):
        raise ValueError(f"Rejected unsafe runtime zip entry: {name}")

    return Path(*parts[1:])


def prune_non_contract_examples(package: Path) -> None:
    """Remove upstream examples whose lifecycle is not part of this runtime package contract."""
    scripts = package / "Runtime" / "Ros2ForUnity" / "Scripts"
    for name in LEAKY_UPSTREAM_EXAMPLES:
        for path in (scripts / name, scripts / (name + ".meta")):
            try:
                path.unlink()
            except FileNotFoundError:
                pass


def patch_ros2_for_unity(package: Path) -> None:
    """Patch ROS2ForUnity.cs so the runtime can live inside a Unity package."""
    source = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
    text = source.read_text(encoding="utf-8")
    text = patch_ros2cs_logger_callback_api(text)
    text = patch_runtime_metadata_path(text)
    text = patch_standalone_environment_isolation(text)
    text = patch_runtime_lifecycle_safety(text)
    if UNITY_PACKAGE_PATH_PATCH_MARKER in text:
        text = patch_rmw_guard(text)
        write_text(source, text)
        return
    if "unity2FoxgloveRuntimePackageName" not in text:
        text = text.replace(
            '    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";\n',
            '    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";\n' + PACKAGE_CONSTANTS_BLOCK,
        )
    old_copyright = "// Modifications Copyright (c) 2026 Jianbin Liu.\n"
    new_copyright = "// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.\n"
    if old_copyright not in text:
        raise ValueError("Could not find upstream modifications copyright line to patch.")
    text = text.replace(old_copyright, new_copyright, 1)
    if UPSTREAM_PATH_BLOCK in text:
        text = text.replace(UPSTREAM_PATH_BLOCK, PACKAGE_PATH_BLOCK)
    elif UPSTREAM_COMPUTE_PATH_BLOCK in text:
        text = text.replace(UPSTREAM_COMPUTE_PATH_BLOCK, PACKAGE_COMPUTE_PATH_BLOCK)
    else:
        raise ValueError("Could not find upstream ROS2ForUnity path block to patch.")
    text = patch_rmw_guard(text)
    text = patch_standalone_environment_isolation(text)
    write_text(source, text)


def patch_runtime_metadata_path(text: str) -> str:
    """Keep Player metadata lookup under the StreamingAssets runtime root."""
    old = 'string ros2csMetadataPath = GetPluginPath() + separator + "metadata_ros2cs.xml";'
    new = 'string ros2csMetadataPath = GetRos2ForUnityPath() + separator + "metadata_ros2cs.xml";'
    if new in text:
        return text
    if UNITY_PACKAGE_PATH_PATCH_MARKER in text:
        return text
    return replace_required_once(text, old, new, "Player metadata staging path")


def patch_ros2cs_logger_callback_api(text: str) -> str:
    """Patch obsolete ros2cs logger callback calls emitted by older runtime artifacts."""
    return text.replace("Ros2csLogger.setCallback", "Ros2csLogger.SetCallback")


def replace_required_once(text: str, old: str, new: str, label: str) -> str:
    """Replace one required upstream anchor or fail before emitting a partial package."""
    matches = text.count(old)
    if matches != 1:
        raise ValueError(f"Could not apply {label}: expected one upstream anchor, found {matches}.")
    return text.replace(old, new, 1)


def patch_runtime_lifecycle_safety(text: str) -> str:
    """Restore local lifecycle guards that a refreshed upstream runtime can omit."""
    register_marker = "        EditorApplication.quitting += ShutdownShared;"
    unregister_marker = "        EditorApplication.quitting -= ShutdownShared;"
    if "EditorApplication.quitting" in text and "AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared" not in text:
        text = replace_required_once(
            text,
            register_marker,
            register_marker + "\n        AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared;",
            "AssemblyReloadEvents registration patch",
        )
    if "EditorApplication.quitting" in text and "AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared" not in text:
        text = replace_required_once(
            text,
            unregister_marker,
            unregister_marker + "\n        AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared;",
            "AssemblyReloadEvents unregistration patch",
        )

    dead_guard = "    private static void ThrowIfUninitialized(string callContext)\n"
    guard_start = text.find(dead_guard)
    if guard_start >= 0:
        guard_end = text.find("\n    }\n\n", guard_start)
        if guard_end < 0:
            raise ValueError("Could not remove the stale ThrowIfUninitialized guard.")
        text = text[:guard_start] + text[guard_end + len("\n    }\n\n"):]

    metadata_prerequisite = "LoadMetadata() must complete before metadata-backed properties are read."
    if "Metadata document is empty while reading" in text and metadata_prerequisite not in text:
        text = replace_required_once(
            text,
            '            throw new InvalidOperationException("Metadata document is empty while reading " + valuePath);\n',
            "            throw new InvalidOperationException(\n"
            '                "Metadata document is empty while reading " + valuePath +\n'
            '                ". LoadMetadata() must complete before metadata-backed properties are read.");\n',
            "metadata lifecycle prerequisite patch",
        )
    return text


def patch_unity_time_source_main_thread_guard(text: str) -> str:
    """Restore a clear construction failure when Unity time is initialized off the main thread."""
    if "mainThreadId" in text and "must be constructed on the Unity main thread" not in text:
        text = replace_required_once(
            text,
            "    mainThreadId = Thread.CurrentThread.ManagedThreadId;\n"
            "    lastReadingSecs = Time.timeAsDouble;\n",
            "    mainThreadId = Thread.CurrentThread.ManagedThreadId;\n"
            "    try\n"
            "    {\n"
            "      lastReadingSecs = Time.timeAsDouble;\n"
            "    }\n"
            "    catch (UnityException exception)\n"
            "    {\n"
            "      throw new InvalidOperationException(\n"
            '        "UnityTimeSource must be constructed on the Unity main thread.", exception);\n'
            "    }\n",
            "UnityTimeSource main-thread guard patch",
        )
    return text


def patch_rmw_guard(text: str) -> str:
    """Patch ROS2ForUnity.cs to fail fast when a different RMW is active."""
    if "expectedRmwImplementation" not in text:
        text = text.replace(
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n",
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n" + RMW_CONSTANT_BLOCK,
            1,
        )
    if "ValidateRmwImplementation" not in text:
        marker = "    private void RegisterCtrlCHandler()\n"
        if marker not in text:
            raise ValueError("Could not find RegisterCtrlCHandler marker for RMW guard patch.")
        text = text.replace(marker, RMW_VALIDATE_BLOCK + marker, 1)
    if "ValidateRmwImplementation(rmwImpl);" not in text:
        marker = "            string rmwImpl = Ros2cs.GetRMWImplementation();\n"
        if marker not in text:
            raise ValueError("Could not find RMW implementation read for RMW guard patch.")
        text = text.replace(marker, marker + "            ValidateRmwImplementation(rmwImpl);\n", 1)
    return text


def patch_standalone_environment_isolation(text: str) -> str:
    """Patch standalone startup so sourced ROS2 shells do not poison Unity."""
    old_check_signature = '''    public void CheckIntegrity()
    {
        string ros2SourcedCodename = GetROSVersionSourced();
'''
    new_check_signature = '''    public void CheckIntegrity()
    {
        CheckIntegrity(GetROSVersionSourced());
    }

    private void CheckIntegrity(string ros2SourcedCodename)
    {
'''
    text = text.replace(old_check_signature, new_check_signature)

    old_prefix = '''        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");
        char envPathSep = GetOS() == Platform.Windows ? ';' : ':';

        if (String.IsNullOrEmpty(currentPrefixPath))
        {
            SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
            return;
        }

        StringComparison comparison = GetOS() == Platform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string entry in currentPrefixPath.Split(envPathSep))
        {
            if (String.Equals(entry.Trim(), prefixPath, comparison))
            {
                return;
            }
        }

        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath + envPathSep + currentPrefixPath);
'''
    new_prefix = '''        // U2F-LOCAL-PATCH: standalone runtime must not inherit a sourced ROS2 workspace.
        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
'''
    text = text.replace(old_prefix, new_prefix)
    old_prefix_with_source = '''        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");
        char envPathSep = GetOS() == Platform.Windows ? ';' : ':';

        if (String.IsNullOrEmpty(currentPrefixPath))
        {
            SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
            Debug.Log("AMENT_PREFIX_PATH set to: " + prefixPath + " (source: " + prefixSource + ")");
            return;
        }

        StringComparison comparison = GetOS() == Platform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string entry in currentPrefixPath.Split(envPathSep))
        {
            if (String.Equals(entry.Trim(), prefixPath, comparison))
            {
                Debug.Log("AMENT_PREFIX_PATH already contains: " + prefixPath + " (source: " + prefixSource + ")");
                return;
            }
        }

        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath + envPathSep + currentPrefixPath);
        Debug.Log("AMENT_PREFIX_PATH prepended with: " + prefixPath + " (source: " + prefixSource + ")");
'''
    text = text.replace(old_prefix_with_source, new_prefix)
    if "standalone runtime must not inherit a sourced ROS2 workspace" not in text:
        prefix_start = text.find('        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");')
        prefix_end = text.find("\n    }\n\n    private static void SetStandaloneRmwImplementation()", prefix_start)
        if prefix_start >= 0 and prefix_end > prefix_start:
            text = text[:prefix_start] + new_prefix.rstrip("\n") + text[prefix_end:]

    # The standalone patch removes every diagnostic that used this provenance.
    # Remove its upstream-only declaration and assignments as well, or Unity emits CS0219.
    for obsolete_prefix_source_line in (
        '        string prefixSource = "asset root";\n',
        '            prefixSource = "StreamingAssets";\n',
        '            prefixSource = "plugin directory";\n',
    ):
        text = text.replace(obsolete_prefix_source_line, "")

    old_rmw = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))
        {
            SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
        }
'''
    new_rmw = '''        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection.
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
'''
    text = text.replace(old_rmw, new_rmw)
    old_rmw_with_comment = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))
        {
            // Fast-RTPS is the bundled standalone RMW; callers may override before ROS2ForUnity initializes.
            SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
        }
'''
    text = text.replace(old_rmw_with_comment, new_rmw)
    if "standalone runtime owns its RMW selection" not in text:
        rmw_start = text.find('        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))')
        rmw_end = text.find("\n    }\n\n    private static void SetStandaloneRosDistro", rmw_start)
        if rmw_start >= 0 and rmw_end > rmw_start:
            text = text[:rmw_start] + new_rmw.rstrip("\n") + text[rmw_end:]

    old_distro = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROS_DISTRO")))
        {
            SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
        }
'''
    new_distro = '''        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.
        SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
'''
    text = text.replace(old_distro, new_distro)
    if "WarnIfStandaloneRosDistroOverride" not in text:
        text = text.replace(
            "\n    private static void SetStandaloneRos2csSpinFallback(string ros2Codename)\n",
            "\n    private static void WarnIfStandaloneRosDistroOverride(string sourcedRosDistro, string packagedRos2Version)\n"
            "    {\n"
            "        if (string.IsNullOrEmpty(sourcedRosDistro)\n"
            "            || string.Equals(sourcedRosDistro, packagedRos2Version, StringComparison.OrdinalIgnoreCase))\n"
            "        {\n"
            "            return;\n"
            "        }\n\n"
            "        Debug.LogWarning(\n"
            "            \"Ignoring sourced ROS_DISTRO '\" + sourcedRosDistro +\n"
            "            \"' because standalone runtime package provides '\" + packagedRos2Version + \"'.\");\n"
            "    }\n\n"
            "    private static void SetStandaloneRos2csSpinFallback(string ros2Codename)\n",
            1,
        )

    old_integrity = '''        if (IsStandalone() && !string.IsNullOrEmpty(ros2SourcedCodename)) {
            string errMessage = "You should not source ROS2 in 'ros2-for-unity' standalone build.";
            FailIntegrity(errMessage);
        }
'''
    text = text.replace(old_integrity, "")

    startup_marker = "            // Load metadata\n            LoadMetadata();\n"
    startup_patch = '''            // Load metadata
            LoadMetadata();
            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
'''
    if "sourcedRosDistroBeforeStandalonePatch" not in text:
        if "packagedRos2Version = GetMetadataValue" not in text and startup_marker in text:
            text = text.replace(startup_marker, startup_patch, 1)
        elif "packagedRos2Version = GetMetadataValue" in text:
            text = text.replace(
                "            LoadMetadata();\n            if (IsStandalone())",
                "            LoadMetadata();\n            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();\n            if (IsStandalone())",
                1,
            )
    duplicate_standalone_block = '''            if (IsStandalone())
            {
                string packagedRos2Version = GetMetadataValue(ros2csMetadata, "/ros2cs/ros2");
                SetStandaloneRosDistro(packagedRos2Version);
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
                SetStandaloneRcutilsConsoleMode();
            }
'''
    text = text.replace(duplicate_standalone_block, "")
    text = text.replace(
        '            string standalone = IsStandalone() ? "standalone" : "non-standalone";\n',
        '            bool standaloneBuild = IsStandalone();\n'
        '            string standalone = standaloneBuild ? "standalone" : "non-standalone";\n',
        1,
    )
    text = text.replace(
        "            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n"
        "            bool standaloneBuild = IsStandalone();\n",
        "            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n",
        1,
    )
    text = text.replace(
        "            CheckIntegrity();\n",
        "            WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);\n"
        "            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n",
        1,
    )
    text = text.replace(
        "            CheckIntegrity(" + "sourcedRosDistroBeforeStandalonePatch);\n",
        "            WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);\n"
        "            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n",
        1,
    )

    required_method_markers = (
        ("private static void SetStandalonePrefixPath()", "standalone runtime must not inherit a sourced ROS2 workspace", "AMENT_PREFIX_PATH"),
        ("private static void SetStandaloneRmwImplementation()", "standalone runtime owns its RMW selection", "RMW_IMPLEMENTATION"),
        ("private static void SetStandaloneRosDistro(", "standalone runtime owns ROS_DISTRO", "ROS_DISTRO"),
    )
    for method_marker, patch_marker, label in required_method_markers:
        if method_marker in text and patch_marker not in text:
            raise ValueError(f"Could not apply {label} isolation patch to the standalone method body.")
    return text


def patch_ros_time_source_contract(package: Path) -> None:
    """Patch ROS2 time sources for the bool-returning ITimeSource contract."""
    time_dir = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Time"
    interface_file = time_dir / "ITimeSource.cs"
    if interface_file.exists():
        interface_text = interface_file.read_text(encoding="utf-8")
        interface_text = interface_text.replace(
            "/// Interface for acquiring ROS-compatible timestamp fields from a concrete time source.",
            "/// Interface for acquiring time.",
            1,
        )
        write_text(interface_file, interface_text)

    dotnet_time = time_dir / "DotnetTimeSource.cs"
    dotnet_text = dotnet_time.read_text(encoding="utf-8")
    dotnet_text = dotnet_text.replace(
        "// Modifications Copyright (c) 2026 Jianbin Liu.\n",
        "// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.\n",
        1,
    )
    if "Outputs are clamped to the last emitted timestamp" not in dotnet_text:
        dotnet_text = dotnet_text.replace(
            "/// DateTime.UtcNow provides the epoch alignment, while Stopwatch improves short-term resolution\n"
            "/// between periodic wall-clock resynchronizations.\n",
            "/// DateTime.UtcNow provides the epoch alignment, while Stopwatch improves short-term resolution\n"
            "/// between periodic wall-clock resynchronizations.\n"
            "/// Outputs are clamped to the last emitted timestamp so wall-clock corrections cannot move time backward.\n",
            1,
        )
    if "lastEmittedSeconds" not in dotnet_text:
        dotnet_text = dotnet_text.replace(
            "    private double systemTimeIntervalStart = 0;\n",
            "    private double systemTimeIntervalStart = 0;\n"
            "    private double lastEmittedSeconds = double.NegativeInfinity;\n",
            1,
        )
        dotnet_text = dotnet_text.replace(
            "            TimeUtils.TimeFromTotalSeconds(systemTimeIntervalStart + timeOffset, out seconds, out nanoseconds);\n",
            "            var totalSeconds = systemTimeIntervalStart + timeOffset;\n"
            "            if (totalSeconds < lastEmittedSeconds)\n"
            "            {\n"
            "                totalSeconds = lastEmittedSeconds;\n"
            "            }\n"
            "            else\n"
            "            {\n"
            "                lastEmittedSeconds = totalSeconds;\n"
            "            }\n"
            "\n"
            "            TimeUtils.TimeFromTotalSeconds(totalSeconds, out seconds, out nanoseconds);\n",
            1,
        )
    if MODIFICATIONS_COPYRIGHT not in dotnet_text:
        raise ValueError("DotnetTimeSource.cs is missing the local modifications copyright line.")
    write_text(dotnet_time, dotnet_text)

    unity_time = time_dir / "UnityTimeSource.cs"
    if unity_time.exists():
        write_text(
            unity_time,
            patch_unity_time_source_main_thread_guard(unity_time.read_text(encoding="utf-8")),
        )

    for name in ("ROS2TimeSource.cs", "ROS2ScalableTimeSource.cs"):
        source = time_dir / name
        text = source.read_text(encoding="utf-8")
        if "public void GetTime(out int seconds, out uint nanoseconds)" in text:
            text = text.replace(
                "  public void GetTime(out int seconds, out uint nanoseconds)\n  {\n",
                "  public bool GetTime(out int seconds, out uint nanoseconds)\n  {\n"
                "    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.\n",
                1,
            )
            text = text.replace(
                '      Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");\n'
                "      return;\n",
                '      Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");\n'
                "      return false;\n",
                1,
            )

        if "public bool GetTime(out int seconds, out uint nanoseconds)" not in text:
            raise ValueError(f"{name} does not expose the bool-returning ITimeSource contract.")
        if "bool-returning ITimeSource contract" not in text:
            text = text.replace(
                "  public bool GetTime(out int seconds, out uint nanoseconds)\n  {\n",
                "  public bool GetTime(out int seconds, out uint nanoseconds)\n  {\n"
                "    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.\n",
                1,
            )

        if name == "ROS2TimeSource.cs" and "return true;" not in text:
            text = text.replace(
                "    TimeUtils.TimeFromTotalSeconds(clock.Now.Seconds, out seconds, out nanoseconds);\n"
                "  }\n\n"
                "  public void Dispose()",
                "    TimeUtils.TimeFromTotalSeconds(clock.Now.Seconds, out seconds, out nanoseconds);\n"
                "    return true;\n"
                "  }\n\n"
                "  public void Dispose()",
                1,
            )
        elif name == "ROS2ScalableTimeSource.cs" and "return true;" not in text:
            text = text.replace(
                "      TimeUtils.TimeFromTotalSeconds(lastReadingSecs + initialTime, out seconds, out nanoseconds);\n"
                "    }\n"
                "  }\n\n"
                "  private void RefreshUnityTimeCache()",
                "      TimeUtils.TimeFromTotalSeconds(lastReadingSecs + initialTime, out seconds, out nanoseconds);\n"
                "    }\n"
                "    return true;\n"
                "  }\n\n"
                "  private void RefreshUnityTimeCache()",
                1,
            )

        if name == "ROS2ScalableTimeSource.cs":
            if "rosUnavailableWarningLogged" not in text:
                text = text.replace(
                    "  private bool timeScaleChangeLogged = false;\n",
                    "  private bool timeScaleChangeLogged = false;\n"
                    "  private int rosUnavailableWarningLogged = 0;\n",
                    1,
                )
                text = text.replace(
                    '      Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");\n'
                    "      return false;\n",
                    "      if (Interlocked.Exchange(ref rosUnavailableWarningLogged, 1) == 0)\n"
                    "      {\n"
                    '        Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");\n'
                    "      }\n"
                    "      return false;\n",
                    1,
                )
            text = text.replace(
                "      lock (mutex)\n"
                "      {\n"
                "        readingSecs = lastReadingSecs;\n"
                "        if (!rosUnityTimeOffsetAcquired)\n"
                "        {\n"
                "          rosUnityTimeOffsetAcquired = true;\n"
                "          rosUnityTimeOffset = GetRosNowSeconds() - readingSecs;\n"
                "        }\n"
                "        adjustedTime = readingSecs + rosUnityTimeOffset;\n"
                "      }\n",
                "      bool needsOffset;\n"
                "      lock (mutex)\n"
                "      {\n"
                "        readingSecs = lastReadingSecs;\n"
                "        needsOffset = !rosUnityTimeOffsetAcquired;\n"
                "        adjustedTime = readingSecs + rosUnityTimeOffset;\n"
                "      }\n"
                "      if (needsOffset)\n"
                "      {\n"
                "        var rosNowSecs = GetRosNowSeconds();\n"
                "        lock (mutex)\n"
                "        {\n"
                "          readingSecs = lastReadingSecs;\n"
                "          if (!rosUnityTimeOffsetAcquired)\n"
                "          {\n"
                "            rosUnityTimeOffset = rosNowSecs - readingSecs;\n"
                "            rosUnityTimeOffsetAcquired = true;\n"
                "          }\n"
                "          adjustedTime = readingSecs + rosUnityTimeOffset;\n"
                "        }\n"
                "      }\n",
                1,
            )

        if "return false;" not in text or "return true;" not in text:
            raise ValueError(f"{name} time-source bool contract patch did not apply.")
        write_text(source, text)


