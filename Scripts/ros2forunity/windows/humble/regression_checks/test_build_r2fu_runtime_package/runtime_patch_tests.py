from __future__ import annotations

from .foundation import *


class RuntimePatchOverlayTests:
    """Exercise source patches applied to the generated runtime."""

    def test_patch_deps_json_sha512_updates_inventory_hash(self) -> None:
        """Generated deps.json files should carry integrity hints and matching inventory hashes."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins"
            support = package / "RuntimeSupport"
            plugin_root.mkdir(parents=True)
            support.mkdir(parents=True)
            (plugin_root / "example.dll").write_bytes(b"example")
            (plugin_root / "dependency.dll").write_bytes(b"dependency")
            deps = plugin_root / "example.deps.json"
            deps.write_text(
                json.dumps({"libraries": {"example/1.0.0": {"sha512": ""}, "dependency/0.0.0": {"sha512": ""}}}),
                encoding="utf-8",
            )
            inventory = support / "r2fu-humble-win64-runtime-inventory.json"
            inventory.write_text(
                json.dumps({"files": [{"path": "Ros2ForUnity/Plugins/example.deps.json", "sha256": "", "size": 0}]}),
                encoding="utf-8",
            )

            self.builder.patch_deps_json_sha512(package)

            patched = json.loads(deps.read_text(encoding="utf-8"))
            self.assertEqual(128, len(patched["libraries"]["example/1.0.0"]["sha512"]))
            self.assertEqual(128, len(patched["libraries"]["dependency/0.0.0"]["sha512"]))
            patched_inventory = json.loads(inventory.read_text(encoding="utf-8"))
            self.assertEqual(self.builder.sha256_file(deps), patched_inventory["files"][0]["sha256"])
            self.assertEqual(deps.stat().st_size, patched_inventory["files"][0]["size"])

    def test_patch_ros2_for_unity_requires_copyright_replacement(self) -> None:
        """Patch generation fails when the expected copyright line is absent."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            source = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
            source.parent.mkdir(parents=True)
            source.write_text(
                '    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";\n'
                + self.builder.UPSTREAM_PATH_BLOCK,
                encoding="utf-8",
            )

            with self.assertRaises(ValueError):
                self.builder.patch_ros2_for_unity(package)

    def test_player_metadata_patch_uses_streaming_assets_root(self) -> None:
        """Player metadata must follow the staged StreamingAssets layout."""
        source = 'string ros2csMetadataPath = GetPluginPath() + separator + "metadata_ros2cs.xml";'
        patched = self.builder.patch_runtime_metadata_path(source)
        self.assertIn('GetRos2ForUnityPath() + separator + "metadata_ros2cs.xml"', patched)
        self.assertNotIn("GetPluginPath()", patched)

    def test_runtime_safety_patches_survive_the_new_upstream_layout(self) -> None:
        """Lifecycle and Unity-time safety patches must survive an upstream runtime refresh."""
        runtime = (
            "        EditorApplication.playModeStateChanged += EditorPlayStateChanged;\n"
            "        EditorApplication.quitting += ShutdownShared;\n"
            "        editorHandlersRegistered = true;\n"
            "        EditorApplication.playModeStateChanged -= EditorPlayStateChanged;\n"
            "        EditorApplication.quitting -= ShutdownShared;\n"
            "        editorHandlersRegistered = false;\n"
            "    }\n\n"
            "    private static void ThrowIfUninitialized(string callContext)\n"
            "    {\n"
            "        if (!isInitialized)\n"
            "        {\n"
            "            throw new InvalidOperationException(\"not initialized\");\n"
            "        }\n"
            "    }\n\n"
            "            throw new InvalidOperationException(\"Metadata document is empty while reading \" + valuePath);\n"
        )
        unity_time = (
            "  public UnityTimeSource()\n"
            "  {\n"
            "    mainThreadId = Thread.CurrentThread.ManagedThreadId;\n"
            "    lastReadingSecs = Time.timeAsDouble;\n"
            "  }\n"
        )

        patched_runtime = self.builder.patch_runtime_lifecycle_safety(runtime)
        patched_time = self.builder.patch_unity_time_source_main_thread_guard(unity_time)
        patched_startup = self.builder.patch_standalone_environment_isolation(
            "            // Load metadata\n"
            "            LoadMetadata();\n"
            "            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();\n"
        )

        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared", patched_runtime)
        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared", patched_runtime)
        self.assertNotIn("ThrowIfUninitialized", patched_runtime)
        self.assertIn("LoadMetadata() must complete before metadata-backed properties are read.", patched_runtime)
        self.assertIn("must be constructed on the Unity main thread", patched_time)
        self.assertEqual(1, patched_startup.count("sourcedRosDistroBeforeStandalonePatch"))

    def test_standalone_prefix_patch_removes_unused_prefix_source(self) -> None:
        """Standalone isolation must not leave the removed AMENT log source behind."""
        source = '''        string prefixPath = GetRos2ForUnityPath();
        string prefixSource = "asset root";
        string streamingAssetsPrefixPath = Path.Combine(Application.streamingAssetsPath, ros2ForUnityAssetFolderName);
        string pluginPrefixPath = GetPluginPath();
        if (Directory.Exists(Path.Combine(streamingAssetsPrefixPath, "share")))
        {
            prefixPath = streamingAssetsPrefixPath;
            prefixSource = "StreamingAssets";
        }
        else if (Directory.Exists(Path.Combine(pluginPrefixPath, "share")))
        {
            prefixPath = pluginPrefixPath;
            prefixSource = "plugin directory";
        }
        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");
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

        patched = self.builder.patch_standalone_environment_isolation(source)

        self.assertNotIn("prefixSource", patched)
        self.assertIn('SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);', patched)


__all__ = ["RuntimePatchOverlayTests"]
