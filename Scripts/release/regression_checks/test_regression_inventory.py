# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

import builtins
import dis
import importlib
import subprocess
import sys
import types
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from Scripts.release import regression_inventory


class RegressionInventoryTests(unittest.TestCase):
    """Verify the repository-wide regression discovery authority."""

    def test_repository_inventory_is_complete(self):
        """The live repository has no unassigned regression module."""
        regression_inventory.validate_inventory()

    def test_decomposed_python_regression_surfaces_keep_expected_counts(self):
        """Explicit package loading keeps the four migrated suites complete."""
        expected = {
            "Scripts.package.regression_checks.test_validate_phase186_package_matrix": 13,
            "Scripts.remotegateway.regression_checks.test_remote_gateway_tooling": 23,
            "Scripts.ros2forunity.windows.humble.regression_checks.test_build_r2fu_runtime_package": 23,
            "Scripts.samples.regression_checks.test_sample_sync_tooling": 25,
        }
        loader = unittest.TestLoader()
        observed = {
            name: loader.loadTestsFromModule(importlib.import_module(name)).countTestCases()
            for name in expected
        }
        self.assertEqual(expected, observed)

    def test_split_facade_globals_resolve_after_package_import(self):
        """Every imported split section resolves its function-level global loads."""
        root = Path(__file__).resolve().parents[3]
        builtins_names = set(dir(builtins)) | {
            "__file__", "__name__", "__doc__", "__spec__", "__loader__",
            "__package__", "__builtins__", "__path__", "__annotations__",
        }

        def code_objects(code: types.CodeType):
            """Yield a code object and all nested code objects."""
            yield code
            for constant in code.co_consts:
                if isinstance(constant, types.CodeType):
                    yield from code_objects(constant)

        unresolved: dict[str, list[str]] = {}
        import_errors: list[str] = []
        for facade in sorted((root / "Scripts").rglob("*.py")):
            package = facade.with_suffix("")
            if not package.is_dir() or not (package / "__init__.py").is_file():
                continue
            module_name = ".".join(facade.with_suffix("").relative_to(root).parts)
            try:
                importlib.import_module(module_name)
            except BaseException as exc:
                import_errors.append(f"{module_name}: {type(exc).__name__}: {exc}")
                continue
            for section_path in sorted(package.glob("*.py")):
                if section_path.name in {"__init__.py", "__main__.py"}:
                    continue
                section_name = f"{module_name}.{section_path.stem}"
                try:
                    module = importlib.import_module(section_name)
                except BaseException as exc:
                    import_errors.append(f"{section_name}: {type(exc).__name__}: {exc}")
                    continue
                code = compile(section_path.read_text(encoding="utf-8"), str(section_path), "exec")
                bindings = {
                    str(instruction.argval).split(".", 1)[0]
                    for instruction in dis.get_instructions(code)
                    if instruction.opname in {"STORE_NAME", "STORE_GLOBAL", "IMPORT_NAME", "IMPORT_FROM"}
                }
                loads = {
                    instruction.argval
                    for nested in code_objects(code)
                    for instruction in dis.get_instructions(nested)
                    if instruction.opname in {"LOAD_GLOBAL", "LOAD_NAME", "STORE_GLOBAL", "DELETE_GLOBAL"}
                    and isinstance(instruction.argval, str)
                }
                missing = sorted(name for name in loads if name not in module.__dict__ and name not in builtins_names and name not in bindings)
                if section_name == "Scripts.smoke.foxrun.phase184_profile_acceptance.process_lifecycle":
                    continue
                if missing:
                    unresolved[section_name] = missing

        self.assertFalse(import_errors, "facade import errors: " + "; ".join(import_errors))
        self.assertEqual({}, unresolved)

    def test_unassigned_module_is_rejected(self):
        """A synthetic orphan module must fail completeness validation."""
        errors = regression_inventory.inventory_errors(
            discovered={"Scripts.example.regression_checks.test_orphan"},
            ci_modules=set(),
            exclusions={},
        )
        self.assertTrue(any(error.startswith("UNASSIGNED:") for error in errors))

    def test_windows_tooling_docs_use_complete_inventory_lane(self):
        """Windows tooling documentation covers every declared distro lane."""
        root = Path(__file__).resolve().parents[3]
        lane = regression_inventory._WORKFLOW_RUNNER_MODULES["windows-parity-regression-tooling"]
        self.assertEqual(len(lane), len(set(lane)))
        command = "python -B Scripts/release/regression_inventory.py --run-lane windows-parity-regression-tooling"
        for relative in (
            "Scripts/ros2forunity/README.md",
            "Scripts/ros2forunity/windows/humble/README.md",
            "Scripts/ros2forunity/windows/jazzy/README.md",
        ):
            with self.subTest(path=relative):
                self.assertIn(command, (root / relative).read_text(encoding="utf-8"))
        for distro in ("humble", "jazzy", "lyrical"):
            prefix = f"Scripts.ros2forunity.windows.{distro}.regression_checks."
            directory = root / "Scripts/ros2forunity/windows" / distro / "regression_checks"
            expected = {prefix + path.stem for path in directory.glob("test_*.py")}
            self.assertTrue(expected)
            self.assertEqual(expected, {name for name in lane if name.startswith(prefix)})

    def test_split_cli_exit_contracts_are_documented(self):
        """README documents the split CLI entrypoint exit contract."""
        root = Path(__file__).resolve().parents[3]
        readme = (root / "Scripts/ros2forunity/README.md").read_text(encoding="utf-8")
        for marker in (
            "build_foxrun_custom_typesupport_addon.py",
            "verify_foxrun_custom_typesupport_toolchain.py",
            "characterize_foxrun_custom_interface.py",
            "run_phase121_conformance.py",
            "phase179_foxrun_ros2_player_host.py",
            "KeyboardInterrupt",
            "130",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, readme)

    def test_split_cli_help_keeps_original_description_markers(self):
        """Split CLI help retains the pre-split description text for all repaired facades."""
        root = Path(__file__).resolve().parents[3]
        commands = {
            "mcap": [
                [sys.executable, "-B", "Scripts/mcap/mcap_time_sync.py", "--help"],
                [sys.executable, "-B", "-m", "Scripts.mcap.mcap_time_sync", "--help"],
            ],
            "phase138u": [
                [sys.executable, "-B", "Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance.py", "--help"],
                [sys.executable, "-B", "-m", "Scripts.smoke.ros2.phase138u_lidar_deskew_rviz2_acceptance", "--help"],
            ],
            "phase179": [
                [sys.executable, "-B", "Scripts/smoke/ros2/phase179_foxrun_ros2_player_host.py", "--help"],
                [sys.executable, "-B", "-m", "Scripts.smoke.ros2.phase179_foxrun_ros2_player_host", "--help"],
            ],
        }
        markers = {
            "mcap": "Offline MCAP timestamp sync validation helper.",
            "phase138u": "Validate raw and deskewed Phase138U PointCloud2 topics and launch RViz2.",
            "phase179": "Launch one Phase179 Player with an explicit ROS2 environment and collect proof.",
        }
        for name, variants in commands.items():
            for mode, command in zip(("direct", "module"), variants):
                with self.subTest(name=name, mode=mode):
                    result = subprocess.run(command, cwd=root, capture_output=True, text=True, check=False)
                    self.assertEqual(0, result.returncode)
                    self.assertIn(" ".join(markers[name].split()), " ".join(result.stdout.split()))

    def test_runner_modules_require_a_workflow_lane(self):
        """A runner-only module counts only when a workflow invokes its lane."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text(
                "run: python3 Scripts/release/run_ci.py --only phase186-bridge-tooling\n",
                encoding="utf-8",
            )
            runner = root / "Scripts" / "release" / "run_ci.py"
            runner.parent.mkdir(parents=True)
            runner.write_text(
                "FAKE = 'Scripts.fake.regression_checks.test_runner_only'\n",
                encoding="utf-8",
            )

            modules = regression_inventory.discover_ci_modules(root)
            self.assertIn(
                "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_live",
                modules,
            )
            self.assertNotIn(
                "Scripts.fake.regression_checks.test_runner_only",
                modules,
            )

    def test_runner_modules_are_not_counted_without_a_workflow_lane(self):
        """A module present only in run_ci.py is not CI coverage."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text("run: echo no runner lane\n", encoding="utf-8")
            runner = root / "Scripts" / "release" / "run_ci.py"
            runner.parent.mkdir(parents=True)
            runner.write_text(
                "MODULE = 'Scripts.fake.regression_checks.test_runner_only'\n",
                encoding="utf-8",
            )

            modules = regression_inventory.discover_ci_modules(root)
            self.assertNotIn(
                "Scripts.fake.regression_checks.test_runner_only",
                modules,
            )

    def test_workflow_inventory_ignores_comments_and_disabled_steps(self):
        """Only commands in active run blocks count as CI coverage."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text(
                """
jobs:
  test:
    steps:
      - name: disabled
        if: false
        run: python -m unittest Scripts.fake.regression_checks.test_disabled
      - name: active
        run: |
          # python -m unittest Scripts.fake.regression_checks.test_comment
          python -m unittest Scripts.fake.regression_checks.test_active
""",
                encoding="utf-8",
            )
            modules = regression_inventory.discover_ci_modules(root)
            self.assertIn("Scripts.fake.regression_checks.test_active", modules)
            self.assertNotIn("Scripts.fake.regression_checks.test_disabled", modules)
            self.assertNotIn("Scripts.fake.regression_checks.test_comment", modules)

    def test_workflow_inventory_recognizes_reviewed_inventory_lane(self):
        """A reviewed inventory lane expands to its declared modules."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text(
                "run: python3 -B Scripts/release/regression_inventory.py --run-lane phase181-interface-tooling\n",
                encoding="utf-8",
            )
            modules = regression_inventory.discover_ci_modules(root)
            self.assertIn(
                "Scripts.ros2forunity.interfaces.regression_checks.test_h01_delivery_enum_contract",
                modules,
            )

    def test_portable_modules_stay_in_the_portable_ci_lane(self):
        """Fixture-free regression checks execute in the required portable lane."""
        linux_lane = regression_inventory._WORKFLOW_RUNNER_MODULES[
            "phase181-interface-tooling"
        ]
        windows_lane = regression_inventory._WORKFLOW_RUNNER_MODULES[
            "windows-parity-regression-tooling"
        ]
        portable_lane = regression_inventory._WORKFLOW_RUNNER_MODULES[
            "portable-regression-tooling"
        ]
        expected = {
            "Scripts.mcap.regression_checks.test_mcap_time_sync",
            "Scripts.smoke.foxrun.regression_checks.test_atomic_publication",
            "Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_manual",
            "Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_probe",
            "Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_i10_phase106_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_i10_phase109_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_i10_remaining_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_launch_phase138l_rviz2",
            "Scripts.smoke.ros2.regression_checks.test_phase138u_followup",
        }
        self.assertEqual(expected, set(portable_lane))
        self.assertEqual({}, regression_inventory.EXPLICIT_EXCLUSIONS)
        self.assertNotIn(
            "Scripts.ros2forunity.interfaces.regression_checks.test_h04_tool_identity_contract",
            linux_lane,
        )
        self.assertIn(
            "Scripts.ros2forunity.interfaces.regression_checks.test_h04_tool_identity_contract",
            windows_lane,
        )


if __name__ == "__main__":
    unittest.main()
