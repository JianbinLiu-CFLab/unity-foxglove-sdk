#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for named Phase179 ROS2 interoperability matrix profiles.

"""Keep the four Phase179 interoperability rows explicit and non-interchangeable."""

from __future__ import annotations

import contextlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[4]
PROFILE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_foxrun_ros2_matrix_profiles.py"
WINDOWS_ENDPOINT_PROBE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_windows_rclpy_endpoint_probe.py"


def load_profile_module():
    """Load the profile engine without requiring a ROS2 installation."""

    smoke_dir = str(PROFILE_PATH.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    spec = importlib.util.spec_from_file_location("phase179_foxrun_ros2_matrix_profiles", PROFILE_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class Phase179FoxRunRos2MatrixProfileTests(unittest.TestCase):
    """Lock every certified distro/RMW row to one visible operator-facing profile."""

    def setUp(self) -> None:
        """Load a fresh profile engine for each isolated contract check."""

        self.profiles = load_profile_module()

    def test_profiles_expose_exactly_the_four_certified_rows(self) -> None:
        """No generic default may conceal a distro or transport selection."""

        actual = {
            profile_id: (profile.distro, profile.rmw)
            for profile_id, profile in self.profiles.PROFILES.items()
        }
        self.assertEqual(
            {
                "humble-fastrtps": ("humble", "rmw_fastrtps_cpp"),
                "jazzy-fastrtps": ("jazzy", "rmw_fastrtps_cpp"),
                "lyrical-fastrtps": ("lyrical", "rmw_fastrtps_cpp"),
                "lyrical-zenoh": ("lyrical", "rmw_zenoh_cpp"),
            },
            actual,
        )
        for profile in self.profiles.PROFILES.values():
            self.assertEqual("/foxrun/phase179", profile.topic_prefix)
            self.assertEqual(("string", "twist", "joy"), profile.message_set)

    def test_linux_and_player_argv_are_pinned_to_the_selected_profile(self) -> None:
        """The profile engine must not let a hand-entered child argument change a row."""

        humble = self.profiles.PROFILES["humble-fastrtps"]
        linux_summary = Path("build/phase179/humble-fastrtps/linux-editor.json")
        linux = self.profiles.build_linux_peer_argv(
            humble,
            surface="editor",
            token="row-token",
            domain_id=17,
            discovery_range="SUBNET",
            summary_json=linux_summary,
        )
        player = self.profiles.build_windows_player_argv(
            humble,
            player=Path(r"C:\build\Phase179.exe"),
            player_log=Path("build/phase179/humble-fastrtps/player.log"),
            token="row-token",
            domain_id=17,
            discovery_range="SUBNET",
            summary_json=Path("build/phase179/humble-fastrtps/windows-player.json"),
        )

        self.assertEqual(
            [
                "--distro",
                "humble",
                "--rmw",
                "rmw_fastrtps_cpp",
                "--domain-id",
                "17",
                "--discovery-range",
                "SUBNET",
                "--topic-prefix",
                "/foxrun/phase179",
                "--message-set",
                "string,twist,joy",
                "--token",
                "row-token",
                "--profile-id",
                "humble-fastrtps",
                "--surface",
                "editor",
                "--summary-json",
                str(linux_summary),
            ],
            linux,
        )
        self.assertIn("--distro", player)
        self.assertEqual("humble", player[player.index("--distro") + 1])
        self.assertEqual("rmw_fastrtps_cpp", player[player.index("--rmw") + 1])
        self.assertEqual("string,twist,joy", player[player.index("--message-set") + 1])
        self.assertEqual("player", player[player.index("--surface") + 1])

    def test_profile_changing_flags_are_rejected_before_delegation(self) -> None:
        """A named Humble profile must never be silently turned into Jazzy by extra argv."""

        with self.assertRaises(ValueError):
            self.profiles.validate_no_profile_overrides(["--distro", "jazzy"])
        with self.assertRaises(ValueError):
            self.profiles.validate_no_profile_overrides(["--rmw=rmw_zenoh_cpp"])
        self.profiles.validate_no_profile_overrides(["--domain-id", "17", "--token", "row-token"])

    def test_each_profile_has_a_dedicated_evidence_root_and_visible_wrapper(self) -> None:
        """Operators choose a named row instead of a generic script plus hidden distro defaults."""

        expected_wrappers = {
            "humble-fastrtps": "phase179_humble_fastrtps_acceptance.py",
            "jazzy-fastrtps": "phase179_jazzy_fastrtps_acceptance.py",
            "lyrical-fastrtps": "phase179_lyrical_fastrtps_acceptance.py",
            "lyrical-zenoh": "phase179_lyrical_zenoh_acceptance.py",
        }

        self.assertEqual(expected_wrappers, self.profiles.WRAPPER_FILENAMES)
        for profile_id, filename in expected_wrappers.items():
            self.assertEqual(
                ROOT / "build" / "phase179" / profile_id / "linux-editor.json",
                self.profiles.profile_evidence_path(
                    self.profiles.PROFILES[profile_id],
                    role="linux-peer",
                    surface="editor",
                    workspace_root=ROOT,
                ),
            )
            self.assertTrue((PROFILE_PATH.parent / filename).is_file())

    def test_all_named_wrappers_default_to_one_command_windows_local_editor(self) -> None:
        """Every named row exposes a safe no-argument local Editor entry point, with Zenoh router ownership explicit."""

        local_editor = [
            "--role",
            "windows-local-editor",
            "--ready-timeout-seconds",
            "300",
            "--apply-timeout-seconds",
            "90",
        ]
        for profile_id in ("humble-fastrtps", "jazzy-fastrtps", "lyrical-fastrtps"):
            self.assertEqual(local_editor, self.profiles.profile_wrapper_argv(profile_id, []))

        with mock.patch.object(self.profiles.ros2env, "default_ros2_root", return_value=Path("C:/ros2_lyrical")):
            self.assertEqual(
                [
                    *local_editor,
                    "--zenoh-router",
                    str(Path("C:/ros2_lyrical") / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"),
                    "--zenoh-topology-id",
                    "phase179-lyrical-zenoh-local-router",
                ],
                self.profiles.profile_wrapper_argv("lyrical-zenoh", []),
            )
        self.assertEqual(
            ["--role", "windows-editor", "--surface", "editor", "--token", "remote-token"],
            self.profiles.profile_wrapper_argv(
                "lyrical-fastrtps",
                ["--role", "windows-editor", "--surface", "editor", "--token", "remote-token"],
            ),
        )
        for filename in self.profiles.WRAPPER_FILENAMES.values():
            wrapper_text = (PROFILE_PATH.parent / filename).read_text(encoding="utf-8")
            self.assertIn("profile_wrapper_argv(PROFILE_ID, sys.argv[1:])", wrapper_text)

    def test_windows_local_publish_command_uses_only_wait_options_supported_by_each_distro(self) -> None:
        """Humble must wait for Unity without receiving Jazzy/Lyrical's unsupported max-wait CLI option."""

        args = SimpleNamespace(token="row-token", ready_timeout_seconds=45.0)
        spec = self.profiles.inbound.MESSAGE_SPECS["string"]
        commands = {
            profile_id: self.profiles.build_windows_local_publish_command(
                self.profiles.PROFILES[profile_id], spec, args
            )
            for profile_id in ("humble-fastrtps", "jazzy-fastrtps", "lyrical-fastrtps", "lyrical-zenoh")
        }

        for command in commands.values():
            self.assertIn("--wait-matching-subscriptions", command)
            self.assertEqual("1", command[command.index("--wait-matching-subscriptions") + 1])
        self.assertNotIn("--max-wait-time-secs", commands["humble-fastrtps"])
        for profile_id in ("jazzy-fastrtps", "lyrical-fastrtps", "lyrical-zenoh"):
            command = commands[profile_id]
            self.assertEqual("45.0", command[command.index("--max-wait-time-secs") + 1])

    def test_windows_local_editor_starts_waiting_publishers_before_waiting_for_unity_ready(self) -> None:
        """The one-command local workflow keeps repo-local publishers alive before the operator enters Play Mode."""

        profile = self.profiles.PROFILES["lyrical-fastrtps"]
        token = "phase179-lyrical-local-token"
        preexisting_ready_token = "phase179-lyrical-ready-before-local-run"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unity_log = root / "Editor.log"
            unity_log.write_text(
                "PHASE179_ROS2_INBOUND_READY runtime=lyrical rmw=rmw_fastrtps_cpp token="
                + preexisting_ready_token
                + "\n",
                encoding="utf-8",
            )
            summary_path = root / "windows-local-editor.json"
            markers = {
                name: self.profiles.inbound.UnityMarker(
                    session=8,
                    topic=f"/foxrun/phase179/{name}",
                    token=token,
                    received=1,
                    applied=1,
                    replaced=0,
                    value=self.profiles.inbound.MESSAGE_SPECS[name].expected_value(token),
                )
                for name in profile.message_set
            }
            ready = self.profiles.inbound.UnityReadyMarker("lyrical", "rmw_fastrtps_cpp", "manual")
            events: list[str] = []

            @contextlib.contextmanager
            def managed_publishers(*_args, **kwargs):
                """Record the staged local publisher lifecycle without launching ROS2."""

                message_names = kwargs.get("message_names", profile.message_set)
                label = ",".join(message_names)
                events.append(f"publishers-started:{label}")
                try:
                    yield
                finally:
                    events.append(f"publishers-stopped:{label}")

            def wait_ready(*_args, **_kwargs):
                """Record the fresh Unity READY boundary for the staged test flow."""

                events.append("ready")
                return ready

            def wait_marker(*marker_args, **_kwargs):
                """Return the copied-value marker for the currently requested topic."""

                name = str(marker_args[1]).rsplit("/", 1)[-1]
                events.append(f"marker:{name}")
                return markers[name]

            with mock.patch.object(
                self.profiles,
                "resolve_windows_ros2_root",
                return_value=(Path("C:/ros2_lyrical"), Path("python.exe"), Path("ros2.py")),
            ):
                with mock.patch.object(self.profiles.ros2env, "build_ros_env", return_value={}) as build_env:
                    with mock.patch.object(self.profiles.inbound, "unity_log_offset", side_effect=[11, 22, 33, 44]):
                        with mock.patch.object(
                            self.profiles,
                            "managed_windows_local_publishers",
                            managed_publishers,
                            create=True,
                        ):
                            with mock.patch.object(
                                self.profiles.inbound,
                                "wait_for_unity_ready_marker",
                                side_effect=wait_ready,
                            ) as ready_waiter:
                                with mock.patch.object(
                                    self.profiles,
                                    "validate_windows_subscription_endpoints",
                                    side_effect=AssertionError(
                                        "Windows-local acceptance must not replace its successful data-path proof "
                                        "with a post-publication rclpy graph observer."
                                    ),
                                ):
                                    with mock.patch.object(
                                        self.profiles.ros2env,
                                        "run_ros2",
                                        return_value=SimpleNamespace(returncode=0, stdout="published"),
                                    ) as publish:
                                        with mock.patch.object(
                                            self.profiles.inbound,
                                            "wait_for_unity_marker",
                                            side_effect=wait_marker,
                                        ) as wait_marker:
                                            exit_code = self.profiles.run_profile(
                                                "lyrical-fastrtps",
                                                [
                                                    "--role",
                                                    "windows-local-editor",
                                                    "--token",
                                                    token,
                                                    "--unity-log",
                                                    str(unity_log),
                                                    "--summary-json",
                                                    str(summary_path),
                                                ],
                                            )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(0, exit_code)
        self.assertEqual("PHASE179_LYRICAL_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS", summary["verdict"])
        self.assertEqual("windows-local-editor", summary["role"])
        self.assertEqual("windows-local-loopback", summary["transportScope"])
        self.assertEqual("publisher-complete-and-unity-applied", summary["windowsLocalDataPathEvidence"])
        self.assertNotIn("endpointEvidence", summary)
        self.assertEqual(["string", "twist", "joy"], [entry["name"] for entry in summary["messageResults"]])
        self.assertEqual(
            [
                "publishers-started:string",
                "ready",
                "marker:string",
                "publishers-stopped:string",
                "publishers-started:twist,joy",
                "marker:twist",
                "marker:joy",
                "publishers-stopped:twist,joy",
            ],
            events,
        )
        self.assertEqual(0, publish.call_count)
        self.assertIsNone(ready_waiter.call_args.args[3])
        self.assertIsNone(ready_waiter.call_args.kwargs["start_offset"])
        self.assertEqual(
            frozenset({preexisting_ready_token}),
            ready_waiter.call_args.kwargs["excluded_tokens"],
        )
        self.assertEqual([None, None, None], [call.kwargs["start_offset"] for call in wait_marker.call_args_list])
        build_env.assert_called_once()

    def test_windows_local_zenoh_owns_router_before_publishers_and_releases_it_afterwards(self) -> None:
        """Zenoh local acceptance owns one router across the publisher, Unity, and endpoint-proof window."""

        profile = self.profiles.PROFILES["lyrical-zenoh"]
        token = "phase179-lyrical-zenoh-local-token"
        topology_id = "phase179-lyrical-zenoh-local-router"
        router = Path("C:/ros2_lyrical/Lib/rmw_zenoh_cpp/rmw_zenohd.exe")
        handle = SimpleNamespace(mode="owned-router", topology_id=topology_id)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unity_log = root / "Editor.log"
            unity_log.write_text("before-local-run\n", encoding="utf-8")
            summary_path = root / "windows-local-editor.json"
            markers = {
                name: self.profiles.inbound.UnityMarker(
                    session=9,
                    topic=f"/foxrun/phase179/{name}",
                    token=token,
                    received=1,
                    applied=1,
                    replaced=0,
                    value=self.profiles.inbound.MESSAGE_SPECS[name].expected_value(token),
                )
                for name in profile.message_set
            }
            events: list[str] = []

            @contextlib.contextmanager
            def managed_publishers(*_args, **kwargs):
                """Record each staged publisher set while the fake Zenoh router remains owned."""

                message_names = kwargs.get("message_names", profile.message_set)
                label = ",".join(message_names)
                events.append(f"publishers-started:{label}")
                try:
                    yield
                finally:
                    events.append(f"publishers-stopped:{label}")

            def start_topology(*_args, **_kwargs):
                """Record owned-router startup and return the fixed test handle."""

                events.append("topology-started")
                return handle

            def close_topology(*_args, **_kwargs):
                """Record owned-router cleanup after staged local publishers finish."""

                events.append("topology-closed")

            def wait_ready(*_args, **_kwargs):
                """Record the fresh Unity READY boundary for the Zenoh staged flow."""

                events.append("ready")

            def wait_marker(*marker_args, **_kwargs):
                """Return the fixed copied-value marker for the requested Zenoh topic."""

                name = str(marker_args[1]).rsplit("/", 1)[-1]
                events.append(f"marker:{name}")
                return markers[name]

            with mock.patch.object(
                self.profiles,
                "resolve_windows_ros2_root",
                return_value=(Path("C:/ros2_lyrical"), Path("python.exe"), Path("ros2.py")),
            ):
                with mock.patch.object(self.profiles.ros2env, "build_ros_env", return_value={}):
                    with mock.patch.object(self.profiles.inbound, "unity_log_offset", return_value=11):
                        with mock.patch.object(self.profiles, "managed_windows_local_publishers", managed_publishers, create=True):
                            with mock.patch.object(self.profiles.zenoh_topology, "start_topology", side_effect=start_topology):
                                with mock.patch.object(self.profiles.zenoh_topology, "close_topology", side_effect=close_topology):
                                    with mock.patch.object(
                                        self.profiles.inbound,
                                        "topology_summary",
                                        return_value={"mode": "owned-router", "readiness": "owned-router-ready"},
                                    ):
                                        with mock.patch.object(
                                            self.profiles.inbound,
                                            "wait_for_unity_ready_marker",
                                            side_effect=wait_ready,
                                        ):
                                            with mock.patch.object(
                                                self.profiles.inbound,
                                                "wait_for_unity_marker",
                                                side_effect=wait_marker,
                                            ):
                                                with mock.patch.object(
                                                    self.profiles,
                                                    "validate_windows_subscription_endpoints",
                                                    side_effect=AssertionError(
                                                        "Windows-local Zenoh acceptance must use its owned router, "
                                                        "publisher completion, and Unity copied-value evidence."
                                                    ),
                                                ):
                                                    exit_code = self.profiles.run_profile(
                                                        "lyrical-zenoh",
                                                        [
                                                            "--role",
                                                            "windows-local-editor",
                                                            "--token",
                                                            token,
                                                            "--unity-log",
                                                            str(unity_log),
                                                            "--summary-json",
                                                            str(summary_path),
                                                            "--zenoh-router",
                                                            str(router),
                                                            "--zenoh-topology-id",
                                                            topology_id,
                                                        ],
                                                    )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(0, exit_code)
        self.assertEqual("PHASE179_LYRICAL_ZENOH_WINDOWS_LOCAL_EDITOR_PASS", summary["verdict"])
        self.assertEqual("publisher-complete-and-unity-applied", summary["windowsLocalDataPathEvidence"])
        self.assertNotIn("endpointEvidence", summary)
        self.assertEqual(topology_id, summary["zenohTopologyId"])
        self.assertEqual(
            [
                "topology-started",
                "publishers-started:string",
                "ready",
                "marker:string",
                "publishers-stopped:string",
                "publishers-started:twist,joy",
                "marker:twist",
                "marker:joy",
                "publishers-stopped:twist,joy",
                "topology-closed",
            ],
            events,
        )

    def test_linux_pending_exit_is_accepted_only_with_complete_graph_and_publication_evidence(self) -> None:
        """The existing Linux peer's exit 2 is meaningful only for its documented pending half-evidence verdict."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        summary = self._linux_summary(profile, surface="editor", token="row-token")

        self.profiles.validate_linux_peer_result(2, summary, profile, surface="editor", token="row-token")

        incomplete = json.loads(json.dumps(summary))
        incomplete["messageResults"][1]["published"] = False
        with self.assertRaises(self.profiles.MatrixFailure) as publication_failure:
            self.profiles.validate_linux_peer_result(2, incomplete, profile, surface="editor", token="row-token")
        self.assertEqual("LINUX_PUBLICATION", publication_failure.exception.category)

        with self.assertRaises(self.profiles.MatrixFailure) as exit_failure:
            self.profiles.validate_linux_peer_result(0, summary, profile, surface="editor", token="row-token")
        self.assertEqual("LINUX_EXIT", exit_failure.exception.category)

    def test_player_pending_exit_requires_zero_embedded_player_exit_and_copied_values(self) -> None:
        """The Player host's own pending process exit remains valid only with full Unity-value proof."""

        profile = self.profiles.PROFILES["jazzy-fastrtps"]
        summary = self._player_summary(profile, token="row-token")

        self.profiles.validate_windows_player_result(2, summary, profile, token="row-token")

        incomplete = json.loads(json.dumps(summary))
        incomplete["playerExitCode"] = 7
        with self.assertRaises(self.profiles.MatrixFailure) as exit_failure:
            self.profiles.validate_windows_player_result(2, incomplete, profile, token="row-token")
        self.assertEqual("PLAYER_EXIT", exit_failure.exception.category)

    def test_correlation_requires_both_matching_halves_and_zenoh_topology_identity(self) -> None:
        """A matching Linux publication and Unity copied-value proof are the minimum final interop evidence."""

        profile = self.profiles.PROFILES["lyrical-zenoh"]
        linux = self._linux_summary(
            profile,
            surface="player",
            token="row-token",
            topology_id="phase179-lyrical-zenoh-router-a",
        )
        windows = self._player_summary(
            profile,
            token="row-token",
            topology_id="phase179-lyrical-zenoh-router-a",
        )

        combined = self.profiles.correlate_summaries(profile, "player", linux, windows)

        self.assertEqual("PHASE179_LYRICAL_ZENOH_PLAYER_PASS", combined["verdict"])
        self.assertEqual("phase179-lyrical-zenoh-router-a", combined["zenohTopologyId"])

        mismatched = json.loads(json.dumps(windows))
        mismatched["zenohTopologyId"] = "phase179-other-router"
        with self.assertRaises(self.profiles.MatrixFailure) as topology_failure:
            self.profiles.correlate_summaries(profile, "player", linux, mismatched)
        self.assertEqual("ZENOH_TOPOLOGY", topology_failure.exception.category)

    def test_editor_correlation_requires_each_serialized_copied_value_not_only_a_boolean(self) -> None:
        """A hand-edited or incomplete Editor summary cannot turn preflight readiness into final interoperability proof."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        linux = self._linux_summary(profile, surface="editor", token="row-token")
        editor = self._editor_summary(profile, token="row-token")

        combined = self.profiles.correlate_summaries(profile, "editor", linux, editor)
        self.assertEqual("PHASE179_HUMBLE_FASTRTPS_EDITOR_PASS", combined["verdict"])

        incomplete = json.loads(json.dumps(editor))
        incomplete["messageResults"] = incomplete["messageResults"][:2]
        with self.assertRaises(self.profiles.MatrixFailure) as value_failure:
            self.profiles.correlate_summaries(profile, "editor", linux, incomplete)
        self.assertEqual("EDITOR_APPLIED", value_failure.exception.category)

    def test_windows_editor_root_uses_repo_local_ros_python_rclpy_endpoint_evidence(self) -> None:
        """Editor preflight queries all three endpoint contracts through the selected Windows ROS Python, never its CLI."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        root = ROOT / "ros2-windows" / "ros2_humble"
        args = SimpleNamespace(ros2_root=None)
        with mock.patch.object(self.profiles.ros2env, "default_ros2_root", return_value=root) as default_root:
            with mock.patch.object(self.profiles.ros2env, "validate_ros2_root", return_value=(Path("python.exe"), Path("ros2.py"))):
                resolved, python, ros2_script = self.profiles.resolve_windows_ros2_root(profile, args, workspace_root=ROOT)

        self.assertEqual(root, resolved)
        self.assertEqual(Path("python.exe"), python)
        self.assertEqual(Path("ros2.py"), ros2_script)
        default_root.assert_called_once_with("humble", ROOT)

        expected_outputs = []
        for name in profile.message_set:
            spec = self.profiles.inbound.MESSAGE_SPECS[name]
            expected_outputs.append(
                SimpleNamespace(
                    returncode=0,
                    stdout=json.dumps(
                        {
                            "topic": f"/foxrun/phase179/{name}",
                            "subscriptionCount": 1,
                            "endpoints": [
                                {
                                    "messageType": spec.message_type,
                                    "qosReliability": spec.qos_reliability,
                                    "qosHistory": spec.qos_history,
                                    "qosDepth": spec.qos_depth,
                                    "qosDurability": spec.qos_durability,
                                }
                            ],
                        }
                    ),
                )
            )
        with mock.patch.object(
            self.profiles.ros2env,
            "run_ros2",
            side_effect=AssertionError("Windows endpoint evidence must not invoke ros2-script.py."),
        ) as run_ros2:
            with mock.patch.object(self.profiles.subprocess, "run", side_effect=expected_outputs) as probe:
                evidence = self.profiles.validate_windows_subscription_endpoints(
                    Path("python.exe"),
                    Path("ros2.py"),
                    {},
                    profile,
                    timeout_seconds=0.1,
                )

        self.assertEqual(["string", "twist", "joy"], [item["name"] for item in evidence])
        run_ros2.assert_not_called()
        self.assertEqual(3, probe.call_count)
        for call in probe.call_args_list:
            self.assertEqual("python.exe", call.args[0][0])
            self.assertEqual(str(WINDOWS_ENDPOINT_PROBE_PATH), call.args[0][1])
            self.assertNotIn("ros2.py", call.args[0])

    def test_linux_role_accepts_only_the_documented_pending_half_exit_without_unity_log(self) -> None:
        """The profile wrapper delegates a pinned Linux row and preserves exit 2 as correlation-pending evidence."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        with tempfile.TemporaryDirectory() as temporary:
            summary_path = Path(temporary) / "linux-editor.json"
            summary = self._linux_summary(profile, surface="editor", token="row-token")

            def fake_linux_main(argv):
                """Write the synthetic peer summary instead of loading a ROS2 runtime."""

                summary_path.write_text(json.dumps(summary), encoding="utf-8")
                return 2

            with mock.patch.object(self.profiles.inbound, "main", side_effect=fake_linux_main) as delegate:
                exit_code = self.profiles.run_profile(
                    "humble-fastrtps",
                    [
                        "--role",
                        "linux-peer",
                        "--surface",
                        "editor",
                        "--token",
                        "row-token",
                        "--summary-json",
                        str(summary_path),
                    ],
                )

        self.assertEqual(2, exit_code)
        child_argv = delegate.call_args.args[0]
        self.assertNotIn("--unity-log", child_argv)
        self.assertEqual("humble", child_argv[child_argv.index("--distro") + 1])
        self.assertEqual("rmw_fastrtps_cpp", child_argv[child_argv.index("--rmw") + 1])

    def test_player_role_accepts_only_the_documented_pending_half_exit_without_windows_cli_path_injection(self) -> None:
        """The profile wrapper lets the R2FU Player own its DLL selection instead of adding ros2-windows paths."""

        profile = self.profiles.PROFILES["jazzy-fastrtps"]
        with tempfile.TemporaryDirectory() as temporary:
            summary_path = Path(temporary) / "windows-player.json"
            summary = self._player_summary(profile, token="row-token")

            def fake_player_main(argv):
                """Write the synthetic Player summary instead of launching a Unity executable."""

                summary_path.write_text(json.dumps(summary), encoding="utf-8")
                return 2

            with mock.patch.object(self.profiles.player_host, "main", side_effect=fake_player_main) as delegate:
                exit_code = self.profiles.run_profile(
                    "jazzy-fastrtps",
                    [
                        "--role",
                        "windows-player",
                        "--token",
                        "row-token",
                        "--player",
                        r"C:\build\Phase179.exe",
                        "--player-log",
                        str(Path(temporary) / "Player.log"),
                        "--summary-json",
                        str(summary_path),
                    ],
                )

        self.assertEqual(2, exit_code)
        child_argv = delegate.call_args.args[0]
        self.assertNotIn("--ros2-root", child_argv)
        self.assertEqual("jazzy", child_argv[child_argv.index("--distro") + 1])
        self.assertEqual("rmw_fastrtps_cpp", child_argv[child_argv.index("--rmw") + 1])

    def test_editor_role_uses_fresh_ready_and_apply_offsets_before_writing_pending_half_evidence(self) -> None:
        """Editor proof rejects stale log markers by observing READY and all copied values only after fresh offsets."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        token = "row-token"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unity_log = root / "Editor.log"
            unity_log.write_text("stale content\n", encoding="utf-8")
            summary_path = root / "windows-editor.json"
            marker_by_name = {
                name: self.profiles.inbound.UnityMarker(
                    session=4,
                    topic=f"/foxrun/phase179/{name}",
                    token=token,
                    received=1,
                    applied=1,
                    replaced=0,
                    value=self.profiles.inbound.MESSAGE_SPECS[name].expected_value(token),
                )
                for name in profile.message_set
            }
            with mock.patch.object(
                self.profiles,
                "resolve_windows_ros2_root",
                return_value=(Path("C:/ros2"), Path("python.exe"), Path("ros2.py")),
            ):
                with mock.patch.object(self.profiles.ros2env, "build_ros_env", return_value={}) as build_env:
                    with mock.patch.object(self.profiles.inbound, "unity_log_offset", side_effect=[12, 34]):
                        with mock.patch.object(self.profiles.inbound, "wait_for_unity_ready_marker") as wait_ready:
                            with mock.patch.object(
                                self.profiles,
                                "validate_windows_subscription_endpoints",
                                return_value=[{"name": name} for name in profile.message_set],
                            ):
                                with mock.patch.object(
                                    self.profiles.inbound,
                                    "wait_for_unity_marker",
                                    side_effect=[marker_by_name[name] for name in profile.message_set],
                                ) as wait_marker:
                                    exit_code = self.profiles.run_profile(
                                        "humble-fastrtps",
                                        [
                                            "--role",
                                            "windows-editor",
                                            "--surface",
                                            "editor",
                                            "--token",
                                            token,
                                            "--unity-log",
                                            str(unity_log),
                                            "--summary-json",
                                            str(summary_path),
                                        ],
                                    )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual("WINDOWS_EDITOR_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING", summary["verdict"])
        self.assertEqual(["string", "twist", "joy"], [entry["name"] for entry in summary["messageResults"]])
        self.assertEqual(12, wait_ready.call_args.kwargs["start_offset"])
        self.assertEqual([34, 34, 34], [call.kwargs["start_offset"] for call in wait_marker.call_args_list])
        build_env.assert_called_once()

    def test_correlation_role_writes_the_only_final_pass_artifact(self) -> None:
        """A named wrapper turns two compatible half-summaries into a final PASS only during explicit correlation."""

        profile = self.profiles.PROFILES["jazzy-fastrtps"]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            linux_path = root / "linux.json"
            player_path = root / "player.json"
            combined_path = root / "combined.json"
            linux_path.write_text(json.dumps(self._linux_summary(profile, surface="player", token="row-token")), encoding="utf-8")
            player_path.write_text(json.dumps(self._player_summary(profile, token="row-token")), encoding="utf-8")

            exit_code = self.profiles.run_profile(
                "jazzy-fastrtps",
                [
                    "--role",
                    "correlate",
                    "--surface",
                    "player",
                    "--linux-summary-json",
                    str(linux_path),
                    "--windows-summary-json",
                    str(player_path),
                    "--summary-json",
                    str(combined_path),
                ],
            )
            combined = json.loads(combined_path.read_text(encoding="utf-8"))

        self.assertEqual(0, exit_code)
        self.assertEqual("PHASE179_JAZZY_FASTRTPS_PLAYER_PASS", combined["verdict"])

    @staticmethod
    def _linux_summary(profile, *, surface: str, token: str, topology_id: str | None = None) -> dict[str, object]:
        """Build one complete synthetic Linux half-summary without a ROS2 installation."""

        messages = []
        for name in profile.message_set:
            spec = load_profile_module().inbound.MESSAGE_SPECS[name]
            messages.append(
                {
                    "name": name,
                    "topic": f"{profile.topic_prefix}/{name}",
                    "published": True,
                    "graph": {
                        "messageType": spec.message_type,
                        "subscriptionCount": 1,
                        "qosReliability": spec.qos_reliability,
                        "qosHistory": spec.qos_history,
                        "qosDepth": spec.qos_depth,
                        "qosDurability": spec.qos_durability,
                    },
                }
            )
        summary: dict[str, object] = {
            "phase": 179,
            "role": "linux-ros2-peer",
            "profileId": profile.profile_id,
            "surface": surface,
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "domainId": 0,
            "discoveryRange": "SUBNET",
            "token": token,
            "topicPrefix": profile.topic_prefix,
            "messageSet": list(profile.message_set),
            "unityLogProvided": False,
            "messageResults": messages,
            "verdict": "PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING",
        }
        if topology_id is not None:
            summary["zenohTopologyId"] = topology_id
        return summary

    @staticmethod
    def _player_summary(profile, *, token: str, topology_id: str | None = None) -> dict[str, object]:
        """Build one complete synthetic Windows Player half-summary without launching Unity."""

        summary: dict[str, object] = {
            "phase": 179,
            "role": "windows-player-host",
            "profileId": profile.profile_id,
            "surface": "player",
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "domainId": 0,
            "discoveryRange": "SUBNET",
            "token": token,
            "topicPrefix": profile.topic_prefix,
            "messageSet": list(profile.message_set),
            "ready": True,
            "allRequiredApplied": True,
            "playerExitCode": 0,
            "verdict": "PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING",
        }
        if topology_id is not None:
            summary["zenohTopologyId"] = topology_id
        return summary

    @staticmethod
    def _editor_summary(profile, *, token: str, topology_id: str | None = None) -> dict[str, object]:
        """Build one complete synthetic Windows Editor half-summary with bounded copied-value evidence."""

        messages = []
        for name in profile.message_set:
            spec = load_profile_module().inbound.MESSAGE_SPECS[name]
            messages.append(
                {
                    "name": name,
                    "topic": f"{profile.topic_prefix}/{name}",
                    "received": 1,
                    "applied": 1,
                    "replaced": 0,
                    "value": spec.expected_value(token),
                }
            )
        summary: dict[str, object] = {
            "phase": 179,
            "role": "windows-editor-host",
            "profileId": profile.profile_id,
            "surface": "editor",
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "domainId": 0,
            "discoveryRange": "SUBNET",
            "token": token,
            "topicPrefix": profile.topic_prefix,
            "messageSet": list(profile.message_set),
            "ready": True,
            "allRequiredApplied": True,
            "messageResults": messages,
            "verdict": "WINDOWS_EDITOR_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING",
        }
        if topology_id is not None:
            summary["zenohTopologyId"] = topology_id
        return summary


if __name__ == "__main__":
    unittest.main()
