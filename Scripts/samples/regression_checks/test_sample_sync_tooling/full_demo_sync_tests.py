from __future__ import annotations

from .foundation import *


class FullDemoSyncTests:
    """Exercise full-demo messagepack and scene synchronization"""

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

    def test_copy_pairs_transaction_restores_prior_destinations_on_failure(self) -> None:
        """A failed multi-file sync must not leave a mixed destination tree."""
        module = load_module("sync_full_demo_transaction_under_test", "Scripts/samples/sync_full_demo.py")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            src1, src2 = root / "src1", root / "src2"
            dst1, dst2 = root / "dst1", root / "dst2"
            src1.write_text("new1", encoding="utf-8"); src2.write_text("new2", encoding="utf-8")
            dst1.write_text("old1", encoding="utf-8"); dst2.write_text("old2", encoding="utf-8")
            original = module.copy_file
            calls = 0
            def fail_second(src, dst, dry_run):
                """Inject a failure on the second copy operation."""
                nonlocal calls
                calls += 1
                if calls == 2:
                    raise OSError("injected copy failure")
                return original(src, dst, dry_run)
            with mock.patch.object(module, "copy_file", side_effect=fail_second):
                with self.assertRaises(OSError):
                    module.copy_pairs_transaction([(src1, dst1), (src2, dst2)], False)
            self.assertEqual("old1", dst1.read_text(encoding="utf-8"))
            self.assertEqual("old2", dst2.read_text(encoding="utf-8"))

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


__all__ = ["FullDemoSyncTests"]
