from __future__ import annotations
from .unity_selection_and_bridge_cache import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _copy_bridge_source(repository: pathlib.Path, overlay: pathlib.Path) -> None:
    """Handle the copy bridge source step."""

    source = _bridge_source_root(repository)
    destination = overlay / "src" / "unity2foxglove_ros2_bridge"
    if not (source / "package.xml").is_file() or destination.exists():
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The Bridge source or owned overlay destination is invalid.",
        )
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(
            source,
            destination,
            ignore=shutil.ignore_patterns(
                *_BRIDGE_SOURCE_IGNORES,
            ),
        )
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The Bridge source could not be staged into its owned overlay.",
        ) from exc


def _paths_are_distinct(first: pathlib.Path, second: pathlib.Path) -> bool:
    """Compare runtime path spellings without resolving a subst/junction target."""

    left = os.path.normcase(os.path.abspath(os.fspath(first)))
    right = os.path.normcase(os.path.abspath(os.fspath(second)))
    return left != right


def prepare_windows_bridge_build_environment(
    environment: Mapping[str, str],
    ros2_root: pathlib.Path,
) -> dict[str, str]:
    """Pin native Bridge dependencies to the selected ROS 2 Pixi prefix."""

    result = dict(environment)
    library = (
        pathlib.Path(ros2_root)
        / ".pixi"
        / "envs"
        / "default"
        / "Library"
    )
    nlohmann_directory = library / "share" / "cmake" / "nlohmann_json"
    tinyxml_directory = library / "lib" / "cmake" / "tinyxml2"
    required = {
        "OpenSSL headers": library / "include" / "openssl" / "opensslv.h",
        "OpenSSL crypto library": library / "lib" / "libcrypto.lib",
        "OpenSSL TLS library": library / "lib" / "libssl.lib",
        "tinyxml2 CMake package": tinyxml_directory / "tinyxml2-config.cmake",
        "nlohmann_json CMake package": (
            nlohmann_directory / "nlohmann_jsonConfig.cmake"
        ),
    }
    missing = [label for label, path in required.items() if not path.is_file()]
    if missing:
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The selected Windows ROS 2 prefix is missing required Bridge "
            "build dependencies: "
            + ", ".join(missing)
            + ".",
        )

    prefix_entries = [str(library)]
    prefix_entries.extend(
        entry
        for entry in str(result.get("CMAKE_PREFIX_PATH", "")).split(os.pathsep)
        if entry and _paths_are_distinct(pathlib.Path(entry), library)
    )
    result["CMAKE_PREFIX_PATH"] = os.pathsep.join(prefix_entries)
    result["OPENSSL_ROOT_DIR"] = str(library)
    result["nlohmann_json_DIR"] = str(nlohmann_directory)
    result["tinyxml2_DIR"] = str(tinyxml_directory)
    return result


def _prepare_ros_runtime(
    *,
    config: Mapping[str, object],
    editor: pathlib.Path,
    repository: pathlib.Path,
    output: pathlib.Path,
    stack: contextlib.ExitStack,
    job: WindowsKillOnCloseJob | None,
) -> PreparedRosRuntime:
    """Select, build, and compose one exact Phase181-backed ROS environment."""

    peer = _phase181_peer_module()
    distro = str(config["rosDistro"])
    rmw = str(config["rmw"])
    profile = str(config["profile"])
    static_package = repository / "Packages" / INTERFACE_PACKAGE_ID
    lock = peer.load_static_interface_lock(static_package)
    if (
        lock.interface_digest != config["interfaceDigest"]
        or lock.ros_package_name != config["interfacePackage"]
        or f"{lock.ros_package_name}/msg/{lock.envelope_message_name}"
        != config["interfaceType"]
    ):
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The Phase181 static interface lock drifted before runtime preparation.",
        )

    _select_unity_runtime(
        peer=peer,
        editor=editor,
        repository=repository,
        output=output,
        distro=distro,
        rmw=rmw,
        job=job,
    )
    selected_addon = peer.require_selected_typesupport_addon(
        repository,
        distro,
    )
    runtime_plugins, custom_plugins = (
        peer.resolve_editor_batch_native_plugin_directories(
            repository,
            distro,
            selected_addon,
        )
    )

    ros2_root = (
        repository / "ros2-windows" / f"ros2_{distro}"
    )
    toolchain = peer.resolve_windows_peer_toolchain(ros2_root)
    ros_environment = peer.ros2env.build_ros_env(
        toolchain.ros2_root,
        rmw,
        str(config["discoveryRange"]),
        str(config["domainId"]),
        distro,
    )
    msvc_environment = peer.capture_windows_msvc_environment(ros_environment)
    build_environment = peer.merge_windows_peer_build_environment(
        ros_environment,
        msvc_environment,
    )

    validator_command = peer.build_addon_validator_command(
        repository,
        distro,
        rmw,
    )
    _run_logged_preflight(
        validator_command,
        cwd=repository,
        environment=peer.ros2env.sanitized_subprocess_env(os.environ),
        log_path=output / "typesupport-preflight.log",
        job=job,
        failure_code="FAIL_PREFLIGHT",
        operation="preflight",
    )

    colcon_command = peer.build_windows_colcon_command(
        toolchain.colcon_executable,
        lock.ros_package_name,
        toolchain.python_executable,
    )
    cache_key = peer.peer_build_cache_key(
        lock,
        profile,
        distro,
        rmw,
        toolchain,
        colcon_command,
    )
    peer_workspace, reused = peer.prepare_peer_build_workspace(
        repository / "build" / "phase181",
        profile,
        cache_key,
        lock.ros_package_name,
    )
    _physical_peer, runtime_peer = stack.enter_context(
        peer.temporary_short_windows_peer_workspace(peer_workspace)
    )
    subst_roots: list[pathlib.Path] = []
    if _paths_are_distinct(runtime_peer, peer_workspace):
        subst_roots.append(runtime_peer)
    if not reused:
        peer.stage_locked_ros_source(
            static_package,
            runtime_peer,
            lock.ros_package_name,
        )
        _run_logged_preflight(
            colcon_command,
            cwd=runtime_peer,
            environment=build_environment,
            log_path=output / "phase181-peer-build.log",
            job=job,
            failure_code="FAIL_BUILD",
            operation="build",
        )
        peer.seal_peer_build_workspace(
            peer_workspace,
            cache_key,
            lock.ros_package_name,
        )

    bridge_install: pathlib.Path | None = None
    bridge_runtime_workspace: pathlib.Path | None = None
    case = str(config["case"])
    if case in {"multi-target", "qos-contract"}:
        bridge_underlay = build_ros_actor_environment(
            build_environment,
            bridge_install=None,
            peer_install=runtime_peer / "install",
            ros2_root=toolchain.ros2_root,
            distro=distro,
            rmw=rmw,
            domain_id=int(config["domainId"]),
            discovery_range=str(config["discoveryRange"]),
            topology_id="",
            zenoh_session_config=None,
        )
        bridge_build_environment = peer.merge_windows_peer_build_environment(
            bridge_underlay,
            msvc_environment,
        )
        bridge_build_environment = prepare_windows_bridge_build_environment(
            bridge_build_environment,
            toolchain.ros2_root,
        )
        bridge_build_command = [
            str(toolchain.colcon_executable),
            "build",
            "--merge-install",
            "--packages-select",
            "unity2foxglove_ros2_bridge",
            "--cmake-args",
            "-G",
            "Ninja",
            "-DCMAKE_BUILD_TYPE=Release",
            "-DBUILD_TESTING=ON",
            "-DPython3_EXECUTABLE="
            + pathlib.Path(toolchain.python_executable).as_posix(),
            "-DPYTHON_EXECUTABLE="
            + pathlib.Path(toolchain.python_executable).as_posix(),
        ]
        bridge_cache_key = bridge_build_cache_key(
            repository,
            profile,
            distro,
            rmw,
            toolchain,
            bridge_build_command,
            bridge_build_environment,
        )
        overlay, bridge_cache_reused = prepare_bridge_build_workspace(
            repository / "build" / "phase184" / "bridge-cache",
            profile,
            bridge_cache_key,
        )
        bridge_install = overlay / "install"
        if bridge_install.resolve(strict=False) != pathlib.Path(
            str(config["bridgeOverlayInstall"])
        ).resolve(strict=False):
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "The prepared Bridge cache does not match the immutable run configuration.",
            )
        _physical_bridge, runtime_bridge = stack.enter_context(
            peer.temporary_short_windows_peer_workspace(overlay)
        )
        bridge_runtime_workspace = runtime_bridge
        if _paths_are_distinct(runtime_bridge, overlay):
            subst_roots.append(runtime_bridge)
        if not bridge_cache_reused:
            _copy_bridge_source(repository, overlay)
            _run_logged_preflight(
                bridge_build_command,
                cwd=runtime_bridge,
                environment=bridge_build_environment,
                log_path=output / "bridge-build.log",
                job=job,
                failure_code="FAIL_BUILD",
                operation="build",
            )
            ctest = shutil.which(
                "ctest.exe",
                path=bridge_build_environment.get("PATH"),
            )
            if not ctest:
                raise AcceptanceFailure(
                    "FAIL_BUILD",
                    "The selected ROS/MSVC environment has no ctest executable.",
                )
            _run_logged_preflight(
                [ctest, "--output-on-failure", "-C", "Release"],
                cwd=runtime_bridge / "build" / _BRIDGE_PACKAGE_NAME,
                environment=bridge_build_environment,
                log_path=output / "bridge-tests.log",
                job=job,
                failure_code="FAIL_BUILD",
                operation="build",
            )
            seal_bridge_build_workspace(
                overlay,
                profile,
                bridge_cache_key,
            )
        manifest = _read_bridge_cache_json(
            overlay / _BRIDGE_CACHE_MANIFEST_NAME
        )
        if manifest is None or not _bridge_cache_matches(
            overlay,
            profile,
            bridge_cache_key,
        ):
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "The tested Bridge cache did not retain exact sealed evidence.",
            )
        protocol.write_json_atomic(
            output / "bridge-cache-evidence.json",
            {
                "schemaVersion": _BRIDGE_CACHE_FORMAT,
                "profile": profile,
                "cacheKey": bridge_cache_key,
                "sourceSha256": _bridge_source_digest(repository),
                "executableSha256": manifest["executableSha256"],
                "reused": bridge_cache_reused,
                "buildVerdict": "CACHE_VALIDATED" if bridge_cache_reused else "PASS",
                "testVerdict": "CACHE_VALIDATED" if bridge_cache_reused else "PASS",
            },
            repo_root=repository,
        )

    zenoh_router: pathlib.Path | None = None
    zenoh_router_environment: dict[str, str] | None = None
    zenoh_router_config: pathlib.Path | None = None
    zenoh_session_config: pathlib.Path | None = None
    zenoh_router_endpoint: UnityZenohRouterEndpoint | None = None
    if rmw == "rmw_zenoh_cpp":
        try:
            import phase179_zenoh_topology as zenoh
        except ImportError as exc:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "Zenoh topology helpers are unavailable.",
            ) from exc
        zenoh_router = (
            toolchain.ros2_root
            / "Lib"
            / "rmw_zenoh_cpp"
            / "rmw_zenohd.exe"
        )
        _require_file(
            zenoh_router,
            "FAIL_RUNTIME_SELECTION",
            "Repository-local Zenoh router",
        )
        templates = (
            toolchain.ros2_root
            / "share"
            / "rmw_zenoh_cpp"
            / "config"
        )
        zenoh_router_endpoint = load_unity_zenoh_router_endpoint(repository)
        if zenoh_router_endpoint.port in {
            int(config["foxglovePort"]),
            int(config["bridgePort"]),
        }:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "The Unity Zenoh router port collides with another owned endpoint.",
            )
        require_available_loopback_port(
            zenoh_router_endpoint.port,
            "Zenoh router",
        )
        owned = zenoh.create_owned_local_router_config(
            router_template=templates / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
            session_template=templates / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
            output_directory=output / "zenoh",
            endpoint=zenoh_router_endpoint.endpoint,
        )
        zenoh_router_config = owned.router_config
        zenoh_session_config = owned.session_config
        zenoh_router_environment = peer.ros2env.build_ros_env(
            toolchain.ros2_root,
            rmw,
            str(config["discoveryRange"]),
            str(config["domainId"]),
            distro,
        )
        zenoh_router_environment["ZENOH_ROUTER_CONFIG_URI"] = str(
            zenoh_router_config
        )
        zenoh_router_environment["ZENOH_SESSION_CONFIG_URI"] = str(
            zenoh_session_config
        )

    actor_environment = build_ros_actor_environment(
        build_environment,
        bridge_install=bridge_install,
        peer_install=runtime_peer / "install",
        ros2_root=toolchain.ros2_root,
        distro=distro,
        rmw=rmw,
        domain_id=int(config["domainId"]),
        discovery_range=str(config["discoveryRange"]),
        topology_id=str(config["zenohTopologyId"]),
        zenoh_session_config=zenoh_session_config,
    )
    unity_base = peer.build_player_environment(
        build_environment,
        distro=distro,
        rmw=rmw,
        domain_id=int(config["domainId"]),
        interface_revision=lock.interface_revision,
        interface_digest=lock.interface_digest,
        topology_id=str(config["zenohTopologyId"]) or None,
        zenoh_session_config=zenoh_session_config,
        discovery_range=str(config["discoveryRange"]),
    )
    custom_plugin_alias = stack.enter_context(
        peer.temporary_short_windows_plugin_alias(custom_plugins)
    )
    if _paths_are_distinct(custom_plugin_alias, custom_plugins):
        subst_roots.append(custom_plugin_alias)
    unity_environment = peer.build_editor_batch_environment(
        unity_base,
        runtime_plugins,
        custom_plugin_alias,
    )
    return PreparedRosRuntime(
        peer=peer,
        toolchain=toolchain,
        lock=lock,
        ros2_root=toolchain.ros2_root,
        peer_workspace=peer_workspace,
        peer_runtime_workspace=runtime_peer,
        build_environment=build_environment,
        actor_environment=actor_environment,
        unity_environment=unity_environment,
        bridge_install=bridge_install,
        bridge_runtime_workspace=bridge_runtime_workspace,
        zenoh_router=zenoh_router,
        zenoh_router_environment=zenoh_router_environment,
        zenoh_router_config=zenoh_router_config,
        zenoh_session_config=zenoh_session_config,
        zenoh_router_endpoint=zenoh_router_endpoint,
        subst_roots=tuple(subst_roots),
    )




__all__ = [name for name in globals() if not name.startswith("__")]
