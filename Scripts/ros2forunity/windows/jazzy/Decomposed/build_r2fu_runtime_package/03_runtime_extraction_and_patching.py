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


def copy_supplemental_runtime_dlls(package: Path, ros2_bin: Path) -> None:
    """Copy Jazzy FastRTPS dependencies missing from the pinned R2FU artifact."""
    plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    for name in PHASE161_SUPPLEMENTAL_RUNTIME_DLLS:
        source = ros2_bin / name
        if not source.exists():
            raise FileNotFoundError(
                f"Missing supplemental Jazzy runtime DLL {source}; "
                "install the repo-local ros2-windows/ros2_jazzy entrypoint before rebuilding this package."
            )
        shutil.copy2(source, plugin_root / name)


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
    if UNITY_PACKAGE_PATH_PATCH_MARKER in text:
        text = patch_standalone_environment_bootstrap(text)
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
    text = patch_standalone_environment_bootstrap(text)
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
        string pluginPrefixPath = GetPluginPath();
        if (Directory.Exists(Path.Combine(pluginPrefixPath, "share")))
        {
            prefixPath = pluginPrefixPath;
        }
        else if (!Directory.Exists(Path.Combine(prefixPath, "share")))
        {
            Debug.LogWarning("Standalone AMENT_PREFIX_PATH fallback has no share directory: " + prefixPath);
        }

        // U2F-LOCAL-PATCH: standalone runtime must not inherit or require a sourced ROS 2 workspace.
        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
    }

'''
    return replace_existing_standalone_method(
        text,
        "private static void SetStandalonePrefixPath()",
        ("private static void SetStandaloneRmwImplementation()",),
        "standalone runtime must not inherit or require a sourced ROS 2 workspace",
        "prefixSource",
        replacement,
    )


def replace_existing_standalone_rmw_method(text: str) -> str:
    """Normalize the legacy Jazzy RMW method before applying environment isolation."""
    replacement = '''    private static void SetStandaloneRmwImplementation()
    {
        // U2F-LOCAL-PATCH: standalone Jazzy runtime owns its RMW selection.
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", expectedRmwImplementation);
    }

'''
    return replace_existing_standalone_method(
        text,
        "private static void SetStandaloneRmwImplementation()",
        ("private static void SetStandaloneRosDistro(",),
        "standalone Jazzy runtime owns its RMW selection",
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


def patch_standalone_environment_bootstrap(text: str) -> str:
    """Patch standalone Jazzy environment writes so native ROS 2 getenv callers see them."""
    expected_rmw_constant = '    private const string expectedRmwImplementation = "rmw_fastrtps_cpp";\n'
    if expected_rmw_constant not in text:
        anchor = '    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";\n'
        if anchor not in text:
            raise ValueError("Could not locate the ROS2ForUnity asset-folder constant for RMW setup.")
        text = text.replace(anchor, anchor + expected_rmw_constant, 1)
    text = replace_existing_standalone_prefix_method(text)
    text = replace_existing_standalone_rmw_method(text)
    text = replace_existing_standalone_distro_method(text)
    if "using System.Runtime.InteropServices;" not in text:
        text = text.replace(
            "using System.Reflection;\n",
            "using System.Reflection;\nusing System.Runtime.InteropServices;\n",
            1,
        )

    if "_wputenv_s" not in text:
        text = text.replace(
            "    private bool ownsLifecycle;\n",
            "    private bool ownsLifecycle;\n\n"
            "    // Windows standalone ROS 2 libraries read getenv() through UCRT, so mirror managed env writes there.\n"
            "    [DllImport(\"ucrtbase.dll\", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]\n"
            "    private static extern int _wputenv_s(string name, string value);\n",
            1,
        )

    if "SetProcessEnvironmentVariable" not in text:
        text = text.replace(
            "    private string GetEnvPathVariableValue()\n"
            "    {\n"
            "        return Environment.GetEnvironmentVariable(GetEnvPathVariableName());\n"
            "    }\n\n",
            "    private string GetEnvPathVariableValue()\n"
            "    {\n"
            "        return Environment.GetEnvironmentVariable(GetEnvPathVariableName());\n"
            "    }\n\n"
            "    private static void SetProcessEnvironmentVariable(string name, string value)\n"
            "    {\n"
            "        Environment.SetEnvironmentVariable(name, value);\n"
            "        if (GetOS() == Platform.Windows)\n"
            "        {\n"
            "            // U2F-LOCAL-PATCH: ROS 2 Windows native code reads getenv() from UCRT.\n"
            "            int result = _wputenv_s(name, value ?? String.Empty);\n"
            "            if (result != 0)\n"
            "            {\n"
            "                throw new InvalidOperationException(\n"
            "                    \"Failed to set Windows CRT environment variable '\" + name + \"' (ucrtbase _wputenv_s returned \" + result + \")\");\n"
            "            }\n"
            "        }\n"
            "    }\n\n",
            1,
        )

    text = text.replace(
        "        Environment.SetEnvironmentVariable(GetEnvPathVariableName(), string.Join(envPathSep.ToString(), entries));",
        "        SetProcessEnvironmentVariable(GetEnvPathVariableName(), string.Join(envPathSep.ToString(), entries));",
        1,
    )
    if "Environment.SetEnvironmentVariable(GetEnvPathVariableName()," in text:
        raise ValueError("Could not patch ROS2ForUnity standalone environment bootstrap call site.")

    if "SetStandalonePrefixPath" not in text:
        text = text.replace(
            "\n    private static string NormalizeEnvPathEntry(string value)\n",
            "\n    private static void SetStandalonePrefixPath()\n"
            "    {\n"
            "        string prefixPath = GetRos2ForUnityPath();\n"
            "        string pluginPrefixPath = GetPluginPath();\n"
            "        if (Directory.Exists(Path.Combine(pluginPrefixPath, \"share\")))\n"
            "        {\n"
            "            prefixPath = pluginPrefixPath;\n"
            "        }\n"
            "        else if (!Directory.Exists(Path.Combine(prefixPath, \"share\")))\n"
            "        {\n"
            "            Debug.LogWarning(\"Standalone AMENT_PREFIX_PATH fallback has no share directory: \" + prefixPath);\n"
            "        }\n\n"
            "        // U2F-LOCAL-PATCH: standalone runtime must not inherit or require a sourced ROS 2 workspace.\n"
            "        SetProcessEnvironmentVariable(\"AMENT_PREFIX_PATH\", prefixPath);\n"
            "    }\n\n"
            "    private static void SetStandaloneRmwImplementation()\n"
            "    {\n"
            "        // U2F-LOCAL-PATCH: standalone Jazzy runtime owns its RMW selection.\n"
            "        SetProcessEnvironmentVariable(\"RMW_IMPLEMENTATION\", expectedRmwImplementation);\n"
            "    }\n\n"
            "    private static void SetStandaloneRosDistro(string ros2Codename)\n"
            "    {\n"
            "        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.\n"
            "        SetProcessEnvironmentVariable(\"ROS_DISTRO\", ros2Codename);\n"
            "    }\n\n"
            "    private static string NormalizeEnvPathEntry(string value)\n",
            1,
        )
    elif "SetStandaloneRosDistro" not in text:
        text = text.replace(
            "\n    private static string NormalizeEnvPathEntry(string value)\n",
            "\n    private static void SetStandaloneRosDistro(string ros2Codename)\n"
            "    {\n"
            "        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.\n"
            "        SetProcessEnvironmentVariable(\"ROS_DISTRO\", ros2Codename);\n"
            "    }\n\n"
            "    private static string NormalizeEnvPathEntry(string value)\n",
            1,
        )

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
    if "WarnIfStandaloneRosDistroOverride" not in text:
        text = text.replace(
            "\n    /// <summary>\n    /// Checks if both ros2cs and ros2-for-unity were build for the same ros version as well as\n",
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
            "    /// <summary>\n"
            "    /// Checks if both ros2cs and ros2-for-unity were build for the same ros version as well as\n",
            1,
        )

    old_metadata_mismatch = '''        if (ros2FromRos4UMetadata != ros2FromRos2csMetadata) {
            Debug.LogError(
                "ROS2 versions in 'ros2cs' and 'ros2-for-unity' metadata files are not the same. " +
                "This is caused by mixing versions/builds. Plugin might not work correctly."
            );
        }
'''
    new_metadata_mismatch = '''        if (ros2FromRos4UMetadata != ros2FromRos2csMetadata) {
            FailIntegrity(
                "ROS2 versions in 'ros2cs' and 'ros2-for-unity' metadata files are not the same. " +
                "This is caused by mixing versions/builds.");
        }
'''
    text = text.replace(old_metadata_mismatch, new_metadata_mismatch)
    old_non_standalone_mismatch = '''        if(!IsStandalone() && ros2SourcedCodename != ros2FromRos2csMetadata) {
            Debug.LogError(
                "ROS2 version in 'ros2cs' metadata doesn't match currently sourced version. " +
                "This is caused by mixing versions/builds. Plugin might not work correctly."
            );
        }
'''
    new_non_standalone_mismatch = '''        if(!IsStandalone() && ros2SourcedCodename != ros2FromRos2csMetadata) {
            FailIntegrity(
                "ROS2 version in 'ros2cs' metadata doesn't match currently sourced version. " +
                "This is caused by mixing versions/builds.");
        }
'''
    text = text.replace(old_non_standalone_mismatch, new_non_standalone_mismatch)
    old_standalone_sourced = '''        if (IsStandalone() && !string.IsNullOrEmpty(ros2SourcedCodename)) {
            Debug.LogError(
                "You should not source ROS2 in 'ros2-for-unity' standalone build. " +
                "Plugin might not work correctly."
            );
        }
    }
'''
    new_standalone_sourced = '''    }

    private static void FailIntegrity(string errMessage)
    {
        Debug.LogError(errMessage);
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
        throw new InvalidOperationException(errMessage);
#else
        const int ROS_METADATA_MISMATCH_ERROR_CODE = 35;
        Application.Quit(ROS_METADATA_MISMATCH_ERROR_CODE);
        throw new InvalidOperationException(errMessage);
#endif
    }
'''
    text = text.replace(old_standalone_sourced, new_standalone_sourced)
    if "sourcedRosDistroBeforeStandalonePatch" not in text:
        old_startup = '''        // Load metadata
        LoadMetadata();
        string currentRos2Version = GetROSVersion();
        string standalone = IsStandalone() ? "standalone" : "non-standalone";

        // Self checks
        if (!IsStandalone())
        {
            CheckROSSupport(currentRos2Version);
        }
        CheckIntegrity();
'''
        new_startup = '''        // Load metadata
        LoadMetadata();
        string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
        bool standaloneBuild = IsStandalone();
        if (standaloneBuild)
        {
            SetStandalonePrefixPath();
            SetStandaloneRmwImplementation();
        }

        string currentRos2Version = standaloneBuild
            ? GetMetadataValue(ros2csMetadata, "/ros2cs/ros2")
            : GetROSVersion();
        if (standaloneBuild)
        {
            SetStandaloneRosDistro(currentRos2Version);
        }
        string standalone = standaloneBuild ? "standalone" : "non-standalone";

        // Self checks
        CheckROSSupport(currentRos2Version);
        WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);
        CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);
'''
        text = text.replace(old_startup, new_startup)
        if "sourcedRosDistroBeforeStandalonePatch" not in text:
            refreshed_upstream_startup = '''            // Load metadata
            LoadMetadata();
            string currentRos2Version = GetROSVersion();
            string standalone = IsStandalone() ? "standalone" : "non-standalone";

            // Self checks
            CheckROSSupport(currentRos2Version);
            CheckIntegrity();
            bool standaloneBuild = IsStandalone();
            WarnIfLyricalSpinFallbackUnset(currentRos2Version, standaloneBuild);

            // Library loading
'''
            refreshed_startup_patch = '''            // Load metadata
            LoadMetadata();
            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
            bool standaloneBuild = IsStandalone();
            if (standaloneBuild)
            {
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
            }

            string currentRos2Version = standaloneBuild
                ? GetMetadataValue(ros2csMetadata, "/ros2cs/ros2")
                : GetROSVersion();
            if (standaloneBuild)
            {
                SetStandaloneRosDistro(currentRos2Version);
            }
            string standalone = standaloneBuild ? "standalone" : "non-standalone";

            // Self checks
            CheckROSSupport(currentRos2Version);
            WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);
            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);
            WarnIfLyricalSpinFallbackUnset(currentRos2Version, standaloneBuild);

            // Library loading
'''
            text = text.replace(refreshed_upstream_startup, refreshed_startup_patch, 1)
            text = text.replace(
                '''            if (standaloneBuild)
            {
                // For standalone, currentRos2Version comes from metadata, not ROS_DISTRO.
                // SetStandaloneRosDistro must stay after CheckROSSupport/CheckIntegrity.
                SetStandaloneRosDistro(currentRos2Version);
                SetStandaloneRos2csSpinFallback(currentRos2Version);
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
                SetStandaloneRcutilsConsoleMode();
            }
''',
                '''            if (standaloneBuild)
            {
                SetStandaloneRos2csSpinFallback(currentRos2Version);
                SetStandaloneRcutilsConsoleMode();
            }
''',
                1,
            )
    text = text.replace(
        "        CheckIntegrity(" + "sourcedRosDistroBeforeStandalonePatch);\n",
        "        WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);\n"
        "        CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n",
        1,
    )

    if "SetStandalonePrefixPath();" not in text:
        text = text.replace(
            "        // Library loading\n"
            "        if (GetOS() == Platform.Windows) {\n",
            "        // Library loading\n"
            "        if (IsStandalone())\n"
            "        {\n"
            "            SetStandalonePrefixPath();\n"
            "            SetStandaloneRmwImplementation();\n"
            "        }\n"
            "        if (GetOS() == Platform.Windows) {\n",
            1,
        )

    for token in (
        "_wputenv_s",
        "SetStandalonePrefixPath",
        "AMENT_PREFIX_PATH",
        "SetStandaloneRmwImplementation",
        "sourcedRosDistroBeforeStandalonePatch",
        "FailIntegrity",
    ):
        if token not in text:
            raise ValueError(f"Could not patch ROS2ForUnity standalone environment bootstrap token: {token}")
    required_method_markers = (
        ("private static void SetStandalonePrefixPath()", "standalone runtime must not inherit or require a sourced ROS 2 workspace", "AMENT_PREFIX_PATH"),
        ("private static void SetStandaloneRmwImplementation()", "standalone Jazzy runtime owns its RMW selection", "RMW_IMPLEMENTATION"),
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
            "/// <summary>\n"
            "/// Interface for acquiring time.\n"
            "/// </summary>\n"
            "public interface ITimeSource\n"
            "{\n"
            "  /// <returns>True when a valid timestamp was acquired; false when the source is not currently usable.</returns>\n"
            "  bool GetTime(out int seconds, out uint nanoseconds);\n"
            "}\n",
            "/// <summary>\n"
            "/// Interface for acquiring time as ROS-compatible timestamp fields from a concrete time source.\n"
            "/// </summary>\n"
            "public interface ITimeSource\n"
            "{\n"
            "  /// <summary>\n"
            "  /// Tries to acquire the current timestamp for ROS message headers and clock messages.\n"
            "  /// </summary>\n"
            "  /// <param name=\"seconds\">Whole seconds of the acquired timestamp, or 0 when this method returns false.</param>\n"
            "  /// <param name=\"nanoseconds\">Nanoseconds within the second, or 0 when this method returns false.</param>\n"
            "  /// <returns>True when a valid timestamp was acquired; false when the source is not currently usable.</returns>\n"
            "  /// <remarks>\n"
            "  /// Epoch semantics are source-specific: DotnetTimeSource and ROS2TimeSource report Unix/ROS-aligned time,\n"
            "  /// while UnityTimeSource reports Unity play time. Callers must not use the out values when this method\n"
            "  /// returns false.\n"
            "  /// </remarks>\n"
            "  bool GetTime(out int seconds, out uint nanoseconds);\n"
            "}\n",
            1,
        )
        if "Epoch semantics are source-specific" not in interface_text:
            raise ValueError("ITimeSource.cs is missing the expanded bool-returning time-source contract documentation.")
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

    time_utils = time_dir / "TimeUtils.cs"
    time_utils_text = time_utils.read_text(encoding="utf-8")
    if "Double.IsNaN(secondsIn)" not in time_utils_text:
        time_utils_text = time_utils_text.replace(
            "internal static class TimeUtils\n"
            "{\n"
            "  public static void TimeFromTotalSeconds(in double secondsIn, out int seconds, out uint nanoseconds)\n"
            "  {\n"
            "    seconds = (int)Math.Floor(secondsIn);\n"
            "    double fractionalSeconds = secondsIn - seconds;\n"
            "    long normalizedNanoseconds = (long)Math.Floor(fractionalSeconds * 1000000000.0);\n"
            "    if (normalizedNanoseconds >= 1000000000L)\n"
            "    {\n"
            "      seconds++;\n"
            "      normalizedNanoseconds -= 1000000000L;\n"
            "    }\n"
            "    else if (normalizedNanoseconds < 0)\n"
            "    {\n"
            "      seconds--;\n"
            "      normalizedNanoseconds += 1000000000L;\n"
            "    }\n"
            "    nanoseconds = (uint)normalizedNanoseconds;\n"
            "  }\n"
            "}\n",
            "internal static class TimeUtils\n"
            "{\n"
            "  private const double NanosecondsPerSecondDouble = 1_000_000_000.0;\n"
            "  private const long NanosecondsPerSecond = 1_000_000_000L;\n"
            "\n"
            "  public static void TimeFromTotalSeconds(in double secondsIn, out int seconds, out uint nanoseconds)\n"
            "  {\n"
            "    if (Double.IsNaN(secondsIn) || Double.IsInfinity(secondsIn))\n"
            "    {\n"
            "      throw new ArgumentOutOfRangeException(nameof(secondsIn), \"ROS time cannot be NaN or infinity\");\n"
            "    }\n"
            "\n"
            "    double wholeSeconds = Math.Floor(secondsIn);\n"
            "    double fractionalSeconds = secondsIn - wholeSeconds;\n"
            "    long normalizedNanoseconds = (long)Math.Floor(fractionalSeconds * NanosecondsPerSecondDouble);\n"
            "    if (normalizedNanoseconds >= NanosecondsPerSecond)\n"
            "    {\n"
            "      wholeSeconds += 1.0;\n"
            "      normalizedNanoseconds -= NanosecondsPerSecond;\n"
            "    }\n"
            "    else if (normalizedNanoseconds < 0)\n"
            "    {\n"
            "      wholeSeconds -= 1.0;\n"
            "      normalizedNanoseconds += NanosecondsPerSecond;\n"
            "    }\n"
            "\n"
            "    if (wholeSeconds < Int32.MinValue || wholeSeconds > Int32.MaxValue)\n"
            "    {\n"
            "      throw new OverflowException(\"ROS time seconds exceed Int32 range\");\n"
            "    }\n"
            "\n"
            "    seconds = (int)wholeSeconds;\n"
            "    nanoseconds = (uint)normalizedNanoseconds;\n"
            "  }\n"
            "}\n",
            1,
        )
    if "Double.IsNaN(secondsIn)" not in time_utils_text or "Int32.MaxValue" not in time_utils_text:
        raise ValueError("TimeUtils.cs is missing hardened seconds validation guards.")
    write_text(time_utils, time_utils_text)

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

        if name == "ROS2TimeSource.cs" and "private readonly object clockMutex = new object();" not in text:
            text = text.replace(
                "  private ROS2.Clock clock;\n",
                "  private readonly object clockMutex = new object();\n"
                "  private ROS2.Clock clock;\n",
                1,
            )
            text = text.replace(
                "    if (clock == null)\n"
                "    { // Create clock which uses system time by default (unless use_sim_time is set in ros2)\n"
                "      if (Volatile.Read(ref disposed) != 0)\n"
                "      {\n"
                "        seconds = 0;\n"
                "        nanoseconds = 0;\n"
                "        return false;\n"
                "      }\n"
                "      clock = new ROS2.Clock();\n"
                "    }\n"
                "  \n"
                "    TimeUtils.TimeFromTotalSeconds(clock.Now.Seconds, out seconds, out nanoseconds);\n",
                "    double nowSeconds;\n"
                "    lock (clockMutex)\n"
                "    {\n"
                "      if (Volatile.Read(ref disposed) != 0)\n"
                "      {\n"
                "        seconds = 0;\n"
                "        nanoseconds = 0;\n"
                "        return false;\n"
                "      }\n"
                "\n"
                "      if (clock == null)\n"
                "      { // Create clock which uses system time by default (unless use_sim_time is set in ros2)\n"
                "        clock = new ROS2.Clock();\n"
                "      }\n"
                "\n"
                "      nowSeconds = clock.Now.Seconds;\n"
                "    }\n"
                "  \n"
                "    TimeUtils.TimeFromTotalSeconds(nowSeconds, out seconds, out nanoseconds);\n",
                1,
            )
            text = text.replace(
                "    // U2F-LOCAL-PATCH: avoid native cleanup from the finalizer thread.\n"
                "    if (clock != null)\n"
                "    {\n"
                "      clock.Dispose();\n"
                "      clock = null;\n"
                "    }\n",
                "    // U2F-LOCAL-PATCH: avoid native cleanup from the finalizer thread.\n"
                "    lock (clockMutex)\n"
                "    {\n"
                "      if (clock != null)\n"
                "      {\n"
                "        clock.Dispose();\n"
                "        clock = null;\n"
                "      }\n"
                "    }\n",
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
