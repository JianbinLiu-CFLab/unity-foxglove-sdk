from __future__ import annotations
from .logging import *
def _poll_base_running(owner: Any, identity: Any) -> None:
    """Handle the poll base running step."""

    try:
        exit_code = owner.poll(identity)
    except job_owner.OwnershipFailure:
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun child identity or Job membership changed.",
        )
    except Exception:
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun child state could not be polled.",
        )
    if exit_code is not None:
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun child exited before Desktop-live evidence completed.",
        )


def _wait_for_run_config(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    path: pathlib.Path,
    repository: pathlib.Path,
    run_id: str,
    port: int,
) -> Mapping[str, Any]:
    """Wait for run config."""

    deadline = _clock_value(dependencies) + RUN_CONFIG_TIMEOUT_SECONDS
    while _clock_value(dependencies) < deadline:
        if dependencies.path_exists(path):
            try:
                document = dependencies.load_json_snapshot(
                    path,
                    MAX_RUN_CONFIG_BYTES,
                )
                if not isinstance(document, Mapping):
                    raise TypeError("Run config is not an object.")
                base_protocol.validate_run_config(document, repository)
            except Exception:
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun child emitted an invalid run-config.",
                )
            if (
                document.get("runId") != run_id
                or document.get("case") != BASE_CASE
                or document.get("foxgloveHost") != LOOPBACK_HOST
                or document.get("foxglovePort") != port
                or pathlib.Path(str(document.get("outputRoot"))) != path.parent
            ):
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun run-config identity drifted.",
                )
            return document
        _poll_base_running(owner, base_identity)
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_FOXRUN_CHILD,
        "Owned FoxRun run-config did not appear before its deadline.",
    )


def _wait_for_foxglove_client_ready(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    config: Mapping[str, Any],
    coordinator_logs: Sequence[pathlib.Path],
) -> None:
    """Wait for foxglove client ready."""

    ready_files = config.get("readyFiles")
    if not isinstance(ready_files, Mapping):
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun run-config has no actor readiness map.",
        )
    raw_ready_path = ready_files.get("foxglove-client")
    if not isinstance(raw_ready_path, str):
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun run-config has no client readiness path.",
        )
    ready_path = pathlib.Path(raw_ready_path)
    deadline = (
        _clock_value(dependencies)
        + BASE_READINESS_TIMEOUT_SECONDS
    )
    while _clock_value(dependencies) < deadline:
        _require_coordinator_log_bounds(
            dependencies,
            coordinator_logs,
        )
        _poll_base_running(owner, base_identity)
        if dependencies.path_exists(ready_path):
            try:
                document = dependencies.load_json_snapshot(
                    ready_path,
                    MAX_ACTOR_READY_BYTES,
                )
            except Exception:
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun client readiness is unreadable.",
                )
            expected_keys = {
                "schemaVersion",
                "runId",
                "case",
                "role",
                "tokenSha256",
                "ready",
                "details",
            }
            if (
                not isinstance(document, Mapping)
                or set(document) != expected_keys
                or type(document.get("schemaVersion")) is not int
                or document.get("schemaVersion") != 1
                or document.get("runId") != config.get("runId")
                or document.get("case") != config.get("case")
                or document.get("role") != "foxglove-client"
                or document.get("tokenSha256")
                != base_protocol.token_sha256(str(config.get("token")))
                or document.get("ready") is not True
            ):
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun client readiness is stale or malformed.",
                )
            details = document.get("details")
            topic_count = len(config.get("topics", ()))
            if (
                not isinstance(details, Mapping)
                or set(details) != {"state", "host", "topicCount"}
                or details.get("state") != "connect-loop-ready"
                or details.get("host") != "loopback"
                or type(details.get("topicCount")) is not int
                or details.get("topicCount") != topic_count
            ):
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun client readiness has another state.",
                )
            return
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_FOXRUN_CHILD,
        "Owned FoxRun client readiness did not appear before its deadline.",
    )


def _wait_for_context_and_initial(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    unity_log: pathlib.Path,
    coordinator_logs: Sequence[pathlib.Path],
    case: str,
    token: str,
) -> tuple[str, float, str, float]:
    """Wait for context and initial."""

    context_deadline = (
        _clock_value(dependencies) + BASE_READINESS_TIMEOUT_SECONDS
    )
    initial_deadline: float | None = None
    context_line: str | None = None
    context_time: float | None = None
    initial_line: str | None = None
    initial_time: float | None = None
    while _clock_value(dependencies) < (
        initial_deadline
        if initial_deadline is not None
        else context_deadline
    ):
        _require_coordinator_log_bounds(
            dependencies,
            coordinator_logs,
        )
        _poll_base_running(owner, base_identity)
        try:
            lines = dependencies.read_log_lines(
                unity_log,
                MAX_UNITY_LOG_BYTES,
            )
            evidence = _scan_unity_log(lines, case=case, token=token)
        except protocol.AcceptanceFailure:
            raise
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity log could not be read for initial transport evidence.",
            )
        sequence = _unique_transport_sequence(evidence)
        _validate_transport_prefix(sequence, maximum_stage=0)
        if sequence and (
            evidence.context is None
            or evidence.context_index is None
            or not evidence.transport_indices
            or evidence.context_index >= evidence.transport_indices[0]
        ):
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Initial transport marker preceded the context marker.",
            )
        if evidence.context is not None and context_line is None:
            context_line = evidence.context
            context_time = _clock_value(dependencies)
            initial_deadline = (
                context_time + CONNECTION_TIMEOUT_SECONDS
            )
        if sequence and initial_line is None:
            initial_line = sequence[0][1]
            initial_time = _clock_value(dependencies)
        if (
            context_line is not None
            and initial_line is not None
            and context_time is not None
            and initial_time is not None
        ):
            _sleep_poll(dependencies)
            _require_coordinator_log_bounds(
                dependencies,
                coordinator_logs,
            )
            _poll_base_running(owner, base_identity)
            stable = _scan_unity_log(
                dependencies.read_log_lines(
                    unity_log,
                    MAX_UNITY_LOG_BYTES,
                ),
                case=case,
                token=token,
            )
            stable_sequence = _unique_transport_sequence(stable)
            _validate_transport_prefix(
                stable_sequence,
                maximum_stage=0,
            )
            if (
                not stable_sequence
                or stable.context is None
                or stable.context_index is None
                or not stable.transport_indices
                or stable.context_index >= stable.transport_indices[0]
            ):
                _raise(
                    protocol.FAIL_DESKTOP_CONNECTION,
                    "Initial 0/0 transport chronology was not stable.",
                )
            return (
                context_line,
                context_time,
                initial_line,
                initial_time,
            )
        _sleep_poll(dependencies)
    if context_line is None:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Context marker did not arrive before its cold-start deadline.",
        )
    _raise(
        protocol.FAIL_DESKTOP_CONNECTION,
        "Stable initial 0/0 marker did not arrive after context readiness.",
    )


def _record_job_member_snapshot(
    owner: Any,
    captured: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ],
) -> tuple[job_owner.ProcessIdentity, ...]:
    """Handle the record job member snapshot step."""

    try:
        members = owner.members()
    except job_owner.OwnershipFailure:
        raise
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Current Job membership could not be captured.",
        )
    if (
        isinstance(members, (str, bytes))
        or not isinstance(members, Sequence)
        or len(members) > MAX_SUMMARY_IDENTITIES
    ):
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Current Job membership exceeded the evidence bound.",
        )
    frozen = tuple(members)
    for identity in frozen:
        try:
            key = _process_identity_key(identity)
        except (ValueError, TypeError, protocol.AcceptanceFailure):
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Current Job membership contains an invalid identity.",
            )
        captured.setdefault(key, identity)
    if len(captured) > MAX_SUMMARY_IDENTITIES:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Captured Job membership exceeded the evidence bound.",
        )
    return frozen


def _refresh_owned_members(
    *,
    owner: Any,
    desktop_identity: job_owner.ProcessIdentity,
    desktop_executable: pathlib.Path,
    captured: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ],
) -> tuple[job_owner.ProcessIdentity, ...]:
    """Handle the refresh owned members step."""

    try:
        desktop_key = _process_identity_key(desktop_identity)
        captured.setdefault(desktop_key, desktop_identity)
        owner.require_owned_identity(desktop_identity)
        externals = owner.external_processes(desktop_executable)
    except job_owner.OwnershipFailure:
        raise
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Desktop ownership could not be revalidated.",
        )
    if externals:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "An exact-path external Desktop process appeared.",
        )
    members = _record_job_member_snapshot(owner, captured)
    if desktop_key not in {
        _process_identity_key(identity)
        for identity in members
    }:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Desktop root is absent from exact Job membership.",
        )
    return members


def _wait_for_transport_stage(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    desktop_identity: Any,
    desktop_executable: pathlib.Path,
    captured_identities: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ],
    unity_log: pathlib.Path,
    coordinator_logs: Sequence[pathlib.Path],
    case: str,
    token: str,
    stage: int,
) -> tuple[str, float, tuple[str, ...]]:
    """Wait for transport stage."""

    if stage not in (1, 2):
        raise ValueError("Transport stage must be one or two.")
    deadline = _clock_value(dependencies) + CONNECTION_TIMEOUT_SECONDS
    while _clock_value(dependencies) < deadline:
        _require_coordinator_log_bounds(
            dependencies,
            coordinator_logs,
        )
        _poll_base_running(owner, base_identity)
        _refresh_owned_members(
            owner=owner,
            desktop_identity=desktop_identity,
            desktop_executable=desktop_executable,
            captured=captured_identities,
        )
        try:
            evidence = _scan_unity_log(
                dependencies.read_log_lines(
                    unity_log,
                    MAX_UNITY_LOG_BYTES,
                ),
                case=case,
                token=token,
            )
        except protocol.AcceptanceFailure:
            raise
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity log could not be read for Desktop transport evidence.",
            )
        sequence = _unique_transport_sequence(evidence)
        _validate_transport_prefix(sequence, maximum_stage=stage)
        if len(sequence) > stage:
            return (
                sequence[stage][1],
                _clock_value(dependencies),
                evidence.transport_lines,
            )
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_DESKTOP_CONNECTION,
        (
            "First Desktop transport marker did not arrive."
            if stage == 1
            else "Second Desktop transport marker did not arrive."
        ),
    )


def _wait_for_base_exit(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    desktop_identity: Any,
    coordinator_logs: Sequence[pathlib.Path],
) -> int:
    """Wait for base exit."""

    deadline = _clock_value(dependencies) + BASE_EXIT_TIMEOUT_SECONDS
    while _clock_value(dependencies) < deadline:
        _require_coordinator_log_bounds(
            dependencies,
            coordinator_logs,
        )
        try:
            owner.require_owned_identity(desktop_identity)
        except job_owner.OwnershipFailure:
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Desktop identity changed while the FoxRun child completed.",
            )
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Desktop identity could not be revalidated during completion.",
            )
        try:
            code = owner.poll(base_identity)
        except Exception:
            _raise(
                protocol.FAIL_FOXRUN_CHILD,
                "Owned FoxRun child exit could not be observed.",
            )
        if code is not None:
            if isinstance(code, bool) or not isinstance(code, int) or code != 0:
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun child did not exit with exact code zero.",
                )
            return code
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_FOXRUN_CHILD,
        "Owned FoxRun child did not exit within its acceptance timeout.",
    )


def _mapped_failure(
    exc: BaseException,
    *,
    stage: str,
) -> protocol.AcceptanceFailure:
    """Handle the mapped failure step."""

    stage_code = {
        "cli": protocol.FAIL_CLI_PROVENANCE,
        "desktop-preflight": protocol.FAIL_DESKTOP_PREFLIGHT,
        "job-create": protocol.FAIL_DESKTOP_PREFLIGHT,
        "base-start": protocol.FAIL_FOXRUN_CHILD,
        "base-config": protocol.FAIL_FOXRUN_CHILD,
        "base-readiness": protocol.FAIL_FOXRUN_CHILD,
        "connection": protocol.FAIL_DESKTOP_CONNECTION,
        "desktop-start": protocol.FAIL_DESKTOP_START,
        "desktop-identity": protocol.FAIL_DESKTOP_IDENTITY,
        "base-exit": protocol.FAIL_FOXRUN_CHILD,
        "evidence": protocol.FAIL_EVIDENCE,
        "cleanup": protocol.FAIL_CLEANUP,
    }.get(stage, protocol.FAIL_EVIDENCE)
    if isinstance(exc, protocol.AcceptanceFailure):
        if exc.code in COORDINATOR_FAILURE_CODES:
            if stage == "desktop-preflight" and exc.code == protocol.FAIL_CLI_PROVENANCE:
                return _failure(protocol.FAIL_DESKTOP_PREFLIGHT, exc.message)
            return exc
        return _failure(stage_code, exc.message)
    if isinstance(exc, job_owner.OwnershipFailure):
        if exc.code in {
            job_owner.FAIL_CLEANUP,
            job_owner.FAIL_DESKTOP_CLOSE,
        }:
            code = protocol.FAIL_CLEANUP
        elif stage in {"desktop-preflight", "job-create"}:
            code = protocol.FAIL_DESKTOP_PREFLIGHT
        elif stage in {"desktop-identity", "connection"} and exc.code in {
            job_owner.FAIL_PROCESS_IDENTITY,
            job_owner.FAIL_PROCESS_OWNERSHIP,
            job_owner.FAIL_DESKTOP_HANDOFF,
            job_owner.FAIL_DESKTOP_PREFLIGHT,
        }:
            code = protocol.FAIL_DESKTOP_IDENTITY
        elif stage == "cleanup":
            code = protocol.FAIL_CLEANUP
        else:
            code = stage_code
        return _failure(code, exc.message)
    return _failure(
        stage_code,
        f"Unexpected {stage} failure: {type(exc).__name__}.",
    )


def _base_command(
    *,
    repository: pathlib.Path,
    unity_editor: pathlib.Path,
    port: int,
    run_id: str,
) -> tuple[str, ...]:
    """Handle the base command step."""

    return (
        str(
            repository
            / "Scripts"
            / "smoke"
            / "foxrun"
            / "phase184_profile_acceptance.py"
        ),
        "--case",
        BASE_CASE,
        "--unity-editor",
        str(unity_editor),
        "--foxglove-port",
        str(port),
        "--run-id",
        run_id,
        "--wait-for-desktop-client",
        "--retain-success-workspace",
    )


def _allocated_run_id(
    args: argparse.Namespace,
    dependencies: CoordinatorDependencies,
) -> str:
    """Handle the allocated run id step."""

    if getattr(args, "run_id", None) is not None:
        return validate_run_id(args.run_id)
    try:
        now = dependencies.utc_now()
        nonce = dependencies.nonce()
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Desktop-live run identity allocation failed.",
        )
    if (
        not isinstance(now, dt.datetime)
        or now.utcoffset() != dt.timedelta(0)
    ):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Desktop-live UTC clock is invalid.",
        )
    return generate_run_id(
        timestamp=now.strftime("%Y%m%d-%H%M%S"),
        nonce=nonce,
    )


def _identity_documents(
    identities: Sequence[job_owner.ProcessIdentity],
) -> list[dict[str, object]]:
    """Handle the identity documents step."""

    if (
        isinstance(identities, (str, bytes))
        or len(identities) > MAX_SUMMARY_IDENTITIES
    ):
        _raise(
            protocol.FAIL_EVIDENCE,
            "Owned identity collection exceeded its bound.",
        )
    return [process_identity_document(identity) for identity in identities]



__all__ = [name for name in globals() if not name.startswith("__")]
