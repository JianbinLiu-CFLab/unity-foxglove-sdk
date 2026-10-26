from __future__ import annotations
from .foundation import *
def validate_desktop_live_summary(
    summary: Mapping[str, Any],
) -> Mapping[str, Any]:
    """Validate the bounded exact wrapper schema and all PASS implications."""

    document = _exact_mapping(summary, _TOP_KEYS, "summary")
    try:
        encoded = json.dumps(
            dict(document),
            allow_nan=False,
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        ).encode("utf-8")
    except (TypeError, ValueError, RecursionError):
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary is not JSON-safe.")
    if len(encoded) > MAX_SUMMARY_BYTES:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary exceeds its bound.")
    if _RAW_TOKEN.search(encoded.decode("ascii")) is not None:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary contains a raw token.")
    if document["schemaVersion"] != SUMMARY_SCHEMA_VERSION:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary schemaVersion is unsupported.")

    verdict = document["verdict"]
    if verdict != "PASS" and verdict not in COORDINATOR_FAILURE_CODES:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live verdict is not a stable terminal code.")
    passing = verdict == "PASS"

    identity = _exact_mapping(document["identity"], _IDENTITY_KEYS, "identity")
    validate_run_id(identity["runId"])
    if identity["baseCase"] != BASE_CASE:
        _raise(protocol.FAIL_EVIDENCE, "identity.baseCase drifted.")
    token_digest = identity["tokenSha256"]
    if token_digest is not None and (
        not isinstance(token_digest, str)
        or _UPPER_SHA256.fullmatch(token_digest) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "identity.tokenSha256 is invalid.")
    head = identity["repositoryHead"]
    if head is not None and (
        not isinstance(head, str)
        or _LOWER_GIT_OBJECT.fullmatch(head) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "identity.repositoryHead is invalid.")
    for key in ("windowsVersion", "unityVersion"):
        _bounded_string(
            identity[key],
            f"identity.{key}",
            allow_none=not passing,
        )

    cli = _exact_mapping(document["cli"], _CLI_KEYS, "cli")
    for key in _CLI_KEYS:
        _bounded_string(cli[key], f"cli.{key}", allow_none=not passing)
    if cli["installedSha256"] is not None and (
        not isinstance(cli["installedSha256"], str)
        or _UPPER_SHA256.fullmatch(cli["installedSha256"]) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "cli.installedSha256 is invalid.")
    if passing:
        try:
            cli_install.VerifiedCliIdentity(
                installed_path=str(cli["installedPath"]),
                installed_version=str(cli["installedVersion"]),
                installed_sha256=str(cli["installedSha256"]),
                release_tag=str(cli["releaseTag"]),
                asset_url=str(cli["assetUrl"]),
                architecture=str(cli["architecture"]),
                receipt_path=str(cli["receiptPath"]),
            )
            distinct_cli_paths = not protocol.windows_paths_equal(
                str(cli["installedPath"]),
                str(cli["receiptPath"]),
            )
        except Exception:
            distinct_cli_paths = False
        if not distinct_cli_paths:
            _raise(
                protocol.FAIL_EVIDENCE,
                "CLI identity fields are not one coherent verified install.",
            )

    desktop = _exact_mapping(document["desktop"], _DESKTOP_KEYS, "desktop")
    for key in (
        "executable",
        "fileVersion",
        "sha256",
        "uriHandler",
        "dataSource",
        "deeplink",
    ):
        _bounded_string(
            desktop[key],
            f"desktop.{key}",
            allow_none=not passing,
        )
    if desktop["sha256"] is not None and (
        not isinstance(desktop["sha256"], str)
        or _UPPER_SHA256.fullmatch(desktop["sha256"]) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "desktop.sha256 is invalid.")
    if desktop["fileVersion"] is not None and (
        not isinstance(desktop["fileVersion"], str)
        or _DESKTOP_FILE_VERSION.fullmatch(desktop["fileVersion"]) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "desktop.fileVersion is invalid.")
    if desktop["dataSource"] is not None and desktop["dataSource"] != DATA_SOURCE:
        _raise(protocol.FAIL_EVIDENCE, "desktop.dataSource drifted.")
    root_identity = _identity_document(
        desktop["rootIdentity"],
        "desktop.rootIdentity",
        allow_none=not passing,
    )
    owned = _identity_list(
        desktop["ownedMemberIdentities"],
        "desktop.ownedMemberIdentities",
    )
    external = _identity_list(
        desktop["externalIdentities"],
        "desktop.externalIdentities",
    )
    job_owned = _bool(desktop["jobOwned"], "desktop.jobOwned")

    connection = _exact_mapping(
        document["connection"],
        _CONNECTION_KEYS,
        "connection",
    )
    if connection["host"] != LOOPBACK_HOST:
        _raise(protocol.FAIL_EVIDENCE, "connection.host must be exact loopback.")
    if connection["port"] is None:
        if passing:
            _raise(protocol.FAIL_EVIDENCE, "connection.port is unavailable.")
    else:
        _validate_port(connection["port"])
    port_preflight = _bool(
        connection["portPreflight"],
        "connection.portPreflight",
    )
    marker_excerpts: dict[str, str | None] = {}
    for key in (
        "contextMarker",
        "initialMarker",
        "firstMarker",
        "secondMarker",
    ):
        marker_excerpts[key] = _validate_marker_excerpt(
            connection[key],
            f"connection.{key}",
            allow_none=not passing,
        )
    times = {
        key: _time_value(
            connection[key],
            f"connection.{key}",
            allow_none=not passing,
        )
        for key in (
            "contextObservedAt",
            "initialObservedAt",
            "desktopIdentityCapturedAt",
            "firstObservedAt",
            "barrierWrittenAt",
            "secondObservedAt",
        )
    }
    barrier_path = _bounded_string(
        connection["barrierPath"],
        "connection.barrierPath",
        allow_none=False,
    )
    barrier_digest = connection["barrierDigest"]
    if barrier_digest is not None and (
        not isinstance(barrier_digest, str)
        or _UPPER_SHA256.fullmatch(barrier_digest) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "connection.barrierDigest is invalid.")
    barrier_removed = _bool(
        connection["barrierRemoved"],
        "connection.barrierRemoved",
    )

    foxrun = _exact_mapping(document["foxrun"], _FOXRUN_KEYS, "foxrun")
    _bounded_string(
        foxrun["baseSummaryPath"],
        "foxrun.baseSummaryPath",
        allow_none=False,
    )
    if foxrun["baseVerdict"] not in (None, "PASS") and not (
        isinstance(foxrun["baseVerdict"], str)
        and re.fullmatch(r"(?:FAIL|BLOCKED)_[A-Z0-9_]+", foxrun["baseVerdict"])
    ):
        _raise(protocol.FAIL_EVIDENCE, "foxrun.baseVerdict is invalid.")
    encodings = foxrun["channelEncodings"]
    if (
        not isinstance(encodings, list)
        or len(encodings) > 16
        or any(not isinstance(value, str) for value in encodings)
        or len(set(encodings)) != len(encodings)
    ):
        _raise(protocol.FAIL_EVIDENCE, "foxrun.channelEncodings is invalid.")
    foxrun_flags = {
        key: _bool(foxrun[key], f"foxrun.{key}")
        for key in (
            "deliveryObserved",
            "remoteApplied",
            "sameOriginDropped",
            "laterLocalPublished",
        )
    }

    cleanup = _exact_mapping(document["cleanup"], _CLEANUP_KEYS, "cleanup")
    cleanup_flags = {
        key: _bool(cleanup[key], f"cleanup.{key}")
        for key in (
            "jobClosed",
            "processes",
            "port",
            "barrier",
            "files",
            "junctions",
            "subst",
        )
    }
    graceful = _identity_list(
        cleanup["gracefulOwnedIdentities"],
        "cleanup.gracefulOwnedIdentities",
    )
    forced = _identity_list(
        cleanup["forcedOwnedIdentities"],
        "cleanup.forcedOwnedIdentities",
    )
    exited = _identity_list(
        cleanup["exitedOwnedIdentities"],
        "cleanup.exitedOwnedIdentities",
    )
    residual = _identity_list(
        cleanup["residualOwnedIdentities"],
        "cleanup.residualOwnedIdentities",
    )
    owned_keys = {_identity_evidence_key(item) for item in owned}
    graceful_keys = {
        _identity_evidence_key(item)
        for item in graceful
    }
    forced_keys = {
        _identity_evidence_key(item)
        for item in forced
    }
    exited_keys = {
        _identity_evidence_key(item)
        for item in exited
    }
    residual_keys = {
        _identity_evidence_key(item)
        for item in residual
    }
    if graceful_keys & forced_keys:
        _raise(protocol.FAIL_EVIDENCE, "Cleanup identity outcomes overlap.")
    if exited_keys & residual_keys:
        _raise(
            protocol.FAIL_EVIDENCE,
            "Cleanup exit and residual identity proofs overlap.",
        )

    if passing:
        required_times = [
            times[key]
            for key in (
                "contextObservedAt",
                "initialObservedAt",
                "desktopIdentityCapturedAt",
                "firstObservedAt",
                "barrierWrittenAt",
                "secondObservedAt",
            )
        ]
        if (
            any(value is None for value in required_times)
            or not all(
                float(left) < float(right)
                for left, right in zip(
                    required_times,
                    required_times[1:],
                )
            )
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Connection evidence is not in strict monotonic order.",
            )
        if not all(cleanup_flags.values()):
            _raise(
                protocol.FAIL_CLEANUP,
                "Desktop-live cleanup evidence is incomplete.",
            )

        context_text = marker_excerpts["contextMarker"]
        initial_text = marker_excerpts["initialMarker"]
        first_text = marker_excerpts["firstMarker"]
        second_text = marker_excerpts["secondMarker"]
        assert isinstance(context_text, str)
        assert isinstance(initial_text, str)
        assert isinstance(first_text, str)
        assert isinstance(second_text, str)
        context_match = _REDACTED_CONTEXT_MARKER.fullmatch(context_text)
        initial_match = _REDACTED_TRANSPORT_MARKER.fullmatch(initial_text)
        first_match = _REDACTED_TRANSPORT_MARKER.fullmatch(first_text)
        second_match = _REDACTED_TRANSPORT_MARKER.fullmatch(second_text)
        if (
            context_match is None
            or initial_match is None
            or first_match is None
            or second_match is None
            or any(
                match.group("case") != BASE_CASE
                for match in (
                    context_match,
                    initial_match,
                    first_match,
                    second_match,
                )
            )
            or context_match.group("digest").upper()
            != str(token_digest)[:12]
            or (
                int(initial_match.group("active")),
                int(initial_match.group("accepted")),
            )
            != (0, 0)
            or (
                int(first_match.group("active")),
                int(first_match.group("accepted")),
            )
            != (1, 1)
            or int(second_match.group("active")) != 2
            or int(second_match.group("accepted")) < 2
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Redacted transport marker envelopes are inconsistent.",
            )

        if root_identity is None:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Desktop root identity is unavailable.",
            )
        root_key = _identity_evidence_key(root_identity)
        try:
            desktop_root_consistent = (
                root_key in owned_keys
                and protocol.windows_paths_equal(
                    str(root_identity["executable"]),
                    str(desktop["executable"]),
                )
            )
            validate_uri_handler(
                desktop["uriHandler"],
                str(desktop["executable"]),
            )
            deeplink_consistent = desktop["deeplink"] == build_deeplink(
                connection["port"]
            )
        except Exception:
            desktop_root_consistent = False
            deeplink_consistent = False
        if not desktop_root_consistent or not deeplink_consistent:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Desktop executable, ownership, handler, or deep link drifted.",
            )

        try:
            barrier_directory = ntpath.dirname(str(barrier_path))
            base_summary_path = str(foxrun["baseSummaryPath"])
            path_consistent = (
                protocol.windows_path_key(str(barrier_path))
                and ntpath.basename(str(barrier_path))
                == protocol.DESKTOP_CLIENT_BARRIER_FILENAME
                and ntpath.basename(barrier_directory) == identity["runId"]
                and ntpath.basename(
                    ntpath.dirname(barrier_directory)
                ).casefold()
                == "acceptance"
                and ntpath.basename(base_summary_path) == "summary.json"
                and protocol.windows_paths_equal(
                    ntpath.dirname(base_summary_path),
                    barrier_directory,
                )
            )
        except Exception:
            path_consistent = False
        if not path_consistent:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Barrier and base-summary paths are not one owned run.",
            )

        if (
            token_digest is None
            or barrier_digest
            != _expected_barrier_digest(
                str(identity["runId"]),
                token_digest,
            )
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Barrier digest is not associated with the run token digest.",
            )
        if (
            not graceful_keys.issubset(owned_keys)
            or not forced_keys.issubset(owned_keys)
            or exited_keys != owned_keys
            or residual_keys
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Owned cleanup identities are incomplete or contradictory.",
            )
        if (
            head is None
            or not job_owned
            or not owned
            or external
            or not port_preflight
            or barrier_digest is None
            or not barrier_removed
            or foxrun["baseVerdict"] != "PASS"
            or set(encodings) != {"json", "protobuf"}
            or not all(foxrun_flags.values())
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Desktop-live PASS evidence is incomplete or contradictory.",
            )
    return summary


def _reconcile_final_summary_validation(
    summary: dict[str, Any],
) -> protocol.AcceptanceFailure | None:
    """Record the strongest final validation failure in the terminal verdict."""

    try:
        validate_desktop_live_summary(summary)
    except protocol.AcceptanceFailure as validation_failure:
        summary["verdict"] = validation_failure.code
        return validation_failure
    return None



__all__ = [name for name in globals() if not name.startswith("__")]
