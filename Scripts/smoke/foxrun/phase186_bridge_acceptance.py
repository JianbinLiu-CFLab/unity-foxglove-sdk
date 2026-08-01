#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Fail-closed Phase186-H Bridge acceptance coordinator.

The coordinator owns current-run identity, exact repository/Unity/ROS
preflight, IPv4 loopback reservations, evidence paths, actor lifetime, terminal
classification, and cleanup.  A build or tooling PASS is deliberately never
promoted into a live PASS.
"""

from __future__ import annotations

import argparse
import contextlib
import dataclasses
import hashlib
import json
import os
import pathlib
import re
import secrets
import socket
import subprocess
import sys
import tempfile
import time
from collections.abc import Mapping, Sequence
from typing import Any


SCRIPT_DIRECTORY = pathlib.Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

try:
    from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
except ImportError:  # Direct script execution from outside the repository root.
    import phase186_bridge_acceptance_protocol as protocol


EXIT_PASS = 0
EXIT_FAIL = 1
EXIT_USAGE = 2
EXIT_NOT_RUN = 3
MAX_RESCUE_LOG_BYTES = 4 * 1024 * 1024
_UNITY_VERSION = re.compile(r"\A[0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+\Z")


class AcceptanceFailure(protocol.ProtocolFailure):
    """Stable coordinator failure."""


class LivePrerequisiteMissing(AcceptanceFailure):
    """A specifically named prerequisite is not provisioned."""


@dataclasses.dataclass(frozen=True)
class UnityEditorIdentity:
    """Exact Editor executable selected by the project version."""

    path: pathlib.Path
    version: str


@dataclasses.dataclass(frozen=True)
class InstalledUnityRunBinding:
    """Exact transient source owned by one Phase186 acceptance run."""

    path: pathlib.Path
    sha256: str


@dataclasses.dataclass
class LoopbackPortReservation:
    """One held IPv4 loopback socket reservation."""

    socket: socket.socket
    host: str
    port: int

    def close(self) -> None:
        self.socket.close()

    def __enter__(self) -> "LoopbackPortReservation":
        return self

    def __exit__(self, _type, _value, _traceback) -> None:
        self.close()


def repository_root() -> pathlib.Path:
    """Locate the repository without walking local ROS junctions."""

    for candidate in (SCRIPT_DIRECTORY, *SCRIPT_DIRECTORY.parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    raise AcceptanceFailure("FAIL_PREFLIGHT", "repository root could not be located")


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse the bounded parent/worker surface."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", choices=tuple(protocol.CASES), required=True)
    parser.add_argument("--manual", action="store_true")
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--output-root", type=pathlib.Path, required=True)
    parser.add_argument("--unity-editor", type=pathlib.Path)
    parser.add_argument("--run-id")
    parser.add_argument("--bridge-port", type=int)
    parser.add_argument("--domain-id", type=int)
    parser.add_argument(
        "--preflight-only",
        action="store_true",
        help="Write preflight evidence without claiming a live PASS.",
    )
    parser.add_argument(
        "--manual-timeout-seconds",
        type=float,
        default=1800.0,
    )
    return parser.parse_args(argv)


def validate_arguments(args: argparse.Namespace) -> argparse.Namespace:
    """Reject contradictory modes and unsafe identifiers before I/O."""

    contract = protocol.require_case(args.case)
    protocol.require_head(args.expected_head)
    if bool(args.manual) is not contract.manual:
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT",
            "--manual must be present exactly for the two blocking manual cases",
        )
    if args.run_id is not None:
        protocol.require_run_id(args.run_id)
    if args.bridge_port is not None and not 1 <= args.bridge_port <= 65535:
        raise protocol.ProtocolFailure("FAIL_PREFLIGHT", "bridge port is outside 1..65535")
    if args.domain_id is not None and not 0 <= args.domain_id <= 232:
        raise protocol.ProtocolFailure("FAIL_PREFLIGHT", "domain ID is outside 0..232")
    if not isinstance(args.manual_timeout_seconds, (int, float)) or not 1 <= float(
        args.manual_timeout_seconds
    ) <= 7200:
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT", "manual timeout must be in [1, 7200] seconds"
        )
    return args


def git_head(repository: pathlib.Path) -> str:
    """Read the exact current Git commit."""

    try:
        completed = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Git HEAD could not be read") from exc
    return protocol.require_head(completed.stdout.strip())


def require_exact_head(repository: pathlib.Path, expected_head: str) -> str:
    """Reject a stale requested SHA even if its text is well formed."""

    expected = protocol.require_head(expected_head)
    actual = git_head(repository)
    if actual != expected:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", f"current Git HEAD {actual} differs from expected {expected}"
        )
    return actual


def require_clean_tracked_tree(repository: pathlib.Path) -> None:
    """Require a clean tracked tree/index while ignoring operator-only files."""

    try:
        completed = subprocess.run(
            ["git", "status", "--porcelain=v1", "--untracked-files=no"],
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "tracked Git status could not be read") from exc
    if completed.stdout.strip():
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "live acceptance requires a clean tracked tree and index"
        )


def resolve_unity_editor(
    project: pathlib.Path,
    explicit_editor: pathlib.Path | None,
) -> UnityEditorIdentity:
    """Resolve the exact Unity version declared by the project."""

    version_file = pathlib.Path(project) / "ProjectSettings" / "ProjectVersion.txt"
    try:
        text = version_file.read_text(encoding="utf-8")
    except OSError as exc:
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_PROJECT_VERSION", "Unity project version file is unavailable"
        ) from exc
    match = re.search(r"(?m)^m_EditorVersion: ([^\r\n]+)$", text)
    if match is None or _UNITY_VERSION.fullmatch(match.group(1)) is None:
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_PROJECT_VERSION", "Unity project version is malformed"
        )
    version = match.group(1)
    editor = (
        pathlib.Path(explicit_editor)
        if explicit_editor is not None
        else pathlib.Path(r"C:\Program Files\Unity\Hub\Editor")
        / version
        / "Editor"
        / "Unity.exe"
    )
    try:
        editor = editor.resolve(strict=True)
    except OSError as exc:
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_EDITOR",
            f"Unity {version} executable is not installed at the selected path",
        ) from exc
    if not editor.is_file() or editor.name.lower() != "unity.exe":
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_EDITOR", "selected Unity executable is not Unity.exe"
        )
    return UnityEditorIdentity(editor, version)


def reserve_loopback_port(port: int | None = None) -> LoopbackPortReservation:
    """Hold an exclusive IPv4 loopback TCP port until actor handoff."""

    if port is not None and not 1 <= port <= 65535:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "requested port is outside 1..65535")
    owned = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        if os.name == "nt":
            owned.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        else:
            owned.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 0)
        owned.bind(("127.0.0.1", 0 if port is None else port))
        host, selected = owned.getsockname()[:2]
        if host != "127.0.0.1" or not 1 <= int(selected) <= 65535:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT", "port reservation did not bind IPv4 loopback"
            )
        return LoopbackPortReservation(owned, host, int(selected))
    except Exception:
        owned.close()
        raise


def _read_json_object(path: pathlib.Path, label: str) -> Mapping[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{label} is unavailable or invalid") from exc
    if not isinstance(value, Mapping):
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{label} must be a JSON object")
    return value


def validate_package_manifests(repository: pathlib.Path) -> dict[str, Any]:
    """Prove the ROS-free Bridge dependency boundary from current manifests."""

    root = pathlib.Path(repository)
    sdk = _read_json_object(
        root / "Packages" / "dev.unity2foxglove.sdk" / "package.json",
        "SDK package manifest",
    )
    bridge = _read_json_object(
        root / "Packages" / "dev.unity2foxglove.ros2bridge" / "package.json",
        "Bridge package manifest",
    )
    if sdk.get("name") != "dev.unity2foxglove.sdk":
        raise AcceptanceFailure("FAIL_PREFLIGHT", "SDK package ID differs from authority")
    if bridge.get("name") != "dev.unity2foxglove.ros2bridge":
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Bridge package ID differs from authority")
    dependencies = bridge.get("dependencies")
    if not isinstance(dependencies, Mapping):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Bridge dependencies must be an object")
    if "dev.unity2foxglove.sdk" not in dependencies:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Bridge does not depend on the SDK")
    forbidden = sorted(
        key
        for key in dependencies
        if key.startswith("dev.unity2foxglove.ros2forunity")
        or key.startswith("dev.unity2foxglove.ros2.")
    )
    if forbidden:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "Bridge manifest depends on R2FU/ROS runtime: " + ", ".join(forbidden)
        )
    return {
        "sdkPackage": str(sdk["name"]),
        "sdkVersion": str(sdk.get("version", "")),
        "bridgePackage": str(bridge["name"]),
        "bridgeVersion": str(bridge.get("version", "")),
        "bridgeDependencies": dict(dependencies),
    }


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


_UNITY_BINDING_RELATIVE_PATH = pathlib.Path(
    "Assets/Scripts/Generated/Phase186AcceptanceRun.cs"
)

_UNITY_CASE_LAYOUTS: Mapping[str, tuple[str, ...]] = {
    "frozen-v1": ("standard_publish", "standard_publish"),
    "bridge-source": ("standard_subscribe", "custom_subscribe"),
    "full-duplex": ("custom_duplex",),
    "fanout-fairness-health": (
        "standard_publish",
        "standard_publish",
        "standard_publish",
    ),
    "reconnect-degraded-recovery": ("custom_duplex", "standard_subscribe"),
    "bounds-hostile-peer": ("standard_subscribe", "custom_subscribe"),
    "lifecycle": ("custom_duplex",),
    "slow-main-thread-640hz": ("custom_subscribe", "standard_duplex"),
    "product-inspector": ("standard_publish", "standard_subscribe"),
    "manual-jazzy-fastrtps-duplex": (
        "custom_duplex",
        "standard_duplex",
        "custom_subscribe",
    ),
    "manual-lyrical-zenoh-duplex": (
        "custom_duplex",
        "standard_duplex",
        "custom_subscribe",
    ),
}


def _render_unity_contract(
    index: int, topic: str, kind: str
) -> tuple[str, str, str, str]:
    """Render one declaration, observation, initialization, and mutation arm."""

    field = f"_phase186GeneratedValue{index}"
    observed = f"_phase186GeneratedObserved{index}"
    sequence = f"_phase186GeneratedSequence{index}"
    topic_name = f"Phase186GeneratedTopic{index}"
    if kind.startswith("custom_"):
        type_name = "Phase181State"
        initializer = (
            f'CreatePhase186State("bootstrap-{index}", {index})'
            if kind == "custom_duplex"
            else "null"
        )
        observe = (
            f"            Phase186ObserveCustom({field}, ref {observed}, "
            f"ref {sequence}, ref evidence, {topic_name});"
        )
        observed_declaration = f"        private string {observed} = string.Empty;"
    else:
        type_name = "Foxglove.Log"
        initializer = (
            f'CreatePhase186Log("bootstrap-{index}", {index})'
            if kind in {"standard_duplex", "standard_publish"}
            else "new Foxglove.Log()"
        )
        observe = (
            f"            Phase186ObserveStandard({field}, ref {observed}, "
            f"ref {sequence}, ref evidence, {topic_name});"
        )
        observed_declaration = f"        private string {observed} = string.Empty;"

    if kind.endswith("duplex"):
        attribute = f"""        [FoxRun(
            {topic_name},
            Mode = FoxRunFlow.PublishAndSubscribe,
            Policy = FoxRunPolicy.Change,
            SubscribeTransportId = Ros2BridgeTransportProvider.ProviderId,
            PublishTransportIds = new[]
            {{
                Ros2BridgeTransportProvider.ProviderId
            }})]"""
    elif kind.endswith("subscribe"):
        attribute = f"""        [FoxRun(
            {topic_name},
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = Ros2BridgeTransportProvider.ProviderId)]"""
    elif kind.endswith("publish"):
        attribute = f"""        [FoxRun(
            {topic_name},
            Mode = FoxRunFlow.Publish,
            PublishTransportIds = new[]
            {{
                Ros2BridgeTransportProvider.ProviderId
            }})]"""
    else:  # pragma: no cover - table is fixed and validated below.
        raise AcceptanceFailure("FAIL_PROTOCOL", f"unknown Unity contract kind {kind}")

    declaration = f"""{attribute}
        [SerializeField] private {type_name} {field} = {initializer};
        {observed_declaration.strip()}
        private long {sequence} = -1;"""

    initialization = ""
    if kind == "custom_duplex":
        initialization = (
            f"            {observed} = {field}.Count.ToString("
            "global::System.Globalization.CultureInfo.InvariantCulture)"
            f" + \":\" + ({field}.Message ?? string.Empty);\n"
            f"            {sequence} = {field}.Count;"
        )
    elif kind == "standard_duplex":
        initialization = (
            f"            {observed} = {field}.Message ?? string.Empty;\n"
            f"            Phase186TryReadSequence({observed}, out {sequence});"
        )

    mutation = ""
    if kind in {"custom_duplex", "custom_publish"}:
        mutation = (
            f"            {field} = CreatePhase186State("
            '"unity-local-b-" + evidence.LocalMutations.ToString('
            "global::System.Globalization.CultureInfo.InvariantCulture), "
            "checked((int)global::System.Math.Min(int.MaxValue, "
            "evidence.LocalMutations)));"
        )
    elif kind in {"standard_duplex", "standard_publish"}:
        mutation = (
            f"            {field} = CreatePhase186Log("
            '"unity-local-b-" + evidence.LocalMutations.ToString('
            "global::System.Globalization.CultureInfo.InvariantCulture), "
            "evidence.LocalMutations);"
        )
    return declaration, observe, initialization, mutation


def render_unity_run_binding(config: Mapping[str, Any]) -> str:
    """Render the ignored, token-specific partial class consumed by Unity."""

    if not isinstance(config, Mapping) or not isinstance(config.get("repository"), str):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Unity run config is malformed")
    protocol.validate_run_config(config, pathlib.Path(str(config["repository"])))
    case_id = str(config["caseId"])
    topics = tuple(str(topic) for topic in config["topics"])
    layout = _UNITY_CASE_LAYOUTS.get(case_id)
    if layout is None or len(layout) != len(topics):
        raise AcceptanceFailure(
            "FAIL_PROTOCOL", "Unity contract layout differs from case topic authority"
        )

    declarations: list[str] = []
    observations: list[str] = []
    initializations: list[str] = []
    mutation = ""
    for index, (topic, kind) in enumerate(zip(topics, layout, strict=True)):
        declaration, observation, initialization, candidate_mutation = (
            _render_unity_contract(index, topic, kind)
        )
        declarations.append(declaration)
        if kind.endswith("subscribe") or kind.endswith("duplex"):
            observations.append(observation)
        if initialization:
            initializations.append(initialization)
        if not mutation and candidate_mutation:
            mutation = candidate_mutation

    topic_constants = "\n".join(
        f'        public const string Phase186GeneratedTopic{index} = "{topic}";'
        for index, topic in enumerate(topics)
    )
    topic_values = ",\n".join(
        f"                    Phase186GeneratedTopic{index}"
        for index in range(len(topics))
    )
    kinds = ", ".join(f'"{kind}"' for kind in layout)
    has_inbound = any(
        kind.endswith("subscribe") or kind.endswith("duplex") for kind in layout
    )
    has_duplex = any(kind.endswith("duplex") for kind in layout)
    slow = case_id in {
        "slow-main-thread-640hz",
        "manual-jazzy-fastrtps-duplex",
        "manual-lyrical-zenoh-duplex",
    }
    mutation_body = (
        "            evidence.LocalMutations++;\n"
        + mutation
        + "\n            published = true;"
        if mutation
        else "            published = false;"
    )
    observation_body = "\n".join(observations)
    initialization_body = "\n".join(initializations)
    can_complete = (
        "evidence.Applied > 0 && evidence.LocalMutations > 0"
        if has_duplex
        else "evidence.Applied > 0"
        if has_inbound
        else "true"
    )

    return f"""// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
// TRANSIENT: generated for one Phase186-H acceptance run; never commit this file.

using System;
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;

namespace Unity2Foxglove.ManualAcceptance
{{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    public sealed partial class Phase186Ros2BridgeAcceptance
    {{
        public const string Phase186GeneratedRunId = "{config['runId']}";
        public const string Phase186GeneratedCaseId = "{case_id}";
        public const string Phase186GeneratedTokenHash = "{config['tokenHash']}";
        public const string Phase186GeneratedHead = "{config['head']}";
        public const string Phase186GeneratedInterfaceDigest = "{protocol.INTERFACE_DIGEST}";
{topic_constants}

{chr(10).join(declarations)}

        partial void Phase186Generated_Describe(ref GeneratedRunIdentity identity)
        {{
            identity.Present = true;
            identity.RunId = Phase186GeneratedRunId;
            identity.CaseId = Phase186GeneratedCaseId;
            identity.TokenHash = Phase186GeneratedTokenHash;
            identity.Head = Phase186GeneratedHead;
            identity.InterfaceDigest = Phase186GeneratedInterfaceDigest;
            identity.Topics = new[]
            {{
{topic_values}
            }};
            identity.ContractKinds = new[] {{ {kinds} }};
        }}

        partial void Phase186Generated_Initialize()
        {{
{initialization_body}
        }}

        partial void Phase186Generated_Tick(ref GeneratedEvidence evidence)
        {{
            evidence.Generated = true;
            evidence.SlowMainThread = {str(slow).lower()};
{observation_body}
            evidence.CanComplete = {can_complete};
        }}

        partial void Phase186Generated_PublishLocalMutation(
            ref GeneratedEvidence evidence,
            ref bool published)
        {{
{mutation_body}
        }}

        private static Foxglove.Log CreatePhase186Log(string label, long sequence)
            => new Foxglove.Log
            {{
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Level = Foxglove.Log.Types.Level.Info,
                Message = "phase186:" + Phase186GeneratedTokenHash.Substring(0, 12)
                          + ":" + sequence.ToString(
                              global::System.Globalization.CultureInfo.InvariantCulture)
                          + ":" + label,
                Name = "Phase186Acceptance",
                File = nameof(Phase186Ros2BridgeAcceptance),
                Line = 186,
            }};

        private static void Phase186ObserveStandard(
            Foxglove.Log value,
            ref string observed,
            ref long sequence,
            ref GeneratedEvidence evidence,
            string topic)
        {{
            var message = value?.Message ?? string.Empty;
            if (string.Equals(message, observed, StringComparison.Ordinal))
                return;
            observed = message;
            evidence.LastStandardMessage = Phase186Bound(message);
            evidence.LastTopic = topic;
            if (Phase186TryReadSequence(message, out var current))
                Phase186RecordSequence(current, ref sequence, ref evidence);
            else
                evidence.Applied++;
        }}

        private static void Phase186ObserveCustom(
            Phase181State value,
            ref string observed,
            ref long sequence,
            ref GeneratedEvidence evidence,
            string topic)
        {{
            if (value == null)
                return;
            var message = value.Message ?? string.Empty;
            var fingerprint = value.Count.ToString(
                                  global::System.Globalization.CultureInfo.InvariantCulture)
                              + ":" + message;
            if (string.Equals(fingerprint, observed, StringComparison.Ordinal))
                return;
            observed = fingerprint;
            evidence.LastCustomMessage = Phase186Bound(message);
            evidence.LastTopic = topic;
            Phase186RecordSequence(value.Count, ref sequence, ref evidence);
        }}

        private static Phase181State CreatePhase186State(string label, int sequence)
            => new Phase181State
            {{
                Count = sequence,
                Kind = Phase181StateKind.Active,
                Message = "phase186:" + Phase186GeneratedTokenHash.Substring(0, 12)
                          + ":" + sequence.ToString(
                              global::System.Globalization.CultureInfo.InvariantCulture)
                          + ":" + label,
                Bytes = new byte[] {{ 0x01, 0x86, 0x48, 0xD2 }},
                Values = new List<long> {{ sequence, sequence + 1L }},
                Nested = new Phase181NestedState {{ Enabled = true, Label = label }},
                OptionalCount = sequence,
                OptionalText = label,
            }};
    }}
}}
"""


def install_unity_run_binding(
    project: pathlib.Path, config: Mapping[str, Any]
) -> InstalledUnityRunBinding:
    """Install one exact ignored source without overwriting foreign content."""

    root = pathlib.Path(project).resolve()
    expected_project = pathlib.Path(str(config.get("projectPath", ""))).resolve()
    if root != expected_project:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "Unity binding project differs from run authority"
        )
    target = root / _UNITY_BINDING_RELATIVE_PATH
    source = render_unity_run_binding(config)
    encoded = source.encode("utf-8")
    digest = hashlib.sha256(encoded).hexdigest()
    if target.exists():
        try:
            existing = target.read_bytes()
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT", "existing Unity run binding cannot be read"
            ) from exc
        if existing != encoded:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT", "refusing to overwrite a foreign Unity run binding"
            )
        return InstalledUnityRunBinding(target, digest)

    target.parent.mkdir(parents=True, exist_ok=True)
    temporary: pathlib.Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=target.parent,
            prefix=target.name + ".",
            suffix=".tmp",
            delete=False,
        ) as stream:
            stream.write(encoded)
            temporary = pathlib.Path(stream.name)
        os.replace(temporary, target)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()
    return InstalledUnityRunBinding(target, digest)


def cleanup_unity_run_binding(installed: InstalledUnityRunBinding) -> None:
    """Remove only the exact source (and Unity-owned meta) installed above."""

    target = pathlib.Path(installed.path)
    if not target.is_file() or sha256_file(target) != installed.sha256:
        raise AcceptanceFailure(
            "FAIL_CLEANUP", "Unity run binding changed after installation"
        )
    meta = pathlib.Path(str(target) + ".meta")
    if meta.exists():
        try:
            meta_text = meta.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError) as exc:
            raise AcceptanceFailure(
                "FAIL_CLEANUP", "Unity run binding meta cannot be verified"
            ) from exc
        if (
            len(meta_text) > 4096
            or "fileFormatVersion: 2" not in meta_text
            or re.search(r"(?m)^guid: [0-9a-f]{32}$", meta_text) is None
        ):
            raise AcceptanceFailure(
                "FAIL_CLEANUP", "Unity run binding meta is foreign or malformed"
            )
        meta.unlink()
    target.unlink()


def validate_static_authority(repository: pathlib.Path) -> dict[str, Any]:
    """Lock tracked protocol, fixture, harness, and analyzer inputs."""

    root = pathlib.Path(repository)
    fixture = (
        root
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "test"
        / "fixtures"
        / "u2r2_protocol_vectors.json"
    )
    bridge_source = (
        root
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "src"
        / "unity2foxglove_ros2_bridge.cpp"
    )
    analyzer = (
        root
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Editor"
        / "SourceGenerators"
        / "analyzers"
        / "dotnet"
        / "cs"
        / "FoxgloveLogSourceGenerator.dll"
    )
    for label, path in (
        ("U2R2 fixture", fixture),
        ("Bridge source", bridge_source),
        ("FoxRun analyzer", analyzer),
    ):
        if not path.is_file():
            raise LivePrerequisiteMissing(
                "NOT_RUN_TRACKED_AUTHORITY", f"{label} is absent: {path}"
            )
    return {
        "fixturePath": str(fixture.resolve()),
        "fixtureSha256": sha256_file(fixture),
        "bridgeSourcePath": str(bridge_source.resolve()),
        "bridgeSourceSha256": sha256_file(bridge_source),
        "analyzerPath": str(analyzer.resolve()),
        "analyzerSha256": sha256_file(analyzer),
        "interfaceType": protocol.INTERFACE_TYPE,
        "interfaceDigest": protocol.INTERFACE_DIGEST,
    }


def find_current_manual_marker(
    lines: Sequence[str],
    *,
    case_id: str,
    run_id: str,
    token: str,
    head: str,
) -> str:
    """Return only the exact current-run Unity completion marker."""

    scanned = 0
    for line in reversed(tuple(lines)):
        scanned += len(line.encode("utf-8", errors="replace"))
        if scanned > MAX_RESCUE_LOG_BYTES:
            break
        candidate = line.strip()
        if not candidate.startswith(protocol.MANUAL_COMPLETE_PREFIX + " "):
            continue
        try:
            protocol.parse_manual_completion_marker(
                candidate,
                case_id=case_id,
                run_id=run_id,
                token=token,
                head=head,
            )
        except protocol.ProtocolFailure:
            continue
        return candidate
    raise AcceptanceFailure(
        "FAIL_TERMINAL", "no exact current-run manual completion marker was found"
    )


def validate_cleanup_evidence(value: Mapping[str, Any]) -> None:
    """Require all owned resources to be absent after teardown."""

    expected = {
        "complete",
        "residualProcesses",
        "residualPorts",
        "residualOverlays",
        "residualTemporaryProjects",
    }
    if not isinstance(value, Mapping) or set(value) != expected:
        raise AcceptanceFailure("FAIL_CLEANUP", "cleanup evidence keys differ")
    if value["complete"] is not True:
        raise AcceptanceFailure("FAIL_CLEANUP", "cleanup did not complete")
    for key in expected - {"complete"}:
        if not isinstance(value[key], list) or value[key]:
            raise AcceptanceFailure("FAIL_CLEANUP", f"cleanup retained {key}")


def promote_build_to_live_summary(_build_summary: Mapping[str, Any]) -> None:
    """Reject the forbidden build-PASS to live-PASS conversion by construction."""

    raise AcceptanceFailure(
        "FAIL_EVIDENCE", "build/tooling evidence cannot be promoted to a live PASS"
    )


def write_json_atomic(path: pathlib.Path, value: Mapping[str, Any]) -> None:
    """Persist one JSON object by atomic replacement within its owned directory."""

    target = pathlib.Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=target.parent,
        prefix=target.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, target)


def persist_not_run(
    output: pathlib.Path,
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str,
    prerequisite: str,
) -> dict[str, Any]:
    """Persist an honest blocking result after prerequisite preflight."""

    root = pathlib.Path(output).resolve()
    result = protocol.make_not_run_summary(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        prerequisite=prerequisite,
        evidence_root=str(root),
    )
    write_json_atomic(root / "terminal-summary.json", result)
    (root / "terminal-marker.txt").write_text(
        protocol.format_terminal_line(result) + "\n", encoding="utf-8"
    )
    return result


def _new_run_identity(case_id: str, requested_run_id: str | None) -> tuple[str, str]:
    token = "p186h_" + secrets.token_hex(16)
    if requested_run_id is not None:
        return protocol.require_run_id(requested_run_id), token
    suffix = secrets.token_hex(6)
    case_slug = case_id.replace("manual-", "")[:28]
    return protocol.require_run_id(f"phase186h-{case_slug}-{suffix}"), token


def _owned_run_root(repository: pathlib.Path, requested: pathlib.Path, run_id: str) -> pathlib.Path:
    root = pathlib.Path(requested)
    if not root.is_absolute():
        root = repository / root
    root = root.resolve()
    phase_root = (repository / "build" / "phase186").resolve()
    if root != phase_root and phase_root not in root.parents:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "output root must stay below repository build/phase186"
        )
    run_root = root / run_id
    if run_root.exists() and any(run_root.iterdir()):
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "owned run directory already exists and is not empty"
        )
    run_root.mkdir(parents=True, exist_ok=True)
    return run_root


def _preflight(
    repository: pathlib.Path,
    args: argparse.Namespace,
    run_root: pathlib.Path,
    run_id: str,
    token: str,
    bridge_port: int,
) -> dict[str, Any]:
    head = require_exact_head(repository, args.expected_head)
    require_clean_tracked_tree(repository)
    project = repository / "Unity2Foxglove"
    unity = resolve_unity_editor(project, args.unity_editor)
    packages = validate_package_manifests(repository)
    authority = validate_static_authority(repository)
    contract = protocol.require_case(args.case)
    row = protocol.require_row(contract.row_id) if contract.row_id else None
    domain_id = args.domain_id if args.domain_id is not None else (row.domain_id if row else 186)
    config = protocol.make_run_config(
        repository=repository,
        project=project,
        output_root=run_root,
        run_id=run_id,
        token=token,
        case_id=contract.case_id,
        head=head,
        bridge_port=bridge_port,
        domain_id=domain_id,
    )
    protocol.validate_run_config(config, repository)
    write_json_atomic(run_root / "run-config.json", config)
    return {
        "schemaVersion": 1,
        "runId": run_id,
        "caseId": contract.case_id,
        "rowId": contract.row_id,
        "tokenHash": protocol.token_sha256(token),
        "head": head,
        "unity": {"path": str(unity.path), "version": unity.version},
        "bridgeEndpoint": {"host": "127.0.0.1", "port": bridge_port},
        "domainId": domain_id,
        "packages": packages,
        "authority": authority,
        "verdict": "PREFLIGHT PASS",
        "liveVerdict": "NOT CLAIMED",
        "createdAt": protocol.timestamp(),
    }


def main(argv: Sequence[str] | None = None) -> int:
    """Run preflight or stop honestly before unimplemented live execution."""

    args = validate_arguments(parse_args(argv))
    repository = repository_root()
    run_id, token = _new_run_identity(args.case, args.run_id)
    run_root: pathlib.Path | None = None
    try:
        run_root = _owned_run_root(repository, args.output_root, run_id)
        with reserve_loopback_port(args.bridge_port) as reservation:
            preflight = _preflight(
                repository,
                args,
                run_root,
                run_id,
                token,
                reservation.port,
            )
            write_json_atomic(run_root / "preflight.json", preflight)
        if args.preflight_only:
            print(
                "PHASE186_PREFLIGHT_PASS"
                + f" run={run_id} case={args.case} tokenHash={protocol.token_sha256(token)}"
                + f" head={args.expected_head}",
                flush=True,
            )
            return EXIT_PASS
        result = persist_not_run(
            run_root,
            run_id=run_id,
            token=token,
            case_id=args.case,
            head=args.expected_head,
            prerequisite="controlled Unity/sidecar live actor phase is not active",
        )
        print(protocol.format_terminal_line(result), flush=True)
        return EXIT_NOT_RUN
    except LivePrerequisiteMissing as exc:
        if run_root is None:
            print(str(exc), file=sys.stderr)
            return EXIT_NOT_RUN
        result = persist_not_run(
            run_root,
            run_id=run_id,
            token=token,
            case_id=args.case,
            head=args.expected_head,
            prerequisite=str(exc),
        )
        print(protocol.format_terminal_line(result), flush=True)
        return EXIT_NOT_RUN
    except protocol.ProtocolFailure as exc:
        print(str(exc), file=sys.stderr)
        return EXIT_FAIL


if __name__ == "__main__":
    raise SystemExit(main())
