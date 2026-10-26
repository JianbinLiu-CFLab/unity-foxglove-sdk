from __future__ import annotations

from .foundation import *
from .process import *
from .jobs import *
from .restore import *

def main() -> int:
    """Parse args, run selected CI suites, and return exit code."""

    parser = argparse.ArgumentParser(description="Run local CI checks.")
    parser.add_argument(
        "--skip-analyzer",
        action="store_true",
        help="Skip Roslyn analyzer DLL build and freshness check.",
    )
    parser.add_argument(
        "--only",
        choices=CI_ONLY_CHOICES,
        help=(
            "Run only one suite: dotnet, dotnet-runtime, mediafoundation-native-smoke, xunit, xunit-adapter, xunit-native, "
            "analyzer, foxrun-publish-panel, phase179-ros2-regression, "
            "phase181-ros2-regression, phase188-replay-regression, phase184-acceptance-tooling, "
            "ros2bridge-xunit, xunit-manager-boundary, performance-regression, phase186-bridge-tooling, phase186-bridge-windows-live, "
            "mcap-conformance, packages, boundary"
        ),
    )
    parser.add_argument(
        "--jobs",
        type=int,
        default=default_parallel_jobs(),
        help="Top-level parallel job count for default CI runs.",
    )
    args = parser.parse_args()
    validate_control_plane(args)

    if args.only == "analyzer" and args.skip_analyzer:
        parser.error("--skip-analyzer cannot be combined with --only analyzer")

    results: dict[str, bool] = {}
    skipped_lanes: tuple[str, ...] = (
        ("analyzer",) if args.only is None and args.skip_analyzer else ()
    )

    if args.only is None:
        results.update(run_ci_jobs(build_default_ci_jobs(args), args.jobs))
        return report_ci_job_results(results, skipped_lanes)

    if args.only == "dotnet":
        results.update(run_ci_jobs(build_dotnet_ci_jobs(), args.jobs))
        return report_ci_job_results(results)

    # --- analyzer build + freshness ---
    if args.only in (None, "analyzer"):
        if args.skip_analyzer:
            print(f"{SKIP} Analyzer build (--skip-analyzer)")
        else:
            results["analyzer-restore"] = restore_with_ignoring_failed_sources(
                SOURCE_GENERATOR_PROJ, "Restore Roslyn analyzer project", ANALYZER_PROPS
            )
            results["analyzer-build"] = run_with_restore_fallback(
                [
                    "dotnet", "build", SOURCE_GENERATOR_PROJ,
                    *ANALYZER_PROPS,
                    "-c", "Release",
                    "-o", str(ANALYZER_OUTPUT_DIR),
                    "--no-restore",
                ],
                [
                    "dotnet", "build", SOURCE_GENERATOR_PROJ,
                    *ANALYZER_PROPS,
                    "-c", "Release",
                    "-o", str(ANALYZER_OUTPUT_DIR),
                ],
                "Build Roslyn analyzer DLL",
            )
            results["analyzer-dll"] = run(
                [
                    sys.executable,
                    SOURCE_GENERATOR_VALIDATOR,
                    "--build-output-dir",
                    str(ANALYZER_OUTPUT_DIR),
                    *validator_msbuild_args(ANALYZER_PROPS),
                ],
                "Source generator DLL freshness"
            )
            if results.get("analyzer-build"):
                results["analyzer-runtime-restore"] = restore_with_ignoring_failed_sources(
                    RUNTIME_TESTS_PROJ,
                    "Restore runtime test project for analyzer freshness",
                    ANALYZER_RUNTIME_TEST_PROPS,
                )
                results["analyzer-freshness"] = run_with_restore_fallback(
                    [
                        "dotnet", "run", "--no-restore",
                        "--project", RUNTIME_TESTS_PROJ,
                        *ANALYZER_RUNTIME_TEST_PROPS,
                        "--", "--phase115f",
                    ],
                    [
                        "dotnet", "run",
                        "--project", RUNTIME_TESTS_PROJ,
                        *ANALYZER_RUNTIME_TEST_PROPS,
                        "--", "--phase115f",
                    ],
                    "Analyzer DLL freshness (--phase115f)",
                )

    # --- independent dotnet validation lanes ---
    if args.only == "mediafoundation-native-smoke":
        results["mediafoundation-native-smoke-restore"] = restore_with_ignoring_failed_sources(
            RUNTIME_TESTS_PROJ,
            "Restore runtime test project for Media Foundation native smoke",
            RUNTIME_TEST_PROPS,
            fatal=False,
        )
        if results["mediafoundation-native-smoke-restore"]:
            smoke_ok, smoke_skipped = run_mediafoundation_native_smoke(
                [
                    "dotnet", "run", "--no-restore",
                    "--project", RUNTIME_TESTS_PROJ,
                    *RUNTIME_TEST_PROPS,
                    "--", "--phase82-native-smoke",
                ],
                [
                    "dotnet", "run",
                    "--project", RUNTIME_TESTS_PROJ,
                    *RUNTIME_TEST_PROPS,
                    "--", "--phase82-native-smoke",
                ],
                "Media Foundation native smoke (--phase82-native-smoke)",
            )
            if smoke_skipped:
                skipped_lanes = (*skipped_lanes, "mediafoundation-native-smoke")
            else:
                results["mediafoundation-native-smoke"] = smoke_ok
        else:
            results["mediafoundation-native-smoke"] = False

    if args.only == "dotnet-runtime":
        results["dotnet-runtime-restore"] = restore_with_ignoring_failed_sources(
            RUNTIME_TESTS_PROJ,
            "Restore runtime test project",
            RUNTIME_TEST_PROPS,
            fatal=False,
        )
        results["dotnet-runtime"] = (
            run(
                [
                    "dotnet",
                    "run",
                    "--no-restore",
                    "--project",
                    RUNTIME_TESTS_PROJ,
                    *RUNTIME_TEST_PROPS,
                ],
                "Dotnet validation suite (default CI)",
            )
            if results["dotnet-runtime-restore"]
            else False
        )

    if args.only == "xunit":
        results["xunit-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ,
            "Restore xUnit unit test project",
            UNIT_TEST_PROPS,
            fatal=False,
        )
        results["xunit"] = (
            run(
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    UNIT_TESTS_PROJ,
                    *UNIT_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests.trx",
                    "--results-directory",
                    str(UNIT_TEST_RESULTS_DIR),
                ],
                "xUnit unit tests",
            )
            if results["xunit-restore"]
            else False
        )

    if args.only == "xunit-adapter":
        results["xunit-adapter-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ,
            "Restore xUnit optional ROS2 adapter lane",
            UNIT_ADAPTER_TEST_PROPS,
            fatal=False,
        )
        results["xunit-adapter"] = (
            run(
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    UNIT_TESTS_PROJ,
                    *UNIT_ADAPTER_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-adapter.trx",
                    "--results-directory",
                    str(UNIT_ADAPTER_TEST_RESULTS_DIR),
                ],
                "xUnit optional ROS2 adapter unit tests",
            )
            if results["xunit-adapter-restore"]
            else False
        )

    if args.only == "ros2bridge-xunit":
        results["ros2bridge-xunit-restore"] = restore_with_ignoring_failed_sources(
            ROS2_BRIDGE_UNIT_TESTS_PROJ,
            "Restore ROS2 Bridge behavioral xUnit lane",
            ROS2_BRIDGE_TEST_PROPS,
            fatal=False,
        )
        results["ros2bridge-xunit"] = (
            run(
                ["dotnet", "test", "--no-restore", ROS2_BRIDGE_UNIT_TESTS_PROJ,
                 *ROS2_BRIDGE_TEST_PROPS,
                 "--logger", "trx;LogFileName=unit-tests-ros2bridge.trx",
                 "--results-directory", str(CI_ROOT / "TestResults" / "Ros2Bridge")],
                "ROS2 Bridge behavioral xUnit tests",
            )
            if results["ros2bridge-xunit-restore"] else False
        )

    if args.only == "performance-regression":
        results["performance-regression-restore"] = restore_with_ignoring_failed_sources(
            PERFORMANCE_REGRESSION_TESTS_PROJ,
            "Restore deterministic performance regression xUnit lane",
            (),
            fatal=False,
        )
        results["performance-regression-xunit"] = (
            run(
                ["dotnet", "test", "--no-restore", PERFORMANCE_REGRESSION_TESTS_PROJ,
                 "--logger", "trx;LogFileName=performance-regression.trx",
                 "--results-directory", str(CI_ROOT / "TestResults" / "PerformanceRegression")],
                "Deterministic performance regression xUnit tests",
            )
            if results["performance-regression-restore"] else False
        )
        results["performance-regression-python"] = run(
            [sys.executable, "-m", "unittest",
             "Scripts.performance.regression_checks.test_performance_tooling",
             "Scripts.performance.regression_checks.test_phase188_replay"],
            "Performance harness regression tests",
        )

    if args.only == "phase188-replay-regression":
        results["phase188-replay-regression"] = run(
            [sys.executable, "-m", "unittest", PHASE188_REPLAY_TOOLING_REGRESSION],
            "Phase188 Unity compile/import and Win64 acceptance harness regressions",
        )

    if args.only == "xunit-native":
        results["xunit-native-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ,
            "Restore xUnit Native ROS2 compilation lane",
            UNIT_NATIVE_TEST_PROPS,
            fatal=False,
        )
        results["xunit-native"] = (
            run(
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    UNIT_TESTS_PROJ,
                    *UNIT_NATIVE_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-native.trx",
                    "--results-directory",
                    str(UNIT_NATIVE_TEST_RESULTS_DIR),
                ],
                "xUnit Native ROS2 compilation unit tests",
            )
            if results["xunit-native-restore"]
            else False
        )

    if args.only == "xunit-manager-boundary":
        results["xunit-manager-boundary-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ,
            "Restore Manager production-boundary xUnit lane",
            UNIT_MANAGER_BOUNDARY_TEST_PROPS,
            fatal=False,
        )
        results["xunit-manager-boundary"] = (
            run(
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    UNIT_TESTS_PROJ,
                    *UNIT_MANAGER_BOUNDARY_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-manager-boundary.trx",
                    "--results-directory",
                    str(UNIT_MANAGER_BOUNDARY_TEST_RESULTS_DIR),
                ],
                "xUnit Manager production-boundary tests",
            )
            if results["xunit-manager-boundary-restore"]
            else False
        )

    # --- FoxRun Publish panel behavior suite ---
    if args.only in (None, "foxrun-publish-panel"):
        results["foxrun-publish-panel-install"] = run(
            foxrun_publish_panel_npm("ci"),
            "FoxRun Publish panel lockfile install",
            fatal=True,
        )
        results["foxrun-publish-panel-typecheck"] = run(
            foxrun_publish_panel_npm("run", "typecheck"),
            "FoxRun Publish panel typecheck",
            fatal=True,
        )
        results["foxrun-publish-panel-test"] = run(
            foxrun_publish_panel_npm("test"),
            "FoxRun Publish panel Vitest behavior tests",
            fatal=True,
        )

    # --- pure ROS2 acceptance-helper regression tests ---
    if args.only in (None, "phase179-ros2-regression"):
        results["phase179-ros2-inbound-acceptance"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ROS2_INBOUND_ACCEPTANCE_REGRESSION],
            "Phase179 Linux ROS2 inbound acceptance helper regressions",
        )
        results["phase179-ros2-player-host"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ROS2_PLAYER_HOST_REGRESSION],
            "Phase179 Windows Player host helper regressions",
        )
        results["phase179-ros2-matrix-profiles"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ROS2_MATRIX_PROFILES_REGRESSION],
            "Phase179 named four-row interop profile regressions",
        )
        results["phase179-zenoh-topology"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ZENOH_TOPOLOGY_REGRESSION],
            "Phase179 Zenoh topology ownership and readiness regressions",
        )

    # --- pure Phase184 acceptance tooling regressions ---
    if args.only in (None, "phase184-acceptance-tooling"):
        results["phase184-profile-acceptance-protocol"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_PROFILE_ACCEPTANCE_PROTOCOL_REGRESSION,
            ],
            "Phase184 acceptance protocol tooling regressions",
        )
        results["phase184-profile-acceptance-orchestrator"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_PROFILE_ACCEPTANCE_ORCHESTRATOR_REGRESSION,
            ],
            "Phase184 acceptance orchestrator tooling regressions",
        )
        results["phase184-foxglove-desktop-live-protocol"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_FOXGLOVE_DESKTOP_LIVE_PROTOCOL_REGRESSION,
            ],
            "Phase184 Foxglove Desktop live protocol regressions",
        )
        results["phase184-foxglove-cli-installer"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_FOXGLOVE_CLI_INSTALL_REGRESSION,
            ],
            "Phase184 Foxglove CLI installer regressions",
        )
        results["phase184-windows-job-owner"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_WINDOWS_JOB_OWNER_REGRESSION,
            ],
            "Phase184 Windows Job owner regressions",
        )
        results["phase184-foxglove-desktop-live-coordinator"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_FOXGLOVE_DESKTOP_LIVE_ACCEPTANCE_REGRESSION,
            ],
            "Phase184 Foxglove Desktop live coordinator regressions",
        )

    # --- pure Phase186 Bridge tooling and package-composition gates ---
    if args.only in (None, "phase186-bridge-tooling"):
        for module in PHASE186_BRIDGE_TOOLING_REGRESSIONS:
            label = module.rsplit(".", 1)[-1].removeprefix("test_").replace("_", " ")
            # The provenance regression suite performs many isolated Git and
            # filesystem fixture operations.  On synced Windows checkouts it
            # legitimately exceeds the generic 600-second command deadline;
            # the enclosing Phase186 job already has no wall-clock timeout.
            slow_provenance = module == "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance"
            results["phase186-" + module.rsplit(".", 1)[-1]] = run(
                [sys.executable, "-m", "unittest", module],
                "Phase186 " + label + " regressions",
                timeout_seconds=job_timeout_seconds() if slow_provenance else None,
            )
        results["phase186-package-matrix"] = run(
            [sys.executable, PHASE186_PACKAGE_MATRIX_VALIDATOR],
            "Phase186 package-composition compile and boundary matrix",
            timeout_seconds=job_timeout_seconds(),
        )
        results["phase186-provenance"] = run(
            [sys.executable, "-m", PHASE186_PROVENANCE_MODULE],
            "Phase186 protocol and source provenance",
            timeout_seconds=job_timeout_seconds(),
        )

    # --- specifically provisioned Windows Unity + ROS/RMW live certification ---
    if args.only == "phase186-bridge-windows-live":
        head = current_git_head()
        ensure_clean_source(PHASE186_CERTIFICATION_MODULE.replace(".", "/") + ".py")
        certification_run_id = phase186_certification_run_id(head)
        results["phase186-bridge-windows-live"] = run(
            [
                sys.executable,
                "-m",
                PHASE186_CERTIFICATION_MODULE,
                "--expected-head",
                head,
                "--output-root",
                "build/phase186/windows-live",
                "--run-id",
                certification_run_id,
            ],
            "Phase186 provisioned Windows Unity and ROS/RMW live certification",
            timeout_seconds=job_timeout_seconds(),
        )

    # --- pure Phase181 custom-interface helper and source-package regressions ---
    if args.only in (None, "phase181-ros2-regression"):
        results["phase181-ros2-peer-protocol"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_PEER_PROTOCOL_REGRESSION],
            "Phase181 custom ROS2 peer protocol regressions",
        )
        results["phase181-ros2-peer"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_PEER_REGRESSION],
            "Phase181 Windows Editor and Player custom ROS2 peer regressions",
        )
        results["phase181-ros2-matrix-profiles"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_MATRIX_PROFILES_REGRESSION],
            "Phase181 named custom ROS2 matrix profile regressions",
        )
        results["phase181-ros2-linux-peer"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_LINUX_PEER_REGRESSION],
            "Phase181 caller-owned Linux custom ROS2 peer regressions",
        )
        results["phase181-interface-tooling"] = run(
            [sys.executable, "-m", "unittest", *PHASE181_INTERFACE_TOOLING_REGRESSIONS],
            "Phase181 static custom ROS2 interface tooling regressions",
        )
        for distro, rmw in (
            ("humble", "rmw_fastrtps_cpp"),
            ("jazzy", "rmw_fastrtps_cpp"),
            ("lyrical", "rmw_fastrtps_cpp"),
            ("lyrical", "rmw_zenoh_cpp"),
        ):
            results["phase181-typesupport-" + distro + "-" + rmw] = run(
                [
                    sys.executable,
                    PHASE181_TYPESUPPORT_VALIDATOR,
                    "--distro",
                    distro,
                    "--require-rmw",
                    rmw,
                ],
                "Phase181 " + distro + " " + rmw + " custom ROS2 typesupport validation",
            )

    # --- official MCAP differential conformance ---
    if args.only in (None, "mcap-conformance"):
        results["mcap-conformance-differential"] = run(
            [
                sys.executable,
                "Scripts/mcap/conformance/run_phase121_conformance.py",
                "--release-blocking",
                "--report-path",
                str(MCAP_REPORT_PATH),
            ],
            "Official MCAP differential conformance",
            timeout_seconds=job_timeout_seconds(),
        )

    # --- package validators ---
    if args.only in (None, "packages"):
        package_results = run_parallel([
            (
                "test_release_tooling.py",
                [sys.executable, "-m", "unittest", RELEASE_TOOLING_REGRESSION],
            ),
            (
                "test_sample_sync_tooling.py",
                [
                    sys.executable,
                    "-m",
                    "unittest",
                    SAMPLE_SYNC_TOOLING_REGRESSION,
                ],
            ),
            (
                "test_remote_gateway_tooling.py",
                [
                    sys.executable,
                    "-m",
                    "unittest",
                    "Scripts.remotegateway.regression_checks.test_remote_gateway_tooling",
                ],
            ),
            (
                "maintained-python-regressions",
                [
                    sys.executable,
                    "-m",
                    "unittest",
                    *PACKAGE_LANE_REGRESSION_MODULES,
                ],
            ),
            ("validate_unity_package.py", [sys.executable, "Scripts/package/validate_unity_package.py"]),
            ("validate_local_entrypoints.py", [sys.executable, "Scripts/package/validate_local_entrypoints.py"]),
            ("sync_full_demo.py", [sys.executable, "Scripts/samples/sync_full_demo.py", "--mode", "validate"]),
            (
                "sync_ros2_bridge_sample.py",
                [
                    sys.executable,
                    "Scripts/samples/sync_ros2_bridge_sample.py",
                    "--dry-run",
                ],
            ),
            ("sync_maze_demo.py", [sys.executable, "Scripts/samples/sync_maze_demo.py", "--dry-run"]),
            ("sync_ros2_samples.py", [sys.executable, "Scripts/samples/sync_ros2_samples.py", "--dry-run"]),
            ("validate_schema_generated_outputs.py", [sys.executable, SCHEMA_GENERATED_OUTPUT_VALIDATOR]),
            *(
                (label, [sys.executable, path])
                for label, path in R2FU_PACKAGE_VALIDATORS
            ),
        ])
        results["release-tooling-regression"] = package_results["test_release_tooling.py"]
        results["sample-sync-tooling-regression"] = package_results[
            "test_sample_sync_tooling.py"
        ]
        results["remote-gateway-tooling-regression"] = package_results[
            "test_remote_gateway_tooling.py"
        ]
        results["maintained-python-regressions"] = package_results[
            "maintained-python-regressions"
        ]
        results["validate-package"] = package_results["validate_unity_package.py"]
        results["validate-entrypoints"] = package_results["validate_local_entrypoints.py"]
        results["validate-full-demo-sync"] = package_results["sync_full_demo.py"]
        results["validate-ros2-bridge-sample-sync"] = package_results[
            "sync_ros2_bridge_sample.py"
        ]
        results["validate-maze-demo-sync"] = package_results["sync_maze_demo.py"]
        results["validate-ros2-samples-sync"] = package_results["sync_ros2_samples.py"]
        results["validate-schema-generated"] = package_results["validate_schema_generated_outputs.py"]
        for label, _path in R2FU_PACKAGE_VALIDATORS:
            results[label] = package_results[label]

    # --- boundary check ---
    if args.only in (None, "boundary"):
        boundary_ok = _check_boundary()
        results["boundary"] = boundary_ok
        results["changelog-verified"] = _check_changelog_verified_stubs()

    # --- summary ---
    return report_ci_job_results(results, skipped_lanes)

__all__ = ['main']
