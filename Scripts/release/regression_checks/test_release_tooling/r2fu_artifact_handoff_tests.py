from .foundation import *

class R2fuArtifactHandoffTests(unittest.TestCase):
    """Keep source gates aligned with the current verified R2FU artifact handoff."""

    def test_v090_runtime_artifact_pins_are_consistent(self) -> None:
        """Every source gate must name the three verified v0.9.0 artifact digests."""
        expected_by_path = {
            "Scripts/ros2forunity/windows/humble/sync_r2fu_artifact_to_unity2foxglove.py": "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0",
            "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py": "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0",
            "Scripts/ros2forunity/windows/jazzy/sync_r2fu_artifact_to_unity2foxglove.py": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Scripts/ros2forunity/windows/lyrical/lyrical_artifact_config.py": "b31f12cccd2c702ec18c5f5ededce9239d8a2bbe244d54b5526606a96a3a5b71",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuHumbleRuntimePackageValidation.cs": "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuJazzyRuntimeRefreshValidation.cs": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuLyricalRuntimePackageValidation.cs": "b31f12cccd2c702ec18c5f5ededce9239d8a2bbe244d54b5526606a96a3a5b71",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase128Validation.cs": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
        }

        for relative_path, expected_sha256 in expected_by_path.items():
            with self.subTest(path=relative_path):
                source = read_python_validation_source(ROOT / relative_path)
                self.assertIn(expected_sha256, source)

    def test_runtime_adoption_sync_updates_only_the_selected_distro(self) -> None:
        """Serial runtime refreshes must preserve every other distro's verified metadata."""
        from Scripts.ros2forunity.windows.runtime_adoption_manifest import (
            sync_runtime_adoption_manifest,
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_name = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
            compliance = root / "Packages/dev.unity2foxglove.ros2forunity/Compliance"
            package = root / "Packages" / package_name
            (package / "RuntimeSupport").mkdir(parents=True)
            compliance.mkdir(parents=True)
            adoption = {
                "currentRecommendedRuntime": {
                    "packageName": package_name,
                    "artifactSha256": "old-jazzy",
                    "artifactSize": 1,
                    "inventoryFileCount": 2,
                    "criticalRuntimeFiles": ["keep-current.dll"],
                },
                "supportedRuntimePackages": [
                    {
                        "packageName": "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                        "artifactSha256": "keep-humble",
                        "artifactSize": 10,
                    },
                    {
                        "packageName": package_name,
                        "artifactSha256": "old-jazzy",
                        "artifactSize": 1,
                        "inventoryFileCount": 2,
                        "criticalRuntimeFiles": ["keep-supported.dll"],
                    },
                ],
            }
            (compliance / "ros2-for-unity-adoption-manifest.json").write_text(
                json.dumps(adoption), encoding="utf-8"
            )
            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                json.dumps(
                    {
                        "artifactSha256": "new-jazzy",
                        "artifactSize": 123,
                        "inventoryFileCount": 456,
                        "criticalRuntimeFiles": ["runtime-only.dll"],
                    }
                ),
                encoding="utf-8",
            )

            sync_runtime_adoption_manifest(
                root,
                package,
                package_name,
                update_current_recommended=True,
            )

            updated = json.loads(
                (compliance / "ros2-for-unity-adoption-manifest.json").read_text(encoding="utf-8")
            )
            humble, jazzy = updated["supportedRuntimePackages"]
            self.assertEqual("keep-humble", humble["artifactSha256"])
            self.assertEqual("new-jazzy", jazzy["artifactSha256"])
            self.assertEqual(123, jazzy["artifactSize"])
            self.assertEqual(456, jazzy["inventoryFileCount"])
            self.assertEqual(["keep-supported.dll"], jazzy["criticalRuntimeFiles"])
            self.assertEqual("new-jazzy", updated["currentRecommendedRuntime"]["artifactSha256"])
            self.assertEqual(["keep-current.dll"], updated["currentRecommendedRuntime"]["criticalRuntimeFiles"])

    def test_runtime_adoption_sync_rejects_missing_core_artifact_metadata_before_write(self) -> None:
        """A partial runtime manifest must not leave stale adoption fields behind."""
        from Scripts.ros2forunity.windows.runtime_adoption_manifest import (
            sync_runtime_adoption_manifest,
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_name = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
            compliance = root / "Packages/dev.unity2foxglove.ros2forunity/Compliance"
            package = root / "Packages" / package_name
            (package / "RuntimeSupport").mkdir(parents=True)
            compliance.mkdir(parents=True)
            adoption_path = compliance / "ros2-for-unity-adoption-manifest.json"
            original = {
                "currentRecommendedRuntime": {
                    "packageName": package_name,
                    "artifactSha256": "verified-old",
                    "artifactSize": 123,
                    "inventoryFileCount": 456,
                },
                "supportedRuntimePackages": [
                    {
                        "packageName": package_name,
                        "artifactSha256": "verified-old",
                        "artifactSize": 123,
                        "inventoryFileCount": 456,
                    }
                ],
            }
            adoption_path.write_text(json.dumps(original), encoding="utf-8")
            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                json.dumps(
                    {
                        "artifactSize": 999,
                        "inventoryFileCount": 1000,
                    }
                ),
                encoding="utf-8",
            )

            with self.assertRaisesRegex(RuntimeError, "artifactSha256"):
                sync_runtime_adoption_manifest(
                    root,
                    package,
                    package_name,
                    update_current_recommended=True,
                )

            self.assertEqual(
                original,
                json.loads(adoption_path.read_text(encoding="utf-8")),
            )

    def test_runtime_adoption_json_replace_failure_preserves_previous_file(self) -> None:
        """A failed final replace must not truncate verified adoption evidence."""
        from Scripts.ros2forunity.windows import runtime_adoption_manifest

        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "adoption.json"
            path.write_text('{"verified":true}\n', encoding="utf-8")
            with mock.patch.object(
                runtime_adoption_manifest.os,
                "replace",
                side_effect=OSError("replace failed"),
            ):
                with self.assertRaisesRegex(OSError, "replace failed"):
                    runtime_adoption_manifest.write_json(
                        path,
                        {"verified": False},
                    )

            self.assertEqual(
                '{"verified":true}\n',
                path.read_text(encoding="utf-8"),
            )

    def test_inactive_runtime_syncs_can_preserve_the_selected_runtime(self) -> None:
        """Refreshing a non-selected payload must not rewrite the Unity runtime selection."""
        scripts = {
            "humble": ROOT / "Scripts/ros2forunity/windows/humble/sync_r2fu_artifact_to_unity2foxglove.py",
            "jazzy": ROOT / "Scripts/ros2forunity/windows/jazzy/sync_r2fu_artifact_to_unity2foxglove.py",
        }

        for distro, path in scripts.items():
            with self.subTest(distro=distro), tempfile.TemporaryDirectory() as temp:
                project_root = Path(temp)
                manifest_path = project_root / "Unity2Foxglove/Packages/manifest.json"
                manifest_path.parent.mkdir(parents=True)
                manifest = {
                    "dependencies": {
                        "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64": (
                            "file:../../Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64"
                        )
                    }
                }
                manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                sync = load_module(f"{distro}_r2fu_sync_under_test", path)

                result = sync.ensure_project_uses_runtime_package(
                    project_root,
                    update=False,
                    require_runtime_dependency=False,
                )

                self.assertFalse(result["manifestUpdated"])
                self.assertFalse(result["runtimeDependencyRequired"])
                self.assertEqual(json.loads(manifest_path.read_text(encoding="utf-8")), manifest)
                source = path.read_text(encoding="utf-8")
                self.assertIn("--skip-project-manifest-check", source)
                self.assertIn("require_runtime_dependency=not args.skip_project_manifest_check", source)

    def test_jazzy_v083_excludes_only_test_typesupport_payload(self) -> None:
        """The current Jazzy gate must not reject real geometry message support as stale debris."""
        paths = (
            ROOT / "Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py",
            ROOT / "Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py",
        )
        expected_test_only = (
            "test_msgs_complex_nested_key__rosidl_typesupport_c_native.dll",
            "test_msgs_keyed_long__rosidl_typesupport_c_native.dll",
            "test_msgs_keyed_string__rosidl_typesupport_c_native.dll",
            "test_msgs_non_keyed_with_nested_key__rosidl_typesupport_c_native.dll",
        )

        for path in paths:
            with self.subTest(path=path):
                source = read_python_validation_source(path)
                self.assertIn("V083_EXCLUDED_TEST_TYPESUPPORT_DLLS", source)
                self.assertNotIn("geometry_msgs_velocity_with_covariance_stamped", source)
                for filename in expected_test_only:
                    self.assertIn(filename, source)

__all__ = ["R2fuArtifactHandoffTests"]
