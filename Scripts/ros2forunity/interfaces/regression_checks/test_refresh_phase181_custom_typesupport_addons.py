"""Regression checks for the Phase181 add-on refresh orchestrator."""

from __future__ import annotations

import json
import hashlib
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces import refresh_phase181_custom_typesupport_addons as refresh
from Scripts.ros2forunity.interfaces.refresh_phase181_custom_typesupport_addons import (
    AddonRefreshRequest,
    AddonRefreshError,
    inspect_addon_state,
    run_refresh,
)


class Phase181CustomTypesupportRefreshTests(unittest.TestCase):
    """Keep drift detection and the bounded build/sync/validate sequence explicit."""

    def test_inspection_reports_runtime_manifest_drift_without_a_build(self) -> None:
        """A changed base runtime manifest marks only that add-on as stale."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)

            state = inspect_addon_state(fixture.root, "humble")

            self.assertFalse(state.current)
            self.assertEqual(("runtime-manifest",), state.reasons)

    def test_current_addon_runs_only_the_final_validator(self) -> None:
        """A current package avoids an unnecessary native rebuild and sync."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha=fixture.runtime_manifest_sha)
            commands: list[tuple[str, ...]] = []

            result = run_refresh(
                AddonRefreshRequest(root=fixture.root, distros=("humble",), apply=False),
                runner=self._recording_runner(commands),
            )

            self.assertTrue(result[0].current)
            self.assertEqual(1, len(commands))
            self.assertIn("validate_foxrun_custom_typesupport_addon.py", commands[0][1])

    def test_apply_rebuilds_and_syncs_only_the_stale_distro(self) -> None:
        """An apply run orders build, verified sync, then validation for a stale package."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            result = run_refresh(request, runner=self._recording_runner(commands))

            self.assertFalse(result[0].current)
            self.assertEqual(3, len(commands))
            self.assertIn("build_foxrun_custom_typesupport_addon.py", commands[0][1])
            self.assertIn("sync_foxrun_custom_typesupport_addon.py", commands[1][1])
            self.assertIn("validate_foxrun_custom_typesupport_addon.py", commands[2][1])
            self.assertIn(str(fixture.root / "build"), commands[0])

    def test_apply_reuses_a_matching_validated_candidate_before_rebuilding(self) -> None:
        """A prior successful candidate is synchronized instead of rebuilding native code again."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            fixture.write_candidate(runtime_manifest_sha=fixture.runtime_manifest_sha)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual(2, len(commands))
            self.assertIn("sync_foxrun_custom_typesupport_addon.py", commands[0][1])
            self.assertIn("validate_foxrun_custom_typesupport_addon.py", commands[1][1])

    def test_apply_rebuilds_when_candidate_payload_is_not_exactly_inventoried(self) -> None:
        """A candidate with an unrecorded PluginImporter file cannot bypass a fresh build."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            fixture.write_candidate(runtime_manifest_sha=fixture.runtime_manifest_sha)
            extra = (
                fixture.root
                / "build"
                / "phase181"
                / "humble"
                / "candidate"
                / "package"
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "unexpected.dll.meta"
            )
            extra.parent.mkdir(parents=True)
            extra.write_text("PluginImporter:\n", encoding="utf-8")
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual(3, len(commands))
            self.assertIn("build_foxrun_custom_typesupport_addon.py", commands[0][1])

    def test_apply_refuses_an_incomplete_ros2cs_install_before_any_child_command(self) -> None:
        """A concurrent ros2cs rebuild must not race an add-on candidate build."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            (fixture.ros2cs_source / "install-humble" / "lib" / "dotnet" / "builtin_interfaces_assembly.dll").unlink()
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            with self.assertRaisesRegex(AddonRefreshError, "wait-for-complete-ros2cs-install"):
                run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual([], commands)

    def test_apply_refuses_a_ros2cs_overlay_without_the_required_native_rosidl_header(self) -> None:
        """The C# overlay cannot hide a half-installed native builtin_interfaces package."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            header = (
                fixture.ros2cs_source
                / "install-humble"
                / "include"
                / "builtin_interfaces"
                / "builtin_interfaces"
                / "msg"
                / "detail"
                / "time__struct.hpp"
            )
            header.unlink()
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            with self.assertRaisesRegex(AddonRefreshError, "wait-for-complete-ros2cs-install"):
                run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual([], commands)

    def test_apply_refuses_a_running_ros2cs_colcon_build_before_any_child_command(self) -> None:
        """A live external install transaction has priority over this add-on refresh."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            with patch.object(refresh, "_ros2cs_install_build_is_active", return_value=True):
                with self.assertRaisesRegex(AddonRefreshError, "wait-for-active-ros2cs-colcon-build"):
                    run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual([], commands)

    def test_apply_never_syncs_a_candidate_when_the_ros2cs_install_changes_during_build(self) -> None:
        """A toolchain mutation after preflight invalidates the candidate rather than publishing it."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            def mutating_runner(command, **_kwargs):
                commands.append(tuple(command))
                if "build_foxrun_custom_typesupport_addon.py" in command[1]:
                    (fixture.ros2cs_source / "install-humble" / "lib" / "dotnet" / "builtin_interfaces_assembly.dll").write_bytes(
                        b"changed-toolchain"
                    )
                return subprocess.CompletedProcess(command, 0, "", "")

            with patch.object(refresh, "_ros2cs_install_build_is_active", return_value=False):
                with self.assertRaisesRegex(AddonRefreshError, "wait-for-stable-ros2cs-install"):
                    run_refresh(request, runner=mutating_runner)

            self.assertEqual(1, len(commands))
            self.assertIn("build_foxrun_custom_typesupport_addon.py", commands[0][1])

    def test_multiple_active_runtimes_fail_before_any_child_command(self) -> None:
        """The orchestrator refuses the duplicate-runtime state that breaks Unity compilation."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha=fixture.runtime_manifest_sha)
            fixture.add_runtime_dependency("jazzy")
            commands: list[tuple[str, ...]] = []

            with self.assertRaisesRegex(AddonRefreshError, "select-exactly-one-runtime"):
                run_refresh(
                    AddonRefreshRequest(root=fixture.root, distros=("humble",), apply=False),
                    runner=self._recording_runner(commands),
                )

            self.assertEqual([], commands)

    def test_stale_runtime_lock_entry_fails_before_any_child_command(self) -> None:
        """A removed manifest runtime must not remain resolved in Unity's lock file."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha=fixture.runtime_manifest_sha)
            fixture.add_runtime_lock_entry("jazzy")
            commands: list[tuple[str, ...]] = []

            with self.assertRaisesRegex(AddonRefreshError, "select-exactly-one-runtime"):
                run_refresh(
                    AddonRefreshRequest(root=fixture.root, distros=("humble",), apply=False),
                    runner=self._recording_runner(commands),
                )

            self.assertEqual([], commands)

    @staticmethod
    def _recording_runner(commands: list[tuple[str, ...]]):
        """Return a completed-process runner that exposes the exact child argv."""

        def run(command, **_kwargs):
            commands.append(tuple(command))
            return subprocess.CompletedProcess(command, 0, "", "")

        return run

    def _fixture(self) -> "_Fixture":
        """Create a minimal repository-shaped refresh fixture."""
        return _Fixture()


class _Fixture:
    """Own the small filesystem fixture used by the refresh tests."""

    def __init__(self) -> None:
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
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self._temporary.cleanup()

    def _write_project_selection(self) -> None:
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
        manifest_path = self.root / "Unity2Foxglove" / "Packages" / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        package = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
        manifest["dependencies"][package] = "file:../../Packages/" + package
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

    def add_runtime_lock_entry(self, distro: str) -> None:
        lock_path = self.root / "Unity2Foxglove" / "Packages" / "packages-lock.json"
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        package = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
        lock["dependencies"][package] = {"version": "file:../../Packages/" + package}
        lock_path.write_text(json.dumps(lock), encoding="utf-8")

    def _write_static_interface(self) -> None:
        support = self.root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces" / "RuntimeSupport"
        support.mkdir(parents=True)
        (support / "foxrun-ros2-interface-lock.json").write_text(
            json.dumps({"interfaceDigest": "b" * 64}),
            encoding="utf-8",
        )

    def _write_runtime(self) -> None:
        support = self.root / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64" / "RuntimeSupport"
        support.mkdir(parents=True)
        (support / "runtime-manifest.json").write_text(json.dumps(self.runtime_manifest), encoding="utf-8")

    def write_addon(self, *, runtime_manifest_sha: str) -> None:
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
            json.dumps({"schemaVersion": 1, "distro": "humble", "validated": True}),
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


if __name__ == "__main__":
    unittest.main()
