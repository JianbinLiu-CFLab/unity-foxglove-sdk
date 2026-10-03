from __future__ import annotations
from .run_config_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def _require_string_list(
    value: object,
    label: str,
    stage: str,
    *,
    allow_empty: bool,
) -> list[str]:
    """Require string list."""

    if (
        not isinstance(value, list)
        or (not allow_empty and not value)
        or any(not isinstance(item, str) or not item for item in value)
    ):
        qualifier = "a string array" if allow_empty else "a non-empty string array"
        raise _fail(stage, f"{label} must be {qualifier}.")
    return value


def _normalized_policy(value: object) -> str:
    """Handle the normalized policy step."""

    text = str(value).lower()
    aliases = {
        "qosreliabilitypolicy.reliable": "reliable",
        "qosreliabilitypolicy.best_effort": "best_effort",
        "qosreliabilitypolicy.system_default": "system_default",
        "qosdurabilitypolicy.volatile": "volatile",
        "qosdurabilitypolicy.transient_local": "transient_local",
        "qosdurabilitypolicy.system_default": "system_default",
        "qoshistorypolicy.keep_last": "keep_last",
        "qoshistorypolicy.keep_all": "keep_all",
        "qoshistorypolicy.system_default": "system_default",
    }
    return aliases.get(text, text)


def _validate_qos_value(
    value: object,
    expected: Mapping[str, object],
    label: str,
    *,
    include_profile: bool,
) -> None:
    """Validate QoS value."""

    actual = _require_mapping(value, label, "qos")
    expected_keys = _QOS_FIELDS if include_profile else _TRANSPORT_QOS_FIELDS
    _require_exact_keys(actual, expected_keys, label, "qos")
    for key in expected_keys - {"depth"}:
        if _normalized_policy(actual[key]) != expected[key]:
            raise _fail("qos", f"{label}.{key} drifted from the requested policy.")
    depth = actual["depth"]
    if isinstance(depth, bool) or not isinstance(depth, int) or depth != expected["depth"]:
        raise _fail("qos", f"{label}.depth drifted from the requested policy.")


def _validate_graph_qos_value(
    value: object,
    expected: Mapping[str, object],
    label: str,
) -> None:
    """Validate represented graph axes and preserve explicit unknowns."""

    actual = _require_mapping(value, label, "qos")
    _require_exact_keys(
        actual,
        _TRANSPORT_QOS_FIELDS | {"representedAxes"},
        label,
        "qos",
    )
    represented = _require_string_list(
        actual["representedAxes"],
        f"{label}.representedAxes",
        "qos",
        allow_empty=True,
    )
    represented_set = set(represented)
    if (
        len(represented_set) != len(represented)
        or not represented_set.issubset(_TRANSPORT_QOS_FIELDS)
    ):
        raise _fail("qos", f"{label}.representedAxes is invalid.")

    for key in _TRANSPORT_QOS_FIELDS - {"depth"}:
        normalized = _normalized_policy(actual[key])
        if key in represented_set:
            expected_policy = expected[key]
            if (
                expected_policy == "system_default"
                and normalized not in _RESOLVED_SYSTEM_DEFAULT_POLICIES[key]
            ):
                raise _fail(
                    "qos",
                    f"{label}.{key} is not a valid resolved SystemDefault policy.",
                )
            if expected_policy != "system_default" and normalized != expected_policy:
                raise _fail(
                    "qos",
                    f"{label}.{key} drifted from the requested policy.",
                )
        elif normalized != "unknown":
            raise _fail(
                "qos",
                f"{label}.{key} is unlabeled graph evidence.",
            )

    depth = actual["depth"]
    if isinstance(depth, bool) or not isinstance(depth, int):
        raise _fail("qos", f"{label}.depth must be an integer.")
    if "depth" in represented_set:
        if expected["history"] == "system_default":
            if depth < 0:
                raise _fail("qos", f"{label}.depth is invalid.")
        elif depth != expected["depth"]:
            raise _fail("qos", f"{label}.depth drifted from the requested policy.")
    elif depth != 0:
        raise _fail("qos", f"{label}.depth is unlabeled graph evidence.")


def _validate_resolved_system_default_agreement(
    values: Sequence[Mapping[str, object]],
    expected: Mapping[str, object],
    label: str,
) -> None:
    """Validate resolved system default agreement."""

    if len(values) < 2:
        return
    for axis in ("reliability", "durability", "history"):
        if expected[axis] != "system_default":
            continue
        represented = [axis in value["representedAxes"] for value in values]
        if any(represented) and not all(represented):
            raise _fail(
                "qos",
                f"{label}.{axis} representation differs between endpoints.",
            )
        if all(represented) and len(
            {_normalized_policy(value[axis]) for value in values}
        ) != 1:
            raise _fail(
                "qos",
                f"{label}.{axis} resolved differently between endpoints.",
            )
    if expected["history"] == "system_default":
        represented = ["depth" in value["representedAxes"] for value in values]
        if any(represented) and not all(represented):
            raise _fail(
                "qos",
                f"{label}.depth representation differs between endpoints.",
            )
        if all(represented) and len({value["depth"] for value in values}) != 1:
            raise _fail(
                "qos",
                f"{label}.depth resolved differently between endpoints.",
            )


def _expected_graph_direction_counts(
    case: str,
    topic: str,
) -> tuple[int, int]:
    """Handle the expected graph direction counts step."""

    if case in {"multi-target", "qos-contract"}:
        return 2, 1 if case == "multi-target" else 0
    if case == "stream-640hz":
        stream_topic, origin_topic = CASE_CONTRACTS[case].topics
        if topic == stream_topic:
            return 0, 1
        if topic == origin_topic:
            return 1, 1
    return 0, 0


def _validate_qos_evidence(
    summary: Mapping[str, Any],
    case: str,
) -> None:
    """Validate QoS evidence."""

    expected_qos = expected_qos_by_topic(case)
    if summary["profile"]["requestedQos"] != expected_qos:
        raise _fail("qos", "profile.requestedQos drifted from the case contract.")
    if summary["qos"]["requested"] != expected_qos:
        raise _fail("qos", "qos.requested drifted from the case contract.")

    transport = _require_mapping(
        summary["qos"]["transportObserved"],
        "qos.transportObserved",
        "qos",
    )
    expected_sources = (
        {"graph", "bridge"}
        if case in {"multi-target", "qos-contract"}
        else {"graph"}
    )
    _require_exact_keys(transport, expected_sources, "qos.transportObserved", "qos")
    graph = _require_mapping(transport["graph"], "qos.transportObserved.graph", "qos")
    _require_exact_keys(graph, set(expected_qos), "qos.transportObserved.graph", "qos")
    for topic, expected in expected_qos.items():
        topic_evidence = _require_mapping(
            graph[topic],
            f"qos.transportObserved.graph.{topic}",
            "qos",
        )
        _require_exact_keys(
            topic_evidence,
            {"publishers", "subscriptions"},
            f"qos.transportObserved.graph.{topic}",
            "qos",
        )
        minimum_publishers, minimum_subscriptions = _expected_graph_direction_counts(
            case,
            topic,
        )
        for direction, minimum in (
            ("publishers", minimum_publishers),
            ("subscriptions", minimum_subscriptions),
        ):
            values = topic_evidence[direction]
            if not isinstance(values, list) or len(values) < minimum:
                raise _fail(
                    "qos",
                    f"qos.transportObserved.graph.{topic}.{direction} is incomplete.",
                )
            for index, value in enumerate(values):
                _validate_graph_qos_value(
                    value,
                    expected,
                    f"qos.transportObserved.graph.{topic}.{direction}[{index}]",
                )
            _validate_resolved_system_default_agreement(
                values,
                expected,
                f"qos.transportObserved.graph.{topic}.{direction}",
            )

    if "bridge" in expected_sources:
        bridge = _require_mapping(
            transport["bridge"],
            "qos.transportObserved.bridge",
            "qos",
        )
        _require_exact_keys(bridge, set(expected_qos), "qos.transportObserved.bridge", "qos")
        for topic, expected in expected_qos.items():
            _validate_qos_value(
                bridge[topic],
                expected,
                f"qos.transportObserved.bridge.{topic}",
                include_profile=True,
            )


def _validate_sample_publisher_evidence(
    graph: Mapping[str, Any],
    case: str,
    expected_token: str,
) -> None:
    """Validate sample publisher evidence."""

    samples = _require_mapping(
        graph["samplePublisherGids"],
        "rosGraph.samplePublisherGids",
        "graph",
    )
    expected_samples = _EXPECTED_PUBLISHER_SAMPLE_STAGES.get(case, {})
    _require_exact_keys(
        samples,
        set(expected_samples),
        "rosGraph.samplePublisherGids",
        "graph",
    )
    graph_gids = set(graph["publisherGids"])
    minimum = 2 if case in {"multi-target", "qos-contract"} else 1
    for key, suffix in expected_samples.items():
        entry = _require_mapping(
            samples[key],
            f"rosGraph.samplePublisherGids.{key}",
            "graph",
        )
        _require_exact_keys(
            entry,
            {"sampleSha256", "publisherGids", "attribution"},
            f"rosGraph.samplePublisherGids.{key}",
            "graph",
        )
        if entry["sampleSha256"] != token_sha256(expected_token + "-" + suffix):
            raise _fail("graph", f"Publisher sample {key!r} is not token-correlated.")
        gids = _require_string_list(
            entry["publisherGids"],
            f"rosGraph.samplePublisherGids.{key}.publisherGids",
            "graph",
            allow_empty=False,
        )
        if len(set(gids)) < minimum or not set(gids).issubset(graph_gids):
            raise _fail("graph", f"Publisher sample {key!r} has invalid endpoint GIDs.")
        attribution = _require_string(
            entry["attribution"],
            f"rosGraph.samplePublisherGids.{key}.attribution",
            "graph",
        )
        if attribution not in _PUBLISHER_ATTRIBUTION_BY_CASE.get(case, frozenset()):
            raise _fail(
                "graph",
                f"Publisher sample {key!r} has an unsupported attribution source.",
            )


def _validate_graph_evidence(
    graph: Mapping[str, Any],
    case: str,
    expected_token: str,
) -> None:
    """Validate graph evidence."""

    allow_empty = case == "degraded-target"
    nodes = _require_string_list(
        graph["nodeIdentities"],
        "rosGraph.nodeIdentities",
        "graph",
        allow_empty=allow_empty,
    )
    gids = _require_string_list(
        graph["publisherGids"],
        "rosGraph.publisherGids",
        "graph",
        allow_empty=allow_empty,
    )
    if len(nodes) != len(set(nodes)) or len(gids) != len(set(gids)):
        raise _fail("graph", "ROS graph identities and GIDs must be unique.")
    publishers = _require_mapping(
        graph["publishersByTopic"],
        "rosGraph.publishersByTopic",
        "graph",
    )
    topics = CASE_CONTRACTS[case].topics
    _require_exact_keys(publishers, set(topics), "rosGraph.publishersByTopic", "graph")
    observed_gids: set[str] = set()
    observed_nodes: set[str] = set()
    for topic in topics:
        entries = publishers[topic]
        expected_count, _ = _expected_graph_direction_counts(case, topic)
        if not isinstance(entries, list) or len(entries) != expected_count:
            raise _fail("graph", f"rosGraph.publishersByTopic.{topic} count drifted.")
        for index, raw in enumerate(entries):
            entry = _require_mapping(
                raw,
                f"rosGraph.publishersByTopic.{topic}[{index}]",
                "graph",
            )
            _require_exact_keys(
                entry,
                {"node", "gid"},
                f"rosGraph.publishersByTopic.{topic}[{index}]",
                "graph",
            )
            node = _require_string(entry["node"], "publisher node", "graph")
            gid = _require_string(entry["gid"], "publisher gid", "graph")
            observed_nodes.add(node)
            observed_gids.add(gid)
    if observed_gids != set(gids) or not observed_nodes.issubset(set(nodes)):
        raise _fail("graph", "ROS graph publisher identities do not match their topic evidence.")

    negative_seconds = graph["negativeObservationSeconds"]
    if isinstance(negative_seconds, bool) or not isinstance(
        negative_seconds,
        (int, float),
    ):
        raise _fail("graph", "rosGraph.negativeObservationSeconds must be numeric.")
    if case == "degraded-target":
        if negative_seconds < 3 or nodes or gids:
            raise _fail("graph", "Degraded graph evidence did not prove an empty negative window.")
    elif negative_seconds != 0:
        raise _fail("graph", "Positive graph evidence cannot claim a negative window.")

    _validate_sample_publisher_evidence(graph, case, expected_token)


def _validate_required_section(
    name: str,
    section: Mapping[str, Any],
    *,
    require_positive: bool,
) -> None:
    """Validate required section."""

    expected = {"applicability"} | _REQUIRED_SECTION_FIELDS[name]
    _require_exact_keys(section, expected, name, "terminal")
    if section["applicability"] != "required":
        raise _fail("terminal", f"{name} must be marked required.")
    for key in _SECTION_BOOLEAN_FIELDS[name]:
        value = section[key]
        if not isinstance(value, bool):
            raise _fail("terminal", f"{name}.{key} must be boolean.")
        if require_positive and not value:
            raise _fail(_SECTION_FAILURE_STAGE[name], f"{name}.{key} is false.")

    if name == "foxglove":
        if not isinstance(section["channelEncodings"], list) or not section["channelEncodings"]:
            raise _fail("client", "foxglove.channelEncodings is empty.")
        _require_string(section["sampleToken"], "foxglove.sampleToken", "client")
        _require_string_list(
            section["sampleStages"],
            "foxglove.sampleStages",
            "client",
            allow_empty=False,
        )
        if isinstance(section["timestamp"], bool) or not isinstance(
            section["timestamp"], (int, float)
        ):
            raise _fail("client", "foxglove.timestamp must be numeric.")
    elif name == "rosGraph":
        for key in ("nodeIdentities", "publisherGids"):
            if not isinstance(section[key], list):
                raise _fail("graph", f"rosGraph.{key} must be an array.")
        _require_mapping(section["publishersByTopic"], "rosGraph.publishersByTopic", "graph")
        _require_mapping(
            section["samplePublisherGids"],
            "rosGraph.samplePublisherGids",
            "graph",
        )
    elif name == "qos":
        _require_mapping(section["requested"], "qos.requested", "qos")
        _require_mapping(section["transportObserved"], "qos.transportObserved", "qos")
    elif name == "targets":
        states = _require_mapping(section["states"], "targets.states", "fanout")
        counts = _require_mapping(
            section["diagnosticCounts"], "targets.diagnosticCounts", "fanout"
        )
        if not states:
            raise _fail("fanout", "targets.states is empty.")
        if any(
            not isinstance(key, str)
            or value not in {"Ready", "Degraded", "Unavailable"}
            for key, value in states.items()
        ):
            raise _fail("fanout", "targets.states contains an invalid target state.")
        _require_mapping(section["statusEvidence"], "targets.statusEvidence", "fanout")
        for key, count in counts.items():
            if (
                not isinstance(key, str)
                or isinstance(count, bool)
                or not isinstance(count, int)
                or count < 0
            ):
                raise _fail("fanout", "targets.diagnosticCounts is malformed.")
    elif name == "stream":
        for key in _REQUIRED_SECTION_FIELDS["stream"] - _SECTION_BOOLEAN_FIELDS["stream"]:
            value = section[key]
            if isinstance(value, bool) or not isinstance(value, int) or value < 0:
                raise _fail("stream", f"stream.{key} must be a non-negative integer.")




__all__ = [name for name in globals() if not name.startswith("__")]
