from __future__ import annotations

from .foundation import *


class RuntimeBuildTransactionTests:
    """Exercise runtime build rollback and artifact identity."""

    def test_build_package_restores_existing_package_when_generation_fails(self) -> None:
        """A failed regeneration should not leave the package directory destroyed."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / self.builder.PACKAGE_NAME
            package.mkdir(parents=True)
            sentinel = package / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="0" * 64,
                size=1,
                inventory_file_count=1,
            )

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with mock.patch.object(self.builder, "extract_runtime", side_effect=RuntimeError("boom")):
                        with self.assertRaises(RuntimeError):
                            _ = self.builder.build_package(paths)

            self.assertTrue(sentinel.exists())
            self.assertEqual("keep", sentinel.read_text(encoding="utf-8"))
            rollback_root = root / "build" / "r2fu-runtime-package-rollback"
            self.assertEqual([], list(rollback_root.iterdir()))

    def test_build_package_preserves_snapshot_when_rollback_fails(self) -> None:
        """A failed rollback must retain the only recoverable copy of the old package."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / self.builder.PACKAGE_NAME
            package.mkdir(parents=True)
            (package / "sentinel.txt").write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="0" * 64,
                size=1,
                inventory_file_count=1,
            )
            real_copytree = self.builder.shutil.copytree
            copy_count = 0

            def fail_restore_copy(source, destination, *args, **kwargs):
                """Fail the restore copy after allowing the backup copy."""
                nonlocal copy_count
                copy_count += 1
                if copy_count == 2:
                    raise OSError("restore copy failed")
                return real_copytree(source, destination, *args, **kwargs)

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with mock.patch.object(self.builder, "extract_runtime", side_effect=RuntimeError("generation failed")):
                        with mock.patch.object(self.builder.shutil, "copytree", side_effect=fail_restore_copy):
                            with self.assertRaises(RuntimeError) as raised:
                                _ = self.builder.build_package(paths)

            rollback_root = root / "build" / "r2fu-runtime-package-rollback"
            snapshots = list(rollback_root.iterdir())
            self.assertEqual(1, len(snapshots), f"expected one preserved snapshot, got {snapshots}")
            snapshot = snapshots[0]
            self.assertEqual("keep", (snapshot / "sentinel.txt").read_text(encoding="utf-8"))
            self.assertIn("generation failed", str(raised.exception))
            self.assertIn("restore copy failed", str(raised.exception))
            self.assertIn(str(snapshot), str(raised.exception))

    def test_build_package_keeps_existing_package_if_reset_fails(self) -> None:
        """Rollback should not mask reset_package_dir path-safety failures."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "not-the-package"
            package.mkdir()
            sentinel = package / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="0" * 64,
                size=1,
                inventory_file_count=1,
            )

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with self.assertRaises(ValueError):
                        _ = self.builder.build_package(paths)

    def test_build_package_returns_artifact_identity(self) -> None:
        """main can print the already-computed hash without hashing the zip again."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / self.builder.PACKAGE_NAME
            paths = self.builder.BuildPaths(root / "runtime.zip", root / "inventory.json", package)
            artifact = self.builder.RuntimeArtifact(
                name=self.builder.ARTIFACT_NAME,
                sha256="1" * 64,
                size=1,
                inventory_file_count=1,
            )

            with mock.patch.object(self.builder, "ROOT", root):
                with mock.patch.object(self.builder, "require_inputs", return_value=({}, artifact)):
                    with mock.patch.object(self.builder, "extract_runtime", return_value=None):
                        with mock.patch.object(self.builder, "normalize_ros2cs_plugin_roots", return_value=None):
                            with mock.patch.object(self.builder, "prune_non_contract_examples", return_value=None):
                                with mock.patch.object(self.builder, "patch_ros2_for_unity", return_value=None):
                                    with mock.patch.object(self.builder, "apply_local_patch_overlays", return_value=None):
                                        with mock.patch.object(self.builder, "patch_ros_time_source_contract", return_value=None):
                                            with mock.patch.object(self.builder, "write_package_files", return_value=None):
                                                with mock.patch.object(self.builder, "apply_meta_overlays", return_value=None):
                                                    with mock.patch.object(self.builder, "write_generated_metas", return_value=None):
                                                        returned = self.builder.build_package(paths)

            self.assertIs(artifact, returned)


__all__ = ["RuntimeBuildTransactionTests"]
