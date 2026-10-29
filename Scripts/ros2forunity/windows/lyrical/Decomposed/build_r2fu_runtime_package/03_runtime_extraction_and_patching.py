def extract_runtime(paths: BuildPaths) -> None:
    """Extract the Ros2ForUnity asset folder into the runtime package layout."""
    runtime_root = paths.package / "Runtime" / "Ros2ForUnity"
    runtime_root.mkdir(parents=True, exist_ok=True)
    runtime_root_resolved = runtime_root.resolve()
    with zipfile.ZipFile(paths.artifact) as archive:
        for info in archive.infolist():
            name = info.filename
            if info.is_dir() or (
                not name.startswith("Ros2ForUnity/")
                and not name.startswith("StreamingAssets/")
            ):
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
    if len(parts) < 2 or parts[0] not in ("Ros2ForUnity", "StreamingAssets"):
        raise ValueError(f"Rejected unexpected runtime zip entry: {name}")
    if any(part in ("", ".", "..") for part in parts):
        raise ValueError(f"Rejected unsafe runtime zip entry: {name}")

    if parts[0] == "StreamingAssets":
        return Path(*parts)
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
    if UNITY_PACKAGE_PATH_PATCH_MARKER in text:
        text = patch_rmw_guard(text)
        text = patch_standalone_environment_isolation(text)
        text = patch_runtime_lifecycle_safety(text)
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
    text = patch_runtime_lifecycle_safety(text)
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
    """Patch ROS2ForUnity.cs to fail fast unless an explicitly supported RMW is active."""
    legacy_constant = '    private const string expectedRmwImplementation = "rmw_fastrtps_cpp";\n'
    if legacy_constant in text:
        text = text.replace(legacy_constant, RMW_CONSTANT_BLOCK, 1)

    if "defaultRmwImplementation" not in text:
        text = text.replace(
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n",
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n" + RMW_CONSTANT_BLOCK,
            1,
        )

    marker = "    private void RegisterCtrlCHandler()\n"
    if marker not in text:
        raise ValueError("Could not find RegisterCtrlCHandler marker for RMW guard patch.")

    if "ValidateRmwImplementation" in text:
        method_pattern = (
            r"    private static void ValidateRmwImplementation\(string rmwImpl\)\n"
            r"    \{\n.*?\n"
            r"    \}\n\n"
            r"(?:    private static bool IsSupportedRmwImplementation\(string rmwImpl\)\n"
            r"    \{\n.*?\n"
            r"    \}\n\n)?"
        )
        text, replacements = re.subn(method_pattern, RMW_VALIDATE_BLOCK, text, count=1, flags=re.S)
        if replacements != 1:
            raise ValueError("Could not replace existing ValidateRmwImplementation block.")
    else:
        text = text.replace(marker, RMW_VALIDATE_BLOCK + marker, 1)

    if "ValidateRmwImplementation(rmwImpl);" not in text:
        marker = "            string rmwImpl = Ros2cs.GetRMWImplementation();\n"
        if marker not in text:
            raise ValueError("Could not find RMW implementation read for RMW guard patch.")
        text = text.replace(marker, marker + "            ValidateRmwImplementation(rmwImpl);\n", 1)
    return text


def replace_existing_standalone_method(
    text: str,
    signature: str,
    next_signatures: tuple[str, ...],
    marker: str,
    legacy_marker: str,
    replacement: str,
) -> str:
    """Replace one legacy standalone method while preserving already patched sources."""
    method_signature = "    " + signature
    if method_signature not in text:
        return text
    start = text.find(method_signature)
    ends = [
        text.find("    " + next_signature, start + len(method_signature))
        for next_signature in next_signatures
    ]
    ends = [end for end in ends if end >= 0]
    if not ends:
        raise ValueError(f"Could not locate the {signature} method boundary.")
    end = min(ends)
    method_body = text[start:end]
    if marker in method_body:
        return text
    if legacy_marker not in method_body:
        label = (
            "AMENT_PREFIX_PATH"
            if "PrefixPath" in signature
            else "RMW_IMPLEMENTATION"
            if "RmwImplementation" in signature
            else "ROS_DISTRO"
        )
        raise ValueError(f"Could not apply {label} compatibility patch.")
    return text[:start] + replacement + text[end:]


def replace_existing_standalone_prefix_method(text: str) -> str:
    """Normalize an upstream prefix method before applying the environment isolation patch."""
    replacement = '''    private static void SetStandalonePrefixPath()
    {
        string prefixPath = GetRos2ForUnityPath();
        string streamingAssetsPrefixPath = Path.Combine(Application.streamingAssetsPath, ros2ForUnityAssetFolderName);
        string pluginPrefixPath = GetPluginPath();
        if (Directory.Exists(Path.Combine(streamingAssetsPrefixPath, "share")))
        {
            prefixPath = streamingAssetsPrefixPath;
        }
        else if (Directory.Exists(Path.Combine(pluginPrefixPath, "share")))
        {
            prefixPath = pluginPrefixPath;
        }
        // U2F-LOCAL-PATCH: standalone runtime must not inherit a sourced ROS2 workspace.
        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
    }

'''
    return replace_existing_standalone_method(
        text,
        "private static void SetStandalonePrefixPath()",
        ("private static void SetStandaloneRmwImplementation()",),
        "standalone runtime must not inherit a sourced ROS2 workspace",
        "prefixSource",
        replacement,
    )


def replace_existing_standalone_rmw_method(text: str) -> str:
    """Normalize the legacy Lyrical RMW method before applying environment isolation."""
    replacement = '''    private static void SetStandaloneRmwImplementation()
    {
        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection while allowing Lyrical Zenoh.
        string requestedRmwImplementation = Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION");
        string selectedRmwImplementation = IsSupportedRmwImplementation(requestedRmwImplementation)
            ? requestedRmwImplementation
            : defaultRmwImplementation;
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", selectedRmwImplementation);
    }

'''
    return replace_existing_standalone_method(
        text,
        "private static void SetStandaloneRmwImplementation()",
        ("private static void SetStandaloneRosDistro(",),
        "standalone runtime owns its RMW selection while allowing Lyrical Zenoh",
        "Fast-RTPS is the bundled standalone RMW",
        replacement,
    )


def replace_existing_standalone_distro_method(text: str) -> str:
    """Normalize the legacy ROS_DISTRO method before applying environment isolation."""
    replacement = '''    private static void SetStandaloneRosDistro(string ros2Codename)
    {
        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.
        SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
    }

'''
    return replace_existing_standalone_method(
        text,
        "private static void SetStandaloneRosDistro(",
        (
            "private static void SetStandaloneRos2csSpinFallback(",
            "private static void SetStandaloneRcutilsConsoleMode(",
            "private static string NormalizeEnvPathEntry(",
        ),
        "standalone runtime owns ROS_DISTRO",
        "String.IsNullOrEmpty(Environment.GetEnvironmentVariable(\"ROS_DISTRO\"))",
        replacement,
    )


def patch_standalone_environment_isolation(text: str) -> str:
    """Patch standalone startup so sourced ROS2 shells do not poison Unity."""
    text = replace_existing_standalone_prefix_method(text)
    text = replace_existing_standalone_rmw_method(text)
    text = replace_existing_standalone_distro_method(text)
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
    text = text.replace(old_check_signature, new_check_signature, 1)

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
    prefix_patch_marker = "standalone runtime must not inherit a sourced ROS2 workspace"
    if "private static void SetStandalonePrefixPath()" in text and prefix_patch_marker not in text:
        text = replace_required_once(text, old_prefix, new_prefix, "AMENT_PREFIX_PATH isolation patch")

    old_rmw = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))
        {
            SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
        }
'''
    new_rmw = '''        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection while allowing Lyrical Zenoh.
        string requestedRmwImplementation = Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION");
        string selectedRmwImplementation = IsSupportedRmwImplementation(requestedRmwImplementation)
            ? requestedRmwImplementation
            : defaultRmwImplementation;
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", selectedRmwImplementation);
'''
    old_owned_rmw = '''        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection.
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
'''
    rmw_patch_marker = "standalone runtime owns its RMW selection while allowing Lyrical Zenoh"
    if "private static void SetStandaloneRmwImplementation()" in text and rmw_patch_marker not in text:
        old_rmw_matches = text.count(old_rmw)
        old_owned_rmw_matches = text.count(old_owned_rmw)
        if old_rmw_matches + old_owned_rmw_matches != 1:
            raise ValueError(
                "Could not apply RMW_IMPLEMENTATION isolation patch: expected one supported upstream body."
            )
        text = (
            replace_required_once(text, old_rmw, new_rmw, "RMW_IMPLEMENTATION isolation patch")
            if old_rmw_matches == 1
            else replace_required_once(text, old_owned_rmw, new_rmw, "RMW_IMPLEMENTATION isolation patch")
        )

    if "#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN\n    [DllImport(\"ucrtbase.dll\"" not in text:
        text = text.replace(
            '''    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int _wputenv_s(string name, string value);
''',
            '''#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int _wputenv_s(string name, string value);
#endif
''',
            1,
        )
    text = text.replace(
        '''        if (GetOS() == Platform.Windows)
        {
            int result = _wputenv_s(name, value ?? String.Empty);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Failed to set Windows CRT environment variable '" + name + "' (ucrtbase _wputenv_s returned " + result + ")");
            }
        }
''',
        '''        if (GetOS() == Platform.Windows)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            int result = _wputenv_s(name, value ?? String.Empty);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Failed to set Windows CRT environment variable '" + name + "' (ucrtbase _wputenv_s returned " + result + ")");
            }
#else
            throw new PlatformNotSupportedException("Windows CRT environment updates require a Windows Unity build target.");
#endif
        }
''',
        1,
    )
    if "internal static void PrewarmUnityPaths()" not in text:
        text = text.replace(
            "\n    public static string GetRos2ForUnityPath()\n",
            "\n    internal static void PrewarmUnityPaths()\n"
            "    {\n"
            "        _ = GetRos2ForUnityPath();\n"
            "        _ = GetPluginPath();\n"
            "    }\n\n"
            "    public static string GetRos2ForUnityPath()\n",
            1,
        )

    old_distro = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROS_DISTRO")))
        {
            SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
        }
'''
    new_distro = '''        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.
        SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
'''
    distro_patch_marker = "standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell"
    if "private static void SetStandaloneRosDistro(" in text and distro_patch_marker not in text:
        text = replace_required_once(text, old_distro, new_distro, "ROS_DISTRO isolation patch")
    if "WarnIfStandaloneRosDistroOverride" not in text:
        text = text.replace(
            "\n    private static void SetStandaloneRcutilsConsoleMode()\n",
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
            "    private static void SetStandaloneRcutilsConsoleMode()\n",
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
            if (IsStandalone())
            {
                string packagedRos2Version = GetMetadataValue(ros2csMetadata, "/ros2cs/ros2");
                SetStandaloneRosDistro(packagedRos2Version);
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
                SetStandaloneRcutilsConsoleMode();
            }
'''
    if "sourcedRosDistroBeforeStandalonePatch" not in text:
        if "packagedRos2Version = GetMetadataValue" not in text and startup_marker in text:
            text = text.replace(startup_marker, startup_patch, 1)
        else:
            text = text.replace(
                startup_marker,
                startup_marker + "            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();\n",
                1,
            )
    if "SetStandalonePrefixPath();" not in text or "SetStandaloneRmwImplementation();" not in text:
        raise ValueError("Standalone environment isolation patch is missing required setup calls.")
    text = text.replace(
        '            string standalone = IsStandalone() ? "standalone" : "non-standalone";\n',
        '            bool standaloneBuild = IsStandalone();\n'
        '            string standalone = standaloneBuild ? "standalone" : "non-standalone";\n',
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
    text = text.replace(
        "                if (IsStandalone())\n",
        "                if (standaloneBuild)\n",
        1,
    )
    text = text.replace(
        '''                if (standaloneBuild)
                {
                    SetStandaloneRosDistro(currentRos2Version);
                    SetStandalonePrefixPath();
                    SetStandaloneRmwImplementation();
                    SetStandaloneRcutilsConsoleMode();
                }
''',
        "",
        1,
    )

    required_method_markers = (
        ("private static void SetStandalonePrefixPath()", "standalone runtime must not inherit a sourced ROS2 workspace", "AMENT_PREFIX_PATH"),
        ("private static void SetStandaloneRmwImplementation()", "standalone runtime owns its RMW selection while allowing Lyrical Zenoh", "RMW_IMPLEMENTATION"),
        ("private static void SetStandaloneRosDistro(", "standalone runtime owns ROS_DISTRO", "ROS_DISTRO"),
    )
    for method_marker, patch_marker, label in required_method_markers:
        if method_marker in text and patch_marker not in text:
            raise ValueError(f"Could not apply {label} isolation patch to the standalone method body.")
    return text


def patch_component_main_thread_prewarm(package: Path) -> None:
    """Patch ROS2UnityComponent so Unity API backed Lazy paths are warmed on the main thread."""
    component = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2UnityComponent.cs"
    text = component.read_text(encoding="utf-8")
    if "ROS2ForUnity.PrewarmUnityPaths();" not in text:
        text = text.replace(
            "    private readonly object mutex = new object();\n    private double spinTimeout = 0.0001;\n\n",
            "    private readonly object mutex = new object();\n"
            "    private double spinTimeout = 0.0001;\n\n"
            "    private void Awake()\n"
            "    {\n"
            "        ROS2ForUnity.PrewarmUnityPaths();\n"
            "    }\n\n"
            "    /// <summary>\n"
            "    /// Checks ROS2 availability. The first call must happen on Unity's main thread,\n"
            "    /// or after Awake has prewarmed Unity API backed package paths.\n"
            "    /// </summary>\n",
            1,
        )
    text = text.replace("            runtimeShutdownRequested = false;\n", "", 1)
    component.write_text(text, encoding="utf-8", newline="\n")


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
    if MODIFICATIONS_COPYRIGHT not in dotnet_text:
        raise ValueError("DotnetTimeSource.cs is missing the local modifications copyright line.")
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

        if "return false;" not in text or "return true;" not in text:
            raise ValueError(f"{name} time-source bool contract patch did not apply.")
        write_text(source, text)


def patch_zenoh_router_config_notes(package: Path) -> None:
    """Document the bundled Zenoh router config as a trusted-lab development profile."""
    runtime_root = package / "Runtime" / "Ros2ForUnity"
    config_relatives = (
        Path("Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"),
        Path("StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"),
    )
    header_note = (
        "/// Unity2Foxglove package note: this is the upstream ROS2 Zenoh router development profile.\n"
        "/// It listens on tcp/[::]:7447 without authentication or ACLs so routed ROS2 peers on\n"
        "/// the local network can connect during lab acceptance. Do not use this profile on an\n"
        "/// untrusted network; copy it to a localhost-only or ACL-protected deployment profile.\n"
    )
    limit_note = (
        "      /// Unity2Foxglove package note: this high development default is unsuitable for\n"
        "      /// untrusted networks; use a hardened deployment profile with lower limits.\n"
    )

    for relative in config_relatives:
        path = runtime_root / relative
        if not path.exists():
            continue

        text = path.read_text(encoding="utf-8")
        if "Unity2Foxglove package note: this is the upstream ROS2 Zenoh router development profile." not in text:
            marker = "/// Note that the values here are correctly typed, but may not be sensible, so copying this file to change only the parts that matter to you is not good practice.\n"
            if marker not in text:
                raise ValueError(f"Could not find Zenoh router header in {relative.as_posix()}.")
            text = text.replace(marker, marker + header_note, 1)

        if "Unity2Foxglove package note: this high development default is unsuitable for" not in text:
            text = text.replace(
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      accept_pending: 10000,\n",
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together.\n"
                + limit_note +
                "      accept_pending: 10000,\n",
                1,
            )
            text = text.replace(
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      max_sessions: 10000,\n",
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together.\n"
                + limit_note +
                "      max_sessions: 10000,\n",
                1,
            )

        write_text(path, text)


def patch_zenoh_session_config_safety(package: Path) -> None:
    """Reapply bounded, non-fatal Zenoh session defaults to both packaged mirrors."""
    runtime_root = package / "Runtime" / "Ros2ForUnity"
    config_relatives = (
        Path("Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"),
        Path("StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"),
    )
    old_rx_block = (
        "        /// Maximum size of the defragmentation buffer at receiver end.\n"
        "        /// Fragmented messages that are larger than the configured size will be dropped.\n"
        "        /// The default value is 1GiB. This would work in most scenarios.\n"
        "        /// NOTE: reduce the value if you are operating on a memory constrained device.\n"
        "        max_message_size: 1073741824,\n"
    )
    new_rx_block = (
        "        /// Maximum size of the defragmentation buffer at receiver end.\n"
        "        /// Fragmented messages that are larger than the configured size will be dropped.\n"
        "        /// Unity2Foxglove package safety: cap the receiver buffer to 128MiB\n"
        "        /// so Unity player processes do not reserve the upstream 1GiB worst case.\n"
        "        max_message_size: 134217728,\n"
    )
    old_adminspace_block = (
        "  adminspace: {\n"
        "    /// Enables the admin space\n"
        "    enabled: true,\n"
        "    /// read and/or write permissions on the admin space\n"
        "    permissions: {\n"
        "      read: true,\n"
        "      write: false,\n"
        "    },\n"
        "  },\n"
    )
    new_adminspace_block = (
        "  adminspace: {\n"
        "    /// Enables the admin space\n"
        "    enabled: false,\n"
        "    /// read and/or write permissions on the admin space\n"
        "    permissions: {\n"
        "      read: false,\n"
        "      write: false,\n"
        "    },\n"
        "  },\n"
    )

    for relative in config_relatives:
        path = runtime_root / relative
        if not path.exists():
            raise ValueError(f"Missing Zenoh session config: {relative.as_posix()}")

        text = path.read_text(encoding="utf-8")
        if text.count("    exit_on_failure: true,\n") != 1:
            raise ValueError(f"Could not find the Zenoh listen exit policy in {relative.as_posix()}.")
        if text.count(old_rx_block) != 1:
            raise ValueError(f"Could not find the Zenoh RX buffer policy in {relative.as_posix()}.")
        if text.count(old_adminspace_block) != 1:
            raise ValueError(f"Could not find the Zenoh adminspace policy in {relative.as_posix()}.")

        text = text.replace(
            "    exit_on_failure: true,\n",
            "    /// Unity2Foxglove package safety: a busy local router port is non-fatal.\n"
            "    exit_on_failure: false,\n",
            1,
        )
        text = text.replace(old_rx_block, new_rx_block, 1)
        text = text.replace(old_adminspace_block, new_adminspace_block, 1)
        write_text(path, text)


def update_zenoh_config_inventory_hashes(package: Path) -> None:
    """Refresh inventory hashes for package-patched Zenoh config mirrors."""
    inventory_path = package / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json"
    if not inventory_path.exists():
        return

    data = json.loads(inventory_path.read_text(encoding="utf-8"))
    changed = False
    for item in data.get("files", []):
        relative = str(item.get("path", ""))
        if "DEFAULT_RMW_ZENOH" not in relative:
            continue

        source_relative = "Ros2ForUnity/" + relative if relative.startswith("StreamingAssets/") else relative
        source_path = package / "Runtime" / source_relative
        if not source_path.exists():
            continue

        item["sha256"] = sha256_file(source_path)
        item["size"] = source_path.stat().st_size
        changed = True

    if changed:
        write_json(inventory_path, data)
