from __future__ import annotations
from .process_cleanup import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_capability_probe.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _generate_serialized_payload(
    python_executable: pathlib.Path,
    environment: Mapping[str, str],
    origin_id: str,
) -> str:
    """Generate the canonical serialized Phase181 envelope payload."""

    code = (
        "from rclpy.serialization import serialize_message;"
        "from unity2foxglove_foxrun_interfaces_v1.msg import "
        "Phase181State48D288ED82F1Envelope as E;"
        "m=E();"
        "m.foxrun_origin_id=" + repr(origin_id) + ";"
        "m.foxrun_sequence=1;"
        "m.payload.message='phase186';"
        "m.payload.foxrun_has_message=True;"
        "print(bytes(serialize_message(m)).hex())"
    )
    try:
        result = subprocess.run(
            [str(python_executable), "-c", code],
            env=dict(environment),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            errors="replace",
            timeout=30,
            check=False,
            shell=False,
        )
    except OSError as exc:
        raise ProbeFailure("row-local ROS Python could not start") from exc
    payload = result.stdout.strip().splitlines()[-1] if result.stdout.strip() else ""
    if result.returncode != 0 or not payload or any(
        character not in "0123456789abcdefABCDEF" for character in payload
    ):
        raise ProbeFailure("row-local custom envelope serialization failed")
    return payload


def _runtime_environment(
    repository: pathlib.Path,
    row: build.BridgeRow,
    overlay: Mapping[str, object],
    domain_id: int,
) -> tuple[dict[str, str], pathlib.Path, object]:
    """Construct the isolated runtime environment for one capability row."""

    peer = build._load_phase181_peer(repository)
    ros2_root = pathlib.Path(
        os.environ.get(
            "PHASE186_ROS2_" + row.distro.upper() + "_ROOT",
            str(repository / "ros2-windows" / ("ros2_" + row.distro)),
        )
    )
    toolchain = peer.resolve_windows_peer_toolchain(ros2_root)
    base = peer.ros2env.build_ros_env(
        toolchain.ros2_root,
        row.rmw,
        "LOCALHOST",
        str(domain_id),
        row.distro,
    )
    install = pathlib.Path(str(overlay["installPrefix"]))
    environment = peer.build_peer_environment(
        base,
        toolchain.ros2_root,
        install,
        distro=row.distro,
        rmw=row.rmw,
        domain_id=domain_id,
        topology_id=None,
        zenoh_session_config=None,
    )
    environment["ROS_DOMAIN_ID"] = str(domain_id)
    environment["RMW_IMPLEMENTATION"] = row.rmw
    environment["ROS_AUTOMATIC_DISCOVERY_RANGE"] = "LOCALHOST"
    environment.pop("ROS_LOCALHOST_ONLY", None)
    environment.pop("ROS_DISCOVERY_SERVER", None)
    return environment, toolchain.python_executable, peer


def run_row(
    repository: pathlib.Path,
    row: build.BridgeRow,
    output_root: pathlib.Path,
    *,
    timeout_seconds: float,
) -> dict[str, object]:
    """Run one live row using only artifacts certified by the row build."""

    repository = pathlib.Path(repository).resolve()
    output_root = pathlib.Path(output_root).resolve()
    row_root = output_root / row.row_id
    result_path = row_root / "capability-result.json"
    started = build.timestamp()
    subscriber: subprocess.Popen[str] | None = None
    publisher: subprocess.Popen[str] | None = None
    subscriber_log_stream = None
    publisher_log_stream = None
    topology_handle = None
    zenoh_evidence: dict[str, object] | None = None
    subscriber_pid = 0
    publisher_pid = 0
    result: dict[str, object] | None = None
    try:
        if os.name != "nt" or platform.system() != "Windows":
            raise build.LivePrerequisiteMissing("Windows-native execution")
        build_summary_path = row_root / "build-summary.json"
        build_summary = _read_json(build_summary_path)
        build.validate_build_summary(build_summary, row)
        overlay = _read_json(row_root / "overlay-authority.json")
        build.validate_overlay_authority(overlay, row, row_root)
        executable = pathlib.Path(
            str(
                (
                    build_summary.get("probeExecutable")
                    if isinstance(build_summary.get("probeExecutable"), Mapping)
                    else {}
                ).get("path", "")
            )
        )
        expected_executable_hash = (
            build_summary.get("probeExecutable")
            if isinstance(build_summary.get("probeExecutable"), Mapping)
            else {}
        ).get("sha256")
        if (
            not executable.is_file()
            or build.sha256_file(executable) != expected_executable_hash
        ):
            raise ProbeFailure("certified origin probe executable is stale")

        domain_id = DOMAIN_IDS[row.row_id]
        environment, python_executable, peer = _runtime_environment(
            repository,
            row,
            overlay,
            domain_id,
        )
        if environment.get("ROS_DOMAIN_ID") != str(domain_id):
            raise ProbeFailure("owned ROS domain was not installed")

        if row.rmw == "rmw_zenoh_cpp":
            ros_scripts = repository / "Scripts" / "smoke" / "ros2"
            if str(ros_scripts) not in sys.path:
                sys.path.insert(0, str(ros_scripts))
            import phase179_zenoh_topology as zenoh

            ros2_root = pathlib.Path(
                os.environ.get(
                    "PHASE186_ROS2_LYRICAL_ROOT",
                    str(repository / "ros2-windows" / "ros2_lyrical"),
                )
            )
            router = ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"
            templates = ros2_root / "share" / "rmw_zenoh_cpp" / "config"
            topology_id = "phase186-" + row.row_id + "-" + uuid.uuid4().hex[:12]
            owned_config = zenoh.create_owned_local_router_config(
                router_template=templates / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                session_template=templates / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
                output_directory=row_root / "zenoh",
            )
            options = zenoh.validate_topology_options(
                row.rmw,
                router=router,
                no_router=False,
                topology_id=topology_id,
            )
            router_environment = dict(environment)
            topology_handle = zenoh.start_topology(
                options,
                env=router_environment,
                cwd=row_root,
                log_path=row_root / "zenoh-router.log",
                ready_timeout_seconds=min(timeout_seconds, 30.0),
                owned_config=owned_config,
            )
            environment["ZENOH_SESSION_CONFIG_URI"] = str(
                owned_config.session_config
            )
            zenoh_evidence = {
                "owned": True,
                "topologyId": topology_id,
                "routerPid": topology_handle.process.pid,
                "sessionConfig": str(owned_config.session_config),
                "routerConfig": str(owned_config.router_config),
                "endpoint": owned_config.endpoint,
            }

        payload_hex = _generate_serialized_payload(
            python_executable,
            environment,
            "phase186-" + row.row_id,
        )
        topic = "/phase186/origin/" + row.row_id.replace("-", "_")
        common = [
            str(executable),
            "--topic",
            topic,
            "--type",
            build.INTERFACE_TYPE,
            "--payload-hex",
            payload_hex,
            "--timeout-ms",
            str(int(timeout_seconds * 1000)),
        ]
        subscriber_log = row_root / "origin-subscriber.log"
        publisher_log = row_root / "origin-publisher.log"
        subscriber_log_stream = subscriber_log.open(
            "w", encoding="utf-8", newline="\n"
        )
        subscriber = subprocess.Popen(
            [common[0], "--role", "subscriber", *common[1:]],
            cwd=str(row_root),
            env=environment,
            stdout=subscriber_log_stream,
            stderr=subprocess.STDOUT,
            text=True,
            shell=False,
            **_process_options(),
        )
        subscriber_pid = subscriber.pid
        _wait_for_marker(
            subscriber_log,
            subscriber,
            "PHASE186_ORIGIN_PROBE_READY",
            min(timeout_seconds, 30.0),
        )
        publisher_log_stream = publisher_log.open(
            "w", encoding="utf-8", newline="\n"
        )
        publisher = subprocess.Popen(
            [common[0], "--role", "publisher", *common[1:]],
            cwd=str(row_root),
            env=environment,
            stdout=publisher_log_stream,
            stderr=subprocess.STDOUT,
            text=True,
            shell=False,
            **_process_options(),
        )
        publisher_pid = publisher.pid
        publisher_exit = publisher.wait(timeout=timeout_seconds)
        subscriber_exit = subscriber.wait(timeout=timeout_seconds)
        publisher_log_stream.close()
        publisher_log_stream = None
        subscriber_log_stream.close()
        subscriber_log_stream = None
        if publisher_exit != 0 or subscriber_exit != 0:
            raise ProbeFailure("owned origin probe process returned nonzero")
        subscriber_result = _load_last_json_line(subscriber_log)
        publisher_result = _load_last_json_line(publisher_log)
        observed_rmw = subscriber_result.get("observedRmw")
        if (
            publisher_result.get("observedRmw") != observed_rmw
            or observed_rmw != row.rmw
        ):
            raise ProbeFailure("requested RMW differs from process observations")
        result = {
            "schemaVersion": SUMMARY_SCHEMA_VERSION,
            "rowId": row.row_id,
            "distro": row.distro,
            "requestedRmw": row.rmw,
            "observedRmw": observed_rmw,
            "verdict": "PASS",
            "platform": platform.system(),
            "domainId": domain_id,
            "domainOwned": True,
            "ambientDomainRejected": True,
            "canonicalType": build.INTERFACE_TYPE,
            "interfaceDigest": build.INTERFACE_DIGEST,
            "overlayAuthority": {
                "validated": True,
                "rowId": row.row_id,
                "installPrefix": overlay["installPrefix"],
                "localSetupSha256": overlay["localSetupSha256"],
            },
            "mechanism": subscriber_result.get("mechanism"),
            "rosObservations": {
                key: subscriber_result.get(key)
                for key in (
                    "localSeen",
                    "localGidMatched",
                    "ignoreLocalSawLocal",
                    "externalSeen",
                    "externalGidMatched",
                    "ignoreLocalSawExternal",
                )
            },
            "ownedProcesses": {
                "subscriberPid": subscriber_pid,
                "publisherPid": publisher_pid,
                "subscriberExitCode": subscriber_exit,
                "publisherExitCode": publisher_exit,
                "cleanupComplete": (
                    subscriber.poll() is not None and publisher.poll() is not None
                ),
            },
            "evidence": {
                "subscriberLog": str(subscriber_log),
                "publisherLog": str(publisher_log),
                "buildSummary": str(build_summary_path),
                "overlayAuthority": str(row_root / "overlay-authority.json"),
            },
            "startedAt": started,
            "finishedAt": build.timestamp(),
        }
        if zenoh_evidence is not None:
            result["zenohTopology"] = zenoh_evidence
        validate_row_result(result, row)
        _write_json_atomic(result_path, result)
        return result
    except build.LivePrerequisiteMissing as exc:
        result = {
            **build.not_run_summary(row, str(exc)),
            "startedAt": started,
            "finishedAt": build.timestamp(),
        }
        _write_json_atomic(result_path, result)
        return result
    except Exception as exc:
        result = {
            "schemaVersion": SUMMARY_SCHEMA_VERSION,
            "rowId": row.row_id,
            "distro": row.distro,
            "requestedRmw": row.rmw,
            "verdict": "FAIL",
            "platform": platform.system(),
            "canonicalType": build.INTERFACE_TYPE,
            "interfaceDigest": build.INTERFACE_DIGEST,
            "failure": str(exc),
            "startedAt": started,
            "finishedAt": build.timestamp(),
        }
        _write_json_atomic(result_path, result)
        return result
    finally:
        cleanup_failures = tuple(
            diagnostic
            for diagnostic in (
                _terminate_owned(publisher),
                _terminate_owned(subscriber),
            )
            if diagnostic is not None
        )
        if result is not None and cleanup_failures:
            _record_cleanup_failures(result, cleanup_failures)
            result["finishedAt"] = build.timestamp()
            _write_json_atomic(result_path, result)
        if publisher_log_stream is not None:
            publisher_log_stream.close()
        if subscriber_log_stream is not None:
            subscriber_log_stream.close()
        if topology_handle is not None:
            try:
                import phase179_zenoh_topology as zenoh

                zenoh.close_topology(topology_handle)
            except Exception:
                pass


__all__ = [name for name in globals() if not name.startswith("__")]
