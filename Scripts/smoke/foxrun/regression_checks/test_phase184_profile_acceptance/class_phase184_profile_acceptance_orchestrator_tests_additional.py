from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_edge_cases import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_additional:
    def test_unity_routes_emit_native_gate_before_full_bridge_readiness(self):
        """Verify unity routes emit native gate before full bridge readiness."""

        source = (
            ROOT
            / "Unity2Foxglove"
            / "Assets"
            / "Scripts"
            / "ManualAcceptance"
            / "Phase184FoxRunProfileAcceptance.cs"
        ).read_text(encoding="utf-8")
        multi = source[
            source.index("public sealed partial class Phase184MultiTargetRoute") :
            source.index("public sealed partial class Phase184DegradedTargetRoute")
        ]
        qos = source[
            source.index("public sealed partial class Phase184QosContractRoute") :
            source.index("public sealed partial class Phase184StreamRoute")
        ]

        marker = '"PHASE184G_NATIVE_READY_FOR_BRIDGE"'
        self.assertIn(marker, multi)
        self.assertIn(marker, qos)
        self.assertLess(
            multi.index(marker),
            multi.index("if (!_initialArmed && allProvidersActive)"),
        )
        self.assertLess(qos.index(marker), qos.index("if (_readyContracts == 3)"))
    def test_multi_target_peer_starts_delivery_window_after_unity_arms_local_token(self):
        """Verify multi target peer starts delivery window after unity arms local token."""

        source = read_orchestrator_source()
        multi_peer = source[
            source.index("def _run_multi_target_peer(") : source.index(
                "def _run_qos_peer(", source.index("def _run_multi_target_peer(")
            )
        ]

        armed_marker = multi_peer.index(
            'wait_for_log_marker(config, "PHASE184G_MULTI_LOCAL_ARMED"'
        )
        delivery_window = multi_peer.index(
            "_spin_until(",
            multi_peer.index("def local_one_ready()"),
        )
        self.assertLess(armed_marker, delivery_window)
    def test_multi_target_foxglove_starts_delivery_window_after_unity_arms_local_token(
        self,
    ):
        """Verify multi target foxglove starts delivery window after unity arms local token."""

        source = read_orchestrator_source()
        client_start = source.index("async def _run_foxglove_client_async(")
        multi_start = source.index('if case == "multi-target":', client_start)
        multi_client = source[
            multi_start : source.index('if case == "degraded-target":', multi_start)
        ]

        armed_marker = multi_client.index(
            '"PHASE184G_MULTI_LOCAL_ARMED"'
        )
        delivery_window = multi_client.index("_receive_foxglove_stages(")
        self.assertLess(armed_marker, delivery_window)
    def test_windows_bridge_build_dependencies_are_selected_from_ros_prefix(self):
        """Verify windows bridge build dependencies are selected from ROS prefix."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="bridge-deps-", dir=TEST_ROOT) as raw:
            ros_root = pathlib.Path(raw) / "ros2_jazzy"
            library = ros_root / ".pixi" / "envs" / "default" / "Library"
            required = (
                library / "include" / "openssl" / "opensslv.h",
                library / "lib" / "libcrypto.lib",
                library / "lib" / "libssl.lib",
                library / "lib" / "cmake" / "tinyxml2" / "tinyxml2-config.cmake",
                library
                / "share"
                / "cmake"
                / "nlohmann_json"
                / "nlohmann_jsonConfig.cmake",
            )
            for path in required:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("fixture\n", encoding="utf-8")

            environment = module.prepare_windows_bridge_build_environment(
                {"CMAKE_PREFIX_PATH": str(ros_root)},
                ros_root,
            )
            self.assertEqual(str(library), environment["OPENSSL_ROOT_DIR"])
            self.assertEqual(
                str(library / "share" / "cmake" / "nlohmann_json"),
                environment["nlohmann_json_DIR"],
            )
            self.assertEqual(
                str(library / "lib" / "cmake" / "tinyxml2"),
                environment["tinyxml2_DIR"],
            )
            self.assertEqual(
                [str(library), str(ros_root)],
                environment["CMAKE_PREFIX_PATH"].split(os.pathsep),
            )

            required[-1].unlink()
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_BUILD"):
                module.prepare_windows_bridge_build_environment(
                    {"CMAKE_PREFIX_PATH": str(ros_root)},
                    ros_root,
                )
    def test_bridge_build_cache_is_stable_exact_and_owned(self):
        """Verify bridge build cache is stable exact and owned."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="bridge-cache-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            cache_root = root / "cache"
            toolchain = mock.Mock()
            toolchain.ros2_root = root / "ros"
            toolchain.python_executable = root / "python.exe"
            toolchain.colcon_executable = root / "colcon.exe"
            command = ["colcon.exe", "build", "--merge-install"]
            environment = {
                "VCToolsVersion": "14.51",
                "WindowsSDKVersion": "10.0.26100.0",
                "CMAKE_PREFIX_PATH": str(root / "ros" / "Library"),
            }
            key = module.bridge_build_cache_key(
                ROOT,
                "jazzy-fastrtps",
                "jazzy",
                "rmw_fastrtps_cpp",
                toolchain,
                command,
                environment,
            )
            self.assertRegex(key, r"\A[0-9a-f]{64}\Z")
            self.assertNotEqual(
                key,
                module.bridge_build_cache_key(
                    ROOT,
                    "jazzy-fastrtps",
                    "jazzy",
                    "rmw_fastrtps_cpp",
                    toolchain,
                    [*command, "--changed"],
                    environment,
                ),
            )

            overlay, reused = module.prepare_bridge_build_workspace(
                cache_root,
                "jazzy-fastrtps",
                key,
            )
            self.assertFalse(reused)
            install = overlay / "install"
            (install / "lib" / "unity2foxglove_ros2_bridge").mkdir(
                parents=True,
                exist_ok=True,
            )
            (install / "share" / "unity2foxglove_ros2_bridge").mkdir(
                parents=True,
                exist_ok=True,
            )
            (install / "local_setup.bat").write_text("@echo off\n", encoding="utf-8")
            (
                install
                / "share"
                / "unity2foxglove_ros2_bridge"
                / "package.xml"
            ).write_text("<package/>\n", encoding="utf-8")
            (
                install
                / "lib"
                / "unity2foxglove_ros2_bridge"
                / "unity2foxglove_ros2_bridge.exe"
            ).write_bytes(b"bridge")
            module.seal_bridge_build_workspace(
                overlay,
                "jazzy-fastrtps",
                key,
            )

            cached, reused = module.prepare_bridge_build_workspace(
                cache_root,
                "jazzy-fastrtps",
                key,
            )
            self.assertTrue(reused)
            self.assertEqual(overlay, cached)

            unowned = cache_root / "lyrical-zenoh" / "bridge-overlay"
            unowned.mkdir(parents=True)
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_BUILD"):
                module.prepare_bridge_build_workspace(
                    cache_root,
                    "lyrical-zenoh",
                    "b" * 64,
                )
    def test_existing_acceptance_scene_still_runs_the_cold_start_preflight(self):
        """Verify existing acceptance scene still runs the cold start preflight."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="scene-preflight-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            output = repository / "build" / "phase184" / "acceptance" / "run"
            scene = (
                repository
                / "Unity2Foxglove"
                / "Assets"
                / "Scenes"
                / "ManualAcceptance"
                / "Phase184FoxRunProfileAcceptance.unity"
            )
            scene.parent.mkdir(parents=True, exist_ok=True)
            scene.write_text("tracked scene\n", encoding="utf-8")
            output.mkdir(parents=True, exist_ok=True)

            with mock.patch.object(module, "_run_logged_preflight") as run:
                with mock.patch.object(
                    module,
                    "read_log_lines",
                    return_value=["PHASE184G_SCENE_BUILDER_PASS"],
                ):
                    actual = module._ensure_acceptance_scene(
                        pathlib.Path(r"C:\Unity.exe"),
                        repository,
                        output,
                        job=None,
                    )

            self.assertEqual(scene, actual)
            run.assert_called_once()
            command = run.call_args.args[0]
            self.assertIn(
                "Unity2Foxglove.Phase184FoxRunProfileAcceptanceBuilder.CreateOrRefreshAcceptanceScene",
                command,
            )
    def test_manual_play_prompt_is_helper_selected_and_single_play(self):
        """Manual helpers must own the selected case and exactly one Play session."""

        prompt = getattr(load_module(), "manual_play_prompt", lambda _case: "")(
            "multi-target"
        )

        self.assertIn("helper-selected case multi-target", prompt)
        self.assertIn("exactly one Play session", prompt)
        self.assertNotIn("select a route", prompt.lower())
        self.assertNotIn("wait for endpoint readiness", prompt.lower())
    def test_generated_scene_routes_are_read_only_inactive_and_helper_owned(self):
        """The generated route assets must stay read-only until the controller arms one."""

        scene = (
            ROOT
            / "Unity2Foxglove"
            / "Assets"
            / "Scenes"
            / "ManualAcceptance"
            / "Phase184FoxRunProfileAcceptance.unity"
        )
        contents = scene.read_text(encoding="utf-8")
        object_blocks = re.findall(
            r"(?ms)^--- !u!1 &(\d+)\r?\n(.*?)(?=^--- !u!|\Z)",
            contents,
        )
        component_blocks = dict(
            re.findall(
                r"(?ms)^--- !u!114 &(\d+)\r?\n(.*?)(?=^--- !u!|\Z)",
                contents,
            )
        )
        transform_blocks = dict(
            re.findall(
                r"(?ms)^--- !u!4 &(\d+)\r?\n(.*?)(?=^--- !u!|\Z)",
                contents,
            )
        )
        for name, route_guid in {
            "Helper-owned Route - Foxglove Profile": "983acb559504477ebd0c4d69a7d1edbe",
            "Helper-owned Route - Multi Target": "7b052fef51264defb3b5934d0271da7a",
            "Helper-owned Route - Degraded Target": "7f1320889ffd4aae8580cf5507278c6a",
            "Helper-owned Route - QoS Contract": "ae2bf84a4ef244ccb4185841a415279b",
            "Helper-owned Route - Stream 640 Hz": "f08839578006415a9d94a9ce4ef663a9",
        }.items():
            with self.subTest(name=name):
                matching = [
                    (file_id, block)
                    for file_id, block in object_blocks
                    if f"m_Name: {name}" in block
                ]
                self.assertEqual(1, len(matching))
                file_id, block = matching[0]
                self.assertRegex(block, r"(?m)^  m_ObjectHideFlags: 8$")
                self.assertRegex(block, rf"(?m)^  m_Name: {re.escape(name)}$")
                self.assertRegex(block, r"(?m)^  m_IsActive: 0$")
                component_ids = re.findall(
                    r"(?m)^  - component: \{fileID: (\d+)\}$",
                    block,
                )
                self.assertEqual(2, len(component_ids))
                route_components = [
                    component_blocks[component_id]
                    for component_id in component_ids
                    if component_id in component_blocks
                    and f"m_GameObject: {{fileID: {file_id}}}" in component_blocks[component_id]
                ]
                self.assertEqual(1, len(route_components))
                self.assertRegex(route_components[0], r"(?m)^  m_ObjectHideFlags: 8$")
                self.assertRegex(
                    route_components[0],
                    rf"(?m)^  m_Script: \{{fileID: 11500000, guid: {route_guid}, type: 3\}}$",
                )
                transforms = [
                    transform_blocks[component_id]
                    for component_id in component_ids
                    if component_id in transform_blocks
                    and f"m_GameObject: {{fileID: {file_id}}}" in transform_blocks[component_id]
                ]
                self.assertEqual(1, len(transforms))
                self.assertRegex(transforms[0], r"(?m)^  m_ObjectHideFlags: 8$")
    def test_workers_wait_for_correlated_unity_context_in_batch_and_manual_modes(self):
        """Cold Batch imports cannot consume finite actor deadlines before Play."""

        module = load_module()
        for execution_mode in ("batch", "manual"):
            with self.subTest(execution_mode=execution_mode):
                config = {
                    "executionMode": execution_mode,
                    "case": "foxglove-profile",
                    "token": "p184g_A1b2C3d4E5f6",
                }
                with mock.patch.object(module, "wait_for_log_marker") as wait:
                    module._wait_for_unity_context(config)

                wait.assert_called_once_with(
                    config,
                    "PHASE184G_CONTEXT_READY",
                    900.0,
                )


__all__ = [name for name in globals() if not name.startswith("__")]
