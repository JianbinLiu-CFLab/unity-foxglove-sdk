from __future__ import annotations
from .class_phase184_foxglove_desktop_live_acceptance_tests_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveDesktopLiveAcceptanceTests_fixtures:
    """Decomposed Phase192 implementation component."""
    def test_pass_summary_rejects_cross_field_semantic_mutations(self):
        """Verify pass summary rejects cross field semantic mutations."""

        coordinator.validate_desktop_live_summary(valid_summary())
        mutations = (
            (
                "cli-architecture",
                lambda value: value["cli"].__setitem__(
                    "architecture",
                    "windows-arm64",
                ),
            ),
            (
                "cli-url",
                lambda value: value["cli"].__setitem__(
                    "assetUrl",
                    "https://example.invalid/foxglove.exe",
                ),
            ),
            (
                "cli-version",
                lambda value: value["cli"].__setitem__(
                    "installedVersion",
                    "v1.2.4",
                ),
            ),
            (
                "cli-path",
                lambda value: value["cli"].__setitem__(
                    "installedPath",
                    "foxglove.exe",
                ),
            ),
            (
                "cli-receipt-alias",
                lambda value: value["cli"].__setitem__(
                    "receiptPath",
                    value["cli"]["installedPath"],
                ),
            ),
            (
                "root-not-owned",
                lambda value: value["desktop"].__setitem__(
                    "ownedMemberIdentities",
                    value["desktop"]["ownedMemberIdentities"][1:],
                ),
            ),
            (
                "job-not-owned",
                lambda value: value["desktop"].__setitem__(
                    "jobOwned",
                    False,
                ),
            ),
            (
                "root-path",
                lambda value: value["desktop"]["rootIdentity"].__setitem__(
                    "executable",
                    r"D:\Other\Foxglove.exe",
                ),
            ),
            (
                "handler-extra",
                lambda value: value["desktop"].__setitem__(
                    "uriHandler",
                    (
                        r'"D:\Apps\Foxglove\Foxglove.exe" '
                        r'"%1" "--extra"'
                    ),
                ),
            ),
            (
                "deeplink-port",
                lambda value: value["desktop"].__setitem__(
                    "deeplink",
                    coordinator.build_deeplink(8766),
                ),
            ),
            (
                "context-case",
                lambda value: value["connection"].__setitem__(
                    "contextMarker",
                    value["connection"]["contextMarker"].replace(
                        "case=foxglove-profile",
                        "case=multi-target",
                    ),
                ),
            ),
            (
                "context-token-digest",
                lambda value: value["connection"].__setitem__(
                    "contextMarker",
                    value["connection"]["contextMarker"].replace(
                        "tokenDigest=AAAAAAAAAAAA",
                        "tokenDigest=BBBBBBBBBBBB",
                    ),
                ),
            ),
            (
                "initial-count",
                lambda value: value["connection"].__setitem__(
                    "initialMarker",
                    value["connection"]["initialMarker"].replace(
                        "active=0 accepted=0",
                        "active=1 accepted=1",
                    ),
                ),
            ),
            (
                "initial-noncanonical-decimal",
                lambda value: value["connection"].__setitem__(
                    "initialMarker",
                    value["connection"]["initialMarker"].replace(
                        "active=0 accepted=0",
                        "active=00 accepted=0",
                    ),
                ),
            ),
            (
                "first-count",
                lambda value: value["connection"].__setitem__(
                    "firstMarker",
                    value["connection"]["firstMarker"].replace(
                        "active=1 accepted=1",
                        "active=1 accepted=2",
                    ),
                ),
            ),
            (
                "second-count",
                lambda value: value["connection"].__setitem__(
                    "secondMarker",
                    value["connection"]["secondMarker"].replace(
                        "active=2 accepted=2",
                        "active=3 accepted=3",
                    ),
                ),
            ),
            (
                "equal-times",
                lambda value: value["connection"].__setitem__(
                    "barrierWrittenAt",
                    value["connection"]["firstObservedAt"],
                ),
            ),
            (
                "token-shape",
                lambda value: value["identity"].__setitem__(
                    "tokenSha256",
                    "a" * 64,
                ),
            ),
            (
                "barrier-association",
                lambda value: value["connection"].__setitem__(
                    "barrierDigest",
                    "0" * 64,
                ),
            ),
            (
                "base-summary-path",
                lambda value: value["foxrun"].__setitem__(
                    "baseSummaryPath",
                    r"D:\repo\build\phase184\acceptance\other\summary.json",
                ),
            ),
            (
                "channel-encodings",
                lambda value: value["foxrun"].__setitem__(
                    "channelEncodings",
                    ["protobuf"],
                ),
            ),
            (
                "exit-proof",
                lambda value: value["cleanup"].__setitem__(
                    "exitedOwnedIdentities",
                    value["cleanup"]["exitedOwnedIdentities"][:-1],
                ),
            ),
            (
                "residual-proof",
                lambda value: value["cleanup"].__setitem__(
                    "residualOwnedIdentities",
                    [value["desktop"]["ownedMemberIdentities"][0]],
                ),
            ),
        )
        for name, mutate in mutations:
            summary = valid_summary()
            mutate(summary)
            with self.subTest(name=name):
                with self.assertRaises(live_protocol.AcceptanceFailure):
                    coordinator.validate_desktop_live_summary(summary)
    def test_every_false_cleanup_axis_overrides_pass(self):
        """Verify every false cleanup axis overrides pass."""

        for key in (
            "jobClosed",
            "processes",
            "port",
            "barrier",
            "files",
            "junctions",
            "subst",
        ):
            summary = valid_summary()
            summary["cleanup"][key] = False
            if key == "barrier":
                summary["connection"]["barrierRemoved"] = False
            with self.subTest(key=key):
                with self.assertRaises(live_protocol.AcceptanceFailure) as context:
                    coordinator.validate_desktop_live_summary(summary)
                self.assertEqual(
                    context.exception.code,
                    live_protocol.FAIL_CLEANUP,
                )
    def test_failure_summary_still_has_exact_schema_and_stable_terminal_code(self):
        """Verify failure summary still has exact schema and stable terminal code."""

        summary = valid_summary()
        summary["verdict"] = live_protocol.FAIL_DESKTOP_CONNECTION
        summary["desktop"]["rootIdentity"] = None
        summary["desktop"]["ownedMemberIdentities"] = []
        summary["desktop"]["jobOwned"] = False
        summary["connection"]["firstMarker"] = None
        summary["connection"]["secondMarker"] = None
        summary["connection"]["desktopIdentityCapturedAt"] = None
        summary["connection"]["firstObservedAt"] = None
        summary["connection"]["barrierWrittenAt"] = None
        summary["connection"]["secondObservedAt"] = None
        summary["connection"]["barrierDigest"] = None
        summary["connection"]["barrierRemoved"] = False
        summary["foxrun"]["baseVerdict"] = None
        summary["foxrun"]["channelEncodings"] = []
        for key in (
            "deliveryObserved",
            "remoteApplied",
            "sameOriginDropped",
            "laterLocalPublished",
        ):
            summary["foxrun"][key] = False
        for key in (
            "jobClosed",
            "processes",
            "port",
            "barrier",
            "files",
            "junctions",
            "subst",
        ):
            summary["cleanup"][key] = False
        summary["cleanup"]["exitedOwnedIdentities"] = []
        summary["cleanup"]["residualOwnedIdentities"] = []

        self.assertIs(
            coordinator.validate_desktop_live_summary(summary),
            summary,
        )
        encoded = json.dumps(summary, sort_keys=True)
        self.assertNotIn("p184g_", encoded)
    def test_final_validation_failure_replaces_an_earlier_terminal_verdict(self):
        """Keep a secondary evidence failure visible after an earlier failure."""

        summary = valid_summary()
        summary["verdict"] = live_protocol.FAIL_DESKTOP_CONNECTION
        summary["connection"]["contextMarker"] = "not-redacted"

        failure = coordinator._reconcile_final_summary_validation(summary)

        self.assertIsNotNone(failure)
        self.assertEqual(live_protocol.FAIL_EVIDENCE, failure.code)
        self.assertEqual(live_protocol.FAIL_EVIDENCE, summary["verdict"])
    def test_injected_success_locks_launch_order_commands_policies_and_evidence(self):
        """Verify injected success locks launch order commands policies and evidence."""

        with temporary_directory("desktop-live-success-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        coordinator.validate_desktop_live_summary(summary)
        self.assertEqual(
            [kind for kind, _record in harness.launches],
            ["base", "desktop"],
        )
        base_launch = harness.launches[0][1]
        desktop_launch = harness.launches[1][1]
        self.assertEqual(
            base_launch["application"],
            str(pathlib.Path(sys.executable)),
        )
        self.assertEqual(
            base_launch["arguments"],
            (
                str(
                    harness.repository
                    / "Scripts"
                    / "smoke"
                    / "foxrun"
                    / "phase184_profile_acceptance.py"
                ),
                "--case",
                "foxglove-profile",
                "--unity-editor",
                str(harness.unity),
                "--foxglove-port",
                str(harness.port),
                "--run-id",
                harness.run_id,
                "--wait-for-desktop-client",
                "--retain-success-workspace",
            ),
        )
        self.assertIs(
            base_launch["policy"],
            job_owner.RootHandoffPolicy.OWNED_PROCESS,
        )
        self.assertNotIn("GITHUB_TOKEN", base_launch["environment"])
        self.assertNotIn("PHASE184G_TOKEN", base_launch["environment"])
        self.assertNotIn("ROS_DISTRO", base_launch["environment"])
        self.assertEqual(
            desktop_launch["application"],
            str(harness.desktop),
        )
        self.assertEqual(
            desktop_launch["arguments"],
            (coordinator.build_deeplink(harness.port),),
        )
        self.assertIs(
            desktop_launch["policy"],
            job_owner.RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE,
        )

        ordered_events = (
            "job:create",
            "job:preflight-no-external",
            "port:release",
            "launch:base",
            "json:read-config",
            "marker:context",
            "marker:initial",
            "launch:desktop",
            "marker:first",
            "barrier:write",
            "marker:second",
            "json:read-summary",
            "desktop:close-request",
            "job:close",
            "identity-exit:verify",
            "barrier:remove",
            "port:probe",
            "summary:write",
        )
        positions = [harness.events.index(event) for event in ordered_events]
        self.assertEqual(positions, sorted(positions))
        self.assertEqual(
            summary["connection"]["initialMarker"].split()[-2:],
            ["active=0", "accepted=0"],
        )
        self.assertEqual(
            summary["connection"]["firstMarker"].split()[-2:],
            ["active=1", "accepted=1"],
        )
        self.assertEqual(
            summary["connection"]["secondMarker"].split()[-2:],
            ["active=2", "accepted=3"],
        )
        self.assertEqual(
            [
                item["pid"]
                for item in summary["desktop"]["ownedMemberIdentities"]
            ],
            [101, 202, 203],
        )
        self.assertEqual(
            harness.barrier_payloads,
            [
                {
                    "schemaVersion": 1,
                    "runId": harness.run_id,
                    "tokenDigest": hashlib.sha256(
                        harness.token.encode("utf-8")
                    ).hexdigest().upper(),
                    "state": "desktop-client-proved",
                    "acceptedClients": 1,
                }
            ],
        )
        self.assertNotIn(
            harness.token,
            json.dumps(summary, sort_keys=True),
        )


__all__ = [name for name in globals() if not name.startswith("__")]
