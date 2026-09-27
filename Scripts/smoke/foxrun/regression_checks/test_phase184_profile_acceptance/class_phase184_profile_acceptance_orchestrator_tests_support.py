from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_support:
    @staticmethod
    def _write_observed_runtime_markers(config):
        """Write current-token Unity evidence used by summary unit tests."""

        case = str(config["case"])
        token = str(config["token"])
        profile_fields = {
            "foxglove-profile": (
                "source=Foxglove targets=Foxglove "
                "publishEncoding=protobuf,json subscribeEncoding=protobuf,json"
            ),
            "multi-target": (
                "source=Ros2Native "
                "targets=Foxglove,Ros2Native,Ros2Bridge "
                "publishEncoding=protobuf subscribeEncoding=protobuf"
            ),
            "degraded-target": (
                "source=None targets=Foxglove,Ros2Bridge "
                "publishEncoding=protobuf subscribeEncoding=not_applicable"
            ),
            "qos-contract": (
                "source=None targets=Ros2Native,Ros2Bridge "
                "publishEncoding=protobuf subscribeEncoding=not_applicable"
            ),
            "stream-640hz": (
                "source=Ros2Native targets=Ros2Native "
                "publishEncoding=protobuf subscribeEncoding=protobuf"
            ),
        }
        lines = [
            "PHASE184G_PROFILE_EVIDENCE "
            f"case={case} token={token} {profile_fields[case]}"
        ]
        if case == "foxglove-profile":
            lines.append(
                "PHASE184G_FOXGLOVE_TARGET_STATUS "
                f"case={case} token={token} status=Ready "
                "succeeded=Foxglove failed=None topics=2"
            )
        elif case == "multi-target":
            lines.append(
                "PHASE184G_MULTI_TARGET_STATUS "
                f"case={case} token={token} status=Ready "
                "succeeded=Foxglove,Ros2Native,Ros2Bridge failed=None "
                "bridgeRuntimeFailures=0"
            )
        elif case == "qos-contract":
            lines.extend(
                "PHASE184G_QOS_TARGET_STATUS "
                f"case={case} token={token} topic={topic} status=Ready "
                "succeeded=Ros2Native,Ros2Bridge failed=None"
                for topic in config["topics"]
            )
        elif case == "stream-640hz":
            lines.append(
                "PHASE184G_STREAM_SUBSCRIPTION_STATUS "
                f"case={case} token={token} state=Receiving received=792 "
                "copyFailed=0 staleCallbacks=0 rejectedAfterStop=0"
            )
        log_path = pathlib.Path(str(config["unityLog"]))
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    @staticmethod
    def _write_reusable_runtime_selection(
        repository: pathlib.Path,
        *,
        distro: str = "jazzy",
        default_rmw: str = "rmw_fastrtps_cpp",
    ) -> tuple[str, str]:
        """Write reusable runtime selection."""

        runtime_package = (
            f"dev.unity2foxglove.ros2forunity.runtime.{distro}.win64"
        )
        addon_package = (
            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
            f"{distro}.win64"
        )
        runtime_reference = f"file:../../Packages/{runtime_package}"
        addon_reference = f"file:../../Packages/{addon_package}"
        packages = repository / "Unity2Foxglove" / "Packages"
        project_settings = repository / "Unity2Foxglove" / "ProjectSettings"
        runtime_root = repository / "Packages" / runtime_package
        addon_root = repository / "Packages" / addon_package
        for directory in (
            packages,
            project_settings,
            runtime_root / "RuntimeSupport",
            addon_root,
        ):
            directory.mkdir(parents=True, exist_ok=True)
        (packages / "manifest.json").write_text(
            json.dumps(
                {
                    "dependencies": {
                        runtime_package: runtime_reference,
                        addon_package: addon_reference,
                    }
                }
            ),
            encoding="utf-8",
        )
        (packages / "packages-lock.json").write_text(
            json.dumps(
                {
                    "dependencies": {
                        runtime_package: {
                            "version": runtime_reference,
                            "depth": 0,
                            "source": "local",
                            "dependencies": {},
                        },
                        addon_package: {
                            "version": addon_reference,
                            "depth": 0,
                            "source": "local",
                            "dependencies": {
                                runtime_package: "0.1.0-preview.1",
                            },
                        },
                    }
                }
            ),
            encoding="utf-8",
        )
        (runtime_root / "package.json").write_text(
            json.dumps({"name": runtime_package, "version": "0.1.0-preview.1"}),
            encoding="utf-8",
        )
        (runtime_root / "RuntimeSupport" / "runtime-manifest.json").write_text(
            json.dumps(
                {
                    "packageName": runtime_package,
                    "rosDistro": distro,
                    "platform": "win64",
                    "architecture": "x86_64",
                    "rmwImplementation": default_rmw,
                }
            ),
            encoding="utf-8",
        )
        (addon_root / "package.json").write_text(
            json.dumps(
                {
                    "name": addon_package,
                    "version": "0.1.0-preview.1",
                    "dependencies": {
                        runtime_package: "0.1.0-preview.1",
                    },
                    "unity2foxgloveFoxRunCustomTypesupportAddOn": True,
                }
            ),
            encoding="utf-8",
        )
        (project_settings / "ProjectSettings.asset").write_text(
            "  applicationIdentifier:\n"
            "    Standalone: dev.unity2foxglove.demo\n"
            "  scriptingDefineSymbols:\n"
            "    Standalone: UNITY2FOXGLOVE_ROS2_FOR_UNITY;"
            "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES\n",
            encoding="utf-8",
        )
        return runtime_package, addon_package
    def test_windows_domain_id_stays_below_the_dynamic_port_collision_range(self):
        """Verify windows domain id stays below the dynamic port collision range."""

        module = load_module()

        self.assertEqual(0, module.choose_domain_id(0))
        self.assertEqual(166, module.choose_domain_id(166))
        for unsafe in (-1, 167, 202, 233):
            with self.subTest(unsafe=unsafe):
                with self.assertRaisesRegex(
                    module.AcceptanceFailure,
                    r"FAIL_PREFLIGHT.*0\.\.166",
                ):
                    module.choose_domain_id(unsafe)

        with mock.patch.object(module.secrets, "randbelow", return_value=0) as random:
            self.assertEqual(64, module.choose_domain_id(None))
            random.assert_called_once_with(96)
        with mock.patch.object(module.secrets, "randbelow", return_value=95):
            self.assertEqual(159, module.choose_domain_id(None))
    def test_manual_domain_defaults_to_hub_domain_zero_and_rejects_nonzero_override(self):
        """A user-owned Hub Editor cannot inherit the helper's isolated domain."""

        module = load_module()

        self.assertEqual(0, module.choose_parent_domain_id(None, "manual"))
        self.assertEqual(0, module.choose_parent_domain_id(0, "manual"))
        with self.assertRaisesRegex(
            module.AcceptanceFailure,
            r"FAIL_PREFLIGHT.*manual.*domain 0",
        ):
            module.choose_parent_domain_id(68, "manual")

        with mock.patch.object(module.secrets, "randbelow", return_value=7) as random:
            self.assertEqual(71, module.choose_parent_domain_id(None, "batch"))
            random.assert_called_once_with(96)
    def test_current_unity_runtime_is_reused_only_for_an_exact_default_selection(self):
        """Verify current unity runtime is reused only for an exact default selection."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="runtime-reuse-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            runtime_package, addon_package = self._write_reusable_runtime_selection(
                repository
            )

            evidence = module._current_unity_runtime_selection_evidence(
                repository,
                "jazzy",
                "rmw_fastrtps_cpp",
            )
            self.assertEqual(
                {
                    "mode": "reused",
                    "runtimePackage": runtime_package,
                    "typesupportPackage": addon_package,
                    "rosDistro": "jazzy",
                    "rmwImplementation": "rmw_fastrtps_cpp",
                },
                evidence,
            )
            self.assertIsNone(
                module._current_unity_runtime_selection_evidence(
                    repository,
                    "jazzy",
                    "rmw_zenoh_cpp",
                )
            )

            lock_path = repository / "Unity2Foxglove" / "Packages" / "packages-lock.json"
            lock_document = json.loads(lock_path.read_text(encoding="utf-8"))
            lock_document["dependencies"][runtime_package]["depth"] = 1
            lock_path.write_text(json.dumps(lock_document), encoding="utf-8")
            self.assertIsNone(
                module._current_unity_runtime_selection_evidence(
                    repository,
                    "jazzy",
                    "rmw_fastrtps_cpp",
                )
            )
    def test_runtime_selection_reuse_skips_unity_resolve_and_persists_evidence(self):
        """Verify runtime selection reuse skips unity resolve and persists evidence."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="runtime-skip-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            self._write_reusable_runtime_selection(repository)
            output = repository / "build" / "phase184" / "acceptance" / "run"
            output.mkdir(parents=True, exist_ok=True)
            peer = mock.Mock()

            with mock.patch.object(module, "_run_logged_preflight") as run:
                module._select_unity_runtime(
                    peer=peer,
                    editor=pathlib.Path(r"C:\Unity.exe"),
                    repository=repository,
                    output=output,
                    distro="jazzy",
                    rmw="rmw_fastrtps_cpp",
                    job=None,
                )

            run.assert_not_called()
            peer.build_runtime_selection_batch_command.assert_not_called()
            selection_log = (output / "runtime-selection.log").read_text(
                encoding="utf-8"
            )
            self.assertIn("PHASE184G_RUNTIME_SELECTION_REUSED", selection_log)
            self.assertIn("rmw=rmw_fastrtps_cpp", selection_log)
    def test_nondefault_rmw_falls_back_to_the_validated_unity_selector(self):
        """Verify nondefault rmw falls back to the validated unity selector."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="runtime-select-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            self._write_reusable_runtime_selection(repository)
            output = repository / "build" / "phase184" / "acceptance" / "run"
            output.mkdir(parents=True, exist_ok=True)
            peer = mock.Mock()
            peer._RUNTIME_SELECTION_READY_MARKER = "PHASE181_RUNTIME_SELECTION_READY"
            peer.build_runtime_selection_batch_command.return_value = [
                r"C:\Unity.exe",
                "-batchmode",
            ]
            peer.ros2env.sanitized_subprocess_env.return_value = {}

            with mock.patch.object(module, "_run_logged_preflight") as run:
                with mock.patch.object(
                    module,
                    "read_log_lines",
                    return_value=["PHASE181_RUNTIME_SELECTION_READY"],
                ):
                    module._select_unity_runtime(
                        peer=peer,
                        editor=pathlib.Path(r"C:\Unity.exe"),
                        repository=repository,
                        output=output,
                        distro="jazzy",
                        rmw="rmw_zenoh_cpp",
                        job=None,
                    )

            run.assert_called_once()
            peer.build_runtime_selection_batch_command.assert_called_once()
    def test_cli_has_exact_parent_and_worker_modes(self):
        """Verify CLI has exact parent and worker modes."""

        module = load_module()

        parent = module.parse_args(
            [
                "--case",
                "multi-target",
                "--profile",
                "jazzy-fastrtps",
                "--unity-editor",
                r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe",
            ]
        )
        module.validate_arguments(parent)
        self.assertEqual("batch", parent.execution_mode)

        worker = module.parse_args(
            [
                "--worker",
                "ros2-peer",
                "--run-config",
                str(ROOT / "build" / "phase184" / "acceptance" / "run-config.json"),
            ]
        )
        module.validate_arguments(worker)
        self.assertEqual("worker", worker.execution_mode)

        contradictory = module.parse_args(
            [
                "--worker",
                "foxglove-client",
                "--run-config",
                str(ROOT / "build" / "phase184" / "acceptance" / "run-config.json"),
                "--case",
                "foxglove-profile",
            ]
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            module.validate_arguments(contradictory)

        missing_profile = module.parse_args(
            [
                "--case",
                "multi-target",
                "--unity-editor",
                r"C:\Unity.exe",
            ]
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_RUNTIME_SELECTION"):
            module.validate_arguments(missing_profile)


__all__ = [name for name in globals() if not name.startswith("__")]
