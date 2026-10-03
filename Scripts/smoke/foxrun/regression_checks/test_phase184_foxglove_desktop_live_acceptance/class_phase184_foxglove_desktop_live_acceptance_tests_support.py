from __future__ import annotations
from .class_coordinator_harness_fixtures_and_coordinator_harness_public import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveDesktopLiveAcceptanceTests_support:
    """Decomposed Phase192 implementation component."""
    def test_coordinator_module_exists(self):
        """Verify coordinator module exists."""

        self.assertTrue(
            COORDINATOR_PATH.is_file(),
            "Phase184-H Desktop-live coordinator has not been implemented.",
        )
    def test_argument_surface_has_exact_required_paths_and_defaults(self):
        """Verify argument surface has exact required paths and defaults."""

        args = coordinator.parse_args(
            [
                "--unity-editor",
                r"C:\Unity\Editor\Unity.exe",
                "--foxglove-cli",
                r"C:\Tools\foxglove.exe",
                "--desktop-executable",
                r"D:\Apps\Foxglove\Foxglove.exe",
            ]
        )

        self.assertEqual(args.unity_editor, pathlib.Path(r"C:\Unity\Editor\Unity.exe"))
        self.assertEqual(args.foxglove_cli, pathlib.Path(r"C:\Tools\foxglove.exe"))
        self.assertEqual(
            args.desktop_executable,
            pathlib.Path(r"D:\Apps\Foxglove\Foxglove.exe"),
        )
        self.assertEqual(args.cli_receipt, coordinator.DEFAULT_CLI_RECEIPT)
        self.assertIsNone(args.foxglove_port)
        self.assertIsNone(args.run_id)

        with redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                coordinator.parse_args(
                    [
                        "--unity-editor",
                        r"C:\Unity\Editor\Unity.exe",
                        "--foxglove-cli",
                        r"C:\Tools\foxglove.exe",
                        "--desktop-executable",
                        r"D:\Apps\Foxglove\Foxglove.exe",
                        "--case",
                        "multi-target",
                    ]
                )
    def test_argument_validation_rejects_non_windows_relative_missing_and_unsafe_values(self):
        """Verify argument validation rejects non windows relative missing and unsafe values."""

        args = argparse.Namespace(
            unity_editor=pathlib.Path(r"C:\Unity\Editor\Unity.exe"),
            foxglove_cli=pathlib.Path(r"C:\Tools\foxglove.exe"),
            desktop_executable=pathlib.Path(r"D:\Apps\Foxglove\Foxglove.exe"),
            cli_receipt=pathlib.Path(r"D:\repo\receipt.json"),
            foxglove_port=8765,
            run_id="phase184g-20260727-desktop01",
        )
        files = {
            str(args.unity_editor),
            str(args.foxglove_cli),
            str(args.desktop_executable),
            str(args.cli_receipt),
        }

        validated = coordinator.validate_arguments(
            args,
            platform_name="nt",
            is_file=lambda path: str(path) in files,
        )
        self.assertIs(validated, args)

        for field, value in (
            ("unity_editor", pathlib.Path("Unity.exe")),
            ("foxglove_cli", pathlib.Path(r"C:\missing.exe")),
            ("run_id", "phase184h-unsafe"),
            ("foxglove_port", 0),
            ("foxglove_port", 65536),
        ):
            invalid = argparse.Namespace(**vars(args))
            setattr(invalid, field, value)
            with self.subTest(field=field, value=value):
                with self.assertRaises(live_protocol.AcceptanceFailure):
                    coordinator.validate_arguments(
                        invalid,
                        platform_name="nt",
                        is_file=lambda path: str(path) in files,
                    )

        with self.assertRaises(live_protocol.AcceptanceFailure) as context:
            coordinator.validate_arguments(
                args,
                platform_name="posix",
                is_file=lambda _path: True,
            )
        self.assertEqual(
            context.exception.code,
            live_protocol.FAIL_DESKTOP_PREFLIGHT,
        )
    def test_generated_run_identity_is_safe_deterministic_and_base_compatible(self):
        """Verify generated run identity is safe deterministic and base compatible."""

        self.assertEqual(
            coordinator.generate_run_id(
                timestamp="20260727-153045",
                nonce="A1B2C3D4E5",
            ),
            "phase184g-20260727-153045-a1b2c3d4e5",
        )
        self.assertEqual(
            coordinator.validate_run_id("phase184g-20260727-desktop01"),
            "phase184g-20260727-desktop01",
        )
        for value in (
            "phase184g-short",
            "phase184h-20260727-desktop01",
            "phase184g-../../escape",
            "phase184g bad value",
        ):
            with self.subTest(value=value):
                with self.assertRaises(live_protocol.AcceptanceFailure):
                    coordinator.validate_run_id(value)
    def test_deeplink_uses_fixed_query_order_and_percent_encoding(self):
        """Verify deeplink uses fixed query order and percent encoding."""

        self.assertEqual(
            coordinator.build_deeplink(8765),
            "foxglove://open?ds=foxglove-websocket&"
            "ds.url=ws%3A%2F%2F127.0.0.1%3A8765%2F",
        )
        with self.assertRaises(live_protocol.AcceptanceFailure):
            coordinator.build_deeplink(0)
    def test_windows_command_line_parser_and_uri_handler_are_exact(self):
        """Verify windows command line parser and URI handler are exact."""

        executable = r"D:\Apps\Foxglove\Foxglove.exe"
        command = rf'"{executable}" "%1"'

        self.assertEqual(
            coordinator.parse_windows_command_line(command),
            (executable, "%1"),
        )
        self.assertEqual(
            coordinator.validate_uri_handler(command, executable),
            command,
        )

        for invalid in (
            rf'"{executable}"',
            rf'"{executable}" "%1" "--extra"',
            rf'"{executable}" "%L"',
            r'"D:\Other\Foxglove.exe" "%1"',
            rf'"{executable}" "foxglove://fixed"',
        ):
            with self.subTest(command=invalid):
                with self.assertRaises(live_protocol.AcceptanceFailure):
                    coordinator.validate_uri_handler(invalid, executable)
    def test_clean_environment_is_explicit_and_drops_tokens_credentials_and_ros_state(self):
        """Verify clean environment is explicit and drops tokens credentials and ROS state."""

        source = {
            "SystemRoot": r"C:\Windows",
            "PATH": r"C:\Windows\System32",
            "TEMP": r"C:\Temp",
            "PHASE184G_TOKEN": "p184g_secret",
            "GITHUB_TOKEN": "secret",
            "FOXGLOVE_API_KEY": "secret",
            "ROS_DISTRO": "jazzy",
            "RMW_IMPLEMENTATION": "rmw_fastrtps_cpp",
            "UNRELATED": "discarded",
        }

        self.assertEqual(
            coordinator.build_clean_environment(source),
            {
                "PATH": r"C:\Windows\System32",
                "SystemRoot": r"C:\Windows",
                "TEMP": r"C:\Temp",
            },
        )
    def test_process_identity_document_has_only_pid_time_and_executable(self):
        """Verify process identity document has only PID time and executable."""

        document = coordinator.process_identity_document(valid_process(202))
        self.assertEqual(
            set(document),
            {"pid", "creationTime100ns", "executable"},
        )
        self.assertEqual(document["pid"], 202)
    def test_log_scan_preserves_raw_context_and_transport_indices(self):
        """Verify log scan preserves raw context and transport indices."""

        token = "p184g_A1b2C3d4E5f6"
        case = "foxglove-profile"
        initial = (
            f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
            f"case={case} token={token} active=0 accepted=0"
        )
        context = (
            f"PHASE184G_CONTEXT_READY case={case} token={token} "
            f"tokenDigest={hashlib.sha256(token.encode()).hexdigest()[:12]}"
        )

        evidence = coordinator._scan_unity_log(
            ("unrelated", initial, context),
            case=case,
            token=token,
        )

        self.assertEqual(2, evidence.context_index)
        self.assertEqual((1,), evidence.transport_indices)
    def test_summary_schema_has_exact_top_and_section_keys(self):
        """Verify summary schema has exact top and section keys."""

        summary = valid_summary()

        self.assertIs(
            coordinator.validate_desktop_live_summary(summary),
            summary,
        )
        self.assertEqual(
            set(summary),
            {
                "schemaVersion",
                "identity",
                "cli",
                "desktop",
                "connection",
                "foxrun",
                "cleanup",
                "verdict",
            },
        )
        self.assertEqual(
            set(summary["identity"]),
            {
                "runId",
                "baseCase",
                "tokenSha256",
                "repositoryHead",
                "windowsVersion",
                "unityVersion",
            },
        )
        self.assertEqual(
            set(summary["cli"]),
            {
                "architecture",
                "assetUrl",
                "installedPath",
                "installedSha256",
                "installedVersion",
                "receiptPath",
                "releaseTag",
            },
        )
        self.assertEqual(
            set(summary["desktop"]),
            {
                "executable",
                "fileVersion",
                "sha256",
                "uriHandler",
                "dataSource",
                "deeplink",
                "rootIdentity",
                "ownedMemberIdentities",
                "externalIdentities",
                "jobOwned",
            },
        )
        self.assertEqual(
            set(summary["connection"]),
            {
                "host",
                "port",
                "portPreflight",
                "contextMarker",
                "initialMarker",
                "firstMarker",
                "secondMarker",
                "contextObservedAt",
                "initialObservedAt",
                "desktopIdentityCapturedAt",
                "firstObservedAt",
                "barrierWrittenAt",
                "secondObservedAt",
                "barrierPath",
                "barrierDigest",
                "barrierRemoved",
            },
        )
        self.assertEqual(
            set(summary["foxrun"]),
            {
                "baseSummaryPath",
                "baseVerdict",
                "channelEncodings",
                "deliveryObserved",
                "remoteApplied",
                "sameOriginDropped",
                "laterLocalPublished",
            },
        )
        self.assertEqual(
            set(summary["cleanup"]),
            {
                "jobClosed",
                "processes",
                "port",
                "barrier",
                "files",
                "junctions",
                "subst",
                "gracefulOwnedIdentities",
                "forcedOwnedIdentities",
                "exitedOwnedIdentities",
                "residualOwnedIdentities",
            },
        )
    def test_pass_summary_rejects_raw_token_external_identity_and_bad_order(self):
        """Verify pass summary rejects raw token external identity and bad order."""

        raw_token = "p184g_A1b2C3d4E5f6"
        for mutate in (
            lambda value: value["connection"].__setitem__(
                "contextMarker",
                f"PHASE184G_CONTEXT_READY token={raw_token}",
            ),
            lambda value: value["desktop"]["externalIdentities"].append(
                coordinator.process_identity_document(valid_process(204))
            ),
            lambda value: value["connection"].__setitem__(
                "barrierWrittenAt",
                3.5,
            ),
        ):
            summary = valid_summary()
            mutate(summary)
            with self.assertRaises(live_protocol.AcceptanceFailure):
                coordinator.validate_desktop_live_summary(summary)


__all__ = [name for name in globals() if not name.startswith("__")]
