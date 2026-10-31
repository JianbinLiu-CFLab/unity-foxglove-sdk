from __future__ import annotations

import os
import shutil

from .foundation import *


class RuntimeLayoutMetadataTests:
    """Exercise plugin layout, docs, and package metadata contracts"""

    def test_generator_alignment_rejects_invalid_section_manifests(self) -> None:
        """Generator alignment requires an exact, readable Python section manifest."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            generator = root / "Scripts/ros2forunity/windows/lyrical/build_r2fu_runtime_package.py"
            section_dir = generator.with_name("Decomposed") / generator.stem
            section_dir.mkdir(parents=True)
            generator.write_text(
                "\n".join(
                    (
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
                ),
                encoding="utf-8",
            )
            for manifest, files in (
                (None, ("01_types.py",)),
                ("01_types.py\n01_types.py\n", ("01_types.py",)),
                ("01_types.py\n", ("01_types.py", "02_extra.py")),
                ("02_missing.py\n", ("01_types.py",)),
            ):
                with self.subTest(manifest=manifest, files=files):
                    for path in section_dir.glob("*.py"):
                        path.unlink()
                    for name in files:
                        (section_dir / name).write_text("VALUE = 1\n", encoding="utf-8")
                    manifest_path = section_dir / "sections.manifest"
                    if manifest is None:
                        manifest_path.unlink(missing_ok=True)
                    else:
                        manifest_path.write_text(manifest, encoding="utf-8")
                    self.validator.ROOT = root
                    results = []
                    self.validator.check_generator_alignment(results)
                    manifest_result = next(
                        result
                        for result in results
                        if result.name == "runtime package generator section manifest valid"
                    )
                    self.assertFalse(manifest_result.ok)

    def test_phase146b_loader_rejects_empty_or_comment_only_manifest(self) -> None:
        """The Phase146B loader fails closed before executing an empty package."""
        implementation_path = (
            ROOT
            / "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build/implementation.py"
        )
        spec = importlib.util.spec_from_file_location(
            "phase146b_r2fu_lyrical_windows_build.implementation_test",
            implementation_path,
        )
        module = importlib.util.module_from_spec(spec)
        self.assertIsNotNone(spec.loader)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as temp:
            section_dir = Path(temp)
            for manifest in ("", "# generated sections\n"):
                with self.subTest(manifest=manifest):
                    (section_dir / "sections.manifest").write_text(manifest, encoding="utf-8")
                    with self.assertRaises((FileNotFoundError, ValueError)):
                        module._load_source_sections(section_dir, {})

    def test_phase146b_implementation_loads_by_file_path_outside_repository_cwd(self) -> None:
        """The implementation resolves the shared loader from its own source path."""
        implementation_path = (
            ROOT
            / "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build/implementation.py"
        )
        code = (
            "import importlib.util, sys\n"
            f"path = {str(implementation_path)!r}\n"
            "spec = importlib.util.spec_from_file_location('phase146b_external_load', path)\n"
            "module = importlib.util.module_from_spec(spec)\n"
            "sys.modules[spec.name] = module\n"
            "spec.loader.exec_module(module)\n"
            "print('PHASE146B_EXTERNAL_FILE_LOAD_OK')\n"
        )
        environment = dict(os.environ)
        environment.pop("PYTHONPATH", None)
        with tempfile.TemporaryDirectory() as temp:
            result = subprocess.run(
                [sys.executable, "-B", "-c", code],
                cwd=temp,
                env=environment,
                capture_output=True,
                text=True,
            )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("PHASE146B_EXTERNAL_FILE_LOAD_OK", result.stdout)

    def test_phase146b_loader_isolated_across_file_path_checkouts(self) -> None:
        """Each file-path checkout must load its own section manifest module."""
        source_root = ROOT / "Scripts/ros2forunity/windows/lyrical"
        implementation_source = source_root / "phase146b_r2fu_lyrical_windows_build/implementation.py"
        section_manifest_source = source_root / "section_manifest.py"
        original_path = list(sys.path)
        original_bare_manifest = sys.modules.get("section_manifest")
        with tempfile.TemporaryDirectory() as temp:
            modules = []
            expected_paths = []
            for index in (1, 2):
                checkout = Path(temp) / f"checkout-{index}"
                checkout.mkdir(parents=True)
                manifest_path = checkout / "section_manifest.py"
                shutil.copy2(section_manifest_source, manifest_path)
                spec = importlib.util.spec_from_file_location(
                    f"phase146b_isolation_{index}",
                    manifest_path,
                )
                module = importlib.util.module_from_spec(spec)
                self.assertIsNotNone(spec.loader)
                spec.loader.exec_module(module)
                modules.append(module)
                expected_paths.append(manifest_path)

            self.assertEqual(
                [path.resolve() for path in expected_paths],
                [Path(module.__file__).resolve() for module in modules],
            )
            self.assertIsNot(modules[0], modules[1])
            implementation_source_text = implementation_source.read_text(encoding="utf-8")
            self.assertNotIn("sys.path.insert", implementation_source_text)
            self.assertIn("spec_from_file_location", implementation_source_text)
        self.assertEqual(original_path, sys.path)
        self.assertIs(original_bare_manifest, sys.modules.get("section_manifest"))

    def test_runtime_dll_metas_use_plugin_importer_in_both_plugin_roots(self) -> None:
        """Managed and native DLL metadata must be Unity plugin metadata."""
        metas = list((self.validator.RUNTIME_ROOT / "Plugins").glob("*.dll.meta"))
        metas.extend(self.validator.PLUGIN_ROOT.glob("*.dll.meta"))
        self.assertGreater(len(metas), 0)
        failures = [path for path in metas if "PluginImporter:" not in path.read_text(encoding="utf-8")]
        self.assertEqual([], failures)

    def test_player_runtime_paths_use_streaming_assets_and_plugin_root(self) -> None:
        """Player metadata and native plugins must resolve from their build outputs."""
        source = (self.validator.RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs").read_text(encoding="utf-8")
        self.assertIn("Application.streamingAssetsPath", source)
        self.assertIn("Application.dataPath", source)
        self.assertIn("StreamingAssets", source)

    def test_all_lyrical_dll_metas_use_plugin_importer(self) -> None:
        """Native DLL metadata must be imported as Windows plugins, not text files."""
        metas = list(self.validator.PLUGIN_ROOT.glob("*.dll.meta"))
        self.assertGreater(len(metas), 0)
        self.assertTrue(all("PluginImporter:" in path.read_text(encoding="utf-8") for path in metas))

    def test_release_gate_rejects_prototype_distribution(self) -> None:
        """Release gate rejects Prototype and records missing-field behavior."""
        self.assertFalse(self.validator.published_runtime_is_not_prototype({"distributionLevel": "Prototype"}))
        self.assertTrue(self.validator.published_runtime_is_not_prototype({"distributionLevel": "Release"}))
        self.assertTrue(self.validator.published_runtime_is_not_prototype({}))

    def test_public_docs_must_include_artifact_hash(self) -> None:
        """Public docs validation rejects README/notices that omit the artifact hash."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "a" * 64

            readme.write_text(
                "runtime.lyrical.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one dev.unity2foxglove.ros2forunity.runtime.* package\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.MANIFEST = manifest
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            results = []

            self.validator.check_public_docs(results)

        failed = [result.name for result in results if not result.ok]
        self.assertIn("README documents artifact SHA-256", failed)

    def test_managed_deps_reject_spurious_service_msgs_for_visualization_packets(self) -> None:
        """Known no-service message packages should not carry service_msgs dependencies."""
        with tempfile.TemporaryDirectory() as temp:
            runtime_root = Path(temp) / "Runtime" / "Ros2ForUnity"
            plugin_root = runtime_root / "Plugins"
            plugin_root.mkdir(parents=True)
            for name in ("stereo_msgs_assembly", "visualization_msgs_assembly"):
                (plugin_root / f"{name}.deps.json").write_text(
                    json.dumps(
                        {
                            "targets": {
                                ".NETStandard,Version=v2.0/": {
                                    f"{name}/1.0.0": {
                                        "dependencies": {
                                            "service_msgs_assembly": "0.0.0.0",
                                        },
                                    },
                                    "service_msgs_assembly/0.0.0.0": {},
                                },
                            },
                            "libraries": {
                                "service_msgs_assembly/0.0.0.0": {},
                            },
                        }
                    ),
                    encoding="utf-8",
                )

            self.validator.RUNTIME_ROOT = runtime_root
            results = []

            self.validator.check_managed_deps_consistency(results)

        failed = [result.name for result in results if not result.ok]
        self.assertEqual(2, len([name for name in failed if "spurious service_msgs" in name]))

    def test_runtime_source_declares_rmw_guard(self) -> None:
        """ROS2ForUnity startup path declares and enforces supported RMWs."""
        source = (
            self.validator.RUNTIME_ROOT
            / "Scripts"
            / "ROS2ForUnity.cs"
        ).read_text(encoding="utf-8", errors="replace")

        self.assertIn("defaultRmwImplementation", source)
        self.assertIn("zenohRmwImplementation", source)
        self.assertIn("IsSupportedRmwImplementation", source)
        self.assertIn("ValidateRmwImplementation", source)
        self.assertIn("rmw_fastrtps_cpp", source)
        self.assertIn("rmw_zenoh_cpp", source)

    def test_runtime_asmdef_is_windows_constrained(self) -> None:
        """The packaged runtime assembly must not compile in unsupported editors."""
        data = self.validator.load_json(
            self.validator.RUNTIME_ROOT
            / "Scripts"
            / "Unity2Foxglove.Ros2ForUnity.Runtime.LyricalWin64.asmdef",
            [],
            "runtime asmdef",
        )
        self.assertEqual(
            ["UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN"],
            data.get("defineConstraints"),
        )

    def test_package_metadata_requires_explicit_empty_dependencies(self) -> None:
        """Runtime package metadata should declare that it has no external package dependencies."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "package.json").write_text(
                '{"name":"dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",'
                '"version":"0.1.0-preview.1",'
                '"displayName":"Unity2Foxglove ROS2 For Unity Runtime - Lyrical Win64",'
                '"license":"Apache-2.0",'
                '"unity":"6000.0",'
                '"description":"Optional Lyrical Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",'
                '"keywords":["ros2","ros2-for-unity","lyrical","win64"]}',
                encoding="utf-8",
            )
            self.validator.PACKAGE = package
            results = []

            self.validator.check_package_metadata(results)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["package declares no external dependencies"].ok)


__all__ = ["RuntimeLayoutMetadataTests"]
