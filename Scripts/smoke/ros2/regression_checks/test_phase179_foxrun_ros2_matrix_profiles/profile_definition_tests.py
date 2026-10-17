from __future__ import annotations

from .foundation import *


class Phase179ProfileDefinitionTests:
    """Exercise certified profile rows and wrapper command definitions"""

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


__all__ = ["Phase179ProfileDefinitionTests"]
