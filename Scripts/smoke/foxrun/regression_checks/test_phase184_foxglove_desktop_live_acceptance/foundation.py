#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the Phase184-H Desktop-live coordinator."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
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


__all__ = [name for name in globals() if not name.startswith("__")]
