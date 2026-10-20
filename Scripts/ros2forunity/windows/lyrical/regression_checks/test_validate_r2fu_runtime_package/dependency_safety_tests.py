from __future__ import annotations

from .foundation import *


class RuntimeDependencySafetyTests:
    """Exercise distro descriptions, dependency closure, and Zenoh safety"""

    def test_ros2cs_metadata_descriptions_accept_shared_ros2cs_release_label(self) -> None:
        """A shared ros2cs release label is not a cross-distro runtime declaration."""
        with tempfile.TemporaryDirectory() as temp:
            runtime_root = Path(temp) / "Runtime" / "Ros2ForUnity"
            plugin_root = runtime_root / "Plugins" / "Windows" / "x86_64"
            metadata_files = (
                runtime_root / "metadata_ros2cs.xml",
                runtime_root / "Plugins" / "metadata_ros2cs.xml",
                plugin_root / "metadata_ros2cs.xml",
            )
            for path in metadata_files:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    "<ros2cs><ros2>lyrical</ros2><version><desc>v0.6.0-jazzy-preview</desc></version></ros2cs>",
                    encoding="utf-8",
                )

            self.validator.RUNTIME_ROOT = runtime_root
            self.validator.PLUGIN_ROOT = plugin_root
            results = []

            self.validator.check_ros2cs_metadata_descriptions(results)

        self.assertTrue(all(result.ok for result in results))

    def test_ros2cs_metadata_descriptions_reject_cross_distro_field(self) -> None:
        """Lyrical metadata validation rejects a genuinely cross-distro field."""
        with tempfile.TemporaryDirectory() as temp:
            runtime_root = Path(temp) / "Runtime" / "Ros2ForUnity"
            plugin_root = runtime_root / "Plugins" / "Windows" / "x86_64"
            metadata_files = (
                runtime_root / "metadata_ros2cs.xml",
                runtime_root / "Plugins" / "metadata_ros2cs.xml",
                plugin_root / "metadata_ros2cs.xml",
            )
            for path in metadata_files:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    "<ros2cs><ros2>jazzy</ros2><version><desc>v0.6.0-jazzy-preview</desc></version></ros2cs>",
                    encoding="utf-8",
                )

            self.validator.RUNTIME_ROOT = runtime_root
            self.validator.PLUGIN_ROOT = plugin_root
            results = []

            self.validator.check_ros2cs_metadata_descriptions(results)

        failed = [result.name for result in results if not result.ok]
        self.assertEqual(3, len([name for name in failed if "declares lyrical ros2cs distro" in name]))

    def test_public_docs_must_explain_facade_independent_runtime(self) -> None:
        """Runtime docs must make the no-facade-dependency package role explicit."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "b" * 64

            readme.write_text(
                "runtime.lyrical.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one dev.unity2foxglove.ros2forunity.runtime.* package\n"
                f"{artifact_sha}\n"
                "WSL2 NAT diagnostic-only Windows Defender Firewall\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            results = []

            self.validator.check_public_docs(results)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["README documents runtime package has no facade dependency"].ok)

    def test_generator_alignment_reports_missing_generator_as_failed_check(self) -> None:
        """Missing generator source should produce a structured failed result."""
        with tempfile.TemporaryDirectory() as temp:
            self.validator.ROOT = Path(temp)
            results = []

            self.validator.check_generator_alignment(results)

        self.assertFalse(results[0].ok)
        self.assertIn("generator script readable", results[0].name)

    def test_runtime_files_fail_when_fastdds_import_dependency_is_missing(self) -> None:
        """Runtime validation rejects an RMW DLL whose transitive imports are absent."""
        source = (
            self.validator.PLUGIN_ROOT
            / "rmw_fastrtps_cpp.dll"
        )
        with tempfile.TemporaryDirectory() as temp:
            plugin_root = Path(temp)
            shutil.copyfile(source, plugin_root / source.name)
            self.validator.PLUGIN_ROOT = plugin_root
            self.validator.RUNTIME_ROOT = plugin_root.parent
            self.validator.CRITICAL_PLUGIN_DLLS = ()
            self.validator.ZENOH_CONFIG_FILES = ()
            results = []

            self.validator.check_runtime_files(results)

        closure = [
            result for result in results
            if result.name == "native DLL dependency closure: rmw_fastrtps_cpp.dll"
        ]
        self.assertEqual(1, len(closure))
        self.assertFalse(closure[0].ok)
        self.assertIn("rosidl_dynamic_typesupport_fastrtps.dll", closure[0].detail)

    def test_dependency_closure_uses_windows_case_insensitive_dll_names(self) -> None:
        """Cross-platform validation must resolve packaged PE imports as Windows does."""
        with tempfile.TemporaryDirectory() as temp:
            plugin_root = Path(temp)
            seed = plugin_root / "seed.dll"
            dependency = plugin_root / "vcruntime140.dll"
            seed.write_bytes(b"seed")
            dependency.write_bytes(b"dependency")
            actual_names = {path.name for path in plugin_root.iterdir()}
            original_exists = Path.exists

            def exact_case_exists(path: Path) -> bool:
                """Emulate a case-sensitive host while keeping the test portable."""
                if path.parent == plugin_root:
                    return path.name in actual_names
                return original_exists(path)

            self.validator.PLUGIN_ROOT = plugin_root
            with (
                mock.patch.object(Path, "exists", autospec=True, side_effect=exact_case_exists),
                mock.patch.object(
                    self.validator,
                    "read_pe_imports",
                    side_effect=lambda path: ["VCRUNTIME140.dll"] if path == seed else [],
                ),
            ):
                missing = self.validator.missing_package_dll_imports(seed)

        self.assertEqual({}, missing)

    def test_runtime_files_reject_unsafe_zenoh_session_defaults(self) -> None:
        """Unity-facing Zenoh session configs must not enable hard exit, 1GiB buffers, or adminspace."""
        unsafe_config = (
            "listen: { exit_on_failure: true }\n"
            "rx: { max_message_size: 1073741824 }\n"
            "adminspace: { enabled: true, permissions: { read: true, write: false } }\n"
        )
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            plugin_config = (
                root
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
            streaming_config = (
                root
                / "Runtime"
                / "Ros2ForUnity"
                / "StreamingAssets"
                / "Ros2ForUnity"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"
            )
            plugin_config.parent.mkdir(parents=True)
            streaming_config.parent.mkdir(parents=True)
            plugin_config.write_text(unsafe_config, encoding="utf-8")
            streaming_config.write_text(unsafe_config, encoding="utf-8")

            self.validator.RUNTIME_ROOT = root / "Runtime" / "Ros2ForUnity"
            self.validator.PLUGIN_ROOT = root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
            self.validator.CRITICAL_PLUGIN_DLLS = ()
            self.validator.RMW_DEPENDENCY_CLOSURE_SEEDS = ()
            self.validator.LEAKY_UPSTREAM_EXAMPLES = ()
            self.validator.ZENOH_CONFIG_FILES = (plugin_config, streaming_config)
            self.validator.ZENOH_CONFIG_MIRRORS = ((plugin_config, streaming_config),)
            results = []

            self.validator.check_runtime_files(results)

        failed_names = {result.name for result in results if not result.ok}
        self.assertTrue(any("listen failure is non-fatal" in name for name in failed_names))
        self.assertTrue(any("defragmentation buffer is bounded" in name for name in failed_names))
        self.assertTrue(any("adminspace disabled" in name for name in failed_names))

    def test_runtime_files_require_zenoh_router_security_notes(self) -> None:
        """Open Zenoh router configs must document the trusted-lab boundary."""
        unsafe_router_config = (
            "listen: { endpoints: [\"tcp/[::]:7447\"] }\n"
            "transport: { unicast: { accept_pending: 10000, max_sessions: 10000 } }\n"
        )
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            plugin_config = (
                root
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
            streaming_config = (
                root
                / "Runtime"
                / "Ros2ForUnity"
                / "StreamingAssets"
                / "Ros2ForUnity"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            plugin_config.parent.mkdir(parents=True)
            streaming_config.parent.mkdir(parents=True)
            plugin_config.write_text(unsafe_router_config, encoding="utf-8")
            streaming_config.write_text(unsafe_router_config, encoding="utf-8")

            self.validator.RUNTIME_ROOT = root / "Runtime" / "Ros2ForUnity"
            self.validator.PLUGIN_ROOT = root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
            self.validator.CRITICAL_PLUGIN_DLLS = ()
            self.validator.RMW_DEPENDENCY_CLOSURE_SEEDS = ()
            self.validator.LEAKY_UPSTREAM_EXAMPLES = ()
            self.validator.ZENOH_CONFIG_FILES = (plugin_config, streaming_config)
            self.validator.ZENOH_CONFIG_MIRRORS = ((plugin_config, streaming_config),)
            results = []

            self.validator.check_runtime_files(results)

        failed_names = {result.name for result in results if not result.ok}
        self.assertTrue(any("open-listen profile is documented" in name for name in failed_names))
        self.assertTrue(any("high connection limits are documented" in name for name in failed_names))

    def test_inventory_rejects_stale_zenoh_config_hash(self) -> None:
        """Runtime inventory should hash-check patched Zenoh configs even in fast mode."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            runtime_root = package / "Runtime" / "Ros2ForUnity"
            config = (
                runtime_root
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
            config.write_text("router config\n", encoding="utf-8")
            inventory.write_text(
                json.dumps(
                    {
                        "runtimeId": "r2fu-lyrical-win64",
                        "artifactName": "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip",
                        "rosDistro": "lyrical",
                        "defaultRmwImplementation": "rmw_fastrtps_cpp",
                        "platform": "win64",
                        "buildType": "standalone",
                        "supportedRmwImplementations": ["rmw_fastrtps_cpp", "rmw_zenoh_cpp"],
                        "sha256": self.validator.EXPECTED_ARTIFACT_SHA256,
                        "artifactSize": 1,
                        "fileCount": 1,
                        "redistributionStatus": "candidate_not_published",
                        "categoryCounts": {"native_libraries": 700},
                        "knownCriticalFiles": [],
                        "files": [
                            {
                                "path": "Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                                "sha256": "0" * 64,
                                "size": config.stat().st_size,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            manifest = {"artifactSha256": self.validator.EXPECTED_ARTIFACT_SHA256, "artifactSize": 1, "inventoryFileCount": 1}

            self.validator.PACKAGE = package
            self.validator.RUNTIME_ROOT = runtime_root
            self.validator.INVENTORY = inventory
            self.validator.MANIFEST = package / "manifest.json"
            self.validator.MANIFEST.write_text(json.dumps(manifest), encoding="utf-8")
            actual_hash = hashlib.sha256(config.read_bytes()).hexdigest()
            results = []

            self.validator.check_inventory(results, release_gate=False, skip_dll_hash=True)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["runtime inventory Zenoh config hashes match disk"].ok)
        self.assertNotEqual("0" * 64, actual_hash)


__all__ = ["RuntimeDependencySafetyTests"]
