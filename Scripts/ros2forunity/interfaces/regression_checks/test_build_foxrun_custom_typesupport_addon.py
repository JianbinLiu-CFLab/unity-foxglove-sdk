"""Regression checks for Phase181 custom typesupport candidate packaging."""

from __future__ import annotations

import json
import unittest
from dataclasses import replace
from pathlib import Path

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces.build_foxrun_custom_typesupport_addon import (
    CandidateBuildError,
    CandidateBuildRequest,
    MANAGED_ASSEMBLY_FILE,
    _catalog_source,
    _managed_package_assembly_path,
    _repair_tracked_addon_catalog,
    _runtime_rmws,
    _unity_plugin_importer_arguments,
    _write_inventory,
    build_candidate,
    candidate_package_root,
    parse_args,
    select_candidate_native_libraries,
    verify_candidate_source_lock,
)
from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
    ROS_PACKAGE_NAME,
    STATIC_INTERFACE_PACKAGE_ID,
    addon_package_id,
    compute_static_interface_digest,
    file_sha256,
)


class CustomTypesupportCandidateBuildTests(unittest.TestCase):
    """Represent CustomTypesupportCandidateBuildTests."""
    def test_check_source_cli_does_not_invent_machine_specific_toolchain_sources(self) -> None:
        """Verify check source cli does not invent machine specific toolchain sources."""
        request, check_source, repair_catalog = parse_args(("--distro", "humble", "--check-source"))

        self.assertTrue(check_source)
        self.assertFalse(repair_catalog)
        self.assertIsNone(request.ros2cs_source)
        self.assertIsNone(request.ros2cs_install)
        self.assertIsNone(request.r2fu_source)

    def test_full_candidate_build_requires_explicit_toolchain_sources(self) -> None:
        """Verify full candidate build requires explicit toolchain sources."""
        with self._fixture() as fixture:
            request = replace(
                fixture.request,
                ros2cs_source=None,
                ros2cs_install=None,
                r2fu_source=None,
            )

            with self.assertRaisesRegex(CandidateBuildError, "provide-ros2cs-source"):
                build_candidate(request)

    def test_candidate_package_is_constrained_to_its_distro_build_root(self) -> None:
        """Verify candidate package is constrained to its distro build root."""
        request = self._request()
        self.assertEqual(
            request.build_root / "phase181" / "humble" / "candidate" / "package",
            candidate_package_root(request),
        )

        unsafe = CandidateBuildRequest(
            distro="humble",
            static_interface_package=request.static_interface_package,
            base_runtime_package=request.base_runtime_package,
            ros2_root=request.ros2_root,
            ros2cs_source=request.ros2cs_source,
            ros2cs_install=request.ros2cs_install,
            r2fu_source=request.r2fu_source,
            build_root=request.build_root.parent,
        )
        with self.assertRaises(CandidateBuildError):
            candidate_package_root(unsafe)

    def test_managed_ros2cs_assembly_stays_at_the_plugins_root(self) -> None:
        """Match the R2FU package layout: managed assembly above Win64 native DLLs."""
        package_root = Path("candidate") / "package"

        self.assertEqual(
            package_root / "Runtime" / "Ros2ForUnity" / "Plugins" / MANAGED_ASSEMBLY_FILE,
            _managed_package_assembly_path(package_root),
        )

    def test_inventory_records_native_plugin_importer_metadata(self) -> None:
        """Native PluginImporter metadata is verified payload, never untracked candidate debris."""
        with temporary_directory("typesupport-native-meta-") as temporary:
            package = Path(temporary) / "package"
            native = package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "custom.dll"
            native.parent.mkdir(parents=True)
            native.write_bytes(b"native")
            native.with_name(native.name + ".meta").write_text("PluginImporter:\n", encoding="utf-8")

            _write_inventory(package)

            inventory = json.loads((package / "RuntimeSupport" / "typesupport-inventory.json").read_text(encoding="utf-8"))
            paths = {entry["path"] for entry in inventory["entries"]}
            self.assertIn("Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll.meta", paths)

    def test_source_lock_drift_fails_before_build(self) -> None:
        """Verify source lock drift fails before build."""
        with self._fixture() as fixture:
            request = fixture.request
            self.assertEqual(fixture.digest, verify_candidate_source_lock(request))
            (fixture.static / "Ros2Package~" / "msg" / "State.msg").write_text("string changed\n", encoding="utf-8")
            with self.assertRaises(CandidateBuildError):
                verify_candidate_source_lock(request)

    def test_native_allowlist_excludes_python_generator_and_preserves_required_closure(self) -> None:
        """Verify native allowlist excludes python generator and preserves required closure."""
        root = Path("candidate") / "i" / "bin"
        paths = (
            root / (ROS_PACKAGE_NAME + "__rosidl_generator_c.dll"),
            root / (ROS_PACKAGE_NAME + "__rosidl_generator_py.dll"),
            root / (ROS_PACKAGE_NAME + "__rosidl_typesupport_c.dll"),
            root / (ROS_PACKAGE_NAME + "_state__rosidl_typesupport_c_native.dll"),
            root / "unrelated.dll",
        )
        selected = select_candidate_native_libraries(paths)
        self.assertEqual(
            (
                paths[0],
                paths[2],
                paths[3],
            ),
            selected,
        )

    def test_runtime_rmw_capability_uses_explicit_modes_then_legacy_manifest_fields(self) -> None:
        """Verify runtime rmw capability uses explicit modes then legacy manifest fields."""
        self.assertEqual(
            ("rmw_fastrtps_cpp",),
            _runtime_rmws({"rmwImplementation": "rmw_fastrtps_cpp"}, "humble"),
        )
        self.assertEqual(
            ("rmw_fastrtps_cpp", "rmw_zenoh_cpp"),
            _runtime_rmws(
                {
                    "communicationModes": [
                        {"rmwImplementation": "rmw_zenoh_cpp"},
                        {"rmwImplementation": "rmw_fastrtps_cpp"},
                    ]
                },
                "lyrical",
            ),
        )
        with self.assertRaises(CandidateBuildError):
            _runtime_rmws({"supportedRmwImplementations": []}, "jazzy")

    def test_generated_catalog_embeds_resolved_static_interface_metadata(self) -> None:
        """Verify generated catalog embeds resolved static interface metadata."""
        interface_digest = "a" * 64
        catalog = _catalog_source(
            distro="lyrical",
            interface_digest=interface_digest,
            type_map=(
                {
                    "canonicalRosType": ROS_PACKAGE_NAME + "/msg/State",
                    "managedType": ROS_PACKAGE_NAME + ".msg.State",
                },
            ),
        )

        self.assertIn('return "' + STATIC_INTERFACE_PACKAGE_ID + '";', catalog)
        self.assertIn('return "' + ROS_PACKAGE_NAME + '";', catalog)
        self.assertIn('return "' + interface_digest + '";', catalog)
        self.assertIn('return "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64";', catalog)
        self.assertIn("public static class FoxRunRos2CustomTypesupportMetadata", catalog)
        self.assertIn("public const int InterfaceRevision = 1;", catalog)
        self.assertIn('public const string InterfaceDigest = "' + interface_digest + '";', catalog)
        self.assertNotIn("STATIC_INTERFACE_PACKAGE_ID", catalog)
        self.assertNotIn("interface_digest", catalog)
        self.assertNotIn("base_runtime", catalog)
        self.assertEqual(1, catalog.count("public string Platform"))
        self.assertIn("FoxRunRos2CustomTypesupportNativePluginBootstrap.Register(", catalog)
        self.assertLess(
            catalog.index("FoxRunRos2CustomTypesupportNativePluginBootstrap.Register("),
            catalog.index("FoxRunRos2CustomTypesupportCatalogRegistry.Register("),
        )
        self.assertNotIn("ROS2.GlobalVariables", catalog)

    def test_unity_plugin_importer_uses_one_native_directory_pass(self) -> None:
        """Keep the managed and native importer boundaries explicit and bounded."""
        managed = _unity_plugin_importer_arguments(
            Path("candidate/i/bin/interfaces_assembly.dll"),
            Path("candidate/package/interfaces_assembly.dll.meta"),
            input_is_directory=False,
        )
        native = _unity_plugin_importer_arguments(
            Path("candidate/package/Runtime/Ros2ForUnity/Plugins/Windows/x86_64"),
            Path("candidate/package/Runtime/Ros2ForUnity/Plugins/Windows/x86_64"),
            input_is_directory=True,
        )

        self.assertEqual(
            (
                "-phase181TypesupportManagedInput",
                "candidate/i/bin/interfaces_assembly.dll",
                "-phase181TypesupportManagedMetaOutput",
                "candidate/package/interfaces_assembly.dll.meta",
            ),
            managed,
        )
        self.assertEqual(
            (
                "-phase181TypesupportPluginInputDirectory",
                "candidate/package/Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
                "-phase181TypesupportPluginMetaOutputDirectory",
                "candidate/package/Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
            ),
            native,
        )

    def test_catalog_repair_regenerates_only_the_tracked_catalog_and_inventory(self) -> None:
        """Verify catalog repair regenerates only the tracked catalog and inventory."""
        with self._fixture() as fixture:
            target = fixture.root / "Packages" / addon_package_id("humble")
            generated = target / "Runtime" / "FoxRun" / "Generated"
            generated.mkdir(parents=True)
            (target / "RuntimeSupport").mkdir()
            (target / "RuntimeSupport" / "typesupport-manifest.json").write_text(
                json.dumps(
                    {
                        "source": {
                            "upmPackageId": STATIC_INTERFACE_PACKAGE_ID,
                            "rosPackageName": ROS_PACKAGE_NAME,
                            "interfaceRevision": 1,
                            "interfaceDigest": fixture.digest,
                        },
                        "baseRuntime": {
                            "packageId": "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                        },
                        "managed": {
                            "typeMap": [
                                {
                                    "canonicalRosType": ROS_PACKAGE_NAME + "/msg/State",
                                    "managedType": ROS_PACKAGE_NAME + ".msg.State",
                                },
                            ],
                        },
                    }
                ),
                encoding="utf-8",
            )
            stale_catalog = generated / "FoxRunCustomTypesupportCatalog.g.cs"
            stale_catalog.write_text("stale catalog", encoding="utf-8")
            # Unity can create this local importer beside a tracked package
            # asset; it is not package payload and must not become inventory.
            (target / "LICENSE.meta").write_text("unity-generated\n", encoding="utf-8")
            request = CandidateBuildRequest(
                distro="humble",
                static_interface_package=fixture.static,
                base_runtime_package=fixture.root / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                ros2_root=fixture.root / "ros2-windows" / "ros2_humble",
                ros2cs_source=fixture.root / "ros2cs",
                ros2cs_install=fixture.root / "ros2cs" / "install-humble",
                r2fu_source=fixture.root / "ros2-for-unity",
                build_root=fixture.root / "build",
                repo_root=fixture.root,
            )

            repaired = _repair_tracked_addon_catalog(request)

            self.assertEqual(stale_catalog, repaired)
            catalog = repaired.read_text(encoding="utf-8")
            self.assertIn('return "' + STATIC_INTERFACE_PACKAGE_ID + '";', catalog)
            self.assertIn('return "dev.unity2foxglove.ros2forunity.runtime.humble.win64";', catalog)
            inventory = json.loads(
                (target / "RuntimeSupport" / "typesupport-inventory.json").read_text(encoding="utf-8")
            )
            catalog_entry = next(
                entry
                for entry in inventory["entries"]
                if entry["path"] == "Runtime/FoxRun/Generated/FoxRunCustomTypesupportCatalog.g.cs"
            )
            self.assertEqual(repaired.stat().st_size, catalog_entry["byteLength"])
            self.assertEqual(file_sha256(repaired), catalog_entry["sha256"])
            self.assertNotIn("LICENSE.meta", {entry["path"] for entry in inventory["entries"]})
            self.assertNotIn(b"\r", repaired.read_bytes())

    def _request(self) -> CandidateBuildRequest:
        """Implement the internal request step."""
        root = Path(__file__).resolve().parents[4] / "build" / "Tests" / "Phase181" / "request"
        return CandidateBuildRequest(
            distro="humble",
            static_interface_package=root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces",
            base_runtime_package=root / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
            ros2_root=root / "ros2-windows" / "ros2_humble",
            ros2cs_source=root / "ros2cs",
            ros2cs_install=root / "ros2cs" / "install-humble",
            r2fu_source=root / "ros2-for-unity",
            build_root=root / "build",
        )

    def _fixture(self) -> "_Fixture":
        """Implement the internal fixture step."""
        return _Fixture()


class _Fixture:
    """Represent Fixture."""
    def __init__(self) -> None:
        """Initialize this object."""
        self._temporary = temporary_directory("typesupport-build-")
        self.root = Path(self._temporary.name)
        self.static = self.root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces"
        (self.static / "Ros2Package~" / "msg").mkdir(parents=True)
        (self.static / "Ros2Package~" / "msg" / "State.msg").write_text("string value\n", encoding="utf-8")
        self.digest = compute_static_interface_digest(self.static)
        (self.static / "RuntimeSupport").mkdir()
        (self.static / "RuntimeSupport" / "foxrun-ros2-interface-lock.json").write_text(
            json.dumps(
                {
                    "unityPackageId": "dev.unity2foxglove.foxrun.ros2.interfaces",
                    "rosPackageName": ROS_PACKAGE_NAME,
                    "interfaceRevision": 1,
                    "interfaceDigest": self.digest,
                }
            ),
            encoding="utf-8",
        )
        self.request = CandidateBuildRequest(
            distro="humble",
            static_interface_package=self.static,
            base_runtime_package=self.root / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
            ros2_root=self.root / "ros2-windows" / "ros2_humble",
            ros2cs_source=self.root / "ros2cs",
            ros2cs_install=self.root / "ros2cs" / "install-humble",
            r2fu_source=self.root / "ros2-for-unity",
            build_root=self.root / "build",
        )

    def __enter__(self) -> "_Fixture":
        """Enter this fixture scope."""
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        """Release this fixture scope."""
        self._temporary.cleanup()


if __name__ == "__main__":
    unittest.main()
