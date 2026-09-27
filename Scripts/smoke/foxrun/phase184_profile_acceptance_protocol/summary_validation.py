from __future__ import annotations
from .qos_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def validate_summary(
    summary: Mapping[str, Any],
    *,
    expected_case: str,
    expected_token: str,
) -> CaseContract:
    """Fail closed on stale, incomplete, contradictory, or synthetic evidence."""

    summary = _require_mapping(summary, "summary", "terminal")
    expected_top_keys = {
        "summarySchemaVersion",
        "identity",
        "profile",
        *_SUMMARY_SECTION_NAMES,
        "processes",
        "cleanup",
        "verdict",
    }
    _require_exact_keys(summary, expected_top_keys, "summary", "terminal")
    if summary["summarySchemaVersion"] != SUMMARY_SCHEMA_VERSION:
        raise _fail("terminal", "Unsupported summary schema version.")

    contract = CASE_CONTRACTS.get(expected_case)
    if contract is None:
        raise _fail("terminal", f"Unknown expected case {expected_case!r}.")

    identity = _require_mapping(summary["identity"], "identity", "terminal")
    _require_exact_keys(
        identity,
        {
            "runId",
            "case",
            "tokenSha256",
            "unityVersion",
            "interfaceIdentity",
            "interfaceDigest",
        },
        "identity",
        "terminal",
    )
    if identity["case"] != expected_case:
        raise _fail("terminal", "Summary case does not match the requested case.")
    if identity["tokenSha256"] != token_sha256(expected_token):
        raise _fail("terminal", "Summary token digest is stale or mismatched.")
    run_id = _require_string(identity["runId"], "identity.runId", "terminal")
    if _SAFE_RUN_ID.fullmatch(run_id) is None:
        raise _fail("terminal", "identity.runId is malformed.")
    _require_string(identity["unityVersion"], "identity.unityVersion", "terminal")
    _require_string(
        identity["interfaceIdentity"], "identity.interfaceIdentity", "terminal"
    )
    digest = _require_string(
        identity["interfaceDigest"], "identity.interfaceDigest", "terminal"
    )
    if _LOWER_SHA256.fullmatch(digest) is None:
        raise _fail("terminal", "identity.interfaceDigest is malformed.")

    profile = _require_mapping(summary["profile"], "profile", "terminal")
    _require_exact_keys(
        profile,
        {
            "profile",
            "runtime",
            "rmw",
            "source",
            "targets",
            "publishEncoding",
            "subscribeEncoding",
            "requestedQos",
        },
        "profile",
        "terminal",
    )
    profile_contract = PROFILE_CONTRACTS[contract.profile]
    if (
        profile["profile"] != contract.profile
        or profile["runtime"] != profile_contract.runtime
        or profile["rmw"] != profile_contract.rmw
    ):
        raise _fail("runtime-selection", "Summary profile/runtime/RMW drifted.")
    _require_string(profile["source"], "profile.source", "terminal")
    if not isinstance(profile["targets"], list):
        raise _fail("terminal", "profile.targets must be an array.")
    for key in ("publishEncoding", "subscribeEncoding"):
        _require_string(profile[key], f"profile.{key}", "terminal")
    _require_mapping(profile["requestedQos"], "profile.requestedQos", "qos")
    expected_source, expected_targets, expected_publish, expected_subscribe = (
        _EXPECTED_PROFILE_EVIDENCE[expected_case]
    )
    if (
        profile["source"] != expected_source
        or profile["targets"] != list(expected_targets)
        or profile["publishEncoding"] != expected_publish
        or profile["subscribeEncoding"] != expected_subscribe
    ):
        raise _fail("terminal", "Summary Source/Targets/Encoding drifted from the case contract.")

    verdict = summary["verdict"]
    if not isinstance(verdict, str) or (
        verdict != "PASS"
        and re.fullmatch(r"(?:FAIL|BLOCKED)_[A-Z0-9_]+", verdict) is None
    ):
        raise _fail("terminal", "verdict must be PASS, FAIL_*, or BLOCKED_*.")
    require_positive = verdict == "PASS"

    for name, rule in contract.applicability.items():
        section = _require_mapping(summary[name], name, "terminal")
        if rule.required:
            _validate_required_section(
                name,
                section,
                require_positive=require_positive,
            )
        else:
            _require_exact_keys(
                section,
                {"applicability", "reason"},
                name,
                "terminal",
            )
            if (
                section["applicability"] != "not_applicable"
                or section["reason"] != rule.reason
            ):
                raise _fail("terminal", f"{name} has an unapproved N/A reason.")

    if require_positive:
        if contract.applicability["foxglove"].required:
            foxglove = summary["foxglove"]
            if foxglove["sampleToken"] != token_sha256(expected_token):
                raise _fail("client", "Foxglove sample evidence is not token-correlated.")
            expected_stages = _EXPECTED_FOXGLOVE_SAMPLE_STAGES[expected_case]
            if foxglove["sampleStages"] != list(expected_stages):
                raise _fail("client", "Foxglove sample stages drifted from the case contract.")

        if expected_case == "foxglove-profile":
            encodings = set(summary["foxglove"]["channelEncodings"])
            if encodings != {"json", "protobuf"}:
                raise _fail(
                    "client",
                    "Foxglove profile case must prove both JSON and Protobuf channels.",
                )
        elif expected_case in {"multi-target", "degraded-target"}:
            if set(summary["foxglove"]["channelEncodings"]) != {"protobuf"}:
                raise _fail(
                    "client",
                    "The selected fanout case must prove its Protobuf channel.",
                )

        if contract.applicability["qos"].required:
            _validate_qos_evidence(summary, expected_case)

        if contract.applicability["rosGraph"].required:
            _validate_graph_evidence(
                summary["rosGraph"],
                expected_case,
                expected_token,
            )

        targets = summary["targets"]
        if targets["states"] != _EXPECTED_TARGET_STATES[expected_case]:
            raise _fail("fanout", "Target states drifted from the exact case contract.")
        if targets["diagnosticCounts"] != _EXPECTED_TARGET_DIAGNOSTICS[expected_case]:
            raise _fail("fanout", "Target diagnostic counts drifted from the case contract.")

        if expected_case == "foxglove-profile":
            expected_status = {
                "aggregate": "Ready",
                "succeeded": "Foxglove",
                "failed": "None",
                "topics": 2,
            }
            if targets["statusEvidence"] != expected_status:
                raise _fail(
                    "fanout",
                    "Foxglove target evidence must come from both runtime dispatches.",
                )
        elif expected_case == "multi-target":
            expected_status = {
                "aggregate": "Ready",
                "succeeded": "Foxglove,Ros2Native,Ros2Bridge",
                "failed": "None",
                "bridgeRuntimeFailures": 0,
            }
            if targets["statusEvidence"] != expected_status:
                raise _fail(
                    "fanout",
                    "Multi-target evidence must come from the runtime dispatch.",
                )
        elif expected_case == "degraded-target":
            expected_status = {
                "aggregate": "Degraded",
                "succeeded": "Foxglove",
                "failed": "Ros2Bridge",
                "bridgeDiagnostics": 1,
            }
            if targets["statusEvidence"] != expected_status:
                raise _fail(
                    "fanout",
                    "Degraded target evidence must come from the runtime status transition.",
                )
        elif expected_case == "qos-contract":
            expected_topics = {
                topic: {
                    "aggregate": "Ready",
                    "succeeded": "Ros2Native,Ros2Bridge",
                    "failed": "None",
                }
                for topic in CASE_CONTRACTS["qos-contract"].topics
            }
            if targets["statusEvidence"] != {"topics": expected_topics}:
                raise _fail(
                    "fanout",
                    "QoS target evidence must cover each exact runtime dispatch.",
                )
        elif expected_case == "stream-640hz":
            status = _require_mapping(
                targets["statusEvidence"],
                "targets.statusEvidence",
                "fanout",
            )
            if (
                set(status)
                != {
                    "bindingState",
                    "received",
                    "copyFailed",
                    "staleCallbacks",
                    "rejectedAfterStop",
                }
                or status["bindingState"] not in {"Ready", "Receiving"}
                or status["received"] != summary["stream"]["received"]
                or status["copyFailed"] != 0
                or status["staleCallbacks"] != 0
                or status["rejectedAfterStop"] != 0
            ):
                raise _fail(
                    "fanout",
                    "Stream target evidence must come from the live native subscription.",
                )

        if expected_case == "stream-640hz":
            stream = summary["stream"]
            if (
                stream["offered"] != 1280
                or stream["received"] <= STREAM_CAPACITY
                or stream["received"] > stream["offered"]
                or stream["received"] + stream["transportDropped"]
                != stream["offered"]
                or stream["accepted"] + stream["rateDropped"]
                != stream["received"]
                or stream["transportDropped"] + stream["rateDropped"]
                != stream["dropped"]
                or stream["drained"] + stream["replaced"] != stream["accepted"]
                or stream["disposed"] != stream["drained"] + stream["replaced"]
                or stream["maximumQueueDepth"] != STREAM_CAPACITY
                or stream["replaced"] <= 0
                or (stream["lastSequence"] + 1) * 1000
                < stream["offered"] * MIN_STREAM_LAST_SEQUENCE_PERMILLE
            ):
                raise _fail(
                    "stream",
                    "Stream counters do not prove the locked delivery/capacity/ownership contract.",
                )

    processes = summary["processes"]
    if not isinstance(processes, list):
        raise _fail("process-exit", "processes must be an array.")
    by_role: dict[str, Mapping[str, Any]] = {}
    for item in processes:
        entry = _require_mapping(item, "process entry", "process-exit")
        role = _require_string(entry.get("role"), "process role", "process-exit")
        if role in by_role:
            raise _fail("process-exit", f"Duplicate process role {role!r}.")
        by_role[role] = entry
    expected_roles = (
        contract.required_actors
        | frozenset(contract.deliberately_absent_actors)
        | frozenset({"unity"})
    )
    if set(by_role) != set(expected_roles):
        raise _fail("process-exit", "Process roles do not match the case contract.")
    for role in contract.required_actors | frozenset({"unity"}):
        entry = by_role[role]
        _require_exact_keys(
            entry,
            {"role", "started", "exitCode", "termination"},
            f"processes.{role}",
            "process-exit",
        )
        if entry["started"] is not True:
            raise _fail("process-exit", f"Required actor {role!r} was not started.")
        exit_code = entry["exitCode"]
        termination = entry["termination"]
        if isinstance(exit_code, bool) or not isinstance(exit_code, int):
            raise _fail("process-exit", f"Actor {role!r} has an invalid exit code.")
        if termination not in {"self", "owner_requested"}:
            raise _fail(
                "process-exit",
                f"Actor {role!r} has invalid termination provenance.",
            )
        if termination == "owner_requested":
            if not process_exit_is_acceptable(
                role,
                exit_code,
                owner_requested=True,
            ):
                raise _fail(
                    "process-exit",
                    f"Actor {role!r} has invalid owner-requested exit evidence.",
                )
        elif require_positive and not process_exit_is_acceptable(
            role,
            exit_code,
            owner_requested=False,
        ):
            raise _fail("process-exit", f"Actor {role!r} has an invalid exit code.")
    for role, reason in contract.deliberately_absent_actors.items():
        entry = by_role[role]
        _require_exact_keys(
            entry,
            {"role", "started", "reason"},
            f"processes.{role}",
            "process-exit",
        )
        if entry["started"] is not False or entry["reason"] != reason:
            raise _fail("process-exit", f"Absent actor {role!r} is misrepresented.")

    cleanup = _require_mapping(summary["cleanup"], "cleanup", "cleanup")
    _require_exact_keys(
        cleanup,
        {"processes", "files", "junctions", "subst"},
        "cleanup",
        "cleanup",
    )
    for key, value in cleanup.items():
        if not isinstance(value, bool):
            raise _fail("cleanup", f"cleanup.{key} must be boolean.")
        if require_positive and not value:
            raise _fail("cleanup", f"cleanup.{key} is false.")

    return contract




__all__ = [name for name in globals() if not name.startswith("__")]
