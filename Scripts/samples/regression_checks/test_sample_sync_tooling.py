#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for sample synchronization helpers.

from __future__ import annotations

import importlib.util
import json
import os
import stat
import sys
import tempfile
import unittest
from io import StringIO
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]


def load_module(name: str, relative: str):
    """Load one repository helper script as an isolated module."""
    path = ROOT / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(path.parent))
    try:
        spec.loader.exec_module(module)
    finally:
        sys.path[:] = original_path
    return module


class SampleSyncToolingTests(unittest.TestCase):
    """Regression coverage for sample sync tooling."""

    def test_ros2_bridge_sample_has_dedicated_sync_tool(self) -> None:
        """The Bridge sample must not rely on an untracked manual copy step."""
        script = ROOT / "Scripts/samples/sync_ros2_bridge_sample.py"

        self.assertTrue(script.is_file(), script)

    def test_ros2_bridge_sample_import_root_tracks_package_version(self) -> None:
        """The checked-in imported copy must follow the Bridge manifest version."""
        module = load_module(
            "sync_ros2_bridge_sample_version_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )
        self.assertTrue(hasattr(module, "default_imported_root"))

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest = (
                root
                / "Packages"
                / "dev.unity2foxglove.ros2bridge"
                / "package.json"
            )
            manifest.parent.mkdir(parents=True)
            manifest.write_text(
                json.dumps({"version": "7.6.5-preview.4"}),
                encoding="utf-8",
            )

            imported = module.default_imported_root(root)

        self.assertTrue(
            imported.as_posix().endswith(
                "Unity2Foxglove ROS2 Bridge/7.6.5-preview.4/ROS2 Bridge Sample"
            )
        )

    def test_ros2_bridge_sample_sync_includes_meta_files(self) -> None:
        """New sample assets and their GUID-bearing meta files move together."""
        module = load_module(
            "sync_ros2_bridge_sample_meta_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )
        self.assertTrue(hasattr(module, "compare_roots"))
        self.assertTrue(hasattr(module, "apply_sync"))

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "package"
            imported = root / "imported"
            package.mkdir()
            imported.mkdir()
            (package / "Duplex.cs").write_text("current\n", encoding="utf-8")
            (package / "Duplex.cs.meta").write_text(
                "guid: 0123456789abcdef0123456789abcdef\n",
                encoding="utf-8",
            )

            drift = module.compare_roots(package, imported)
            module.apply_sync(package, imported, drift)
            self.assertEqual([], module.compare_roots(package, imported))

    def test_ros2_bridge_sample_capture_updates_only_generated_scene(self) -> None:
        """Unity owns scene generation while package source remains canonical."""
        module = load_module(
            "sync_ros2_bridge_sample_scene_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )
        self.assertTrue(hasattr(module, "capture_generated_scene"))

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "package"
            imported = root / "imported"
            for sample_root in (package, imported):
                (sample_root / "Scenes").mkdir(parents=True)
                (sample_root / "Scripts").mkdir(parents=True)
            (package / "Scenes/Ros2BridgeSample.unity").write_text(
                "old scene\n",
                encoding="utf-8",
            )
            (imported / "Scenes/Ros2BridgeSample.unity").write_text(
                "unity generated scene\n",
                encoding="utf-8",
            )
            (package / "Scripts/Duplex.cs").write_text(
                "package source\n",
                encoding="utf-8",
            )
            (imported / "Scripts/Duplex.cs").write_text(
                "local drift\n",
                encoding="utf-8",
            )

            module.capture_generated_scene(package, imported)

            self.assertEqual(
                "unity generated scene\n",
                (package / "Scenes/Ros2BridgeSample.unity").read_text(
                    encoding="utf-8"
                ),
            )
            self.assertEqual(
                "package source\n",
                (package / "Scripts/Duplex.cs").read_text(encoding="utf-8"),
            )

    def test_ros2_bridge_sample_rejects_missing_or_aliased_roots(self) -> None:
        """Bridge sample synchronization must fail before unsafe writes."""
        module = load_module(
            "sync_ros2_bridge_sample_roots_under_test",
            "Scripts/samples/sync_ros2_bridge_sample.py",
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            existing = root / "sample"
            existing.mkdir()
            with self.assertRaises(FileNotFoundError):
                module._validate_roots(root / "missing", existing)
            with self.assertRaises(ValueError):
                module._validate_roots(existing, existing)

    def test_full_demo_messagepack_source_and_meta_have_canonical_mappings(self) -> None:
        """The controlled MessagePack partial must sync live -> package -> imported."""
        module = load_module(
            "sync_full_demo_messagepack_maps_under_test",
            "Scripts/samples/sync_full_demo.py",
        )

        mapped = {(item.demo, item.sample) for item in module.FILE_MAPS}

        self.assertIn(
            (
                module.FULL_DEMO_VISUALIZATION_SCRIPTS / "TestLog.MessagePack.cs",
                module.PACKAGE_SAMPLE / "Scripts" / "TestLog.MessagePack.cs",
            ),
            mapped,
        )
        self.assertIn(
            (
                module.FULL_DEMO_VISUALIZATION_SCRIPTS / "TestLog.MessagePack.cs.meta",
                module.PACKAGE_SAMPLE / "Scripts" / "TestLog.MessagePack.cs.meta",
            ),
            mapped,
        )

    def test_full_demo_messagepack_imported_pairs_preserve_meta_parity(self) -> None:
        """Imported sync must derive both MessagePack destinations from package mappings."""
        module = load_module(
            "sync_full_demo_messagepack_import_under_test",
            "Scripts/samples/sync_full_demo.py",
        )

        with tempfile.TemporaryDirectory() as temp:
            imported = Path(temp) / "Full Demo Visualization"
            pairs = module.imported_maps(imported, "package")

        by_name = {
            destination.name: source.name
            for source, destination in pairs
            if destination.name.startswith("TestLog.MessagePack")
        }
        self.assertEqual(
            {
                "TestLog.MessagePack.cs": "TestLog.MessagePack.cs",
                "TestLog.MessagePack.cs.meta": "TestLog.MessagePack.cs.meta",
            },
            by_name,
        )

    def test_full_demo_scene_sanitizes_portable_fields_with_variable_indentation(self) -> None:
        """Sample sync should sanitize local-only fields even if Unity changes indentation."""
        module = load_module("sync_full_demo_under_test", "Scripts/samples/sync_full_demo.py")

        with tempfile.TemporaryDirectory() as temp:
            scene = Path(temp) / "scene.unity"
            scene.write_text(
                "--- !u!114 &1\n"
                "MonoBehaviour:\n"
                f"  m_Script: {{fileID: 11500000, guid: {module.FOXGLOVE_MANAGER_SCRIPT_GUID}, type: 3}}\n"
                "    _sharedToken: secret-token\n"
                "    _replayFilePath: C:/Users/Alice/private.mcap\n"
                "    _transportMode: 2\n"
                "    _recordingDirectory: C:/recordings\n"
                "    _certificatePfxPath: C:/cert.pfx\n"
                "    _certificatePassword: password\n"
                "    _rootCaDistributorEnabled: 1\n"
                "    _rootCaFilePath: C:/root.pem\n",
                encoding="utf-8",
            )

            payload = module.portable_full_demo_scene_payload(scene).decode("utf-8")

        self.assertIn("    _sharedToken:", payload)
        self.assertIn("    _replayFilePath:", payload)
        self.assertNotIn("secret-token", payload)
        self.assertNotIn("C:/Users/Alice", payload)
        self.assertIn("    _transportMode: 0", payload)
        self.assertIn("    _rootCaDistributorEnabled: 0", payload)

    def test_full_demo_scene_validation_rejects_local_paths_and_tokens(self) -> None:
        """Portable scene payload validation should fail loudly on local-only data."""
        module = load_module("sync_full_demo_validate_under_test", "Scripts/samples/sync_full_demo.py")

        payload = (
            "--- !u!114 &1\n"
            "MonoBehaviour:\n"
            f"  m_Script: {{fileID: 11500000, guid: {module.FOXGLOVE_MANAGER_SCRIPT_GUID}, type: 3}}\n"
            "  _transportMode: 0\n"
            "  _replayFilePath:\n"
            "  _recordingDirectory:\n"
            "  _certificatePfxPath: C:/Users/Alice/cert.pfx\n"
            "  _certificatePassword:\n"
            "  _rootCaDistributorEnabled: 0\n"
            "  _rootCaFilePath:\n"
            "  _sharedToken: secret\n"
        )
        with self.assertRaises(ValueError):
            module.validate_portable_full_demo_scene_payload(payload)

    def test_full_demo_scene_validation_rejects_nonportable_switches(self) -> None:
        """Portable transport and CA switches must retain exact safe defaults."""
        module = load_module("sync_full_demo_switches_under_test", "Scripts/samples/sync_full_demo.py")
        payload = (
            "--- !u!114 &1\n"
            "MonoBehaviour:\n"
            f"  m_Script: {{fileID: 11500000, guid: {module.FOXGLOVE_MANAGER_SCRIPT_GUID}, type: 3}}\n"
            "  _transportMode: 2\n"
            "  _replayFilePath:\n"
            "  _recordingDirectory:\n"
            "  _certificatePfxPath:\n"
            "  _certificatePassword:\n"
            "  _rootCaDistributorEnabled: 1\n"
            "  _rootCaFilePath:\n"
            "  _sharedToken:\n"
        )

        with self.assertRaises(ValueError):
            module.validate_portable_full_demo_scene_payload(payload)

    def test_full_demo_scene_sanitizes_only_foxglove_manager_component(self) -> None:
        """A same-named field on another component must remain untouched."""
        module = load_module("sync_full_demo_scope_under_test", "Scripts/samples/sync_full_demo.py")
        with tempfile.TemporaryDirectory() as temp:
            scene = Path(temp) / "scene.unity"
            scene.write_text(
                "--- !u!114 &1\n"
                "MonoBehaviour:\n"
                "  m_Script: {fileID: 11500000, guid: unrelated, type: 3}\n"
                "  _sharedToken: unrelated-value\n"
                "--- !u!114 &2\n"
                "MonoBehaviour:\n"
                f"  m_Script: {{fileID: 11500000, guid: {module.FOXGLOVE_MANAGER_SCRIPT_GUID}, type: 3}}\n"
                "  _transportMode: 2\n"
                "  _replayFilePath: local.mcap\n"
                "  _recordingDirectory: local\n"
                "  _certificatePfxPath: local.pfx\n"
                "  _certificatePassword: local-password\n"
                "  _rootCaDistributorEnabled: 1\n"
                "  _rootCaFilePath: local.pem\n"
                "  _sharedToken: local-token\n",
                encoding="utf-8",
            )
            payload = module.portable_full_demo_scene_payload(scene).decode("utf-8")

        self.assertIn("  _sharedToken: unrelated-value", payload)
        self.assertNotIn("local-token", payload)

    def test_full_demo_explicit_import_root_does_not_require_project(self) -> None:
        """An explicit imported sample path is sufficient to build copy pairs."""
        module = load_module("sync_full_demo_explicit_root_under_test", "Scripts/samples/sync_full_demo.py")
        with tempfile.TemporaryDirectory() as temp:
            args = type(
                "Args",
                (),
                {
                    "mode": "package-to-imported",
                    "target_project": None,
                    "imported_sample_path": temp,
                },
            )()
            pairs = module.build_pairs(args)

        self.assertEqual(len(module.FILE_MAPS), len(pairs))

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


if __name__ == "__main__":
    unittest.main()
