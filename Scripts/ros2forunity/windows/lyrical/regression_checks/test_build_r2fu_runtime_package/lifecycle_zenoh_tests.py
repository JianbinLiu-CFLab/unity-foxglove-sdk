from __future__ import annotations

from .foundation import *


class LyricalLifecycleZenohTests:
    """Exercise lifecycle, standalone isolation, and Zenoh configuration patches"""

    def test_standalone_isolation_keeps_windows_env_setup_single_pass(self) -> None:
        """Standalone env ownership should not be repeated in the Windows PATH block."""
        source = (
            ROOT
            / "Packages"
            / self.builder.PACKAGE_NAME
            / "Runtime"
            / "Ros2ForUnity"
            / "Scripts"
            / "ROS2ForUnity.cs"
        ).read_text(encoding="utf-8")

        patched = self.builder.patch_standalone_environment_isolation(source)
        constructor = patched[patched.find("internal ROS2ForUnity()") :]
        windows_start = constructor.find("if (GetOS() == Platform.Windows)")
        windows_end = constructor.find("} else {", windows_start)
        windows_block = constructor[windows_start:windows_end]

        crt_guard_count = (
            patched.count("#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN")
            + patched.count("#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN")
        )
        legacy_crt_guard = (
            crt_guard_count >= 2
            and "[DllImport(\"ucrtbase.dll\"" in patched
            and "_wputenv_s" in patched
        )
        lease_crt_guard = (
            "Ros2ForUnityProcessEnvironmentLease.Set(" in patched
            and "Ros2ForUnityProcessEnvironmentLease.Restore(" in patched
            and legacy_crt_guard
        )
        self.assertTrue(legacy_crt_guard or lease_crt_guard)
        self.assertIn("PrewarmUnityPaths", patched)
        self.assertNotIn("SetStandaloneRosDistro(currentRos2Version)", windows_block)
        self.assertNotIn("SetStandalonePrefixPath();", windows_block)
        self.assertNotIn("SetStandaloneRmwImplementation();", windows_block)
        self.assertNotIn("SetStandaloneRcutilsConsoleMode();", windows_block)

    def test_component_patch_prewarms_paths_and_removes_dead_shutdown_reset(self) -> None:
        """ROS2UnityComponent patch should prewarm Unity API paths and remove the dead reset."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            component = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2UnityComponent.cs"
            component.parent.mkdir(parents=True)
            component.write_text(
                "class ROS2UnityComponent\n"
                "{\n"
                "    private readonly object mutex = new object();\n"
                "    private double spinTimeout = 0.0001;\n\n"
                "    void LazyConstruct()\n"
                "    {\n"
                "            runtimeShutdownRequested = false;\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )

            self.builder.patch_component_main_thread_prewarm(package)

            patched = component.read_text(encoding="utf-8")
            self.assertIn("private void Awake()", patched)
            self.assertIn("ROS2ForUnity.PrewarmUnityPaths();", patched)
            self.assertNotIn("runtimeShutdownRequested = false;", patched)

    def test_zenoh_router_patch_documents_development_profile(self) -> None:
        """Generated router configs should retain trusted-lab security notes."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            config = (
                package
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            mirror = (
                package
                / "Runtime"
                / "Ros2ForUnity"
                / "StreamingAssets"
                / "Ros2ForUnity"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            config.parent.mkdir(parents=True)
            mirror.parent.mkdir(parents=True)
            text = (
                "/// This file attempts to list and document available configuration elements.\n"
                "/// For a more complete view of the configuration's structure, check out `zenoh/src/config.rs`'s `Config` structure.\n"
                "/// Note that the values here are correctly typed, but may not be sensible, so copying this file to change only the parts that matter to you is not good practice.\n"
                "{\n"
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      accept_pending: 10000,\n"
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      max_sessions: 10000,\n"
                "}\n"
            )
            config.write_text(text, encoding="utf-8")
            mirror.write_text(text, encoding="utf-8")

            self.builder.patch_zenoh_router_config_notes(package)

            patched = config.read_text(encoding="utf-8")
            self.assertIn("without authentication or ACLs", patched)
            self.assertIn("localhost-only or ACL-protected deployment profile", patched)
            self.assertIn("high development default is unsuitable", patched)
            self.assertEqual(patched, mirror.read_text(encoding="utf-8"))

    def test_zenoh_session_patch_enforces_memory_and_adminspace_safety(self) -> None:
        """The packaged Zenoh session defaults must retain bounded, local-safe behavior."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            config = (
                package
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"
            )
            mirror = (
                package
                / "Runtime"
                / "Ros2ForUnity"
                / "StreamingAssets"
                / "Ros2ForUnity"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"
            )
            config.parent.mkdir(parents=True)
            mirror.parent.mkdir(parents=True)
            text = (
                "{\n"
                "  listen: {\n"
                "    exit_on_failure: true,\n"
                "  },\n"
                "  transport: { link: { rx: {\n"
                "        /// Maximum size of the defragmentation buffer at receiver end.\n"
                "        /// Fragmented messages that are larger than the configured size will be dropped.\n"
                "        /// The default value is 1GiB. This would work in most scenarios.\n"
                "        /// NOTE: reduce the value if you are operating on a memory constrained device.\n"
                "        max_message_size: 1073741824,\n"
                "  } } },\n"
                "  adminspace: {\n"
                "    /// Enables the admin space\n"
                "    enabled: true,\n"
                "    /// read and/or write permissions on the admin space\n"
                "    permissions: {\n"
                "      read: true,\n"
                "      write: false,\n"
                "    },\n"
                "  },\n"
                "}\n"
            )
            config.write_text(text, encoding="utf-8")
            mirror.write_text(text, encoding="utf-8")

            self.builder.patch_zenoh_session_config_safety(package)

            patched = config.read_text(encoding="utf-8")
            self.assertIn("exit_on_failure: false", patched)
            self.assertIn("max_message_size: 134217728", patched)
            self.assertNotIn("max_message_size: 1073741824", patched)
            self.assertIn("enabled: false", patched)
            self.assertIn("read: false", patched)
            self.assertEqual(patched, mirror.read_text(encoding="utf-8"))

    def test_zenoh_config_inventory_hashes_are_refreshed_after_patches(self) -> None:
        """Generated inventory hashes should describe the package-patched config files."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            config = (
                package
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            inventory = package / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json"
            config.parent.mkdir(parents=True)
            inventory.parent.mkdir(parents=True)
            config.write_text("patched router config\n", encoding="utf-8")
            inventory.write_text(
                json.dumps(
                    {
                        "files": [
                            {
                                "path": "Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                                "sha256": "0" * 64,
                                "size": 1,
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )

            self.builder.update_zenoh_config_inventory_hashes(package)

            data = json.loads(inventory.read_text(encoding="utf-8"))
            entry = data["files"][0]
            self.assertEqual(hashlib.sha256(config.read_bytes()).hexdigest(), entry["sha256"])
            self.assertEqual(config.stat().st_size, entry["size"])

    def test_standalone_isolation_rejects_partial_startup_patch(self) -> None:
        """Do not accept a source that declares metadata without standalone setup calls."""
        source = (
            "    public void CheckIntegrity()\n"
            "    {\n"
            "        string ros2SourcedCodename = GetROSVersionSourced();\n"
            "    }\n"
            "    private void Start()\n"
            "    {\n"
            "            // Load metadata\n"
            "            LoadMetadata();\n"
            "            string packagedRos2Version = GetMetadataValue(ros2csMetadata, \"/ros2cs/ros2\");\n"
            "            string standalone = IsStandalone() ? \"standalone\" : \"non-standalone\";\n"
            "            CheckIntegrity();\n"
            "                if (IsStandalone())\n"
            "    }\n"
        )

        with self.assertRaisesRegex(ValueError, "missing required setup calls"):
            self.builder.patch_standalone_environment_isolation(source)

    def test_lifecycle_patches_fail_closed_when_upstream_anchors_drift(self) -> None:
        """Lifecycle safety markers cannot be silently skipped after upstream reformatting."""
        runtime = "        EditorApplication.quitting /* drift */ += ShutdownShared;\n"
        unity_time = (
            "    mainThreadId = Thread.CurrentThread.ManagedThreadId;\n"
            "    // upstream inserted a statement\n"
            "    lastReadingSecs = Time.timeAsDouble;\n"
        )

        with self.assertRaisesRegex(ValueError, "AssemblyReloadEvents registration"):
            self.builder.patch_runtime_lifecycle_safety(runtime)
        with self.assertRaisesRegex(ValueError, "UnityTimeSource main-thread guard"):
            self.builder.patch_unity_time_source_main_thread_guard(unity_time)

    def test_standalone_isolation_fails_closed_when_method_bodies_drift(self) -> None:
        """Already-patched method bodies cannot lose their safety marker silently."""
        source_path = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64"
            / "Runtime"
            / "Ros2ForUnity"
            / "Scripts"
            / "ROS2ForUnity.cs"
        )
        source = source_path.read_text(encoding="utf-8")
        cases = (
            ("standalone runtime must not inherit a sourced ROS2 workspace", "AMENT_PREFIX_PATH"),
            ("standalone runtime owns its RMW selection while allowing Lyrical Zenoh", "RMW_IMPLEMENTATION"),
            ("standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell", "ROS_DISTRO"),
        )

        for marker, label in cases:
            with self.subTest(marker=marker):
                drifted = source.replace(marker, "upstream body drift", 1)
                with self.assertRaisesRegex(ValueError, label):
                    self.builder.patch_standalone_environment_isolation(drifted)

    def test_patch_ros_time_source_contract_updates_dotnet_copyright(self) -> None:
        """Patch DotnetTimeSource copyright alongside bool-returning time contracts."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            time_dir = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Time"
            time_dir.mkdir(parents=True)
            (time_dir / "DotnetTimeSource.cs").write_text(
                "// Modifications Copyright (c) 2026 Jianbin Liu.\n",
                encoding="utf-8",
            )
            for name in ("ROS2TimeSource.cs", "ROS2ScalableTimeSource.cs"):
                (time_dir / name).write_text(
                    "  public bool GetTime(out int seconds, out uint nanoseconds)\n"
                    "  {\n"
                    "    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.\n"
                    "    seconds = 0;\n"
                    "    nanoseconds = 0;\n"
                    "    if (seconds == 0) return false;\n"
                    "    return true;\n"
                    "  }\n",
                    encoding="utf-8",
                )

            self.builder.patch_ros_time_source_contract(package)

            self.assertIn(
                "Unity2Foxglove contributors",
                (time_dir / "DotnetTimeSource.cs").read_text(encoding="utf-8"),
            )


__all__ = ["LyricalLifecycleZenohTests"]
