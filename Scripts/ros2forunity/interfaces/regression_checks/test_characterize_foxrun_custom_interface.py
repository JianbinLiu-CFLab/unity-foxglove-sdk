"""Regression checks for Phase181 custom-interface characterization."""

from __future__ import annotations

from contextlib import redirect_stdout
import io
import json
import os
import sys
import unittest
from dataclasses import replace
from pathlib import Path
from unittest.mock import patch

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces import characterize_foxrun_custom_interface as characterization
from Scripts.ros2forunity.interfaces.characterize_foxrun_custom_interface import (
    CharacterizationError,
    CharacterizationRequest,
    build_colcon_command,
    build_characterization_environment,
    inspect_managed_evidence,
    prepare_characterization_workspace,
    requires_short_windows_build_alias,
    characterization_root,
)


class CustomInterfaceCharacterizationTests(unittest.TestCase):
    """Represent CustomInterfaceCharacterizationTests."""
    def test_windows_build_alias_is_required_only_for_an_overlong_projected_object_path(self) -> None:
        """Verify windows build alias is required only for an overlong projected object path."""
        with patch.object(characterization, "_is_windows_host", return_value=True):
            self.assertFalse(requires_short_windows_build_alias(Path("T:/")))
            self.assertTrue(requires_short_windows_build_alias(Path("D:/") / ("very-long-root-" * 20)))

    def test_candidate_workspace_uses_a_separate_out_of_tree_root(self) -> None:
        """Verify candidate workspace uses a separate out of tree root."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            request = replace(
                self._make_request(root, self._make_static_package(root)),
                workspace_name="candidate",
            )

            self.assertEqual(
                root / "build" / "phase181" / "humble" / "candidate",
                characterization_root(request),
            )

    def test_workspace_is_out_of_tree_and_colcon_command_uses_explicit_bases(self) -> None:
        """Verify workspace is out of tree and colcon command uses explicit bases."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            static_package = self._make_static_package(root)
            request = self._make_request(root, static_package)

            workspace = prepare_characterization_workspace(request)
            command = build_colcon_command(request, workspace)

            self.assertEqual(root / "build" / "phase181" / "humble" / "c", workspace)
            self.assertTrue((workspace / "s" / "unity2foxglove_foxrun_interfaces_v1" / "package.xml").is_file())
            self.assertEqual("colcon", command[0])
            self.assertEqual(("--log-base", str(workspace / "l"), "build"), command[1:4])
            self.assertIn("--base-paths", command)
            self.assertIn(str(workspace / "s"), command)
            self.assertIn("--build-base", command)
            self.assertIn(str(workspace / "b"), command)
            self.assertIn("--install-base", command)
            self.assertIn(str(workspace / "i"), command)
            self.assertIn("-G", command)
            self.assertIn("Ninja", command)

    def test_workspace_retries_a_transient_windows_build_file_lock(self) -> None:
        """Verify a previous Ninja handle does not require operator cleanup."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            static_package = self._make_static_package(root)
            request = self._make_request(root, static_package)
            workspace = characterization_root(request)
            workspace.mkdir(parents=True)
            (workspace / ".ninja_deps").write_text("stale", encoding="utf-8")
            real_rmtree = characterization.shutil.rmtree
            locked = PermissionError(32, "locked")
            locked.winerror = 32

            with (
                patch.object(
                    characterization.shutil,
                    "rmtree",
                    side_effect=[locked, real_rmtree],
                ) as remove,
                patch.object(characterization.time, "sleep") as sleep,
            ):
                prepared = prepare_characterization_workspace(request, replace_existing=True)

            self.assertEqual(workspace, prepared)
            self.assertEqual(2, remove.call_count)
            sleep.assert_called_once()
            self.assertTrue((prepared / "s" / "unity2foxglove_foxrun_interfaces_v1" / "package.xml").is_file())

    def test_colcon_command_uses_cmake_safe_forward_slash_python_paths(self) -> None:
        """Verify colcon command uses cmake safe forward slash python paths."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            static_package = self._make_static_package(root)
            request = self._make_request(root, static_package)

            command = build_colcon_command(
                request,
                root / "workspace",
                python=r"D:\ROS 2\humble\python.exe",
            )

            self.assertIn("-DPython3_EXECUTABLE=D:/ROS 2/humble/python.exe", command)
            self.assertIn("-DPYTHON_EXECUTABLE=D:/ROS 2/humble/python.exe", command)
            self.assertIn(
                "-DOPENSSL_ROOT_DIR="
                + (request.ros2_root / ".pixi" / "envs" / "default" / "Library").as_posix(),
                command,
            )

    def test_wrong_ros2_message_identity_fails_closed(self) -> None:
        """Verify wrong ros2 message identity fails closed."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            payload = {
                "managedAssembly": {"name": "unity2foxglove_foxrun_interfaces_v1_assembly"},
                "messages": [
                    {
                        "fullName": "unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope",
                        "interfaces": ["ROS2.Message"],
                        "constructors": [".ctor()"],
                        "disposable": True,
                    }
                ],
                "ros2MessageIdentity": "other.Message",
            }
            evidence = root / "managed-evidence.json"
            evidence.write_text(json.dumps(payload), encoding="utf-8")

            with self.assertRaises(CharacterizationError) as raised:
                inspect_managed_evidence(
                    evidence,
                    "unity2foxglove_foxrun_interfaces_v1_assembly",
                    "unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope",
                    "ROS2.Message",
                )

            self.assertEqual("FOXRUN_TOOLCHAIN002", raised.exception.code)
            self.assertEqual("repair-ros2cs-message-identity", raised.exception.remediation)

    def test_environment_keeps_declared_dotnet_after_msvc_capture(self) -> None:
        """Verify environment keeps declared dotnet after msvc capture."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            static_package = self._make_static_package(root)
            request = self._make_request(root, static_package)
            dotnet = root / "dotnet" / "dotnet.exe"
            dotnet.parent.mkdir(parents=True)
            dotnet.write_text("fixture", encoding="utf-8")
            ros2cs_install = root / "ros2cs-install"
            (ros2cs_install / "share" / "rosidl_generator_cs").mkdir(parents=True)

            request = CharacterizationRequest(
                distro=request.distro,
                static_package=request.static_package,
                ros2_root=request.ros2_root,
                ros2cs_source=request.ros2cs_source,
                ros2cs_install=ros2cs_install,
                r2fu_source=request.r2fu_source,
                build_root=request.build_root,
                dotnet=dotnet,
            )

            msvc_bin = root / "visual-studio" / "bin"
            with patch.object(characterization, "_capture_msvc_environment", return_value={"PATH": str(msvc_bin)}):
                environment = build_characterization_environment(request)

            self.assertIn(str(dotnet.parent), environment["PATH"].split(os.pathsep))
            self.assertIn(str(msvc_bin), environment["PATH"].split(os.pathsep))
            self.assertEqual(os.environ.get("APPDATA"), environment.get("APPDATA"))
            self.assertEqual(os.environ.get("ProgramFiles"), environment.get("ProgramFiles"))

    def test_environment_forces_utf8_for_rosidl_template_expansion(self) -> None:
        """Verify environment forces utf8 for rosidl template expansion."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            static_package = self._make_static_package(root)
            request = self._make_request(root, static_package)
            ros2cs_install = root / "ros2cs-install"
            (ros2cs_install / "share" / "rosidl_generator_cs").mkdir(parents=True)
            request = CharacterizationRequest(
                distro=request.distro,
                static_package=request.static_package,
                ros2_root=request.ros2_root,
                ros2cs_source=request.ros2cs_source,
                ros2cs_install=ros2cs_install,
                r2fu_source=request.r2fu_source,
                build_root=request.build_root,
            )

            with patch.object(characterization, "_capture_msvc_environment", return_value={"PATH": r"C:\\VS\\bin"}):
                environment = build_characterization_environment(request)

            self.assertEqual("1", environment["PYTHONUTF8"])

    def test_environment_layers_the_complete_ros2cs_overlay_before_the_ros_base(self) -> None:
        """Generated packages need each ros2cs dependency's native and managed exports together."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            static_package = self._make_static_package(root)
            request = self._make_request(root, static_package)
            ros2cs_install = root / "ros2cs-install"
            (ros2cs_install / "share" / "rosidl_generator_cs").mkdir(parents=True)
            request = CharacterizationRequest(
                distro=request.distro,
                static_package=request.static_package,
                ros2_root=request.ros2_root,
                ros2cs_source=request.ros2cs_source,
                ros2cs_install=ros2cs_install,
                r2fu_source=request.r2fu_source,
                build_root=request.build_root,
            )

            with patch.object(characterization, "_capture_msvc_environment", return_value={"PATH": r"C:\\VS\\bin"}):
                environment = build_characterization_environment(request)

            expected = [str(ros2cs_install), str(request.ros2_root)]
            self.assertEqual(expected, environment["AMENT_PREFIX_PATH"].split(os.pathsep))
            self.assertEqual(expected, environment["CMAKE_PREFIX_PATH"].split(os.pathsep))
            self.assertEqual(expected, environment["COLCON_PREFIX_PATH"].split(os.pathsep))

    def test_colcon_execution_streams_visible_progress_while_retaining_the_log(self) -> None:
        """A minutes-long native build must not look silent or lose its durable log."""
        with temporary_directory("characterize-") as temporary_root:
            root = Path(temporary_root)
            log = root / "e" / "colcon.log"
            output = io.StringIO()

            with redirect_stdout(output):
                characterization._run(
                    (sys.executable, "-c", "print('phase181 visible build progress')"),
                    cwd=root,
                    environment=dict(os.environ),
                    log_path=log,
                )

            self.assertIn("phase181 visible build progress", output.getvalue())
            self.assertIn("phase181 visible build progress", log.read_text(encoding="utf-8"))

    @staticmethod
    def _make_static_package(root: Path) -> Path:
        """Implement the internal make static package step."""
        package = root / "interfaces"
        ros_package = package / "Ros2Package~"
        (ros_package / "msg").mkdir(parents=True)
        (ros_package / "package.xml").write_text("<package/>", encoding="utf-8")
        (ros_package / "CMakeLists.txt").write_text("project(unity2foxglove_foxrun_interfaces_v1)", encoding="utf-8")
        (ros_package / "msg" / "Phase181State48D288ED82F1Envelope.msg").write_text("string message\n", encoding="utf-8")
        return package

    @staticmethod
    def _make_request(root: Path, static_package: Path) -> CharacterizationRequest:
        """Implement the internal make request step."""
        return CharacterizationRequest(
            distro="humble",
            static_package=static_package,
            ros2_root=root / "ros2_humble",
            ros2cs_source=root / "ros2cs",
            ros2cs_install=root / "ros2cs-install",
            r2fu_source=root / "ros2-for-unity",
            build_root=root / "build",
        )


if __name__ == "__main__":
    unittest.main()
