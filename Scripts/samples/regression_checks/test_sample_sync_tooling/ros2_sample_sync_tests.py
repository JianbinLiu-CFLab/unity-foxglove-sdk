from __future__ import annotations

from .foundation import *


class Ros2SampleSyncTests:
    """Exercise ROS2 sample apply, validation, and root comparison"""

    def test_validate_file_maps_reports_invalid_portable_scene_source(self) -> None:
        """Validate mode should collect portable-scene errors instead of throwing."""
        module = load_module("sync_full_demo_validate_collect_under_test", "Scripts/samples/sync_full_demo.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            src = root / "Unity2Foxglove" / "Assets" / "Scenes" / "SampleScene.unity"
            dst = root / "Packages" / "dev.unity2foxglove.sdk" / "Samples~" / "FullDemoVisualization" / "Scenes" / "FullDemoVisualization.unity"
            src.parent.mkdir(parents=True)
            dst.parent.mkdir(parents=True)
            src.write_text("  _sharedToken: secret\n", encoding="utf-8")
            dst.write_text("placeholder\n", encoding="utf-8")
            with mock.patch.object(module, "DEMO_ASSETS", root / "Unity2Foxglove" / "Assets"):
                with mock.patch.object(
                    module,
                    "portable_full_demo_scene_payload",
                    side_effect=ValueError("portable scene still has local value"),
                ):
                    errors = module.validate_file_maps([(src, dst)])

        self.assertEqual(1, len(errors))
        self.assertIn("invalid source", errors[0])

    def test_validate_mode_prints_neutral_error_label(self) -> None:
        """Validate mode should not classify stale content as missing files."""
        module = load_module("sync_full_demo_validate_label_under_test", "Scripts/samples/sync_full_demo.py")

        with mock.patch.object(module, "parse_args", return_value=type("Args", (), {"mode": "validate"})()):
            with mock.patch.object(module, "build_pairs", return_value=[]):
                with mock.patch.object(module, "validate_file_maps", return_value=["stale destination: sample"]):
                    stderr = StringIO()
                    with mock.patch("sys.stderr", stderr):
                        result = module.main()

        self.assertEqual(module.EXIT_FAILURE, result)
        self.assertIn("[error] stale destination: sample", stderr.getvalue())
        self.assertNotIn("[missing]", stderr.getvalue())

    def test_ros2_sample_default_imported_root_uses_package_manifest_version(self) -> None:
        """Sample sync should not hardcode the imported sample package version."""
        module = load_module("sync_ros2_samples_under_test", "Scripts/samples/sync_ros2_samples.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest = root / "Packages" / "dev.unity2foxglove.ros2forunity" / "package.json"
            manifest.parent.mkdir(parents=True)
            manifest.write_text(json.dumps({"version": "9.8.7-preview.6"}), encoding="utf-8")

            imported_root = module.default_imported_root(root)

        self.assertTrue(imported_root.as_posix().endswith("Unity2Foxglove ROS2 For Unity/9.8.7-preview.6"))

    def test_ros2_sample_apply_does_not_fail_on_extra_imported_files(self) -> None:
        """Apply mode should leave imported-owned extras in place without reporting sync failure."""
        module = load_module("sync_ros2_samples_apply_under_test", "Scripts/samples/sync_ros2_samples.py")

        drift = [
            module.Drift("extra imported", Path("local_only.cs")),
            module.Drift("changed", Path("package_owned.cs")),
        ]

        self.assertEqual([module.Drift("changed", Path("package_owned.cs"))], module.blocking_drift_after_apply(drift))

    def test_ros2_sample_apply_and_dry_run_accept_imported_owned_extras(self) -> None:
        """Apply and validation modes should agree that imported-owned extras are non-blocking."""
        module = load_module("sync_ros2_samples_extra_under_test", "Scripts/samples/sync_ros2_samples.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_root = root / "package"
            imported_root = root / "imported"
            package_root.mkdir()
            imported_root.mkdir()
            (imported_root / "local_only.cs").write_text("imported-owned\n", encoding="utf-8")

            def run(apply: bool) -> int:
                """Run the real command body with explicit temporary roots."""
                args = type(
                    "Args",
                    (),
                    {
                        "apply": apply,
                        "package_root": str(package_root),
                        "imported_root": str(imported_root),
                    },
                )()
                with mock.patch.object(module, "parse_args", return_value=args):
                    with mock.patch("sys.stdout", StringIO()), mock.patch("sys.stderr", StringIO()):
                        return module.main()

            self.assertEqual(module.EXIT_SUCCESS, run(apply=True))
            self.assertEqual(module.EXIT_SUCCESS, run(apply=False))

    def test_ros2_sample_explicit_roots_do_not_resolve_manifest_default(self) -> None:
        """Two explicit roots should not require the repository package manifest."""
        module = load_module("sync_ros2_samples_explicit_under_test", "Scripts/samples/sync_ros2_samples.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_root = root / "package"
            imported_root = root / "imported"
            package_root.mkdir()
            imported_root.mkdir()
            args = type(
                "Args",
                (),
                {
                    "apply": False,
                    "package_root": str(package_root),
                    "imported_root": str(imported_root),
                },
            )()
            with mock.patch.object(module, "parse_args", return_value=args):
                with mock.patch.object(module, "default_imported_root", return_value=root / "unused") as default_root:
                    with mock.patch("sys.stdout", StringIO()), mock.patch("sys.stderr", StringIO()):
                        result = module.main()

        self.assertEqual(module.EXIT_SUCCESS, result)
        default_root.assert_not_called()

    def test_ros2_sample_apply_refreshes_imported_file_timestamp(self) -> None:
        """Applying drift must make Unity notice the newly copied sample content."""
        module = load_module("sync_ros2_samples_timestamp_under_test", "Scripts/samples/sync_ros2_samples.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_root = root / "package"
            imported_root = root / "imported"
            package_root.mkdir()
            imported_root.mkdir()
            package_file = package_root / "sample.cs"
            imported_file = imported_root / "sample.cs"
            package_file.write_text("current\n", encoding="utf-8")
            imported_file.write_text("stale\n", encoding="utf-8")
            old_timestamp = 946684800
            os.utime(package_file, (old_timestamp, old_timestamp))
            os.utime(imported_file, (old_timestamp, old_timestamp))

            module.apply_sync(
                package_root,
                imported_root,
                [module.Drift("changed", Path("sample.cs"))],
            )

            self.assertEqual("current\n", imported_file.read_text(encoding="utf-8"))
            self.assertGreater(imported_file.stat().st_mtime, package_file.stat().st_mtime)

    def test_ros2_sample_apply_preserves_existing_destination_mode(self) -> None:
        """Atomic replacement should retain the existing imported file's access mode."""
        module = load_module("sync_ros2_samples_mode_under_test", "Scripts/samples/sync_ros2_samples.py")
        real_copyfile = module.shutil.copyfile

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_root = root / "package"
            imported_root = root / "imported"
            package_root.mkdir()
            imported_root.mkdir()
            package_file = package_root / "sample.cs"
            imported_file = imported_root / "sample.cs"
            package_file.write_text("current\n", encoding="utf-8")
            imported_file.write_text("stale\n", encoding="utf-8")
            original_mode = stat.S_IMODE(imported_file.stat().st_mode)

            def copy_with_restrictive_temp_mode(source: Path, destination: Path) -> str:
                """Model the owner-only mode used by NamedTemporaryFile on POSIX."""
                copied = real_copyfile(source, destination)
                os.chmod(destination, stat.S_IREAD)
                return copied

            with mock.patch.object(module.shutil, "copyfile", side_effect=copy_with_restrictive_temp_mode):
                module.apply_sync(
                    package_root,
                    imported_root,
                    [module.Drift("changed", Path("sample.cs"))],
                )

            self.assertEqual(original_mode, stat.S_IMODE(imported_file.stat().st_mode))

    def test_ros2_sample_compare_roots_limits_ignored_and_allowlisted_paths(self) -> None:
        """Only Unity metadata and the documented imported-owned probe are ignored."""
        module = load_module("sync_ros2_samples_boundaries_under_test", "Scripts/samples/sync_ros2_samples.py")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "package"
            imported = root / "imported"
            package.mkdir()
            imported.mkdir()
            allowlisted = next(iter(module.ALLOWLISTED_RELATIVE_FILES))
            for relative, package_text, imported_text in (
                (Path("Asset.cs.meta"), "package-meta", "imported-meta"),
                (allowlisted, "package-probe", "imported-probe"),
                (Path("Runtime.cs"), "package-runtime", "imported-runtime"),
            ):
                (package / relative).parent.mkdir(parents=True, exist_ok=True)
                (imported / relative).parent.mkdir(parents=True, exist_ok=True)
                (package / relative).write_text(package_text, encoding="utf-8")
                (imported / relative).write_text(imported_text, encoding="utf-8")

            drift = module.compare_roots(package, imported)

        self.assertEqual([module.Drift("changed", Path("Runtime.cs"))], drift)


__all__ = ["Ros2SampleSyncTests"]
