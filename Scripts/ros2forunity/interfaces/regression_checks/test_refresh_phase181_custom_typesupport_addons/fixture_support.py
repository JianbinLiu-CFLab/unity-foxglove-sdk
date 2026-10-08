from __future__ import annotations
from .refresh_tests import *
class _Fixture:
    """Own the small filesystem fixture used by the refresh tests."""

    def __init__(self) -> None:
        """Create an isolated repository-shaped filesystem fixture."""

        self._temporary = temporary_directory("phase181-addon-refresh-")
        self.root = Path(self._temporary.name)
        self.runtime_manifest = {"runtimeId": "humble", "revision": 1}
        self.runtime_manifest_sha = hashlib.sha256(
            json.dumps(self.runtime_manifest, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
        ).hexdigest()
        self.ros2cs_source = self.root / "external" / "ros2cs"
        self.r2fu_source = self.root / "external" / "ros2-for-unity"
        self.unity = self.root / "external" / "Unity.exe"
        install = self.ros2cs_source / "install-humble"
        for relative in (
            "share/rosidl_generator_cs/cmake/rosidl_generator_csConfig.cmake",
            "share/builtin_interfaces/cmake/builtin_interfacesConfig.cmake",
            "include/builtin_interfaces/builtin_interfaces/msg/detail/time__struct.hpp",
            "lib/dotnet/ros2cs_common.dll",
            "lib/dotnet/builtin_interfaces_assembly.dll",
        ):
            path = install / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture")
        self.r2fu_source.mkdir(parents=True)
        self.unity.write_bytes(b"test unity executable")
        tooling = self.root / "Scripts" / "ros2forunity" / "interfaces"
        tooling.mkdir(parents=True)
        for name in (
            "build_foxrun_custom_typesupport_addon.py",
            "sync_foxrun_custom_typesupport_addon.py",
            "validate_foxrun_custom_typesupport_addon.py",
        ):
            (tooling / name).write_text("# test tooling\n", encoding="utf-8")
        self._write_project_selection()
        self._write_static_interface()
        self._write_runtime()

    def __enter__(self) -> "_Fixture":
        """Return the prepared fixture for a scoped test body."""

        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        """Dispose the fixture's temporary directory when the test scope exits."""

        self._temporary.cleanup()

    def _write_project_selection(self) -> None:
        """Write a Unity manifest and lockfile with exactly one selected runtime."""

        packages = self.root / "Unity2Foxglove" / "Packages"
        packages.mkdir(parents=True)
        package = "dev.unity2foxglove.ros2forunity.runtime.humble.win64"
        (packages / "manifest.json").write_text(
            json.dumps({"dependencies": {package: "file:../../Packages/" + package}}),
            encoding="utf-8",
        )
        (packages / "packages-lock.json").write_text(
            json.dumps({"dependencies": {package: {"version": "file:../../Packages/" + package}}}),
            encoding="utf-8",
        )

    def add_runtime_dependency(self, distro: str) -> None:
        """Add a second runtime manifest dependency to create an invalid selection."""

        manifest_path = self.root / "Unity2Foxglove" / "Packages" / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        package = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
        manifest["dependencies"][package] = "file:../../Packages/" + package
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

    def add_runtime_lock_entry(self, distro: str) -> None:
        """Add a stale runtime lock entry without changing the manifest."""

        lock_path = self.root / "Unity2Foxglove" / "Packages" / "packages-lock.json"
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        package = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
        lock["dependencies"][package] = {"version": "file:../../Packages/" + package}
        lock_path.write_text(json.dumps(lock), encoding="utf-8")

    def _write_static_interface(self) -> None:
        """Write the minimum static interface lock needed by the refresher."""

        support = self.root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces" / "RuntimeSupport"
        support.mkdir(parents=True)
        (support / "foxrun-ros2-interface-lock.json").write_text(
            json.dumps({"interfaceDigest": "b" * 64}),
            encoding="utf-8",
        )

    def _write_runtime(self) -> None:
        """Write the selected runtime manifest used for identity comparison."""

        support = self.root / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64" / "RuntimeSupport"
        support.mkdir(parents=True)
        (support / "runtime-manifest.json").write_text(json.dumps(self.runtime_manifest), encoding="utf-8")

    def write_addon(self, *, runtime_manifest_sha: str) -> None:
        """Write the tracked add-on manifest with a caller-controlled runtime hash."""

        self._write_typesupport_manifest(
            self.root
            / "Packages"
            / "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.humble.win64"
            / "RuntimeSupport",
            runtime_manifest_sha,
        )

    def write_candidate(self, *, runtime_manifest_sha: str) -> None:
        """Create the minimal identity and proof shape of a successful candidate."""
        candidate = self.root / "build" / "phase181" / "humble" / "candidate"
        support = candidate / "package" / "RuntimeSupport"
        self._write_typesupport_manifest(support, runtime_manifest_sha)
        (support / "typesupport-inventory.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "entries": [{"path": "RuntimeSupport/typesupport-manifest.json"}],
                }
            ),
            encoding="utf-8",
        )
        evidence = candidate / "e"
        evidence.mkdir(parents=True)
        (evidence / "candidate-validation.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "distro": "humble",
                    "candidatePackageSha256": refresh.normalized_json_sha256(
                        json.loads((support / "typesupport-inventory.json").read_text(encoding="utf-8"))
                    ),
                    "validated": True,
                }
            ),
            encoding="utf-8",
        )

    @staticmethod
    def _write_typesupport_manifest(support: Path, runtime_manifest_sha: str) -> None:
        """Write only the identity fields relevant to the refresh decision."""
        support.mkdir(parents=True)
        (support / "typesupport-manifest.json").write_text(
            json.dumps(
                {
                    "source": {"interfaceDigest": "b" * 64},
                    "baseRuntime": {"runtimeManifestSha256": runtime_manifest_sha},
                }
            ),
            encoding="utf-8",
        )


__all__ = [name for name in globals() if not name.startswith("__")]
