from __future__ import annotations
from .foundation import *
from .fixtures import _Fixture

class CustomTypesupportAddonValidationTests(unittest.TestCase):
    """Represent CustomTypesupportAddonValidationTests."""
    def test_validation_suite_uses_named_split_package(self) -> None:
        """Require this validation suite to keep a named one-level package."""
        module = __import__(
            "Scripts.ros2forunity.interfaces.regression_checks.test_validate_foxrun_custom_typesupport_addon",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())

    def test_source_uses_named_split_package(self) -> None:
        """Require the common validator facade to use its named source package."""
        package = Path(common.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())

    def test_valid_matching_addon_passes(self) -> None:
        """Verify valid matching addon passes."""
        with self._fixture() as fixture:
            result = validate_addon(fixture.request)

            self.assertEqual("humble", result.distro)
            self.assertEqual(
                "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.humble.win64",
                result.package_id,
            )

    def test_portable_validation_compares_actual_base_runtime_hash_without_windows_metadata_probe(self) -> None:
        """Verify non-Windows package checks retain a real base-DLL identity gate."""
        with self._fixture() as fixture:
            request = replace(fixture.request, base_ros2_message_identity=None)

            with (
                patch.object(common, "_is_windows_host", return_value=False),
                patch.object(
                    common,
                    "_read_windows_managed_identity",
                    side_effect=AssertionError("portable validation must not invoke the Windows probe"),
                ),
            ):
                result = validate_addon(request)

            self.assertEqual("humble", result.distro)

        with self._fixture() as fixture:
            fixture.manifest_at(("managed", "ros2Message", "sha256"), "0" * 64)
            fixture.refresh_inventory()
            request = replace(fixture.request, base_ros2_message_identity=None)

            with patch.object(common, "_is_windows_host", return_value=False):
                with self.assertRaisesRegex(AddonValidationError, "repair-ros2cs-common-identity"):
                    validate_addon(request)

    def test_source_identity_distro_and_digest_mismatches_fail_closed(self) -> None:
        """Verify source identity distro and digest mismatches fail closed."""
        for path, value in (
            (("source", "upmPackageId"), "other.source"),
            (("source", "rosPackageName"), "other_ros_package"),
            (("distro",), "jazzy"),
            (("source", "interfaceDigest"), "not-a-sha256"),
        ):
            with self.subTest(path=path), self._fixture() as fixture:
                fixture.manifest_at(path, value)
                fixture.refresh_inventory()
                self._assert_invalid(fixture)
        with self.subTest("static source drift"), self._fixture() as fixture:
            fixture.write_static("Ros2Package~/msg/Phase181State48D288ED82F1Envelope.msg", "string changed\n")
            self._assert_invalid(fixture)

    def test_inventory_hash_drift_missing_native_and_unexpected_file_fail_closed(self) -> None:
        """Verify inventory hash drift missing native and unexpected file fail closed."""
        with self.subTest("hash drift"), self._fixture() as fixture:
            fixture.write("Runtime/Ros2ForUnity/Plugins/custom.dll", b"changed")
            self._assert_invalid(fixture)
        with self.subTest("missing native"), self._fixture() as fixture:
            (fixture.addon / "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll").unlink()
            self._assert_invalid(fixture)
        with self.subTest("unexpected file"), self._fixture() as fixture:
            fixture.write("unexpected.dll", b"unexpected")
            self._assert_invalid(fixture)

    def test_package_conflicts_runtime_abi_and_ros2cs_mvid_drift_fail_closed(self) -> None:
        """Verify package conflicts runtime abi and ros2cs mvid drift fail closed."""
        with self.subTest("conflict omission"), self._fixture() as fixture:
            package = fixture.package_json()
            package["unity2foxgloveConflicts"] = []
            fixture.write_json("package.json", package)
            fixture.refresh_inventory()
            self._assert_invalid(fixture)
        with self.subTest("runtime manifest drift"), self._fixture() as fixture:
            runtime = fixture.runtime_manifest()
            runtime["packageVersion"] = "0.2.0-preview.1"
            fixture.write_json(fixture.base_runtime.relative_to(fixture.root) / "RuntimeSupport/runtime-manifest.json", runtime)
            self._assert_invalid(fixture)
        with self.subTest("ros2cs mvid drift"), self._fixture() as fixture:
            fixture.manifest_at(("managed", "ros2Message", "mvid"), "11111111-1111-1111-1111-111111111111")
            fixture.refresh_inventory()
            self._assert_invalid(fixture)
        with self.subTest("ros2cs assembly identity drift"), self._fixture() as fixture:
            fixture.manifest_at(("managed", "ros2Message", "assemblyName"), "other_ros2cs_common")
            fixture.refresh_inventory()
            self._assert_invalid(fixture)

    def test_managed_type_map_native_location_and_pe_closure_fail_closed(self) -> None:
        """Verify managed type map native location and pe closure fail closed."""
        with self.subTest("wrong managed type"), self._fixture() as fixture:
            fixture.manifest_at(("managed", "typeMap", 0, "managedType"), "other.Message")
            fixture.refresh_inventory()
            self._assert_invalid(fixture)
        with self.subTest("managed assembly nested with native plugins"), self._fixture() as fixture:
            source = fixture.addon / "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll"
            target = fixture.addon / "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/unity2foxglove_foxrun_interfaces_v1_assembly.dll"
            target.parent.mkdir(parents=True, exist_ok=True)
            source.replace(target)
            fixture.manifest_at(
                ("managed", "assembly", "path"),
                "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/unity2foxglove_foxrun_interfaces_v1_assembly.dll",
            )
            fixture.manifest_at(
                ("managed", "assembly", "sha256"),
                _sha256(target),
            )
            fixture.manifest_at(
                ("managed", "pluginImporter", "metaPath"),
                "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/unity2foxglove_foxrun_interfaces_v1_assembly.dll.meta",
            )
            fixture.write(
                "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/unity2foxglove_foxrun_interfaces_v1_assembly.dll.meta",
                _plugin_importer_meta(),
            )
            fixture.refresh_inventory()
            self._assert_invalid(fixture)

    def test_native_plugin_importer_is_required_for_every_native_library(self) -> None:
        """Reject a native typesupport closure that Unity cannot load as a plugin."""
        with self._fixture() as fixture:
            native_meta = fixture.addon / "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll.meta"
            native_meta.unlink()
            fixture.refresh_inventory()

            with self.assertRaisesRegex(AddonValidationError, "repair-native-plugin-importer"):
                validate_addon(fixture.request)
        with self.subTest("native library outside plugin root"), self._fixture() as fixture:
            fixture.write("Runtime/other.dll", b"native")
            fixture.manifest_at(("nativeLibraries", 0, "path"), "Runtime/other.dll")
            fixture.manifest_at(("nativeLibraries", 0, "sha256"), _sha256(fixture.addon / "Runtime/other.dll"))
            fixture.refresh_inventory()
            self._assert_invalid(fixture)

    def test_forbidden_absolute_path_and_plugin_importer_scope_fail_closed(self) -> None:
        """Verify forbidden absolute path and plugin importer scope fail closed."""
        with self.subTest("absolute path"), self._fixture() as fixture:
            fixture.manifest_at(("managed", "assembly", "path"), r"C:\private\custom.dll")
            fixture.refresh_inventory()
            self._assert_invalid(fixture)

    def test_unity_6000_win64_plugin_importer_shape_is_accepted(self) -> None:
        """Verify unity 6000 win64 plugin importer shape is accepted."""
        with self._fixture() as fixture:
            fixture.write(
                "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll.meta",
                _unity6000_plugin_importer_meta(),
            )
            fixture.refresh_inventory()

            result = validate_addon(fixture.request)

            self.assertEqual("humble", result.distro)
        with self.subTest("plugin scope"), self._fixture() as fixture:
            fixture.manifest_at(("managed", "pluginImporter", "includePlatforms"), ["Any"])
            fixture.refresh_inventory()
            self._assert_invalid(fixture)

    def test_legacy_plugin_importer_rejects_values_bound_to_wrong_platforms(self) -> None:
        """Require every legacy importer safety value in its owning platform entry."""
        inverted = _plugin_importer_meta().replace(
            "Any:\n    second:\n      enabled: 0",
            "Any:\n    second:\n      enabled: 1",
        ).replace(
            "Editor: Editor\n    second:\n      enabled: 1",
            "Editor: Editor\n    second:\n      enabled: 0",
        ).replace(
            "Standalone: Windows\n    second:\n      enabled: 1",
            "Standalone: Windows\n    second:\n      enabled: 0",
        )

        self.assertFalse(common._has_restricted_windows_plugin_importer(inverted))

    def test_invalid_json_and_missing_license_notice_fail_closed(self) -> None:
        """Verify invalid json and missing license notice fail closed."""
        with self.subTest("invalid json"), self._fixture() as fixture:
            (fixture.addon / "RuntimeSupport/typesupport-manifest.json").write_text("{", encoding="utf-8")
            self._assert_invalid(fixture)
        with self.subTest("license"), self._fixture() as fixture:
            (fixture.addon / "LICENSE").unlink()
            self._assert_invalid(fixture)
        with self.subTest("notice"), self._fixture() as fixture:
            (fixture.addon / "THIRD_PARTY_NOTICES.md").unlink()
            self._assert_invalid(fixture)

    def test_duplicate_addons_and_lyrical_rmw_policy_fail_closed(self) -> None:
        """Verify duplicate addons and lyrical rmw policy fail closed."""
        with self._fixture() as first, self._fixture() as second:
            self._assert_invalid_set((first.request, second.request))
        with self._fixture(distro="lyrical") as fixture:
            fixture.manifest_at(("supportedRmwImplementations",), ["rmw_fastrtps_cpp"])
            fixture.refresh_inventory()
            self._assert_invalid(fixture)

    def test_lyrical_requires_explicit_fastdds_and_zenoh_closure_evidence(self) -> None:
        """Verify lyrical requires explicit fastdds and zenoh closure evidence."""
        with self.subTest("missing-zenoh-closure"), self._fixture(distro="lyrical") as fixture:
            fixture.manifest_at(("rmwClosures", "rmw_zenoh_cpp"), {})
            fixture.refresh_inventory()
            self._assert_invalid(fixture)
        with self.subTest("missing-zenoh-base-library"), self._fixture(distro="lyrical") as fixture:
            (fixture.base_runtime / "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/zenohc.dll").unlink()
            self._assert_invalid(fixture)

    def _assert_invalid(self, fixture: "_Fixture") -> None:
        """Implement the internal assert invalid step."""
        with self.assertRaises(AddonValidationError):
            validate_addon(fixture.request)

    def _assert_invalid_set(self, requests: tuple[AddonValidationRequest, ...]) -> None:
        """Implement the internal assert invalid set step."""
        with self.assertRaises(AddonValidationError):
            validate_addon_set(requests)

    def _fixture(self, distro: str = "humble") -> "_Fixture":
        """Implement the internal fixture step."""
        return _Fixture(distro)



__all__ = ['CustomTypesupportAddonValidationTests']
