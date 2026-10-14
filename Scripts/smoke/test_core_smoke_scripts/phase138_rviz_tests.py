from __future__ import annotations

from .foundation import *


class Phase138RvizTests:
    """Exercise Phase138L/M/U RViz and subscriber contracts"""

    def test_split_ros2_cli_failure_contracts_match_legacy_entrypoints(self) -> None:
        """Package and legacy entrypoints must retain failure and interrupt contracts."""
        modules = (
            ("launch_phase138m_rviz2", "phase138m-rviz"),
            ("launch_phase138t_camera_raw_rviz2", "phase138t-rviz"),
            ("phase138s_imu_native_dds_acceptance", "phase138s-imu-dds"),
            ("phase160_humble_lidar_deskew_acceptance", "phase160-humble"),
        )
        for name, prefix in modules:
            for package_entry in (False, True):
                for exception, expected_exit in (("RuntimeError('synthetic failure')", 1), ("KeyboardInterrupt()", 130), ("SystemExit(2)", 2)):
                    with self.subTest(module=name, package_entry=package_entry, exception=exception):
                        script = "\n".join((
                            "import importlib, runpy, sys",
                            "from pathlib import Path",
                            "from unittest import mock",
                            f"name = {name!r}",
                            "parent = Path('Scripts/smoke/ros2').resolve()",
                            "sys.path.insert(0, str(parent))",
                            f"package_entry = {package_entry!r}",
                            "module_name = 'Scripts.smoke.ros2.' + name if package_entry else name",
                            "module = importlib.import_module(module_name)",
                            "sys.argv = [name]",
                            f"with mock.patch.object(module, 'main', side_effect={exception}):",
                            "    if package_entry:",
                            "        runpy.run_module(module_name, run_name='__main__', alter_sys=True)",
                            "    else:",
                            "        runpy.run_path(str(parent / (name + '.py')), run_name='__main__')",
                        ))
                        result = subprocess.run([sys.executable, "-c", script], cwd=ROOT, capture_output=True, text=True, timeout=20)
                        self.assertEqual(expected_exit, result.returncode, result.stderr)
                        self.assertNotIn("Traceback", result.stderr)
                        self.assertEqual("", result.stdout)
                        if expected_exit == 1:
                            self.assertIn(f"[{prefix}] FAIL: synthetic failure", result.stderr)
                            if name == "phase160_humble_lidar_deskew_acceptance":
                                self.assertIn("Check Unity Console", result.stderr)
                        else:
                            self.assertEqual("", result.stderr)

    def test_phase138l_rviz_config_patch_fails_when_required_topic_tokens_are_missing(self) -> None:
        """RViz2 topic patching should not silently leave the default /points topic."""
        module = load_smoke_module("phase138l_rviz_under_test", "ros2/launch_phase138l_rviz2.py")

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            base_config = root / "base.rviz"
            base_config.write_text("Fixed Frame: map\nValue: /not-points\n", encoding="utf-8")

            with self.assertRaises(RuntimeError):
                module.write_runtime_rviz_config(base_config, root, "/unity/point_cloud2", "map")

    def test_phase138u_rviz_pointcloud2_uses_sensor_data_qos(self) -> None:
        """Phase138U/162 RViz2 PointCloud2 displays should not queue stale high-bandwidth frames."""
        module = load_smoke_module("phase138u_rviz_under_test", "ros2/launch_phase138u_lidar_deskew_rviz2.py")

        with tempfile.TemporaryDirectory() as tmp:
            config = module.write_config(Path(tmp), "/unity/point_cloud2", "/unity/point_cloud2_deskewed", "map")
            text = config.read_text(encoding="utf-8")

        self.assertIn("Reliability Policy: Best Effort", text)
        self.assertIn("Depth: 1", text)
        self.assertIn("Value: /unity/point_cloud2", text)
        self.assertIn("Value: /unity/point_cloud2_deskewed", text)

    def test_phase138m_republisher_closes_parent_log_handle_after_spawn(self) -> None:
        """The parent process should not retain the republisher log handle."""
        module = load_smoke_module("phase138m_log_under_test", "ros2/launch_phase138m_rviz2.py")

        class FakeLog:
            """Writable log stub that records close calls."""

            def __init__(self):
                """Initialize close tracking."""
                self.closed = False

            def write(self, _text):
                """Accept writes from subprocess setup."""
                return None

            def close(self):
                """Record close."""
                self.closed = True

        class FakeProcess:
            """Process stub that stays alive after spawn."""

            pid = 42

            def poll(self):
                """Report a running child."""
                return None

        fake_log = FakeLog()
        with tempfile.TemporaryDirectory() as tmp:
            log_path = Path(tmp) / "republisher.log"
            with mock.patch.object(module.pathlib.Path, "open", return_value=fake_log):
                with mock.patch.object(module.subprocess, "Popen", return_value=FakeProcess()):
                    with mock.patch.object(module.time, "sleep"):
                        module.launch_image_republisher(Path(sys.executable), {}, "/camera/compressed", "/camera", log_path)

        self.assertTrue(fake_log.closed)

    def test_phase138m_cleanup_escapes_powershell_wildcards(self) -> None:
        """PowerShell cleanup should escape wildcard characters in paths before -like matching."""
        module = load_smoke_module("phase138m_cleanup_under_test", "ros2/launch_phase138m_rviz2.py")
        commands: list[str] = []

        def capture_run(args, **_kwargs):
            """Capture the PowerShell command text."""
            commands.append(args[-1])
            return SimpleNamespace(stdout="", returncode=0)

        with mock.patch.object(module.os, "name", "nt"):
            with mock.patch.object(module.subprocess, "run", side_effect=capture_run):
                module.cleanup_stale_processes(Path("C:/project[1]/script.py"), Path("C:/project[1]/view.rviz"), "map", "cam[1]")

        self.assertEqual(1, len(commands))
        self.assertIn("WildcardPattern", commands[0])

    def test_phase138m_source_uses_named_split_package(self) -> None:
        """Phase138M keeps its runtime sections in one named package."""
        package = Path(__file__).resolve().parent / "ros2" / "launch_phase138m_rviz2"
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())

    def test_phase138_inline_subscribers_use_monotonic_deadlines(self) -> None:
        """Inline ROS2 subscriber deadlines should use monotonic time."""
        for relative in (
            "ros2/phase138s_imu_native_dds_acceptance.py",
            "ros2/phase138t_camera_raw_image_dds_acceptance.py",
            "ros2/phase138u_lidar_deskew_rviz2_acceptance.py",
        ):
            with self.subTest(relative=relative):
                source = read_source_with_split_sections(relative)
                self.assertNotIn("deadline = time.time() + spin_seconds", source)
                self.assertNotIn("while time.time() < deadline", source)
                self.assertIn("deadline = time.monotonic() + spin_seconds", source)

    def test_phase138u_rejects_raw_and_deskewed_samples_from_different_scans(self) -> None:
        """Motion evidence must compare PointCloud2 samples with the same stamp."""
        module = load_smoke_module(
            "phase138u_correlation_under_test",
            "ros2/phase138u_lidar_deskew_rviz2_acceptance.py",
        )
        raw = {"topic": "/raw", "stamp": {"sec": 10, "nanosec": 20}}
        deskewed = {"topic": "/deskewed", "stamp": {"sec": 10, "nanosec": 21}}

        with self.assertRaisesRegex(RuntimeError, "stamp mismatch"):
            module.validate_correlated_pair(raw, deskewed)

        deskewed["stamp"]["nanosec"] = 20
        module.validate_correlated_pair(raw, deskewed)

    def test_phase138u_inline_subscriber_remains_syntactically_valid(self) -> None:
        """The generated rclpy program must compile before a live ROS2 run."""
        module = load_smoke_module(
            "phase138u_subscriber_program_under_test",
            "ros2/phase138u_lidar_deskew_rviz2_acceptance.py",
        )
        payload = {
            "raw": {"stamp": {"sec": 1, "nanosec": 2}},
            "deskewed": {"stamp": {"sec": 1, "nanosec": 2}},
        }

        def compile_subscriber(command, **_kwargs):
            """Compile the generated program and return its bounded protocol output."""
            compile(command[2], "<phase138u-subscriber>", "exec")
            return SimpleNamespace(
                returncode=0,
                stdout="PHASE138U_POINTCLOUD2_JSON=" + json.dumps(payload),
            )

        with mock.patch.object(module.subprocess, "run", side_effect=compile_subscriber):
            result = module.subscribe_once_pointcloud2_pair(
                Path(sys.executable),
                {},
                "/raw",
                "/deskewed",
                0.1,
                10,
                0.05,
                2,
            )

        self.assertEqual(payload, result)


__all__ = ["Phase138RvizTests"]
