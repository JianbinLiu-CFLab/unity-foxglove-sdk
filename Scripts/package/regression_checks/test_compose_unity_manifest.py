"""Regression checks for the Phase202 Unity manifest composer."""

from __future__ import annotations

import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from Scripts.package import run_package_topology_unity_matrix as topology


ROOT = Path(__file__).resolve().parents[3]


class ComposeUnityManifestTests(unittest.TestCase):
    """Exercise manifest composition through the public module entrypoint."""

    def test_all_profiles_produce_dependency_closed_manifests(self) -> None:
        """Every README profile and distro produces a conflict-free dependency closure."""
        combinations = [
            ("core", None),
            ("bridge", None),
            ("gateway", None),
            *(("native", distro) for distro in ("humble", "jazzy", "lyrical")),
            *(("native-custom", distro) for distro in ("humble", "jazzy", "lyrical")),
        ]
        for profile, distro in combinations:
            with self.subTest(profile=profile, distro=distro):
                with self._project() as project:
                    result = self._run(project, profile, distro)
                    self.assertEqual(0, result.returncode, result.stderr)
                    manifest = self._read_manifest(project)
                    selected = {
                        name
                        for name in manifest["dependencies"]
                        if name.startswith("dev.unity2foxglove.")
                    }
                    self.assertTrue(selected)
                    package_data = {
                        name: json.loads(
                            (ROOT / "Packages" / name / "package.json").read_text(encoding="utf-8")
                        )
                        for name in selected
                    }
                    for name, data in package_data.items():
                        for dependency, version in data.get("dependencies", {}).items():
                            if dependency.startswith("dev.unity2foxglove."):
                                self.assertIn(dependency, selected)
                                self.assertEqual(
                                    version,
                                    package_data[dependency]["version"],
                                )
                        self.assertFalse(set(data.get("unity2foxgloveConflicts", [])) & selected)

    def test_rejects_invalid_distro_shape_without_writing(self) -> None:
        """Distro arguments must match the selected native profile before mutation."""
        with self._project() as project:
            before = (project / "Packages" / "manifest.json").read_bytes()
            result = self._run(project, "native", None)
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(before, (project / "Packages" / "manifest.json").read_bytes())

    def test_removes_stale_local_entries_and_preserves_unrelated_entries(self) -> None:
        """Composition removes stale repository packages while preserving unrelated dependencies."""
        with self._project() as project:
            manifest_path = project / "Packages" / "manifest.json"
            manifest = self._read_manifest(project)
            manifest["dependencies"] = {
                "com.unity.modules.jsonserialize": "1.0.0",
                "dev.unity2foxglove.ros2forunity.runtime.humble.win64": (
                    "file:"
                    + os.path.relpath(
                        ROOT / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                        manifest_path.parent,
                    ).replace(os.sep, "/")
                ),
                "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64": (
                    "file:"
                    + os.path.relpath(
                        ROOT / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64",
                        manifest_path.parent,
                    ).replace(os.sep, "/")
                ),
            }
            manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            result = self._run(project, "native-custom", "jazzy")
            self.assertEqual(0, result.returncode, result.stderr)
            dependencies = self._read_manifest(project)["dependencies"]
            self.assertNotIn("dev.unity2foxglove.ros2forunity.runtime.humble.win64", dependencies)
            self.assertIn("dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64", dependencies)
            self.assertEqual("1.0.0", dependencies["com.unity.modules.jsonserialize"])

    def test_profile_switch_removes_custom_interface_closure(self) -> None:
        """Switching away from native-custom removes its static interface closure."""
        with self._project() as project:
            self.assertEqual(0, self._run(project, "native-custom", "jazzy").returncode)
            self.assertEqual(0, self._run(project, "native", "jazzy").returncode)
            dependencies = self._read_manifest(project)["dependencies"]
            self.assertNotIn("dev.unity2foxglove.foxrun.ros2.interfaces", dependencies)
            self.assertNotIn(
                "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64",
                dependencies,
            )
            self.assertIn("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", dependencies)
            self.assertEqual(0, self._run(project, "core", None).returncode)
            dependencies = self._read_manifest(project)["dependencies"]
            self.assertNotIn("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", dependencies)

    def test_atomic_replace_preserves_manifest_mode(self) -> None:
        """Atomic composition retains the existing POSIX manifest mode."""
        if os.name == "nt":
            self.skipTest("Windows mode bits are not enforced")
        with self._project() as project:
            manifest_path = project / "Packages" / "manifest.json"
            manifest_path.chmod(0o640)
            self.assertEqual(0, self._run(project, "core", None).returncode)
            self.assertEqual(0o640, stat.S_IMODE(manifest_path.stat().st_mode))

    def test_resolution_failure_requires_package_manager_diagnostic(self) -> None:
        """A generic Unity failure cannot satisfy an expected resolution failure."""
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "project"
            (project / "Packages").mkdir(parents=True)
            log_path = project / "unity.log"
            log_path.write_text("Unity failed before package initialization\n", encoding="utf-8")
            with mock.patch.object(
                topology.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], 1),
            ):
                result = topology._run_unity(Path("Unity.exe"), project, log_path, 1)
            self.assertEqual("INFRASTRUCTURE_FAILURE", result["failureKind"])

            log_path.write_text("Failed to resolve packages: missing file package\n", encoding="utf-8")
            with mock.patch.object(
                topology.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], 1),
            ):
                result = topology._run_unity(Path("Unity.exe"), project, log_path, 1)
            self.assertEqual("EXPECTED_PACKAGE_RESOLUTION_FAILURE", result["failureKind"])

    def test_preserves_nonconflicting_first_party_package(self) -> None:
        """A non-conflicting bridge package remains available to demo assets."""
        with self._project() as project:
            manifest_path = project / "Packages" / "manifest.json"
            manifest = self._read_manifest(project)
            manifest["dependencies"]["dev.unity2foxglove.ros2bridge"] = (
                "file:"
                + os.path.relpath(
                    ROOT / "Packages" / "dev.unity2foxglove.ros2bridge",
                    manifest_path.parent,
                ).replace(os.sep, "/")
            )
            manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            result = self._run(project, "native-custom", "jazzy")
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertIn("dev.unity2foxglove.ros2bridge", self._read_manifest(project)["dependencies"])

    def test_rejects_external_fake_local_entry_without_writing(self) -> None:
        """A local-looking package entry outside the repository is rejected before writing."""
        with self._project() as project:
            manifest_path = project / "Packages" / "manifest.json"
            manifest = self._read_manifest(project)
            manifest["dependencies"]["dev.unity2foxglove.fake"] = "file:../../outside/fake"
            manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            before = manifest_path.read_bytes()
            result = self._run(project, "core", None)
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(before, manifest_path.read_bytes())

    def test_rejects_forged_package_name(self) -> None:
        """A selected package directory whose package name is forged is rejected."""
        with self._project() as project:
            result = self._run(project, "core", None, packages_root=project / "source-packages")
            self.assertNotEqual(0, result.returncode)

    def test_rejects_missing_package_json_without_writing(self) -> None:
        """A missing package manifest fails closed without changing the target."""
        with self._project() as project:
            result = self._run(project, "core", None, packages_root=project / "missing-packages")
            self.assertNotEqual(0, result.returncode)

    def test_rejects_comments_and_trailing_commas(self) -> None:
        """JSON comments and trailing commas are rejected instead of rewritten."""
        for text in ('{"dependencies": {}, // comment\n}', '{"dependencies": {},}'):
            with self.subTest(text=text):
                with self._project() as project:
                    manifest_path = project / "Packages" / "manifest.json"
                    manifest_path.write_text(text, encoding="utf-8")
                    before = manifest_path.read_bytes()
                    result = self._run(project, "core", None)
                    self.assertNotEqual(0, result.returncode)
                    self.assertEqual(before, manifest_path.read_bytes())

    def test_preserves_crlf_and_is_idempotent(self) -> None:
        """The first composition preserves CRLF and a second run is byte-identical."""
        with self._project() as project:
            manifest_path = project / "Packages" / "manifest.json"
            manifest_path.write_bytes(b'{\r\n  "dependencies": {\r\n    "com.unity.modules.jsonserialize": "1.0.0"\r\n  }\r\n}\r\n')
            first = self._run(project, "core", None)
            self.assertEqual(0, first.returncode, first.stderr)
            first_bytes = manifest_path.read_bytes()
            self.assertIn(b"\r\n", first_bytes)
            second = self._run(project, "core", None)
            self.assertEqual(0, second.returncode, second.stderr)
            self.assertEqual(first_bytes, manifest_path.read_bytes())

    def test_rejects_read_only_manifest(self) -> None:
        """A read-only manifest is rejected before an atomic replacement is attempted."""
        with self._project() as project:
            manifest_path = project / "Packages" / "manifest.json"
            original_mode = stat.S_IMODE(manifest_path.stat().st_mode)
            manifest_path.chmod(stat.S_IRUSR | stat.S_IRGRP | stat.S_IROTH)
            try:
                result = self._run(project, "core", None)
                if result.returncode == 0:
                    self.skipTest("the Windows filesystem did not enforce the read-only mode")
                self.assertNotEqual(0, result.returncode)
            finally:
                manifest_path.chmod(original_mode)

    def test_accepts_project_path_with_spaces_and_non_ascii(self) -> None:
        """A project path containing spaces and non-ASCII characters is supported."""
        with self._project(name="项目 with spaces") as project:
            result = self._run(project, "core", None)
            self.assertEqual(0, result.returncode, result.stderr)

    def test_rejects_legacy_runtime_with_packaged_runtime(self) -> None:
        """A legacy Assets/Ros2ForUnity runtime cannot coexist with a packaged native runtime."""
        with self._project() as project:
            (project / "Assets" / "Ros2ForUnity").mkdir(parents=True)
            before = (project / "Packages" / "manifest.json").read_bytes()
            result = self._run(project, "native", "jazzy")
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(before, (project / "Packages" / "manifest.json").read_bytes())

    def _project(self, *, name: str = "phase202-project"):
        """Create a temporary Unity project with a minimal manifest."""
        temporary = tempfile.TemporaryDirectory(prefix="phase202-")
        project = Path(temporary.name) / name
        (project / "Packages").mkdir(parents=True)
        (project / "ProjectSettings").mkdir()
        (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.3.14f1\n", encoding="utf-8"
        )
        (project / "Packages" / "manifest.json").write_text(
            '{\n  "dependencies": {\n    "com.unity.modules.jsonserialize": "1.0.0"\n  }\n}\n',
            encoding="utf-8",
        )
        return _TemporaryProject(temporary, project)

    def _run(self, project: Path, profile: str, distro: str | None, *, packages_root: Path | None = None):
        """Run the composer module with the real Python interpreter."""
        command = [
            sys.executable,
            "-m",
            "Scripts.package.compose_unity_manifest",
            "--project",
            str(project),
            "--profile",
            profile,
        ]
        if distro is not None:
            command.extend(("--distro", distro))
        environment = dict(os.environ)
        if packages_root is not None:
            environment["PHASE202_PACKAGE_ROOT"] = str(packages_root)
        return subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True)

    @staticmethod
    def _read_manifest(project: Path) -> dict:
        """Read the temporary project's manifest as JSON."""
        return json.loads((project / "Packages" / "manifest.json").read_text(encoding="utf-8"))


class _TemporaryProject:
    """Own a temporary project and clean it up at context exit."""

    def __init__(self, temporary: tempfile.TemporaryDirectory, project: Path) -> None:
        """Store the temporary directory owner and project path."""
        self._temporary = temporary
        self.path = project

    def __enter__(self) -> Path:
        """Return the project path."""
        return self.path

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        """Remove the temporary project."""
        self._temporary.cleanup()


if __name__ == "__main__":
    unittest.main()
