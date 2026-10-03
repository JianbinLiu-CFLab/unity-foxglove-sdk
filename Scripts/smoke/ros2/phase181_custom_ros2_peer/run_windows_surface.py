from __future__ import annotations
from .release_subst_mapping_and_require_player_path import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _run_windows_surface(
    args: argparse.Namespace,
    *,
    surface: str,
    zenoh_session_config: pathlib.Path | None = None,
) -> int:
    """Share strict preflight, source staging, and evidence between Editor/Player."""

    if surface not in {"editor", "player"}:
        raise ValueError("Phase181 Windows surface must be editor or player.")
    success_verdict = getattr(args, "success_verdict", "PASS")
    if not isinstance(success_verdict, str) or not re.fullmatch(r"(?:PASS|PHASE181_[A-Z0-9_]+_PASS)", success_verdict):
        raise PeerFailure("FAIL_ARGUMENTS", "The requested Windows acceptance success verdict is not a stable Phase181 identifier.")
    repository = workspace_root()
    profile_id = args.profile_id or (args.distro + "-" + args.rmw.removeprefix("rmw_").replace("_cpp", ""))
    output_directory = _profile_build_directory(repository, profile_id)
    summary_name = "windows-local-editor.json" if surface == "editor" else "windows-player.json"
    summary_path = pathlib.Path(args.summary_json) if args.summary_json is not None else output_directory / summary_name
    summary: dict[str, object] = {
        "phase": 181,
        "role": "windows-local-editor" if surface == "editor" else "windows-player",
        "surface": surface,
        "transportScope": "windows-local-loopback",
        "profileId": profile_id,
        "distro": args.distro,
        "rmwImplementation": args.rmw,
        "domainId": args.domain_id,
        "commandLabels": {},
        "processOwnership": {},
    }
    failure: PeerFailure | None = None
    workspace: pathlib.Path | None = None
    worker_process: subprocess.Popen[str] | None = None
    player_process: subprocess.Popen[str] | None = None
    editor_process: subprocess.Popen[str] | None = None
    editor_plugin_alias_stack = contextlib.ExitStack()
    peer_workspace_alias_stack = contextlib.ExitStack()
    worker_stream = None
    peer_build_sealed = False
    exit_code = 1
    try:
        if args.workspace is not None:
            raise PeerFailure("FAIL_PEER_WORKSPACE", "The profile helper owns its peer workspace and does not accept an external workspace.")
        if args.unity_batch and surface != "editor":
            raise PeerFailure("FAIL_ARGUMENTS", "Unity Editor Batch acceptance is valid only for the Editor surface.")
        ready_timeout = _require_positive_timeout(args.ready_timeout_seconds, "The Unity readiness timeout")
        apply_timeout = _require_positive_timeout(args.apply_timeout_seconds, "The Unity apply timeout")
        if args.rmw == "rmw_zenoh_cpp" and not args.zenoh_topology_id:
            raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "Zenoh custom-interface acceptance requires an explicit topology identity.")
        if zenoh_session_config is not None and args.rmw != "rmw_zenoh_cpp":
            raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "Only the Zenoh profile may supply an explicit Zenoh session config.")

        static_package = pathlib.Path(args.static_interface_package or default_static_interface_package(repository))
        lock = load_static_interface_lock(static_package)
        try:
            protocol.require_interface_digest(lock.interface_digest, args.interface_digest or lock.interface_digest)
        except protocol.ProtocolFailure as exc:
            raise PeerFailure(exc.code, "The requested custom interface digest is not the locked source digest.") from exc
        summary.update(
            {
                "interfacePackage": STATIC_INTERFACE_PACKAGE_ID,
                "rosPackageName": lock.ros_package_name,
                "interfaceRevision": lock.interface_revision,
                "interfaceDigest": lock.interface_digest,
                "interfaceDigestPrefix": protocol.digest_prefix(lock.interface_digest),
            }
        )
        selected_addon = require_selected_typesupport_addon(repository, args.distro)
        summary["selectedTypesupportAddon"] = selected_addon
        editor_runtime_plugins: pathlib.Path | None = None
        editor_custom_plugins: pathlib.Path | None = None
        if args.unity_batch:
            editor_runtime_plugins, editor_custom_plugins = resolve_editor_batch_native_plugin_directories(
                repository,
                args.distro,
                selected_addon,
            )

        ros2_root = pathlib.Path(args.ros2_root or ros2env.default_ros2_root(args.distro, repository))
        toolchain = resolve_windows_peer_toolchain(ros2_root)
        build_environment = ros2env.build_ros_env(
            toolchain.ros2_root,
            args.rmw,
            args.discovery_range,
            str(args.domain_id),
            args.distro,
        )
        build_environment = merge_windows_peer_build_environment(
            build_environment,
            capture_windows_msvc_environment(build_environment),
        )
        colcon_command = build_windows_colcon_command(
            toolchain.colcon_executable,
            lock.ros_package_name,
            toolchain.python_executable,
        )
        cache_key = peer_build_cache_key(
            lock,
            profile_id,
            args.distro,
            args.rmw,
            toolchain,
            colcon_command,
        )
        workspace, peer_build_reused = prepare_peer_build_workspace(
            repository / "build" / "phase181",
            profile_id,
            cache_key,
            lock.ros_package_name,
        )
        _, peer_runtime_workspace = peer_workspace_alias_stack.enter_context(
            temporary_short_windows_peer_workspace(workspace)
        )
        peer_build_sealed = peer_build_reused
        summary["processOwnership"] = {"workspaceOwned": True}
        summary["peerBuild"] = "reused" if peer_build_reused else "cold"

        license_repair_command = build_addon_license_repair_command(repository, args.distro)
        validator_command = build_addon_validator_command(repository, args.distro, args.rmw)
        summary["commandLabels"] = {
            "addonLicenseEolRepair": protocol.bounded_command_label(license_repair_command),
            "addonValidator": protocol.bounded_command_label(validator_command),
        }
        print("[phase181:" + profile_id + "] Verifying the selected custom typesupport legal-text inventory.", flush=True)
        run_logged_owned_command(
            license_repair_command,
            cwd=repository,
            env=ros2env.sanitized_subprocess_env(os.environ),
            log_path=workspace / "typesupport-license-eol-repair.log",
            timeout_seconds=min(60.0, ready_timeout),
            failure_code="FAIL_TYPESUPPORT_PREFLIGHT",
        )
        print("[phase181:" + profile_id + "] Checking the selected custom typesupport add-on.", flush=True)
        run_logged_owned_command(
            validator_command,
            cwd=repository,
            env=ros2env.sanitized_subprocess_env(os.environ),
            log_path=workspace / "typesupport-preflight.log",
            timeout_seconds=min(60.0, ready_timeout),
            failure_code="FAIL_TYPESUPPORT_PREFLIGHT",
        )
        summary["typesupportPreflight"] = "passed"

        summary["commandLabels"] = {
            **summary["commandLabels"],
            "colcon": protocol.bounded_command_label(colcon_command),
        }
        if peer_build_reused:
            print(
                "[phase181:" + profile_id + "] Reusing the verified locked custom ROS2 peer build; starting endpoints.",
                flush=True,
            )
        else:
            print(
                "[phase181:" + profile_id + "] Cold-building the locked custom ROS2 peer with no total timeout; "
                + "only "
                + format(peer_build_timeout_seconds(), "g")
                + " seconds without build output is treated as stalled. Live progress follows and is saved to "
                + str(workspace / "colcon-build.log")
                + ".",
                flush=True,
            )
            stage_locked_ros_source(static_package, peer_runtime_workspace, lock.ros_package_name)
            run_logged_owned_command(
                colcon_command,
                cwd=peer_runtime_workspace,
                env=build_environment,
                log_path=workspace / "colcon-build.log",
                timeout_seconds=peer_build_timeout_seconds(),
                failure_code="FAIL_PEER_BUILD",
                stream_output=True,
                output_prefix="[phase181:" + profile_id + "][build] ",
            )
            seal_peer_build_workspace(workspace, cache_key, lock.ros_package_name)
            peer_build_sealed = True

        peer_environment = build_peer_environment(
            build_environment,
            toolchain.ros2_root,
            peer_runtime_workspace / "install",
            distro=args.distro,
            rmw=args.rmw,
            domain_id=args.domain_id,
            topology_id=args.zenoh_topology_id or None,
            zenoh_session_config=zenoh_session_config,
        )
        run_token = "phase181-peer-" + uuid.uuid4().hex
        editor_command: list[str] | None = None
        editor_environment: dict[str, str] | None = None
        if surface == "player":
            player = _require_player_path(args.player)
            unity_log = pathlib.Path(args.player_log or output_directory / "windows-player.log")
            unity_log_offset = protocol.log_offset(unity_log)
            player_timeout = min(600.0, ready_timeout + apply_timeout + 30.0)
            player_command = build_player_command(player, unity_log, run_token, player_timeout)
            player_environment = build_player_environment(
                build_environment,
                distro=args.distro,
                rmw=args.rmw,
                domain_id=args.domain_id,
                interface_revision=lock.interface_revision,
                interface_digest=lock.interface_digest,
                topology_id=args.zenoh_topology_id or None,
                zenoh_session_config=zenoh_session_config,
                discovery_range=args.discovery_range,
            )
            summary["commandLabels"] = {
                **summary["commandLabels"],
                "player": protocol.bounded_command_label(player_command),
            }
            player_process = subprocess.Popen(
                player_command,
                cwd=str(player.parent),
                env=player_environment,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                text=True,
                shell=False,
                **worker_launch_options(),
            )
            summary["processOwnership"] = {
                **summary["processOwnership"],
                "playerPid": player_process.pid,
            }
            worker_role = "windows-player"
        else:
            if args.unity_batch:
                editor = _require_unity_editor_path(args.unity_editor)
                unity_log = pathlib.Path(args.unity_log or output_directory / "unity-editor-batch.log")
                unity_log.parent.mkdir(parents=True, exist_ok=True)
                editor_command = build_editor_batch_command(editor, repository / "Unity2Foxglove", unity_log)
                editor_environment = build_player_environment(
                    build_environment,
                    distro=args.distro,
                    rmw=args.rmw,
                    domain_id=args.domain_id,
                    interface_revision=lock.interface_revision,
                    interface_digest=lock.interface_digest,
                    topology_id=args.zenoh_topology_id or None,
                    zenoh_session_config=zenoh_session_config,
                    discovery_range=args.discovery_range,
                )
                summary["commandLabels"] = {
                    **summary["commandLabels"],
                    "unityBatch": protocol.bounded_command_label(editor_command),
                }
            else:
                unity_log = pathlib.Path(args.unity_log or default_unity_editor_log_path())
            unity_log_offset = protocol.log_offset(unity_log)
            worker_role = "windows-local-editor"

        worker_result_path = workspace / "worker-result.json"
        worker_ready_path = workspace / "worker-ready.json"
        try:
            worker_result_path.unlink(missing_ok=True)
            worker_ready_path.unlink(missing_ok=True)
        except OSError as exc:
            raise PeerFailure("FAIL_PEER_WORKSPACE", "The owned peer workspace could not clear prior worker evidence.") from exc
        worker_command = build_worker_command(
            toolchain.python_executable,
            role=worker_role,
            surface=surface,
            workspace=workspace,
            interface_digest=lock.interface_digest,
            token=run_token,
            unity_log=unity_log,
            result_json=worker_result_path,
            worker_ready_json=worker_ready_path,
            distro=args.distro,
            rmw=args.rmw,
            domain_id=args.domain_id,
            unity_log_offset=unity_log_offset,
            static_interface_package=static_package,
            ready_timeout_seconds=ready_timeout,
            apply_timeout_seconds=apply_timeout,
        )
        summary["commandLabels"] = {
            **summary["commandLabels"],
            "worker": protocol.bounded_command_label(worker_command),
        }
        worker_log_path = workspace / "peer-worker.log"
        worker_stream = worker_log_path.open("w", encoding="utf-8", errors="replace")
        print(
            "[phase181:" + profile_id + "] Starting generated custom ROS2 endpoints (up to "
            + format(min(_WORKER_STARTUP_TIMEOUT_SECONDS, ready_timeout), "g")
            + " seconds).",
            flush=True,
        )
        worker_process = subprocess.Popen(
            worker_command,
            cwd=str(peer_runtime_workspace),
            env=peer_environment,
            text=True,
            stdout=worker_stream,
            stderr=subprocess.STDOUT,
            shell=False,
            **worker_launch_options(),
        )
        summary["processOwnership"] = {
            **summary["processOwnership"],
            "workerPid": worker_process.pid,
        }
        wait_for_matching_worker_ready(
            worker_process,
            worker_ready_path,
            lock,
            min(_WORKER_STARTUP_TIMEOUT_SECONDS, ready_timeout),
        )
        if surface == "editor":
            if args.unity_batch:
                if editor_command is None or editor_environment is None:
                    raise PeerFailure("FAIL_EDITOR_BATCH", "The owned Unity Editor Batch launch was not prepared.")
                print(
                    "[phase181:" + profile_id + "] Peer endpoints are ready; starting the owned Unity Editor Batch probe.",
                    flush=True,
                )
            else:
                print(
                    "[phase181:" + profile_id + "] Peer endpoints are ready. Enter Play Mode now; waiting for Unity's subscription "
                    + "for up to " + format(ready_timeout, "g") + " seconds.",
                    flush=True,
                )
        if surface == "editor" and args.unity_batch:
            if editor_runtime_plugins is None or editor_custom_plugins is None:
                raise PeerFailure("FAIL_EDITOR_BATCH", "The selected Unity native plugin directories were not prepared.")
            custom_plugin_alias = editor_plugin_alias_stack.enter_context(
                temporary_short_windows_plugin_alias(editor_custom_plugins)
            )
            editor_environment = build_editor_batch_environment(
                editor_environment,
                editor_runtime_plugins,
                custom_plugin_alias,
            )
            editor_process = subprocess.Popen(
                editor_command,
                cwd=str(repository),
                env=editor_environment,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                text=True,
                shell=False,
                **worker_launch_options(),
            )
            summary["processOwnership"] = {
                **summary["processOwnership"],
                "unityBatchPid": editor_process.pid,
            }
        worker_deadline = time.monotonic() + ready_timeout + apply_timeout + 30.0
        while worker_process.poll() is None:
            if editor_process is not None:
                editor_exit = editor_process.poll()
                if editor_exit is not None and editor_exit != 0:
                    summary["unityBatchExitCode"] = editor_exit
                    raise PeerFailure(
                        "FAIL_EDITOR_BATCH_EXIT",
                        "The Unity Editor Batch probe stopped before the custom ROS2 peer completed; "
                        + "operating-system exit code " + str(editor_exit) + ".",
                    )
            remaining = worker_deadline - time.monotonic()
            if remaining <= 0.0:
                _terminate_owned_child(worker_process)
                raise PeerFailure("FAIL_WORKER_TIMEOUT", "The helper-owned custom ROS2 peer exceeded its bounded acceptance window.")
            time.sleep(min(0.1, remaining))
        worker_exit = worker_process.returncode
        worker_result = read_successful_worker_result(worker_result_path, lock)
        if worker_exit != 0:
            raise PeerFailure("FAIL_WORKER_EXIT", "The typed worker reported PASS but returned a nonzero operating-system exit code.")
        if player_process is not None:
            try:
                player_exit = player_process.wait(timeout=30.0)
            except subprocess.TimeoutExpired as exc:
                _terminate_owned_child(player_process)
                raise PeerFailure("FAIL_PLAYER_EXIT", "The Player emitted peer evidence but did not exit after its terminal marker.") from exc
            summary["playerExitCode"] = player_exit
            require_player_exit_code(player_exit)
        if editor_process is not None:
            try:
                editor_exit = editor_process.wait(timeout=30.0)
            except subprocess.TimeoutExpired as exc:
                _terminate_owned_child(editor_process)
                raise PeerFailure(
                    "FAIL_EDITOR_BATCH_EXIT",
                    "The Unity Editor Batch probe emitted peer evidence but did not exit after its terminal dwell.",
                ) from exc
            summary["unityBatchExitCode"] = editor_exit
            require_editor_batch_exit_code(editor_exit)
        summary["unityMarkerOffsets"] = {
            "start": unity_log_offset,
            "end": worker_result.get("markerOffsetEnd"),
        }
        summary["workerEvidence"] = worker_result
        summary["verdict"] = success_verdict
        exit_code = 0
    except PeerFailure as exc:
        failure = exc
    except KeyboardInterrupt:
        failure = PeerFailure("FAIL_INTERRUPTED", "The operator interrupted the helper-owned custom ROS2 peer.")
    except (OSError, subprocess.SubprocessError):
        failure = PeerFailure("FAIL_ENVIRONMENT", "A helper-owned custom ROS2 process could not be started or completed.")
    finally:
        if worker_process is not None and worker_process.poll() is None:
            _terminate_owned_child(worker_process)
        if player_process is not None and player_process.poll() is None:
            _terminate_owned_child(player_process)
        if editor_process is not None and editor_process.poll() is None:
            _terminate_owned_child(editor_process)
        editor_plugin_alias_stack.close()
        peer_workspace_alias_stack.close()
        if worker_stream is not None:
            worker_stream.close()
        if workspace is not None:
            if failure is not None:
                preserve_failure_log(workspace, output_directory, "colcon-build.log", "peer-build-failure.log")
                preserve_failure_log(workspace, output_directory, "typesupport-preflight.log", "typesupport-preflight-failure.log")
                preserve_failure_log(workspace, output_directory, "peer-worker.log", "peer-worker-failure.log")
                preserve_failure_log(workspace, output_directory, "worker-result.json", "peer-worker-result-failure.json")
            if not peer_build_sealed:
                try:
                    cleanup_owned_workspace(workspace, repository / "build" / "phase181")
                except PeerFailure as cleanup_error:
                    if failure is None:
                        failure = cleanup_error
                        exit_code = 1
        if failure is not None:
            summary["failureCode"] = failure.code
            summary["error"] = str(failure)
            summary["verdict"] = failure.code
        elif "verdict" not in summary:
            summary["verdict"] = "FAIL_UNKNOWN"
            exit_code = 1
        protocol.write_summary_atomic(summary_path, summary)
        print("Summary: " + str(summary_path))
        print("Verdict: " + str(summary["verdict"]))
    return exit_code


__all__ = [name for name in globals() if not name.startswith("__")]
