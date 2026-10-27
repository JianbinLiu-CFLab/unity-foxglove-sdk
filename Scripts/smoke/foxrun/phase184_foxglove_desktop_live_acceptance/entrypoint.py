from __future__ import annotations
from .orchestration import *
def run_acceptance(
    args: argparse.Namespace,
    *,
    dependencies: CoordinatorDependencies | None = None,
) -> dict[str, object]:
    """Run one injected fail-closed Desktop-live coordination transaction."""

    active = _default_dependencies() if dependencies is None else dependencies
    repository = pathlib.Path(active.repository_root).resolve()
    run_id = _allocated_run_id(args, active)
    base_output = (
        repository
        / "build"
        / "phase184"
        / "acceptance"
        / run_id
    ).resolve()
    coordinator_output = (
        repository
        / "build"
        / "phase184"
        / "desktop-live"
        / run_id
    ).resolve()
    barrier_path = (
        base_output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
    )
    coordinator_logs = (
        coordinator_output / "foxrun.stdout.log",
        coordinator_output / "foxrun.stderr.log",
        coordinator_output / "desktop.stdout.log",
        coordinator_output / "desktop.stderr.log",
    )
    wrapper_summary_path = (
        coordinator_output / DESKTOP_LIVE_SUMMARY_FILENAME
    )
    summary = _empty_summary(
        run_id=run_id,
        base_output=base_output,
        barrier_path=barrier_path,
        desktop_executable=getattr(args, "desktop_executable", None),
        receipt_path=getattr(args, "cli_receipt", None),
    )

    stage = "desktop-preflight"
    failure: protocol.AcceptanceFailure | None = None
    owner: Any | None = None
    reservation: Any | None = None
    reservation_released = False
    output_created = False
    barrier_owned = False
    job_closed = False
    close_attempted = False
    port: int | None = None
    base_identity: Any | None = None
    desktop_identity: Any | None = None
    captured_identities: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ] = {}
    base_cleanup: dict[str, bool] = {}
    success_ready = False
    close_summary: job_owner.CloseSummary | None = None
    raw_token: str | None = None
    desktop_lease_manager: Any | None = None
    desktop_lease: Any | None = None
    desktop_snapshot: cli_install.ExecutableSnapshot | None = None
    propagating_exception: BaseException | None = None

    try:
        validate_arguments(
            args,
            platform_name=active.platform_name,
            is_file=active.is_file,
        )
        base_script = (
            repository
            / "Scripts"
            / "smoke"
            / "foxrun"
            / "phase184_profile_acceptance.py"
        )
        if active.is_file(base_script) is not True:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Exact Phase184-G base acceptance script is unavailable.",
            )
        if active.path_exists(base_output):
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Selected Phase184-G base output already exists.",
            )
        if active.path_exists(coordinator_output):
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Selected Desktop-live coordinator output already exists.",
            )
        active.make_directory(coordinator_output)
        output_created = True

        stage = "cli"
        verified_cli = active.verify_cli(
            args.foxglove_cli,
            args.cli_receipt,
        )
        if not isinstance(
            verified_cli,
            cli_install.VerifiedCliIdentity,
        ):
            _raise(
                protocol.FAIL_CLI_PROVENANCE,
                "CLI verifier did not return a public verified identity.",
            )
        summary["cli"] = verified_cli.to_document()

        stage = "desktop-preflight"
        try:
            lease_manager = active.desktop_executable_lease_factory(
                args.desktop_executable
            )
            entered_lease = lease_manager.__enter__()
        except protocol.AcceptanceFailure:
            raise
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove Desktop executable lease could not be acquired.",
            )
        desktop_lease_manager = lease_manager
        desktop_lease = entered_lease
        desktop_snapshot = _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_PREFLIGHT,
        )
        desktop_version = active.read_desktop_file_version(
            args.desktop_executable
        )
        desktop_version = _bounded_string(
            desktop_version,
            "Desktop file version",
            allow_none=False,
        )
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_PREFLIGHT,
            expected=desktop_snapshot,
        )
        try:
            path_sha256 = protocol.validate_sha256(
                active.sha256_file(args.desktop_executable)
            )
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove Desktop SHA-256 could not be verified.",
            )
        if path_sha256 != desktop_snapshot.sha256:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove Desktop path hash does not match its lease.",
            )
        handler_command = active.read_uri_handler_command()
        handler_command = validate_uri_handler(
            handler_command,
            args.desktop_executable,
            parser=active.parse_windows_command_line,
        )
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_PREFLIGHT,
            expected=desktop_snapshot,
        )
        head = active.read_repository_head(repository)
        if (
            not isinstance(head, str)
            or _LOWER_GIT_OBJECT.fullmatch(head) is None
        ):
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Repository HEAD identity is malformed.",
            )
        windows_version = _bounded_string(
            active.read_windows_version(),
            "Windows version identity",
            allow_none=False,
        )
        summary["identity"]["repositoryHead"] = head
        summary["identity"]["windowsVersion"] = windows_version
        summary["desktop"].update(
            {
                "executable": str(args.desktop_executable),
                "fileVersion": desktop_version,
                "sha256": desktop_snapshot.sha256,
                "uriHandler": handler_command,
            }
        )

        reservation = active.reserve_port(args.foxglove_port)
        try:
            port = _validate_port(getattr(reservation, "port"))
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Exclusive loopback reservation returned an invalid port.",
            )
        if args.foxglove_port is not None and port != args.foxglove_port:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Explicit Foxglove port reservation drifted.",
            )
        summary["connection"]["port"] = port
        summary["connection"]["portPreflight"] = True
        deeplink = build_deeplink(port)
        summary["desktop"]["deeplink"] = deeplink

        stage = "job-create"
        owner = active.job_owner_factory(args.desktop_executable)
        stage = "desktop-preflight"
        owner.require_no_external_processes(args.desktop_executable)

        reservation.release()
        reservation_released = True
        stage = "base-start"
        cleaned_environment = build_clean_environment(active.environment)
        base_identity = owner.launch_suspended_owned(
            pathlib.Path(sys.executable),
            _base_command(
                repository=repository,
                unity_editor=args.unity_editor,
                port=port,
                run_id=run_id,
            ),
            cwd=repository,
            environment=cleaned_environment,
            stdout_log=coordinator_output / "foxrun.stdout.log",
            stderr_log=coordinator_output / "foxrun.stderr.log",
            handoff_policy=job_owner.RootHandoffPolicy.OWNED_PROCESS,
        )
        captured_identities.setdefault(
            _process_identity_key(base_identity),
            base_identity,
        )

        stage = "base-config"
        config = _wait_for_run_config(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            path=base_output / "run-config.json",
            repository=repository,
            run_id=run_id,
            port=port,
        )
        raw_token = str(config["token"])
        token_digest = hashlib.sha256(
            raw_token.encode("utf-8")
        ).hexdigest().upper()
        summary["identity"]["tokenSha256"] = token_digest
        unity_log = pathlib.Path(str(config["unityLog"]))

        stage = "base-readiness"
        _wait_for_foxglove_client_ready(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            config=config,
            coordinator_logs=coordinator_logs,
        )
        stage = "connection"
        (
            context_line,
            context_time,
            initial_line,
            initial_time,
        ) = _wait_for_context_and_initial(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            unity_log=unity_log,
            coordinator_logs=coordinator_logs,
            case=BASE_CASE,
            token=raw_token,
        )
        summary["connection"].update(
            {
                "contextMarker": _redact_excerpt(
                    context_line,
                    raw_token,
                ),
                "initialMarker": _redact_excerpt(
                    initial_line,
                    raw_token,
                ),
                "contextObservedAt": context_time,
                "initialObservedAt": initial_time,
            }
        )

        stage = "desktop-identity"
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_IDENTITY,
            expected=desktop_snapshot,
        )
        stage = "desktop-start"
        desktop_identity = owner.launch_suspended_owned(
            args.desktop_executable,
            (deeplink,),
            cwd=repository,
            environment=cleaned_environment,
            stdout_log=coordinator_output / "desktop.stdout.log",
            stderr_log=coordinator_output / "desktop.stderr.log",
            handoff_policy=(
                job_owner.RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE
            ),
        )
        stage = "desktop-identity"
        try:
            launched_selected_executable = (
                protocol.windows_paths_equal(
                    desktop_identity.executable,
                    args.desktop_executable,
                )
            )
        except Exception:
            launched_selected_executable = False
        if not launched_selected_executable:
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Desktop process image does not match the leased executable.",
            )
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_IDENTITY,
            expected=desktop_snapshot,
        )
        captured_identities.setdefault(
            _process_identity_key(desktop_identity),
            desktop_identity,
        )
        summary["desktop"]["rootIdentity"] = process_identity_document(
            desktop_identity
        )
        summary["desktop"]["jobOwned"] = True
        summary["connection"]["desktopIdentityCapturedAt"] = _clock_value(
            active
        )

        _refresh_owned_members(
            owner=owner,
            desktop_identity=desktop_identity,
            desktop_executable=args.desktop_executable,
            captured=captured_identities,
        )
        summary["desktop"]["ownedMemberIdentities"] = _identity_documents(
            tuple(captured_identities.values())
        )
        _release_executable_lease(desktop_lease_manager)
        desktop_lease_manager = None
        desktop_lease = None

        stage = "connection"
        first_line, first_time, _first_transport_lines = (
            _wait_for_transport_stage(
                dependencies=active,
                owner=owner,
                base_identity=base_identity,
                desktop_identity=desktop_identity,
                desktop_executable=args.desktop_executable,
                captured_identities=captured_identities,
                unity_log=unity_log,
                coordinator_logs=coordinator_logs,
                case=BASE_CASE,
                token=raw_token,
                stage=1,
            )
        )
        summary["connection"]["firstMarker"] = _redact_excerpt(
            first_line,
            raw_token,
        )
        summary["connection"]["firstObservedAt"] = first_time

        _poll_base_running(owner, base_identity)
        barrier_payload = {
            "schemaVersion": protocol.DESKTOP_CLIENT_BARRIER_SCHEMA_VERSION,
            "runId": run_id,
            "tokenDigest": token_digest,
            "state": protocol.DESKTOP_CLIENT_BARRIER_STATE,
            "acceptedClients": 1,
        }
        active.write_json_atomic(
            barrier_path,
            barrier_payload,
            max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
        )
        barrier_owned = True
        try:
            barrier_digest = protocol.validate_sha256(
                active.sha256_file(barrier_path)
            )
        except Exception:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Owned Desktop barrier SHA-256 could not be captured.",
            )
        summary["connection"]["barrierDigest"] = barrier_digest
        summary["connection"]["barrierWrittenAt"] = _clock_value(active)

        (
            second_line,
            second_time,
            transport_lines,
        ) = _wait_for_transport_stage(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            desktop_identity=desktop_identity,
            desktop_executable=args.desktop_executable,
            captured_identities=captured_identities,
            unity_log=unity_log,
            coordinator_logs=coordinator_logs,
            case=BASE_CASE,
            token=raw_token,
            stage=2,
        )
        try:
            protocol.validate_transport_client_transition_order(
                transport_lines,
                case=BASE_CASE,
                token=raw_token,
            )
        except protocol.AcceptanceFailure:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Full transport client transition evidence is invalid.",
            )
        summary["connection"]["secondMarker"] = _redact_excerpt(
            second_line,
            raw_token,
        )
        summary["connection"]["secondObservedAt"] = second_time

        stage = "base-exit"
        _wait_for_base_exit(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            desktop_identity=desktop_identity,
            coordinator_logs=coordinator_logs,
        )

        stage = "evidence"
        base_summary_path = base_output / "summary.json"
        try:
            base_summary = active.load_json_snapshot(
                base_summary_path,
                MAX_BASE_SUMMARY_BYTES,
            )
            if not isinstance(base_summary, Mapping):
                raise TypeError("Base summary is not an object.")
            base_protocol.validate_summary(
                base_summary,
                expected_case=BASE_CASE,
                expected_token=raw_token,
            )
        except Exception:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Phase184-G base summary is invalid or stale.",
            )
        summary["foxrun"]["baseVerdict"] = base_summary.get("verdict")
        summary["identity"]["unityVersion"] = base_summary.get(
            "identity",
            {},
        ).get("unityVersion")
        cleanup_document = base_summary.get("cleanup")
        if isinstance(cleanup_document, Mapping):
            base_cleanup = {
                key: cleanup_document.get(key) is True
                for key in (
                    "processes",
                    "files",
                    "junctions",
                    "subst",
                )
            }
        foxglove = base_summary.get("foxglove")
        origin = base_summary.get("origin")
        if isinstance(foxglove, Mapping):
            summary["foxrun"]["channelEncodings"] = list(
                foxglove.get("channelEncodings", [])
            )
            summary["foxrun"]["deliveryObserved"] = (
                foxglove.get("deliveryObserved") is True
            )
        if isinstance(origin, Mapping):
            for key in (
                "remoteApplied",
                "sameOriginDropped",
                "laterLocalPublished",
            ):
                summary["foxrun"][key] = origin.get(key) is True
        if base_summary.get("verdict") != "PASS":
            _raise(
                protocol.FAIL_EVIDENCE,
                "Phase184-G base summary did not report PASS.",
            )

        stage = "desktop-identity"
        _refresh_owned_members(
            owner=owner,
            desktop_identity=desktop_identity,
            desktop_executable=args.desktop_executable,
            captured=captured_identities,
        )
        summary["desktop"]["ownedMemberIdentities"] = _identity_documents(
            tuple(captured_identities.values())
        )
        close_attempted = True
        close_summary = owner.request_owned_desktop_close(
            grace_seconds=DESKTOP_CLOSE_GRACE_SECONDS,
            reject_external=True,
        )
        if not isinstance(close_summary, job_owner.CloseSummary):
            _raise(
                protocol.FAIL_CLEANUP,
                "Desktop close did not return owned identity evidence.",
            )
        for identity in (
            close_summary.requested
            + close_summary.graceful
            + close_summary.forced
        ):
            captured_identities.setdefault(
                _process_identity_key(identity),
                identity,
            )
        if len(captured_identities) > MAX_SUMMARY_IDENTITIES:
            _raise(
                protocol.FAIL_CLEANUP,
                "Desktop close identity evidence exceeded its bound.",
            )
        job_closed = True
        summary["cleanup"]["gracefulOwnedIdentities"] = (
            _identity_documents(close_summary.graceful)
        )
        summary["cleanup"]["forcedOwnedIdentities"] = (
            _identity_documents(close_summary.forced)
        )
        success_ready = True
    except BaseException as exc:
        if isinstance(exc, (KeyboardInterrupt, SystemExit)):
            propagating_exception = exc
            raise
        failure = _mapped_failure(exc, stage=stage)
    finally:
        if desktop_lease_manager is not None:
            try:
                _release_executable_lease(
                    desktop_lease_manager,
                    propagating_exception,
                )
                desktop_lease_manager = None
                desktop_lease = None
            except BaseException as exc:
                if (
                    propagating_exception is None
                    and failure is None
                ):
                    failure = _mapped_failure(
                        exc,
                        stage="desktop-identity",
                    )

        if reservation is not None and not reservation_released:
            try:
                reservation.release()
                reservation_released = True
            except Exception:
                if failure is None:
                    failure = _failure(
                        protocol.FAIL_CLEANUP,
                        "Loopback port reservation cleanup failed.",
                    )

        if owner is not None:
            if not job_closed and not close_attempted:
                try:
                    _record_job_member_snapshot(
                        owner,
                        captured_identities,
                    )
                except Exception as exc:
                    if failure is None:
                        failure = _mapped_failure(
                            exc,
                            stage="desktop-identity",
                        )
            if captured_identities:
                summary["desktop"]["ownedMemberIdentities"] = (
                    _identity_documents(
                        tuple(captured_identities.values())
                    )
                )
            if not job_closed:
                try:
                    owner.close()
                    job_closed = True
                except Exception as exc:
                    if failure is None:
                        failure = _mapped_failure(exc, stage="cleanup")
            try:
                externals = owner.recorded_external_processes
                summary["desktop"]["externalIdentities"] = (
                    _identity_documents(externals)
                )
            except Exception as exc:
                if failure is None:
                    failure = _mapped_failure(
                        exc,
                        stage="desktop-identity",
                    )

        exited_identities: tuple[job_owner.ProcessIdentity, ...] = ()
        residual_identities: tuple[job_owner.ProcessIdentity, ...] = ()
        identity_cleanup_proved = not captured_identities
        if job_closed and captured_identities:
            frozen_captured = tuple(captured_identities.values())
            captured_keys = set(captured_identities)
            try:
                verification = active.verify_identities_exited(
                    frozen_captured,
                    IDENTITY_EXIT_TIMEOUT_SECONDS,
                )
                if not isinstance(
                    verification,
                    IdentityExitVerification,
                ):
                    raise TypeError(
                        "Identity verifier returned another type."
                    )
                exited_keys = {
                    _process_identity_key(identity)
                    for identity in verification.exited
                }
                residual_keys = {
                    _process_identity_key(identity)
                    for identity in verification.residual
                }
                if (
                    exited_keys | residual_keys != captured_keys
                    or exited_keys & residual_keys
                ):
                    raise ValueError(
                        "Identity verifier did not cover the capture."
                    )
                exited_identities = verification.exited
                residual_identities = verification.residual
                identity_cleanup_proved = (
                    exited_keys == captured_keys
                    and not residual_keys
                )
            except Exception:
                residual_identities = frozen_captured
                identity_cleanup_proved = False
        summary["cleanup"]["exitedOwnedIdentities"] = (
            _identity_documents(exited_identities)
        )
        summary["cleanup"]["residualOwnedIdentities"] = (
            _identity_documents(residual_identities)
        )

        barrier_removed = False
        if barrier_owned:
            try:
                barrier_removed = (
                    active.remove_owned_file(barrier_path) is True
                )
            except Exception:
                barrier_removed = False
        else:
            try:
                barrier_removed = not active.path_exists(barrier_path)
            except Exception:
                barrier_removed = False
        summary["connection"]["barrierRemoved"] = barrier_removed

        port_bindable = False
        if (
            port is not None
            and reservation_released
            and job_closed
        ):
            try:
                port_bindable = (
                    active.port_is_bindable(LOOPBACK_HOST, port) is True
                )
            except Exception:
                port_bindable = False

        process_cleanup = (
            job_closed
            and identity_cleanup_proved
            and (
                base_cleanup.get("processes", False)
                if base_cleanup
                else True
            )
        )
        summary["cleanup"].update(
            {
                "jobClosed": job_closed,
                "processes": process_cleanup,
                "port": port_bindable,
                "barrier": barrier_removed,
                "files": base_cleanup.get("files", False),
                "junctions": base_cleanup.get("junctions", False),
                "subst": base_cleanup.get("subst", False),
            }
        )

        if job_closed and captured_identities and not identity_cleanup_proved:
            failure = _failure(
                protocol.FAIL_CLEANUP,
                "A captured owned process identity remained after Job close.",
            )

        if success_ready and summary["desktop"]["externalIdentities"]:
            failure = _failure(
                protocol.FAIL_DESKTOP_IDENTITY,
                "External Desktop identity invalidated acceptance.",
            )
        required_cleanup = (
            summary["cleanup"]["jobClosed"],
            summary["cleanup"]["processes"],
            summary["cleanup"]["port"],
            summary["cleanup"]["barrier"],
            summary["cleanup"]["files"],
            summary["cleanup"]["junctions"],
            summary["cleanup"]["subst"],
        )
        if success_ready and not all(required_cleanup):
            failure = _failure(
                protocol.FAIL_CLEANUP,
                "Desktop-live cleanup proof is incomplete.",
            )
        summary["verdict"] = "PASS" if failure is None else failure.code

        validation_failure = _reconcile_final_summary_validation(summary)
        if validation_failure is not None:
            failure = validation_failure

        if output_created:
            try:
                active.write_json_atomic(
                    wrapper_summary_path,
                    summary,
                    max_bytes=MAX_SUMMARY_BYTES,
                )
            except Exception:
                summary["verdict"] = protocol.FAIL_EVIDENCE
    return summary


def main(
    argv: Sequence[str] | None = None,
    *,
    dependencies: CoordinatorDependencies | None = None,
) -> int:
    """Return zero only for one validated durable PASS summary."""

    try:
        args = parse_args(argv)
        summary = run_acceptance(args, dependencies=dependencies)
        validate_desktop_live_summary(summary)
    except protocol.AcceptanceFailure:
        return 1
    except SystemExit as exc:
        return int(exc.code)
    return 0 if summary["verdict"] == "PASS" else 1


def _entrypoint() -> int:
    """Translate stable failures into a process exit code."""

    return main()



__all__ = [name for name in globals() if not name.startswith("__")]
