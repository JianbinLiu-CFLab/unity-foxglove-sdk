from __future__ import annotations
from .configuration_and_preflight import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def sha256_file(path: pathlib.Path) -> str:
    """Compute the SHA-256 digest for file."""
    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


_UNITY_BINDING_RELATIVE_PATH = pathlib.Path(
    "Assets/Scripts/Generated/Phase186AcceptanceRun.cs"
)

def _render_unity_contract(
    index: int, topic: str, kind: str
) -> tuple[str, str, str, str, str]:
    """Render one declaration, observation, initialization, mutation, and warm-up arm."""

    field = (
        f"_incomingPhase186Generated{index}"
        if kind.endswith("subscribe")
        else f"_phase186GeneratedValue{index}"
    )
    observed = f"_phase186GeneratedObserved{index}"
    sequence = f"_phase186GeneratedSequence{index}"
    topic_name = f"Phase186GeneratedTopic{index}"
    if kind.startswith("custom_"):
        type_name = "Phase181State"
        initializer = (
            f'CreatePhase186State("bootstrap-{index}", {index})'
            if kind in {"custom_duplex", "custom_publish"}
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
    warmup = ""
    if kind in {"custom_duplex", "custom_publish"}:
        mutation = (
            f"            {field} = CreatePhase186State("
            '"unity-local-b-" + evidence.LocalMutations.ToString('
            "global::System.Globalization.CultureInfo.InvariantCulture), "
            "checked((int)global::System.Math.Min(int.MaxValue, "
            "evidence.LocalMutations)));"
        )
        if kind == "custom_duplex":
            warmup = (
                f"            {field} = CreatePhase186State("
                '"unity-publisher-warmup", 0);'
            )
    elif kind in {"standard_duplex", "standard_publish"}:
        mutation = (
            f"            {field} = CreatePhase186Log("
            '"unity-local-b-" + evidence.LocalMutations.ToString('
            "global::System.Globalization.CultureInfo.InvariantCulture), "
            "evidence.LocalMutations);"
        )
        if kind == "standard_duplex":
            warmup = (
                f"            {field} = CreatePhase186Log("
                '"unity-publisher-warmup", 0);'
            )
    return declaration, observe, initialization, mutation, warmup


def render_unity_run_binding(config: Mapping[str, Any]) -> str:
    """Render the ignored, token-specific partial class consumed by Unity."""

    if not isinstance(config, Mapping) or not isinstance(config.get("repository"), str):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Unity run config is malformed")
    protocol.validate_run_config(config, pathlib.Path(str(config["repository"])))
    case_id = str(config["caseId"])
    topics = tuple(str(topic) for topic in config["topics"])
    layout = protocol.CASE_CONTRACT_KINDS.get(case_id)
    if layout is None or len(layout) != len(topics):
        raise AcceptanceFailure(
            "FAIL_PROTOCOL", "Unity contract layout differs from case topic authority"
        )

    declarations: list[str] = []
    observations: list[str] = []
    initializations: list[str] = []
    mutation = ""
    fanout_mutations: list[str] = []
    duplex_mutations: list[str] = []
    warmup_mutations: list[str] = []
    for index, (topic, kind) in enumerate(zip(topics, layout, strict=True)):
        (
            declaration,
            observation,
            initialization,
            candidate_mutation,
            candidate_warmup,
        ) = _render_unity_contract(index, topic, kind)
        if case_id == "fanout-fairness-health" and kind.endswith("publish"):
            declaration = declaration.replace(
                "                Ros2BridgeTransportProvider.ProviderId\n            })]",
                "                FoxgloveWebSocketTransport.Id,\n"
                "                \"unity2foxglove.r2fu\",\n"
                "                Ros2BridgeTransportProvider.ProviderId\n            })]",
            )
        declarations.append(declaration)
        if kind.endswith("subscribe") or kind.endswith("duplex"):
            observations.append(observation)
        if initialization:
            initializations.append(initialization)
        if candidate_mutation:
            fanout_mutations.append(candidate_mutation)
            if kind.endswith("duplex"):
                duplex_mutations.append(candidate_mutation)
        if not mutation and candidate_mutation:
            mutation = candidate_mutation
        if candidate_warmup:
            warmup_mutations.append(candidate_warmup)

    if case_id == "fanout-fairness-health":
        mutation = "\n".join(fanout_mutations)
    elif len(duplex_mutations) > 1:
        mutation = "\n".join(duplex_mutations)

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
    warmup_body = (
        "\n".join(warmup_mutations) + "\n            published = true;"
        if warmup_mutations
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

        partial void Phase186Generated_WarmPublishers(ref bool published)
        {{
{warmup_body}
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


__all__ = [name for name in globals() if not name.startswith("__")]
