"""Regression checks for Phase181 custom ROS2 typesupport add-on validation."""

from __future__ import annotations

import hashlib
import json
import unittest
from pathlib import Path

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
    AddonValidationError,
    AddonValidationRequest,
    compute_static_interface_digest,
    validate_addon,
    validate_addon_set,
)


class CustomTypesupportAddonValidationTests(unittest.TestCase):
    """Represent CustomTypesupportAddonValidationTests."""
    def test_valid_matching_addon_passes(self) -> None:
        """Verify valid matching addon passes."""
        with self._fixture() as fixture:
            result = validate_addon(fixture.request)

            self.assertEqual("humble", result.distro)
            self.assertEqual(
                "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.humble.win64",
                result.package_id,
            )

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


class _Fixture:
    """Represent Fixture."""
    def __init__(self, distro: str) -> None:
        """Initialize this object."""
        self._temporary = temporary_directory("typesupport-validate-")
        self.root = Path(self._temporary.name)
        self.distro = distro
        self.static = self.root / "Packages/dev.unity2foxglove.foxrun.ros2.interfaces"
        self.base_runtime = self.root / (
            "Packages/dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
        )
        self.addon = self.root / (
            "Packages/dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + distro + ".win64"
        )
        self._create()
        self.request = AddonValidationRequest(
            distro=distro,
            addon_package=self.addon,
            static_interface_package=self.static,
            base_runtime_package=self.base_runtime,
            base_ros2_message_identity={
                "assemblyName": "ros2cs_common",
                "version": "0.0.0.0",
                "publicKeyToken": "",
                "mvid": "93949883-9308-4238-a8a6-55ed7003760c",
                "sha256": _sha256(self.base_runtime / "Runtime/Ros2ForUnity/Plugins/ros2cs_common.dll"),
            },
        )

    def __enter__(self) -> "_Fixture":
        """Enter this fixture scope."""
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        """Release this fixture scope."""
        self._temporary.cleanup()

    def _create(self) -> None:
        """Implement the internal create step."""
        self.write_static("package.json", "{\"name\":\"dev.unity2foxglove.foxrun.ros2.interfaces\"}\n")
        self.write_static("README.md", "fixture\n")
        self.write_static("RuntimeSupport/foxrun-ros2-interface-settings.json", "{\"locked\":true}\n")
        self.write_static("Ros2Package~/package.xml", "<package/>\n")
        self.write_static("Ros2Package~/CMakeLists.txt", "project(unity2foxglove_foxrun_interfaces_v1)\n")
        self.write_static(
            "Ros2Package~/msg/Phase181State48D288ED82F1Envelope.msg",
            "string foxrun_origin_id\n",
        )
        interface_digest = compute_static_interface_digest(self.static)
        runtime = {
            "schemaVersion": 1,
            "packageName": "dev.unity2foxglove.ros2forunity.runtime." + self.distro + ".win64",
            "packageVersion": "0.1.0-preview.1",
            "rosDistro": self.distro,
            "platform": "win64",
            "architecture": "x86_64",
        }
        self.write_json(
            self.static.relative_to(self.root) / "RuntimeSupport/foxrun-ros2-interface-lock.json",
            {
                "unityPackageId": "dev.unity2foxglove.foxrun.ros2.interfaces",
                "rosPackageName": "unity2foxglove_foxrun_interfaces_v1",
                "interfaceRevision": 1,
                "interfaceDigest": interface_digest,
            },
        )
        self.write_json(
            self.base_runtime.relative_to(self.root) / "RuntimeSupport/runtime-manifest.json",
            runtime,
        )
        self.write_base("Runtime/Ros2ForUnity/Plugins/ros2cs_common.dll", b"base-ros2cs-common")
        self.write_base("Runtime/Ros2ForUnity/Plugins/Windows/x86_64/base-runtime.dll", b"base-runtime")
        self.write_base("Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rmw_fastrtps_cpp.dll", b"fastdds")
        if self.distro == "lyrical":
            self.write_base("Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rmw_zenoh_cpp.dll", b"zenoh-rmw")
            self.write_base("Runtime/Ros2ForUnity/Plugins/Windows/x86_64/zenohc.dll", b"zenoh-core")
        self.write(
            "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll",
            b"custom-native",
        )
        self.write(
            "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll",
            b"custom-managed",
        )
        self.write(
            "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll.meta",
            _plugin_importer_meta(),
        )
        self.write("Runtime/FoxRun/Generated/CustomCatalog.cs", b"// catalog\n")
        self.write("LICENSE", b"Apache-2.0\n")
        self.write("README.md", b"fixture\n")
        self.write("THIRD_PARTY_NOTICES.md", b"fixture notice\n")
        package_id = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + self.distro + ".win64"
        others = [
            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + candidate + ".win64"
            for candidate in ("humble", "jazzy", "lyrical")
            if candidate != self.distro
        ]
        self.write_json(
            "package.json",
            {
                "name": package_id,
                "version": "0.1.0-preview.1",
                "license": "Apache-2.0",
                "unity2foxgloveConflicts": others,
                "unity2foxgloveFoxRunCustomTypesupportAddOn": True,
                "dependencies": {
                    "dev.unity2foxglove.ros2forunity": "0.1.0-preview.1",
                    runtime["packageName"]: runtime["packageVersion"],
                },
            },
        )
        manifest = {
            "schemaVersion": 1,
            "source": {
                "upmPackageId": "dev.unity2foxglove.foxrun.ros2.interfaces",
                "rosPackageName": "unity2foxglove_foxrun_interfaces_v1",
                "interfaceRevision": 1,
                "interfaceDigest": interface_digest,
                "generatorSchemaVersion": 1,
            },
            "distro": self.distro,
            "platform": "win64",
            "architecture": "x86_64",
            "baseRuntime": {
                "packageId": runtime["packageName"],
                "runtimeManifestSha256": _normalized_json_sha256(runtime),
                "runtimeManifestVersion": 1,
            },
            "supportedRmwImplementations": (
                ["rmw_fastrtps_cpp", "rmw_zenoh_cpp"] if self.distro == "lyrical" else ["rmw_fastrtps_cpp"]
            ),
            "managed": {
                "assembly": {
                    "path": "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll",
                    "name": "unity2foxglove_foxrun_interfaces_v1_assembly",
                    "sha256": _sha256(self.addon / "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll"),
                },
                "typeMap": [
                    {
                        "canonicalRosType": "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                        "managedType": "unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope",
                    }
                ],
                "ros2Message": {
                    "assemblyName": "ros2cs_common",
                    "version": "0.0.0.0",
                    "publicKeyToken": "",
                    "mvid": "93949883-9308-4238-a8a6-55ed7003760c",
                    "sha256": _sha256(self.base_runtime / "Runtime/Ros2ForUnity/Plugins/ros2cs_common.dll"),
                },
                "pluginImporter": {
                    "metaPath": "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll.meta",
                    "includePlatforms": ["Editor", "WindowsStandalone64"],
                },
            },
            "nativeLibraries": [
                {
                    "path": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll",
                    "sha256": _sha256(self.addon / "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll"),
                    "classification": "direct",
                }
            ],
            "rmwClosures": {
                "rmw_fastrtps_cpp": {
                    "baseRuntimeLibraries": ["rmw_fastrtps_cpp.dll"],
                    "addOnLibraries": ["Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll"],
                },
                **(
                    {
                        "rmw_zenoh_cpp": {
                            "baseRuntimeLibraries": ["rmw_zenoh_cpp.dll", "zenohc.dll"],
                            "addOnLibraries": ["Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll"],
                        }
                    }
                    if self.distro == "lyrical"
                    else {}
                ),
            },
            "provenance": {"source": "controlled-out-of-tree-build"},
        }
        self.write_json("RuntimeSupport/typesupport-manifest.json", manifest)
        self._write_inventory()

    def _write_inventory(self) -> None:
        """Implement the internal write inventory step."""
        entries = []
        excluded = {"RuntimeSupport/typesupport-inventory.json"}
        for path in sorted(self.addon.rglob("*"), key=lambda item: item.as_posix().lower()):
            if not path.is_file():
                continue
            relative = path.relative_to(self.addon).as_posix()
            if relative in excluded:
                continue
            entries.append(
                {
                    "path": relative,
                    "byteLength": path.stat().st_size,
                    "sha256": _sha256(path),
                    "role": _role_for(relative),
                    "classification": _classification_for(relative),
                }
            )
        self.write_json("RuntimeSupport/typesupport-inventory.json", {"schemaVersion": 1, "entries": entries})

    def refresh_inventory(self) -> None:
        """Run refresh inventory."""
        self._write_inventory()

    def write(self, relative: str | Path, data: bytes | str) -> None:
        """Run write."""
        path = self.addon / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data if isinstance(data, bytes) else data.encode("utf-8"))

    def write_base(self, relative: str | Path, data: bytes | str) -> None:
        """Run write base."""
        path = self.base_runtime / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data if isinstance(data, bytes) else data.encode("utf-8"))

    def write_static(self, relative: str | Path, data: bytes | str) -> None:
        """Run write static."""
        path = self.static / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data if isinstance(data, bytes) else data.encode("utf-8"))

    def write_json(self, relative: str | Path, payload: object) -> None:
        """Run write json."""
        relative_path = Path(relative)
        path = self.root / relative_path if relative_path.parts and relative_path.parts[0] == "Packages" else self.addon / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(payload, sort_keys=True, indent=2) + "\n", encoding="utf-8")

    def package_json(self) -> dict:
        """Run package json."""
        return json.loads((self.addon / "package.json").read_text(encoding="utf-8"))

    def runtime_manifest(self) -> dict:
        """Run runtime manifest."""
        return json.loads((self.base_runtime / "RuntimeSupport/runtime-manifest.json").read_text(encoding="utf-8"))

    def manifest_at(self, path: tuple[str, ...], value: object) -> None:
        """Run manifest at."""
        manifest_path = self.addon / "RuntimeSupport/typesupport-manifest.json"
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
        target = payload
        for key in path[:-1]:
            target = target[key]
        target[path[-1]] = value
        manifest_path.write_text(json.dumps(payload, sort_keys=True, indent=2) + "\n", encoding="utf-8")


def _sha256(path: Path) -> str:
    """Implement the internal sha256 step."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _normalized_json_sha256(payload: object) -> str:
    """Implement the internal normalized json sha256 step."""
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def _role_for(path: str) -> str:
    """Implement the internal role for step."""
    if path.endswith(".dll"):
        return "managed" if path.endswith("_assembly.dll") else "native"
    if path.endswith(".cs"):
        return "catalog"
    if path.endswith(".meta"):
        return "importer"
    if path.startswith("RuntimeSupport/"):
        return "metadata"
    return "notice"


def _classification_for(path: str) -> str:
    """Implement the internal classification for step."""
    return "direct" if path.endswith("custom.dll") else "metadata"


def _plugin_importer_meta() -> str:
    """Implement the internal plugin importer meta step."""
    return """fileFormatVersion: 2
PluginImporter:
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: x86_64
        OS: Windows
  - first:
      Standalone: Windows
    second:
      enabled: 1
      settings:
        CPU: x86_64
"""


def _unity6000_plugin_importer_meta() -> str:
    """Implement the internal unity6000 plugin importer meta step."""
    return """fileFormatVersion: 2
PluginImporter:
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 1
      settings:
        CPU: x86_64
        OS: Windows
    Standalone:
      enabled: 0
      settings:
        CPU: x86_64
    Win64:
      enabled: 1
      settings: {}
"""


if __name__ == "__main__":
    unittest.main()
