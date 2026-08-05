#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the Phase184-H Desktop-live coordinator."""

from __future__ import annotations

import argparse
import dataclasses
import datetime as dt
import hashlib
import io
import json
import pathlib
import sys
import tempfile
import unittest
from contextlib import redirect_stderr

from Scripts.smoke.foxrun import phase184_foxglove_cli_install as cli_install
from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_acceptance as coordinator
from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_protocol as live_protocol
from Scripts.smoke.foxrun import phase184_profile_acceptance_protocol as base_protocol
from Scripts.smoke.foxrun import phase184_windows_job_owner as job_owner


ROOT = pathlib.Path(__file__).resolve().parents[4]
COORDINATOR_PATH = (
    ROOT
    / "Scripts"
    / "smoke"
    / "foxrun"
    / "phase184_foxglove_desktop_live_acceptance.py"
)
PHASE184_TEST_ROOT = ROOT / "build" / "Tests" / "Phase184"


def valid_process(pid: int = 101) -> job_owner.ProcessIdentity:
    """Handle the valid process step."""

    return job_owner.ProcessIdentity(
        pid=pid,
        creation_time_100ns=13_400_000_000_000_000 + pid,
        executable=(
            r"D:\Apps\Foxglove\Foxglove.exe"
            if pid != 101
            else r"C:\Python\python.exe"
        ),
    )


def expected_barrier_digest(run_id: str, token_digest: str) -> str:
    """Handle the expected barrier digest step."""

    payload = {
        "acceptedClients": 1,
        "runId": run_id,
        "schemaVersion": 1,
        "state": "desktop-client-proved",
        "tokenDigest": token_digest,
    }
    serialized = (
        json.dumps(
            payload,
            allow_nan=False,
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        )
        + "\n"
    ).encode("utf-8")
    return hashlib.sha256(serialized).hexdigest().upper()


def valid_summary() -> dict[str, object]:
    """Handle the valid summary step."""

    root = valid_process(202)
    member = valid_process(203)
    run_id = "phase184g-20260727-desktop01"
    token_digest = "A" * 64
    return {
        "schemaVersion": 1,
        "identity": {
            "runId": run_id,
            "baseCase": "foxglove-profile",
            "tokenSha256": token_digest,
            "repositoryHead": "a" * 40,
            "windowsVersion": "Microsoft Windows 11 10.0.26100",
            "unityVersion": "6000.3.14f1",
        },
        "cli": {
            "architecture": "windows-amd64",
            "assetUrl": (
                "https://github.com/foxglove/foxglove-cli/releases/"
                "download/v1.2.3/foxglove-windows-amd64.exe"
            ),
            "installedPath": r"C:\Tools\foxglove.exe",
            "installedSha256": "B" * 64,
            "installedVersion": "1.2.3",
            "receiptPath": (
                r"D:\repo\build\phase184\tooling"
                r"\foxglove-cli-install-receipt.json"
            ),
            "releaseTag": "v1.2.3",
        },
        "desktop": {
            "executable": root.executable,
            "fileVersion": "2.9.0.0",
            "sha256": "C" * 64,
            "uriHandler": (
                r'"D:\Apps\Foxglove\Foxglove.exe" "%1"'
            ),
            "dataSource": "foxglove-websocket",
            "deeplink": (
                "foxglove://open?ds=foxglove-websocket&"
                "ds.url=ws%3A%2F%2F127.0.0.1%3A8765%2F"
            ),
            "rootIdentity": coordinator.process_identity_document(root),
            "ownedMemberIdentities": [
                coordinator.process_identity_document(root),
                coordinator.process_identity_document(member),
            ],
            "externalIdentities": [],
            "jobOwned": True,
        },
        "connection": {
            "host": "127.0.0.1",
            "port": 8765,
            "portPreflight": True,
            "contextMarker": (
                "PHASE184G_CONTEXT_READY case=foxglove-profile "
                "token=<redacted> tokenDigest=AAAAAAAAAAAA"
            ),
            "initialMarker": (
                "PHASE184H_TRANSPORT_CLIENTS case=foxglove-profile "
                "token=<redacted> active=0 accepted=0"
            ),
            "firstMarker": (
                "PHASE184H_TRANSPORT_CLIENTS case=foxglove-profile "
                "token=<redacted> active=1 accepted=1"
            ),
            "secondMarker": (
                "PHASE184H_TRANSPORT_CLIENTS case=foxglove-profile "
                "token=<redacted> active=2 accepted=2"
            ),
            "contextObservedAt": 1.0,
            "initialObservedAt": 2.0,
            "desktopIdentityCapturedAt": 3.0,
            "firstObservedAt": 4.0,
            "barrierWrittenAt": 5.0,
            "secondObservedAt": 6.0,
            "barrierPath": (
                r"D:\repo\build\phase184\acceptance"
                r"\phase184g-20260727-desktop01"
                r"\desktop-client-barrier.json"
            ),
            "barrierDigest": expected_barrier_digest(
                run_id,
                token_digest,
            ),
            "barrierRemoved": True,
        },
        "foxrun": {
            "baseSummaryPath": (
                r"D:\repo\build\phase184\acceptance"
                r"\phase184g-20260727-desktop01\summary.json"
            ),
            "baseVerdict": "PASS",
            "channelEncodings": ["json", "protobuf"],
            "deliveryObserved": True,
            "remoteApplied": True,
            "sameOriginDropped": True,
            "laterLocalPublished": True,
        },
        "cleanup": {
            "jobClosed": True,
            "processes": True,
            "port": True,
            "barrier": True,
            "files": True,
            "junctions": True,
            "subst": True,
            "gracefulOwnedIdentities": [
                coordinator.process_identity_document(root)
            ],
            "forcedOwnedIdentities": [
                coordinator.process_identity_document(member)
            ],
            "exitedOwnedIdentities": [
                coordinator.process_identity_document(root),
                coordinator.process_identity_document(member),
            ],
            "residualOwnedIdentities": [],
        },
        "verdict": "PASS",
    }


def temporary_directory(prefix: str):
    """Handle the temporary directory step."""

    PHASE184_TEST_ROOT.mkdir(parents=True, exist_ok=True)
    return tempfile.TemporaryDirectory(prefix=prefix, dir=PHASE184_TEST_ROOT)


def base_run_config(
    repository: pathlib.Path,
    run_id: str,
    token: str,
    port: int,
) -> dict[str, object]:
    """Handle the base run config step."""

    output = (
        repository / "build" / "phase184" / "acceptance" / run_id
    ).resolve()
    profile = "core-foxglove"
    actors = ("foxglove-client",)
    return {
        "schemaVersion": base_protocol.RUN_CONFIG_SCHEMA_VERSION,
        "executionMode": "batch",
        "runId": run_id,
        "token": token,
        "case": "foxglove-profile",
        "profile": profile,
        "projectPath": str((repository / "Unity2Foxglove").resolve()),
        "outputRoot": str(output),
        "rosDistro": "core",
        "rmw": "none",
        "domainId": 48,
        "discoveryRange": "LOCALHOST",
        "zenohTopologyId": "",
        "phase181Workspace": str(
            (
                repository
                / "build"
                / "phase181"
                / profile
                / "peer-workspace"
            ).resolve()
        ),
        "phase181Install": str(
            (
                repository
                / "build"
                / "phase181"
                / profile
                / "peer-workspace"
                / "install"
            ).resolve()
        ),
        "bridgeOverlayInstall": str(
            (output / "bridge-overlay" / "install").resolve()
        ),
        "foxgloveHost": "127.0.0.1",
        "foxglovePort": port,
        "bridgeHost": "127.0.0.1",
        "bridgePort": port + 1,
        "interfacePackage": "unity2foxglove_phase181_v1",
        "interfaceType": (
            "unity2foxglove_phase181_v1/msg/Phase181State"
        ),
        "interfaceDigest": "a" * 64,
        "topics": list(
            base_protocol.CASE_CONTRACTS["foxglove-profile"].topics
        ),
        "observationWindows": {
            "positiveSeconds": 3,
            "negativeSeconds": 3,
            "streamProductionSeconds": 2,
            "terminalSeconds": 30,
            "teardownSeconds": 30,
        },
        "readyFiles": {
            actor: str((output / "ready" / f"{actor}.json").resolve())
            for actor in actors
        },
        "resultFiles": {
            actor: str(
                (output / "results" / f"{actor}.json").resolve()
            )
            for actor in actors
        },
        "unityLog": str((output / "unity-editor.log").resolve()),
    }


def base_pass_summary(config: dict[str, object]) -> dict[str, object]:
    """Handle the base pass summary step."""

    token = str(config["token"])
    return {
        "summarySchemaVersion": base_protocol.SUMMARY_SCHEMA_VERSION,
        "identity": {
            "runId": config["runId"],
            "case": "foxglove-profile",
            "tokenSha256": base_protocol.token_sha256(token),
            "unityVersion": "6000.3.14f1",
            "interfaceIdentity": config["interfaceType"],
            "interfaceDigest": config["interfaceDigest"],
        },
        "profile": {
            "profile": "core-foxglove",
            "runtime": "core",
            "rmw": "none",
            "source": "Foxglove",
            "targets": ["Foxglove"],
            "publishEncoding": "protobuf,json",
            "subscribeEncoding": "protobuf,json",
            "requestedQos": {},
        },
        "foxglove": {
            "applicability": "required",
            "deliveryObserved": True,
            "channelEncodings": ["protobuf", "json"],
            "sampleToken": base_protocol.token_sha256(token),
            "sampleStages": [
                "profile-outbound",
                "json-outbound",
                "profile-a",
                "profile-b",
                "profile-local-after-remote",
            ],
            "timestamp": 42.0,
        },
        "rosGraph": {
            "applicability": "not_applicable",
            "reason": "Foxglove-only case",
        },
        "qos": {
            "applicability": "not_applicable",
            "reason": "No ROS direction",
        },
        "targets": {
            "applicability": "required",
            "states": {"foxglove": "Ready"},
            "diagnosticCounts": {"failedTargets": 0},
            "healthyDelivery": True,
            "statusEvidence": {
                "aggregate": "Ready",
                "succeeded": "Foxglove",
                "failed": "None",
                "topics": 2,
            },
        },
        "origin": {
            "applicability": "required",
            "remoteApplied": True,
            "sameOriginDropped": True,
            "laterLocalPublished": True,
        },
        "stream": {
            "applicability": "not_applicable",
            "reason": "Ordinary fields",
        },
        "processes": [
            {
                "role": "foxglove-client",
                "started": True,
                "exitCode": 0,
                "termination": "self",
            },
            {
                "role": "unity",
                "started": True,
                "exitCode": 0,
                "termination": "self",
            },
        ],
        "cleanup": {
            "processes": True,
            "files": True,
            "junctions": True,
            "subst": True,
        },
        "verdict": "PASS",
    }


class FakeClock:
    """Represent the fake clock contract."""

    def __init__(self):
        """Initialize the fake clock."""

        self.value = 100.0

    def __call__(self) -> float:
        """Handle the call step."""

        self.value += 0.01
        return self.value

    def sleep(self, seconds: float) -> None:
        """Handle the sleep step."""

        self.value += max(float(seconds), 5.0)


class FakeReservation:
    """Represent the fake reservation contract."""

    def __init__(self, harness, port: int):
        """Initialize the fake reservation."""

        self.harness = harness
        self.port = port
        self.released = False

    def release(self) -> None:
        """Release the owned reservation."""

        if not self.released:
            self.released = True
            self.harness.events.append("port:release")


class FakeJobOwner:
    """Represent the fake job owner contract."""

    def __init__(self, harness, desktop_executable: pathlib.Path):
        """Initialize the fake job owner."""

        self.harness = harness
        self.desktop_executable = str(desktop_executable)
        self.base_identity = job_owner.ProcessIdentity(
            pid=101,
            creation_time_100ns=13_400_000_000_000_101,
            executable=str(pathlib.Path(sys.executable)),
        )
        self.desktop_identity = job_owner.ProcessIdentity(
            pid=202,
            creation_time_100ns=13_400_000_000_000_202,
            executable=self.desktop_executable,
        )
        self.desktop_member = job_owner.ProcessIdentity(
            pid=203,
            creation_time_100ns=13_400_000_000_000_203,
            executable=self.desktop_executable,
        )
        self.late_member = job_owner.ProcessIdentity(
            pid=303,
            creation_time_100ns=13_400_000_000_000_303,
            executable=self.desktop_executable,
        )
        self.external_identity = job_owner.ProcessIdentity(
            pid=404,
            creation_time_100ns=13_400_000_000_000_404,
            executable=self.desktop_executable,
        )
        self.base_launched = False
        self.desktop_launched = False
        self.base_exited = False
        self.closed = False
        self._recorded_external: list[job_owner.ProcessIdentity] = []
        self.harness.events.append("job:create")

    @property
    def recorded_external_processes(self):
        """Handle the recorded external processes step."""

        return tuple(self._recorded_external)

    def require_no_external_processes(self, executable=None) -> None:
        """Require no external processes."""

        self.harness.events.append("job:preflight-no-external")
        if self.harness.mode == "preexisting":
            self._recorded_external.append(self.external_identity)
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_DESKTOP_PREFLIGHT,
                "pre-existing exact-path process",
            )

    def launch_suspended_owned(
        self,
        application_path,
        arguments,
        *,
        cwd,
        environment,
        stdout_log,
        stderr_log,
        handoff_policy=None,
    ):
        """Handle the launch suspended owned step."""

        record = {
            "application": str(application_path),
            "arguments": tuple(arguments),
            "cwd": str(cwd),
            "environment": dict(environment),
            "stdout": str(stdout_log),
            "stderr": str(stderr_log),
            "policy": handoff_policy,
        }
        if str(application_path) == str(pathlib.Path(sys.executable)):
            self.harness.events.append("launch:base")
            self.harness.launches.append(("base", record))
            self.base_launched = True
            self.harness.create_base_run()
            return self.base_identity
        if self.harness.mode == "desktop-start-fail":
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_PROCESS_CREATE,
                "injected Desktop start failure",
            )
        self.harness.events.append("launch:desktop")
        self.harness.events.append(
            (
                "desktop:launch-lease-active"
                if self.harness.desktop_lease_active
                else "desktop:launch-lease-inactive"
            )
        )
        self.harness.launches.append(("desktop", record))
        self.desktop_launched = True
        if self.harness.mode == "desktop-identity-mismatch":
            return job_owner.ProcessIdentity(
                pid=202,
                creation_time_100ns=13_400_000_000_000_202,
                executable=r"D:\Other\Foxglove.exe",
            )
        return self.desktop_identity

    def members(self):
        """Handle the members step."""

        if self.closed:
            raise AssertionError("closed Job handle was queried")
        self.harness.events.append("job:members")
        members = []
        if self.base_launched and not self.base_exited:
            members.append(self.base_identity)
        if self.desktop_launched and not self.closed:
            if self.harness.mode != "root-absent":
                members.append(self.desktop_identity)
            members.append(self.desktop_member)
            if (
                self.harness.mode == "late-spawn"
                and self.harness.barrier.is_file()
            ):
                members.append(self.late_member)
        return tuple(members)

    def external_processes(self, executable=None):
        """Handle the external processes step."""

        self.harness.events.append("job:external-scan")
        if (
            self.harness.mode == "external-after-desktop"
            and self.desktop_launched
        ) or (
            self.harness.mode == "late-external"
            and self.harness.barrier.is_file()
        ):
            if not self._recorded_external:
                self._recorded_external.append(self.external_identity)
            return tuple(self._recorded_external)
        return ()

    def require_owned_identity(self, identity):
        """Require owned identity."""

        if (
            self.harness.mode
            in {"external-after-desktop", "late-external"}
            and identity == self.desktop_identity
        ):
            if self.external_processes():
                raise job_owner.OwnershipFailure(
                    job_owner.FAIL_DESKTOP_HANDOFF,
                    "single-instance handoff",
                )
        return identity

    def poll(self, identity):
        """Handle the poll step."""

        if identity != self.base_identity:
            return None
        if self.harness.mode == "base-exit-nonzero":
            self.base_exited = True
            return 7
        if self.harness.second_seen:
            self.harness.write_base_summary()
            self.base_exited = True
            return 0
        return None

    def request_owned_desktop_close(
        self,
        *,
        grace_seconds=10.0,
        reject_external=True,
    ):
        """Handle the request owned desktop close step."""

        self.harness.events.append("desktop:close-request")
        if self.harness.mode == "external-during-close":
            self._recorded_external.append(self.external_identity)
            self.close()
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_DESKTOP_HANDOFF,
                "external process during close",
            )
        self.closed = True
        self.harness.events.append("job:close")
        return job_owner.CloseSummary(
            requested=(self.desktop_identity, self.desktop_member),
            graceful=(self.desktop_identity,),
            forced=(self.desktop_member,),
        )

    def close(self) -> None:
        """Close all resources owned by this helper."""

        if not self.closed:
            self.closed = True
            self.harness.events.append("job:close")


class CoordinatorHarness:
    """Represent the coordinator harness contract."""

    def __init__(self, repository: pathlib.Path, *, mode: str = "success"):
        """Initialize the coordinator harness."""

        self.repository = repository.resolve()
        self.mode = mode
        self.run_id = "phase184g-20260727-desktop01"
        self.token = "p184g_A1b2C3d4E5f6"
        self.port = 8765
        self.events: list[str] = []
        self.launches: list[tuple[str, dict[str, object]]] = []
        self.clock = FakeClock()
        self.owner: FakeJobOwner | None = None
        self.second_seen = False
        self.summary_writes: list[pathlib.Path] = []
        self.barrier_payloads: list[dict[str, object]] = []
        self._noted_events: set[str] = set()
        self.pre_desktop_log_reads = 0
        self.exit_verifier_inputs: list[
            tuple[job_owner.ProcessIdentity, ...]
        ] = []
        self.desktop_lease_active = False
        self.desktop_lease_snapshot_count = 0
        self.base_ready_at: float | None = None
        self.context_ready_at: float | None = None
        self._prepare_files()

    @property
    def output(self) -> pathlib.Path:
        """Handle the output step."""

        return (
            self.repository
            / "build"
            / "phase184"
            / "acceptance"
            / self.run_id
        )

    @property
    def coordinator_output(self) -> pathlib.Path:
        """Handle the coordinator output step."""

        return (
            self.repository
            / "build"
            / "phase184"
            / "desktop-live"
            / self.run_id
        )

    @property
    def barrier(self) -> pathlib.Path:
        """Handle the barrier step."""

        return self.output / live_protocol.DESKTOP_CLIENT_BARRIER_FILENAME

    @property
    def base_ready(self) -> pathlib.Path:
        """Handle the base ready step."""

        return self.output / "ready" / "foxglove-client.json"

    def _write_file(self, relative: str, payload: bytes = b"x") -> pathlib.Path:
        """Write file."""

        path = self.repository / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
        return path

    def _prepare_files(self) -> None:
        """Prepare files."""

        self.unity = self._write_file("tools/Unity.exe")
        self.cli = self._write_file("tools/foxglove.exe", b"cli")
        self.desktop = self._write_file(
            "tools/Foxglove.exe",
            b"desktop",
        )
        self.receipt = self._write_file(
            "build/phase184/tooling/foxglove-cli-install-receipt.json",
            b"{}",
        )
        self._write_file(
            "Scripts/smoke/foxrun/phase184_profile_acceptance.py",
            b"# fake base runner\n",
        )

    def args(self) -> argparse.Namespace:
        """Handle the args step."""

        return argparse.Namespace(
            unity_editor=self.unity,
            foxglove_cli=self.cli,
            desktop_executable=self.desktop,
            cli_receipt=self.receipt,
            foxglove_port=self.port,
            run_id=self.run_id,
        )

    def create_base_run(self) -> None:
        """Handle the create base run step."""

        self.output.mkdir(parents=True, exist_ok=False)
        (self.output / "ready").mkdir()
        (self.output / "results").mkdir()
        config = base_run_config(
            self.repository,
            self.run_id,
            self.token,
            self.port,
        )
        base_protocol.validate_run_config(config, self.repository)
        (self.output / "run-config.json").write_text(
            json.dumps(config, sort_keys=True),
            encoding="utf-8",
        )
        pathlib.Path(str(config["unityLog"])).write_text(
            "",
            encoding="utf-8",
        )
        if self.mode == "delayed-base-ready":
            self.base_ready_at = (
                self.clock.value
                + coordinator.CONNECTION_TIMEOUT_SECONDS
                + 30.0
            )
        elif self.mode != "missing-base-ready":
            self.write_base_ready(config)
        if self.mode == "delayed-context":
            self.context_ready_at = (
                self.clock.value
                + coordinator.CONNECTION_TIMEOUT_SECONDS
                + 30.0
            )

    def write_base_ready(
        self,
        config: dict[str, object] | None = None,
    ) -> None:
        """Write base ready."""

        if self.base_ready.exists():
            return
        if config is None:
            config = json.loads(
                (self.output / "run-config.json").read_text(
                    encoding="utf-8"
                )
            )
        token_digest = base_protocol.token_sha256(
            str(config["token"])
        )
        if self.mode == "stale-base-ready":
            token_digest = "f" * 64
        details: object = {
            "state": "connect-loop-ready",
            "host": "loopback",
            "topicCount": len(config["topics"]),
        }
        if self.mode == "malformed-base-ready":
            details = {"state": "scene-builder-running"}
        payload = {
            "schemaVersion": (
                True
                if self.mode == "boolean-base-ready-schema"
                else 1
            ),
            "runId": config["runId"],
            "case": config["case"],
            "role": "foxglove-client",
            "tokenSha256": token_digest,
            "ready": True,
            "details": details,
        }
        self.base_ready.write_text(
            json.dumps(payload, sort_keys=True),
            encoding="utf-8",
        )
        self.events.append("base:ready")

    def write_base_summary(self) -> None:
        """Write base summary."""

        path = self.output / "summary.json"
        if path.exists():
            return
        config = json.loads(
            (self.output / "run-config.json").read_text(encoding="utf-8")
        )
        summary = base_pass_summary(config)
        if self.mode == "base-summary-fail":
            summary["verdict"] = "FAIL_TERMINAL"
            summary["foxglove"]["deliveryObserved"] = False
        path.write_text(
            json.dumps(summary, sort_keys=True),
            encoding="utf-8",
        )

    def read_log_lines(self, path: pathlib.Path, max_bytes: int):
        """Read log lines."""

        self.events.append("log:read")
        if (
            self.mode
            in {"delayed-base-ready", "missing-base-ready"}
            and not self.base_ready.exists()
        ):
            return ()
        if self.mode == "missing-context":
            return ()
        if (
            self.mode == "delayed-context"
            and self.context_ready_at is not None
            and self.clock.value < self.context_ready_at
        ):
            return ()
        case = "foxglove-profile"
        token = self.token
        if self.mode == "wrong-token":
            token = "p184g_Z9y8X7w6V5u4"
        if self.mode == "wrong-case":
            case = "multi-target"
        lines = [
            (
                f"PHASE184G_CONTEXT_READY case={case} token={token} "
                f"tokenDigest={hashlib.sha256(token.encode()).hexdigest()[:12]}"
            ),
            (
                f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                f"case={case} token={token} active=0 accepted=0"
            ),
        ]
        if self.mode == "context-after-initial":
            lines.reverse()
        if self.owner is not None and not self.owner.desktop_launched:
            self.pre_desktop_log_reads += 1
            if (
                self.mode == "unstable-initial"
                and self.pre_desktop_log_reads >= 2
            ):
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                    f"case={case} token={token} active=1 accepted=1"
                )
        self._event_once("marker:context")
        self._event_once("marker:initial")
        if self.owner is not None and self.owner.desktop_launched:
            if self.mode == "overflow":
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_OVERFLOW_MARKER} "
                    f"case={case} token={token} active=1 accepted=1"
                )
            elif self.mode == "wrong-order":
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                    f"case={case} token={token} active=2 accepted=2"
                )
            elif self.mode != "missing-first":
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                    f"case={case} token={token} active=1 accepted=1"
                )
                self._event_once("marker:first")
                if self.mode == "transport-regression":
                    lines.append(
                        f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                        f"case={case} token={token} active=0 accepted=0"
                    )
        if self.barrier.is_file():
            lines.append(
                f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                f"case={case} token={token} active=2 accepted=3"
            )
            self.second_seen = True
            self._event_once("marker:second")
        return tuple(lines)

    def _event_once(self, event: str) -> None:
        """Handle the event once step."""

        if event not in self._noted_events:
            self._noted_events.add(event)
            self.events.append(event)

    def load_json_snapshot(self, path: pathlib.Path, max_bytes: int):
        """Load JSON snapshot."""

        self.events.append(
            "json:read-summary"
            if pathlib.Path(path).name == "summary.json"
            else "json:read-config"
        )
        raw = pathlib.Path(path).read_bytes()
        if not raw or len(raw) > max_bytes:
            raise ValueError("bounded JSON read failed")
        return json.loads(raw.decode("utf-8"))

    def write_json_atomic(
        self,
        path: pathlib.Path,
        payload,
        *,
        max_bytes: int,
    ) -> None:
        """Write JSON atomic."""

        path = pathlib.Path(path)
        self.events.append(
            "summary:write"
            if path.name == coordinator.DESKTOP_LIVE_SUMMARY_FILENAME
            else "barrier:write"
        )
        if path.name != coordinator.DESKTOP_LIVE_SUMMARY_FILENAME:
            self.barrier_payloads.append(dict(payload))
        live_protocol.write_json_atomic(
            path,
            payload,
            max_bytes=max_bytes,
        )
        if path.name == coordinator.DESKTOP_LIVE_SUMMARY_FILENAME:
            self.summary_writes.append(path)

    def remove_owned_file(self, path: pathlib.Path) -> bool:
        """Remove owned file."""

        self.events.append("barrier:remove")
        try:
            pathlib.Path(path).unlink()
        except FileNotFoundError:
            pass
        return not pathlib.Path(path).exists()

    def verify_cli(self, install_path, receipt_path):
        """Handle the verify CLI step."""

        self.events.append("cli:verify")
        if self.mode == "cli-fail":
            raise live_protocol.AcceptanceFailure(
                live_protocol.FAIL_CLI_PROVENANCE,
                "injected CLI provenance failure",
            )
        return cli_install.VerifiedCliIdentity(
            installed_path=str(install_path),
            installed_version="1.2.3",
            installed_sha256=live_protocol.sha256_file(install_path),
            release_tag="v1.2.3",
            asset_url=(
                "https://github.com/foxglove/foxglove-cli/releases/"
                "download/v1.2.3/foxglove-windows-amd64.exe"
            ),
            architecture="windows-amd64",
            receipt_path=str(receipt_path),
        )

    def read_desktop_version(self, _path):
        """Read desktop version."""

        if self.mode == "desktop-version-fail":
            raise OSError("injected version failure")
        if self.mode == "desktop-version-race":
            self.desktop.write_bytes(b"desktop replaced after version")
        return "2.9.0.0"

    def hash_file(self, path):
        """Handle the hash file step."""

        if (
            self.mode == "desktop-hash-fail"
            and pathlib.Path(path) == self.desktop
        ):
            raise OSError("injected hash failure")
        return live_protocol.sha256_file(path)

    def read_uri_handler(self):
        """Read URI handler."""

        if self.mode == "desktop-handler-fail":
            return r'"D:\Other\Foxglove.exe" "%1"'
        if self.mode == "desktop-handler-race":
            self.desktop.write_bytes(b"desktop replaced after handler")
        return f'"{self.desktop}" "%1"'

    class _DesktopExecutableLease:
        """Represent the desktop executable lease contract."""

        def __init__(self, harness):
            """Initialize the desktop executable lease."""

            self.harness = harness
            self.active = False

        def __enter__(self):
            """Enter the desktop executable lease context."""

            self.active = True
            self.harness.desktop_lease_active = True
            self.harness.events.append("desktop-lease:open")
            return self

        def __exit__(self, exc_type, exc, traceback):
            """Exit the desktop executable lease context without suppressing failures."""

            del exc_type, exc, traceback
            if self.active:
                self.active = False
                self.harness.desktop_lease_active = False
                self.harness.events.append("desktop-lease:close")
            return False

        def _identity(self):
            """Handle the identity step."""

            info = self.harness.desktop.stat()
            return cli_install.ExecutableFileIdentity(
                volume_serial=int(info.st_dev),
                file_id=int(info.st_ino),
            )

        def snapshot(self):
            """Handle the snapshot step."""

            self.harness.desktop_lease_snapshot_count += 1
            count = self.harness.desktop_lease_snapshot_count
            if (
                self.harness.mode == "desktop-updater-before-launch"
                and count == 4
            ):
                self.harness.desktop.write_bytes(
                    b"desktop updater before launch"
                )
            if (
                self.harness.mode == "desktop-updater-after-launch"
                and count == 5
            ):
                self.harness.desktop.write_bytes(
                    b"desktop updater after launch"
                )
            if (
                self.harness.mode == "desktop-lease-interrupt"
                and count == 4
            ):
                raise KeyboardInterrupt("injected lease interruption")
            self.harness.events.append("desktop-lease:snapshot")
            return cli_install.ExecutableSnapshot(
                identity=self._identity(),
                sha256=self.harness.hash_file(self.harness.desktop),
            )

        def path_identity(self):
            """Handle the path identity step."""

            self.harness.events.append("desktop-lease:path-identity")
            return self._identity()

    def make_desktop_lease(self, _path):
        """Build desktop lease."""

        if self.mode == "desktop-reparse":
            raise live_protocol.AcceptanceFailure(
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                "injected reparse component",
            )
        return self._DesktopExecutableLease(self)

    def make_owner(self, desktop_executable):
        """Build owner."""

        if self.mode == "job-create-fail":
            self.events.append("job:create-fail")
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_JOB_CREATE,
                "injected outer Job creation failure",
            )
        self.owner = FakeJobOwner(self, pathlib.Path(desktop_executable))
        return self.owner

    def verify_identity_exits(self, identities, timeout_seconds):
        """Handle the verify identity exits step."""

        frozen = tuple(identities)
        self.events.append("identity-exit:verify")
        self.exit_verifier_inputs.append(frozen)
        assert (
            timeout_seconds
            == coordinator.IDENTITY_EXIT_TIMEOUT_SECONDS
        )
        if self.owner is not None and not self.owner.closed:
            raise AssertionError("identity exits checked before Job close")
        residual = (
            (frozen[-1],)
            if self.mode == "residual-owned-process" and frozen
            else ()
        )
        exited = tuple(
            identity for identity in frozen if identity not in residual
        )
        return coordinator.IdentityExitVerification(
            exited=exited,
            residual=residual,
        )

    def dependencies(self):
        """Handle the dependencies step."""

        arguments = {
            "repository_root": self.repository,
            "platform_name": "nt",
            "environment": {
                "SystemRoot": r"C:\Windows",
                "PATH": r"C:\Windows\System32",
                "TEMP": str(self.repository / "temp"),
                "GITHUB_TOKEN": "secret",
                "PHASE184G_TOKEN": self.token,
                "ROS_DISTRO": "jazzy",
            },
            "clock": self.clock,
            "sleep": self.clock.sleep,
            "utc_now": lambda: dt.datetime(
                2026,
                7,
                27,
                15,
                30,
                45,
                tzinfo=dt.timezone.utc,
            ),
            "nonce": lambda: "a1b2c3d4e5",
            "is_file": lambda path: pathlib.Path(path).is_file(),
            "path_exists": self.path_exists,
            "make_directory": lambda path: pathlib.Path(path).mkdir(
                parents=True,
                exist_ok=False,
            ),
            "verify_cli": self.verify_cli,
            "sha256_file": self.hash_file,
            "read_desktop_file_version": self.read_desktop_version,
            "read_uri_handler_command": self.read_uri_handler,
            "parse_windows_command_line": coordinator.parse_windows_command_line,
            "read_repository_head": lambda _root: "a" * 40,
            "read_windows_version": lambda: "Microsoft Windows 11 10.0.26100",
            "reserve_port": lambda requested: self._reserve_port(requested),
            "port_is_bindable": lambda host, port: self._port_is_bindable(
                host,
                port,
            ),
            "job_owner_factory": self.make_owner,
            "verify_identities_exited": self.verify_identity_exits,
            "read_log_lines": self.read_log_lines,
            "coordinator_logs_within_bound": (
                lambda _paths, _bound: self.mode
                != "coordinator-log-overflow"
            ),
            "load_json_snapshot": self.load_json_snapshot,
            "write_json_atomic": self.write_json_atomic,
            "remove_owned_file": self.remove_owned_file,
        }
        dependency_fields = {
            field.name
            for field in dataclasses.fields(
                coordinator.CoordinatorDependencies
            )
        }
        if "desktop_executable_lease_factory" in dependency_fields:
            arguments["desktop_executable_lease_factory"] = (
                self.make_desktop_lease
            )
        return coordinator.CoordinatorDependencies(
            **arguments,
        )

    def path_exists(self, path):
        """Handle the path exists step."""

        candidate = pathlib.Path(path)
        if (
            self.mode == "base-ready-path-error"
            and candidate == self.base_ready
        ):
            raise OSError("injected readiness path failure")
        if (
            self.mode == "delayed-base-ready"
            and candidate == self.base_ready
            and not candidate.exists()
            and self.base_ready_at is not None
            and self.clock.value >= self.base_ready_at
        ):
            self.write_base_ready()
        return candidate.exists()

    def _reserve_port(self, requested):
        """Handle the reserve port step."""

        self.events.append("port:reserve")
        if self.mode == "busy-port":
            raise live_protocol.AcceptanceFailure(
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                "injected busy explicit loopback port",
            )
        self.assert_loopback_port(requested)
        return FakeReservation(self, self.port)

    def assert_loopback_port(self, requested):
        """Handle the assert loopback port step."""

        if requested is not None:
            assert requested == self.port

    def _port_is_bindable(self, host, port):
        """Handle the port is bindable step."""

        self.events.append("port:probe")
        return (
            host == "127.0.0.1"
            and port == self.port
            and self.mode != "cleanup-port-busy"
        )


class Phase184FoxgloveDesktopLiveAcceptanceTests(unittest.TestCase):
    """Lock the coordinator into the focused, owned Windows live route."""

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

    def test_connection_window_starts_after_current_client_readiness(self):
        """Verify connection window starts after current client readiness."""

        with temporary_directory("desktop-live-delayed-ready-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="delayed-base-ready",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        self.assertIn("base:ready", harness.events)
        self.assertLess(
            harness.events.index("base:ready"),
            harness.events.index("marker:context"),
        )
        self.assertGreater(
            harness.clock.value,
            100.0 + coordinator.CONNECTION_TIMEOUT_SECONDS,
        )

    def test_context_readiness_uses_cold_start_budget_before_connection_window(self):
        """Verify a bounded cold Unity start does not consume connection time."""

        with temporary_directory("desktop-live-delayed-context-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="delayed-context",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        self.assertIn("marker:context", harness.events)
        self.assertGreater(
            harness.clock.value,
            100.0 + coordinator.CONNECTION_TIMEOUT_SECONDS,
        )

    def test_base_readiness_is_required_and_exactly_correlated(self):
        """Verify base readiness is required and exactly correlated."""

        for mode in (
            "missing-base-ready",
            "stale-base-ready",
            "malformed-base-ready",
            "boolean-base-ready-schema",
            "base-ready-path-error",
        ):
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(
                    summary["verdict"],
                    live_protocol.FAIL_FOXRUN_CHILD,
                )
                self.assertNotIn(
                    "desktop",
                    [kind for kind, _record in harness.launches],
                )

    def test_success_records_graceful_forced_cleanup_and_removes_barrier(self):
        """Verify success records graceful forced cleanup and removes barrier."""

        with temporary_directory("desktop-live-cleanup-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

            self.assertFalse(harness.barrier.exists())

        self.assertTrue(summary["cleanup"]["jobClosed"])
        self.assertTrue(summary["cleanup"]["port"])
        self.assertTrue(summary["cleanup"]["barrier"])
        self.assertEqual(
            [item["pid"] for item in summary["cleanup"]["gracefulOwnedIdentities"]],
            [202],
        )
        self.assertEqual(
            [item["pid"] for item in summary["cleanup"]["forcedOwnedIdentities"]],
            [203],
        )
        self.assertEqual(
            {
                item["pid"]
                for item in summary["cleanup"]["exitedOwnedIdentities"]
            },
            {
                item["pid"]
                for item in summary["desktop"]["ownedMemberIdentities"]
            },
        )
        self.assertEqual(
            summary["cleanup"]["residualOwnedIdentities"],
            [],
        )
        self.assertEqual(summary["desktop"]["externalIdentities"], [])

    def test_all_captured_members_are_identity_verified_after_job_close(self):
        """Verify all captured members are identity verified after job close."""

        with temporary_directory("desktop-live-exit-proof-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        self.assertEqual(len(harness.exit_verifier_inputs), 1)
        self.assertLess(
            harness.events.index("job:close"),
            harness.events.index("identity-exit:verify"),
        )
        captured = {
            (
                identity.pid,
                identity.creation_time_100ns,
                identity.executable,
            )
            for identity in harness.exit_verifier_inputs[0]
        }
        summarized = {
            (
                item["pid"],
                item["creationTime100ns"],
                item["executable"],
            )
            for item in summary["desktop"]["ownedMemberIdentities"]
        }
        self.assertEqual(captured, summarized)

    def test_residual_owned_member_forces_cleanup_failure(self):
        """Verify residual owned member forces cleanup failure."""

        with temporary_directory("desktop-live-residual-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="residual-owned-process",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], live_protocol.FAIL_CLEANUP)
        self.assertFalse(summary["cleanup"]["processes"])
        self.assertEqual(
            len(summary["cleanup"]["residualOwnedIdentities"]),
            1,
        )

    def test_late_spawn_is_refreshed_recorded_and_exit_verified(self):
        """Verify late spawn is refreshed recorded and exit verified."""

        with temporary_directory("desktop-live-late-spawn-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="late-spawn",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], "PASS")
        owned_pids = {
            item["pid"]
            for item in summary["desktop"]["ownedMemberIdentities"]
        }
        self.assertIn(303, owned_pids)
        self.assertIn(
            303,
            {
                item["pid"]
                for item in summary["cleanup"]["exitedOwnedIdentities"]
            },
        )
        self.assertGreaterEqual(harness.events.count("job:members"), 3)
        close_index = harness.events.index("desktop:close-request")
        final_member_index = max(
            index
            for index, event in enumerate(harness.events)
            if event == "job:members"
        )
        self.assertLess(final_member_index, close_index)
        self.assertGreater(
            final_member_index,
            harness.events.index("json:read-summary"),
        )

    def test_preexisting_exact_path_process_rejects_before_any_root(self):
        """Verify preexisting exact path process rejects before any root."""

        with temporary_directory("desktop-live-preexisting-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="preexisting",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(
            summary["verdict"],
            live_protocol.FAIL_DESKTOP_PREFLIGHT,
        )
        self.assertEqual(harness.launches, [])
        self.assertNotIn("desktop:close-request", harness.events)
        self.assertIn("job:close", harness.events)
        self.assertEqual(
            [item["pid"] for item in summary["desktop"]["externalIdentities"]],
            [404],
        )

    def test_external_single_instance_and_close_races_are_never_close_targets(self):
        """Verify external single instance and close races are never close targets."""

        for mode in (
            "external-after-desktop",
            "late-external",
            "external-during-close",
        ):
            with self.subTest(mode=mode):
                with temporary_directory(f"desktop-live-{mode}-") as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(
                    summary["verdict"],
                    live_protocol.FAIL_DESKTOP_IDENTITY,
                )
                self.assertEqual(
                    [item["pid"] for item in summary["desktop"]["externalIdentities"]],
                    [404],
                )
                self.assertIn("job:close", harness.events)
                if mode in {
                    "external-after-desktop",
                    "late-external",
                }:
                    self.assertNotIn("desktop:close-request", harness.events)

    def test_transport_and_child_failure_modes_have_stable_terminal_classes(self):
        """Verify transport and child failure modes have stable terminal classes."""

        expected = {
            "cli-fail": live_protocol.FAIL_CLI_PROVENANCE,
            "job-create-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "busy-port": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-version-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-hash-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-handler-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-start-fail": live_protocol.FAIL_DESKTOP_START,
            "root-absent": live_protocol.FAIL_DESKTOP_IDENTITY,
            "unstable-initial": live_protocol.FAIL_DESKTOP_CONNECTION,
            "context-after-initial": (
                live_protocol.FAIL_DESKTOP_CONNECTION
            ),
            "transport-regression": (
                live_protocol.FAIL_DESKTOP_CONNECTION
            ),
            "wrong-token": live_protocol.FAIL_DESKTOP_CONNECTION,
            "wrong-case": live_protocol.FAIL_DESKTOP_CONNECTION,
            "wrong-order": live_protocol.FAIL_DESKTOP_CONNECTION,
            "overflow": live_protocol.FAIL_DESKTOP_CONNECTION,
            "coordinator-log-overflow": (
                live_protocol.FAIL_DESKTOP_CONNECTION
            ),
            "missing-context": live_protocol.FAIL_DESKTOP_CONNECTION,
            "missing-first": live_protocol.FAIL_DESKTOP_CONNECTION,
            "base-exit-nonzero": live_protocol.FAIL_FOXRUN_CHILD,
            "base-summary-fail": live_protocol.FAIL_EVIDENCE,
        }
        for mode, failure_code in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(f"desktop-live-{mode}-") as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(summary["verdict"], failure_code)
                self.assertEqual(len(harness.summary_writes), 1)
                self.assertNotIn(
                    harness.token,
                    json.dumps(summary, sort_keys=True),
                )

    def test_job_create_and_desktop_preflight_failures_never_launch_desktop(self):
        """Verify job create and desktop preflight failures never launch desktop."""

        expected = {
            "job-create-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "busy-port": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-version-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-hash-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
            "desktop-handler-fail": live_protocol.FAIL_DESKTOP_PREFLIGHT,
        }
        for mode, failure_code in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-preflight-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(summary["verdict"], failure_code)
                self.assertNotIn(
                    "desktop",
                    [kind for kind, _record in harness.launches],
                )

    def test_desktop_executable_reparse_and_updater_races_fail_closed(self):
        """Verify desktop executable reparse and updater races fail closed."""

        expected = {
            "desktop-reparse": (
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                False,
            ),
            "desktop-version-race": (
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                False,
            ),
            "desktop-handler-race": (
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                False,
            ),
            "desktop-updater-before-launch": (
                live_protocol.FAIL_DESKTOP_IDENTITY,
                False,
            ),
            "desktop-updater-after-launch": (
                live_protocol.FAIL_DESKTOP_IDENTITY,
                True,
            ),
        }
        for mode, (failure_code, desktop_started) in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-integrity-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(failure_code, summary["verdict"])
                self.assertEqual(
                    desktop_started,
                    any(
                        kind == "desktop"
                        for kind, _record in harness.launches
                    ),
                )
                self.assertNotEqual("PASS", summary["verdict"])
                self.assertFalse(harness.desktop_lease_active)
                if mode != "desktop-reparse":
                    self.assertIn("desktop-lease:close", harness.events)

    def test_desktop_process_identity_is_bound_before_lease_release(self):
        """Verify desktop process identity is bound before lease release."""

        with temporary_directory("desktop-live-identity-bind-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="desktop-identity-mismatch",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(
            live_protocol.FAIL_DESKTOP_IDENTITY,
            summary["verdict"],
        )
        self.assertIn("job:close", harness.events)
        self.assertIn("desktop-lease:close", harness.events)
        self.assertLess(
            harness.events.index("launch:desktop"),
            harness.events.index("desktop-lease:close"),
        )

    def test_desktop_lease_baseexception_releases_before_cleanup_finishes(self):
        """Verify desktop lease baseexception releases before cleanup finishes."""

        with temporary_directory("desktop-live-lease-interrupt-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="desktop-lease-interrupt",
            )

            with self.assertRaises(KeyboardInterrupt):
                coordinator.run_acceptance(
                    harness.args(),
                    dependencies=harness.dependencies(),
                )

        self.assertFalse(harness.desktop_lease_active)
        self.assertIn("desktop-lease:close", harness.events)
        self.assertIn("job:close", harness.events)

    def test_success_launches_desktop_while_production_shaped_lease_is_active(self):
        """Verify success launches desktop while production shaped lease is active."""

        with temporary_directory("desktop-live-lease-shape-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual("PASS", summary["verdict"])
        self.assertIn("desktop:launch-lease-active", harness.events)
        dependency_fields = {
            field.name
            for field in dataclasses.fields(
                coordinator.CoordinatorDependencies
            )
        }
        self.assertIn(
            "desktop_executable_lease_factory",
            dependency_fields,
        )
        self.assertIs(
            cli_install.WindowsExecutableLease,
            coordinator._default_dependencies().desktop_executable_lease_factory,
        )

    def test_root_absence_and_unstable_zero_state_fail_before_desktop_proof(self):
        """Verify root absence and unstable zero state fail before desktop proof."""

        expected = {
            "root-absent": live_protocol.FAIL_DESKTOP_IDENTITY,
            "unstable-initial": live_protocol.FAIL_DESKTOP_CONNECTION,
        }
        for mode, failure_code in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-proof-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(summary["verdict"], failure_code)
                self.assertNotIn("desktop:close-request", harness.events)
                if mode == "unstable-initial":
                    self.assertNotIn(
                        "desktop",
                        [kind for kind, _record in harness.launches],
                    )

    def test_raw_context_order_and_transport_regression_never_pass(self):
        """Verify raw context order and transport regression never pass."""

        expected = {
            "context-after-initial": False,
            "transport-regression": True,
        }
        for mode, desktop_started in expected.items():
            with self.subTest(mode=mode):
                with temporary_directory(
                    f"desktop-live-chronology-{mode}-"
                ) as temporary:
                    harness = CoordinatorHarness(
                        pathlib.Path(temporary),
                        mode=mode,
                    )

                    summary = coordinator.run_acceptance(
                        harness.args(),
                        dependencies=harness.dependencies(),
                    )

                self.assertEqual(
                    live_protocol.FAIL_DESKTOP_CONNECTION,
                    summary["verdict"],
                )
                self.assertEqual(
                    desktop_started,
                    any(
                        kind == "desktop"
                        for kind, _record in harness.launches
                    ),
                )

    def test_base_summary_is_validated_before_desktop_close(self):
        """Verify base summary is validated before desktop close."""

        with temporary_directory("desktop-live-summary-order-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertLess(
            harness.events.index("json:read-summary"),
            harness.events.index("desktop:close-request"),
        )

    def test_barrier_written_only_after_first_client_and_second_follows_barrier(self):
        """Verify barrier written only after first client and second follows barrier."""

        with temporary_directory("desktop-live-barrier-order-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        connection = summary["connection"]
        self.assertLess(
            connection["desktopIdentityCapturedAt"],
            connection["firstObservedAt"],
        )
        self.assertLess(
            connection["firstObservedAt"],
            connection["barrierWrittenAt"],
        )
        self.assertLess(
            connection["barrierWrittenAt"],
            connection["secondObservedAt"],
        )
        self.assertGreater(
            harness.events.index("barrier:write"),
            harness.events.index("launch:desktop"),
        )

    def test_failure_after_barrier_still_closes_job_and_removes_only_owned_barrier(self):
        """Verify failure after barrier still closes job and removes only owned barrier."""

        with temporary_directory("desktop-live-failure-cleanup-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="base-summary-fail",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )
            barrier_exists = harness.barrier.exists()

        self.assertEqual(summary["verdict"], live_protocol.FAIL_EVIDENCE)
        self.assertFalse(barrier_exists)
        self.assertTrue(summary["cleanup"]["jobClosed"])
        self.assertTrue(summary["connection"]["barrierRemoved"])
        self.assertTrue(summary["cleanup"]["barrier"])
        self.assertNotIn("desktop:close-request", harness.events)

    def test_cleanup_port_failure_prevents_pass(self):
        """Verify cleanup port failure prevents pass."""

        with temporary_directory("desktop-live-port-busy-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="cleanup-port-busy",
            )

            summary = coordinator.run_acceptance(
                harness.args(),
                dependencies=harness.dependencies(),
            )

        self.assertEqual(summary["verdict"], live_protocol.FAIL_CLEANUP)
        self.assertFalse(summary["cleanup"]["port"])

    def test_main_returns_zero_only_for_validated_pass(self):
        """Verify main returns zero only for validated pass."""

        with temporary_directory("desktop-live-main-pass-") as temporary:
            harness = CoordinatorHarness(pathlib.Path(temporary))
            argv = [
                "--unity-editor",
                str(harness.unity),
                "--foxglove-cli",
                str(harness.cli),
                "--desktop-executable",
                str(harness.desktop),
                "--cli-receipt",
                str(harness.receipt),
                "--foxglove-port",
                str(harness.port),
                "--run-id",
                harness.run_id,
            ]
            self.assertEqual(
                coordinator.main(argv, dependencies=harness.dependencies()),
                0,
            )

        with temporary_directory("desktop-live-main-fail-") as temporary:
            harness = CoordinatorHarness(
                pathlib.Path(temporary),
                mode="wrong-token",
            )
            argv = [
                "--unity-editor",
                str(harness.unity),
                "--foxglove-cli",
                str(harness.cli),
                "--desktop-executable",
                str(harness.desktop),
                "--cli-receipt",
                str(harness.receipt),
                "--foxglove-port",
                str(harness.port),
                "--run-id",
                harness.run_id,
            ]
            self.assertEqual(
                coordinator.main(argv, dependencies=harness.dependencies()),
                1,
            )


if __name__ == "__main__":
    unittest.main()
