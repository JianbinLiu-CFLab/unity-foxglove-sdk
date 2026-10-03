from __future__ import annotations
from .process_execution import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_build.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def run_row(
    repository: pathlib.Path,
    row: BridgeRow,
    output_root: pathlib.Path,
    *,
    run_tests: bool,
) -> dict[str, object]:
    """Build one exact row and persist its terminal result."""

    repository = pathlib.Path(repository).resolve()
    output_root = pathlib.Path(output_root).resolve()
    row_root = output_root / row.row_id
    row_root.mkdir(parents=True, exist_ok=True)
    summary_path = row_root / "build-summary.json"
    started = timestamp()
    base: dict[str, object] = {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "rowId": row.row_id,
        "distro": row.distro,
        "requestedRmw": row.rmw,
        "selectedRmw": row.rmw,
        "platform": platform.system(),
        "canonicalType": INTERFACE_TYPE,
        "interfaceDigest": INTERFACE_DIGEST,
        "standardCanonicalType": STANDARD_SCHEMA_TYPE,
        "standardSchemaDigest": STANDARD_SCHEMA_DIGEST,
        "startedAt": started,
    }
    try:
        if os.name != "nt" or platform.system() != "Windows":
            raise LivePrerequisiteMissing("Windows-native execution")
        authority = load_interface_authority(repository)
        standard_authority = load_standard_schema_authority(repository)
        peer = _load_phase181_peer(repository)
        ros2_root = pathlib.Path(
            os.environ.get(
                "PHASE186_ROS2_" + row.distro.upper() + "_ROOT",
                str(repository / "ros2-windows" / ("ros2_" + row.distro)),
            )
        )
        try:
            toolchain = peer.resolve_windows_peer_toolchain(ros2_root)
        except Exception as exc:
            raise LivePrerequisiteMissing(
                row.distro + " Windows ROS2 root"
            ) from exc
        ros_environment = peer.ros2env.build_ros_env(
            toolchain.ros2_root,
            row.rmw,
            "LOCALHOST",
            "0",
            row.distro,
        )
        try:
            msvc_environment = peer.capture_windows_msvc_environment(
                ros_environment
            )
            build_environment = peer.merge_windows_peer_build_environment(
                ros_environment,
                msvc_environment,
            )
        except Exception as exc:
            raise LivePrerequisiteMissing(
                "Visual Studio C++ x64 toolchain"
            ) from exc

        colcon_command = build_overlay_colcon_command(
            toolchain.colcon_executable,
            toolchain.python_executable,
        )
        cache_key = overlay_build_cache_key(
            peer.peer_build_cache_key(
                authority["_lock"],
                row.row_id,
                row.distro,
                row.rmw,
                toolchain,
                colcon_command,
            ),
            str(standard_authority["sourceDigest"]),
        )
        workspace, reused = peer.prepare_peer_build_workspace(
            output_root,
            row.row_id,
            cache_key,
            ROS_PACKAGE_NAME,
        )
        command_results: dict[str, object] = {}
        with contextlib.ExitStack() as stack:
            physical_workspace, runtime_workspace = stack.enter_context(
                peer.temporary_short_windows_peer_workspace(workspace)
            )
            if reused:
                command_results["colcon"] = {
                    "exitCode": 0,
                    "startedAt": started,
                    "finishedAt": timestamp(),
                    "durationSeconds": 0.0,
                    "log": str(workspace / "colcon-build.log"),
                    "reused": True,
                }
            else:
                peer.stage_locked_ros_source(
                    pathlib.Path(str(authority["staticPackage"])),
                    runtime_workspace,
                    ROS_PACKAGE_NAME,
                )
                stage_standard_schema_package(
                    repository,
                    runtime_workspace,
                )
                command_results["colcon"] = run_logged(
                    colcon_command,
                    cwd=runtime_workspace,
                    env=build_environment,
                    log_path=physical_workspace / "colcon-build.log",
                    timeout_seconds=1800,
                )
                validate_installed_standard_schema(
                    physical_workspace / "install",
                    str(standard_authority["sourceDigest"]),
                )
                peer.seal_peer_build_workspace(
                    physical_workspace,
                    cache_key,
                    ROS_PACKAGE_NAME,
                )

        install_prefix = workspace / "install"
        validate_installed_standard_schema(
            install_prefix,
            str(standard_authority["sourceDigest"]),
        )
        overlay = expected_overlay_authority(
            row,
            row_root,
            install_prefix,
            source_digest=str(authority["sourceDigest"]),
            standard_source_digest=str(
                standard_authority["sourceDigest"]
            ),
        )
        validate_overlay_authority(overlay, row, row_root)
        _write_json_atomic(row_root / "overlay-authority.json", overlay)

        source_root = (
            repository
            / "Tools"
            / "ros2_bridge"
            / "unity2foxglove_ros2_bridge"
        )
        with peer.temporary_short_windows_peer_workspace(row_root) as (
            physical_cpp_root,
            runtime_cpp_root,
        ):
            cpp_build, runtime_cpp_build, runtime_install_prefix = (
                cpp_runtime_paths(physical_cpp_root, runtime_cpp_root)
            )
            reset_cmake_build_for_runtime_alias(cpp_build, runtime_cpp_build)
            cpp_temp = runtime_cpp_root / "tmp"
            cpp_environment = _build_cpp_environment(
                build_environment,
                toolchain.ros2_root,
                runtime_install_prefix,
                cpp_temp,
            )
            cmake = _find_tool("cmake.exe", cpp_environment)
            ctest = _find_tool("ctest.exe", cpp_environment)
            ninja = _find_tool("ninja.exe", cpp_environment)
            library = toolchain.ros2_root / ".pixi" / "envs" / "default" / "Library"
            nlohmann_directories = (
                library / "share" / "cmake" / "nlohmann_json",
                library / "lib" / "cmake" / "nlohmann_json",
                pathlib.Path(sys.prefix)
                / "Library"
                / "share"
                / "cmake"
                / "nlohmann_json",
            )
            nlohmann_directory = next(
                (
                    candidate
                    for candidate in nlohmann_directories
                    if (candidate / "nlohmann_jsonConfig.cmake").is_file()
                ),
                None,
            )
            if nlohmann_directory is None:
                raise LivePrerequisiteMissing("nlohmann_json CMake package")
            configure_command = [
                str(cmake),
                "-S",
                str(source_root),
                "-B",
                str(runtime_cpp_build),
                "-G",
                "Ninja",
                "-DBUILD_TESTING=ON",
                "-DCMAKE_BUILD_TYPE=Release",
                "-DCMAKE_MAKE_PROGRAM=" + str(ninja).replace("\\", "/"),
                "-DPython3_EXECUTABLE="
                + str(toolchain.python_executable).replace("\\", "/"),
                "-DPYTHON_EXECUTABLE="
                + str(toolchain.python_executable).replace("\\", "/"),
                "-DOPENSSL_ROOT_DIR=" + str(library).replace("\\", "/"),
                "-Dnlohmann_json_DIR="
                + str(nlohmann_directory).replace("\\", "/"),
                "-Dtinyxml2_DIR="
                + str(library / "lib" / "cmake" / "tinyxml2").replace("\\", "/"),
            ]
            command_results["cmakeConfigure"] = run_logged(
                configure_command,
                cwd=runtime_cpp_root,
                env=cpp_environment,
                log_path=row_root / "cmake-configure.log",
                timeout_seconds=300,
            )
            command_results["cmakeBuild"] = run_logged(
                [str(cmake), "--build", str(runtime_cpp_build)],
                cwd=runtime_cpp_root,
                env=cpp_environment,
                log_path=row_root / "cmake-build.log",
                timeout_seconds=900,
            )

            if not run_tests:
                result = {
                    **base,
                    "verdict": "BUILD ONLY",
                    "finishedAt": timestamp(),
                    "overlayAuthority": overlay,
                    "commands": command_results,
                    "bridgeSourceDigest": hash_source_tree(source_root),
                }
                _write_json_atomic(summary_path, result)
                return result

            command_results["ctest"] = run_logged(
                [
                    str(ctest),
                    "--test-dir",
                    str(runtime_cpp_build),
                    "--output-on-failure",
                    "-C",
                    "Release",
                ],
                cwd=runtime_cpp_root,
                env=cpp_environment,
                log_path=row_root / "ctest.log",
                timeout_seconds=300,
            )
            test_count, passed = _ctest_counts(row_root / "ctest.log")
            probe_executable = cpp_build / "phase186_origin_suppression_probe.exe"
            if not probe_executable.is_file():
                raise BridgeBuildFailure(
                    "origin-suppression probe executable was not built"
                )
            generated_duplex_probe = cpp_build / "test_generated_duplex.exe"
            if not generated_duplex_probe.is_file():
                raise BridgeBuildFailure(
                    "generated standard and Phase181 duplex probe was not built"
                )
            result = {
                **base,
                "verdict": "PASS",
                "finishedAt": timestamp(),
                "ros2Root": str(toolchain.ros2_root.resolve()),
                "overlayAuthority": overlay,
                "overlayReused": reused,
                "commands": command_results,
                "ctest": {"tests": test_count, "passed": passed},
                "compiler": _compiler_identity(
                    cpp_build / "CMakeCache.txt",
                    cpp_environment,
                ),
                "bridgeSourceDigest": hash_source_tree(source_root),
                "probeExecutable": {
                    "path": str(probe_executable.resolve()),
                    "sha256": sha256_file(probe_executable),
                },
                "generatedDuplexProbe": {
                    "path": str(generated_duplex_probe.resolve()),
                    "sha256": sha256_file(generated_duplex_probe),
                },
            }
            validate_build_summary(result, row)
            _write_json_atomic(summary_path, result)
            return result
    except LivePrerequisiteMissing as exc:
        result = {
            **not_run_summary(row, str(exc)),
            "startedAt": started,
            "finishedAt": timestamp(),
        }
        _write_json_atomic(summary_path, result)
        return result
    except Exception as exc:
        result = {
            **base,
            "verdict": "FAIL",
            "finishedAt": timestamp(),
            "failure": str(exc),
        }
        _write_json_atomic(summary_path, result)
        return result


__all__ = [name for name in globals() if not name.startswith("__")]
