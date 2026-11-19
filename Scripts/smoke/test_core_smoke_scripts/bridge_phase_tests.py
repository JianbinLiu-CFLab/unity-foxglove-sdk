from __future__ import annotations

from .foundation import *


class BridgePhaseTests:
    """Exercise bridge preflight, Phase146, Phase160, and Phase138T contracts"""

    def test_phase146b_rejects_conflicting_motion_flags(self) -> None:
        """The strict wrapper must not silently downgrade an explicit motion gate."""
        module = load_smoke_module(
            "phase146b_flags_under_test",
            "ros2/phase146b_lyrical_lidar_deskew_acceptance.py",
        )

        with mock.patch.object(module.phase138u, "main", return_value=0) as delegated:
            with self.assertRaisesRegex(ValueError, "mutually exclusive"):
                module.main(["--require-motion", "--allow-static"])

        delegated.assert_not_called()

    def test_bridge_shell_preflight_reports_missing_foxglove_msgs(self) -> None:
        """The shell bridge sample should explain missing foxglove_msgs."""
        source = read_repo_source("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.sh")

        self.assertIn("if ! ros2 pkg prefix foxglove_msgs", source)
        self.assertIn("foxglove_msgs is not installed", source)
        self.assertIn("cdr-with-encapsulation|cdr-body-only", source)
        self.assertNotIn("cdr-with-encapsulation|raw-cdr", source)

    def test_bridge_powershell_preserves_ros2_error_output(self) -> None:
        """The PowerShell bridge sample should not discard ros2 diagnostics."""
        source = read_repo_source("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.ps1")

        self.assertNotIn("| Out-Null", source)
        self.assertIn("$output", source)
        self.assertIn('[ValidateRange(1, 65535)]', source)
        self.assertIn('[ValidateSet("cdr-with-encapsulation", "cdr-body-only")]', source)

    def test_phase138t_cleanup_uses_configured_camera_frame(self) -> None:
        """Camera raw RViz cleanup should not hardcode os_sensor."""
        source = read_source("ros2/launch_phase138t_camera_raw_rviz2.py")

        self.assertNotIn("--child-frame-id os_sensor", source)
        self.assertNotIn("child_frame_id: 'os_sensor'", source)

    def test_phase138t_cleanup_escapes_powershell_wildcards(self) -> None:
        """Phase138T cleanup should mirror the bounded wildcard-safe Phase138M cleanup."""
        module = load_smoke_module("phase138t_cleanup_under_test", "ros2/launch_phase138t_camera_raw_rviz2.py")
        calls: list[tuple[list[str], dict]] = []

        def capture_run(args, **kwargs):
            """Capture PowerShell cleanup invocation."""
            calls.append((args, kwargs))
            return SimpleNamespace(stdout="", returncode=0)

        with mock.patch.object(module.sys, "platform", "win32"):
            with mock.patch.object(module.subprocess, "run", side_effect=capture_run):
                module.cleanup_stale_processes(Path("C:/project[1]/script.py"), Path("C:/project[1]/view.rviz"), "cam[1]")

        self.assertEqual(1, len(calls))
        self.assertIn("WildcardPattern", calls[0][0][-1])
        self.assertEqual(10.0, calls[0][1].get("timeout"))

    def test_phase138t_source_uses_named_split_package(self) -> None:
        """Phase138T keeps its runtime sections in one named package."""
        package = Path(__file__).resolve().parent / "ros2" / "launch_phase138t_camera_raw_rviz2"
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())

    def test_phase160_source_uses_named_split_package(self) -> None:
        """Phase160 keeps its acceptance sections in one named package."""
        package = Path(__file__).resolve().parent / "ros2" / "phase160_humble_lidar_deskew_acceptance"
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())

    def test_phase160_module_entrypoint_matches_facade_failure_exit_contract(self) -> None:
        """The Phase160 module entrypoint preserves the facade failure contract."""
        import subprocess

        script = r'''
import importlib
import runpy
import sys
from unittest import mock

package_name = sys.argv[1]
case = sys.argv[2]
package = importlib.import_module(package_name)
cli = importlib.import_module(package_name + ".cli")
sys.argv = [package_name]

def effect(*args, **kwargs):
    if case == "return":
        return 3
    if case == "keyboard":
        raise KeyboardInterrupt
    raise RuntimeError("boom")

package_main = mock.Mock(side_effect=effect)
cli_main = mock.Mock(side_effect=effect)
try:
    with mock.patch.object(package, "main", package_main):
        with mock.patch.object(cli, "main", cli_main):
            runpy.run_module(package_name + ".__main__", run_name="__main__")
except BaseException:
    print("PACKAGE_CALLS=" + repr(package_main.call_args_list))
    print("CLI_CALLS=" + repr(cli_main.call_args_list))
    raise
'''
        package_name = "Scripts.smoke.ros2.phase160_humble_lidar_deskew_acceptance"
        for case, expected_return in (("return", 3), ("keyboard", 130), ("runtime", 1)):
            with self.subTest(case=case):
                completed = subprocess.run(
                    [sys.executable, "-c", script, package_name, case],
                    cwd=ROOT,
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=60,
                )
                self.assertEqual(expected_return, completed.returncode)
                self.assertIn("PACKAGE_CALLS=[call([])]", completed.stdout)
                self.assertIn("CLI_CALLS=[]", completed.stdout)
                if case == "runtime":
                    self.assertIn("[phase160-humble] FAIL: boom", completed.stderr)
                    self.assertIn("Check Unity Console", completed.stderr)

    def test_phase138h_t_field_requires_exact_datatype_match(self) -> None:
        """PointCloud2 t-field validation should not accept substring datatype matches."""
        module = load_smoke_module("phase138h_t_field_under_test", "ros2/phase138h_pointcloud2_t_field_ros2_acceptance.py")

        with self.assertRaises(RuntimeError):
            module.validate_fields([{"name": "t", "datatype": "uint32_extra"}], "uint32")


__all__ = ["BridgePhaseTests"]
