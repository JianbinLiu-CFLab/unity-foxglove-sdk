from __future__ import annotations
from .foundation import *

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
            "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll.meta",
            _plugin_importer_meta(),
        )
        managed_fixture = bytearray(256)
        managed_fixture[:2] = b"MZ"
        managed_fixture[0x3C:0x40] = (0x80).to_bytes(4, "little")
        managed_fixture[0x80:0x84] = b"PE\0\0"
        managed_fixture[0x84:0x86] = b"d\x86"
        managed_fixture[0xC0:0xC4] = b"BSJB"
        managed_fixture[0xD0:0xD0 + len(b"Phase181State48D288ED82F1Envelope")] = b"Phase181State48D288ED82F1Envelope"
        self.write(
            "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll",
            bytes(managed_fixture),
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



__all__ = ['_Fixture']
