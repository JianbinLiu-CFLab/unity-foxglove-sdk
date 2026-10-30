from .foundation import *

class RunCiTests(unittest.TestCase):
    """Regression coverage for local CI runner reliability."""

    PHASE184_ACCEPTANCE_TOOLING_SUITES = (
        (
            "PHASE184_PROFILE_ACCEPTANCE_PROTOCOL_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance_protocol",
            "Phase184 acceptance protocol tooling regressions",
        ),
        (
            "PHASE184_PROFILE_ACCEPTANCE_ORCHESTRATOR_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance",
            "Phase184 acceptance orchestrator tooling regressions",
        ),
        (
            "PHASE184_FOXGLOVE_DESKTOP_LIVE_PROTOCOL_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_protocol",
            "Phase184 Foxglove Desktop live protocol regressions",
        ),
        (
            "PHASE184_FOXGLOVE_CLI_INSTALL_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_cli_install",
            "Phase184 Foxglove CLI installer regressions",
        ),
        (
            "PHASE184_WINDOWS_JOB_OWNER_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_windows_job_owner",
            "Phase184 Windows Job owner regressions",
        ),
        (
            "PHASE184_FOXGLOVE_DESKTOP_LIVE_ACCEPTANCE_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_acceptance",
            "Phase184 Foxglove Desktop live coordinator regressions",
        ),
    )
    PHASE186_BRIDGE_TOOLING_SUITES = (
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance_protocol",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_live",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_certification",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_build",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_capability_probe",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance",
    )

    def setUp(self) -> None:
        """Load a fresh run_ci module for each test."""
        self.run_ci = load_module("run_ci_under_test", RUN_CI_PATH)

    def test_run_ci_imports_under_current_python(self) -> None:
        """The CI runner module loads without f-string syntax failures."""
        self.assertTrue(hasattr(self.run_ci, "main"))

    def test_run_ci_load_clears_cached_package_sections(self) -> None:
        """Each isolated load must rebuild the split implementation package."""
        _phase192_first_section = sys.modules["run_ci.foundation"]
        _phase192_first_section.REPO_ROOT = Path("stale-run-ci-root")

        second = load_module("run_ci_under_test", RUN_CI_PATH)

        self.assertIsNot(_phase192_first_section, sys.modules["run_ci.foundation"])
        self.assertEqual(RUN_CI_PATH.parents[2], second.REPO_ROOT)

    def test_run_ci_uses_repository_build_root(self) -> None:
        """The split runner must keep CI artifacts below the repository build root."""
        expected_root = RUN_CI_PATH.parents[2]
        self.assertEqual(self.run_ci.REPO_ROOT, expected_root)
        self.assertTrue(self.run_ci.CI_ROOT.is_relative_to(expected_root / "build" / "ci"))

    def test_run_ci_supports_module_entrypoint(self) -> None:
        """The package preserves the module invocation supported by the former script."""
        result = subprocess.run(
            [sys.executable, "-m", "Scripts.release.run_ci", "--help"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("usage:", result.stdout)

    def test_boundary_check_fails_when_git_command_fails(self) -> None:
        """Boundary checks must not pass when git itself fails."""
        failed = subprocess.CompletedProcess(args=["git"], returncode=128, stdout="", stderr="fatal")
        with mock.patch.object(self.run_ci.subprocess, "run", return_value=failed):
            self.assertFalse(self.run_ci._check_boundary())

    def test_changelog_verified_stub_check_fails_on_unreplaced_stub(self) -> None:
        """Release CI should fail if changelog verified entries still contain generated stubs."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "CHANGELOG.md").write_text(
                "### Verified\n\n"
                "- Runtime validation suite should be run before tagging this release.\n",
                encoding="utf-8",
            )
            with mock.patch.object(self.run_ci, "REPO_ROOT", root):
                self.assertFalse(self.run_ci._check_changelog_verified_stubs())

            (root / "CHANGELOG.md").write_text(
                "### Verified\n\n"
                "- Runtime validation suite passed before tagging this release.\n",
                encoding="utf-8",
            )
            with mock.patch.object(self.run_ci, "REPO_ROOT", root):
                self.assertTrue(self.run_ci._check_changelog_verified_stubs())

    def test_boundary_check_uses_plain_ls_files_for_nested_developer(self) -> None:
        """Nested Developer checks should not depend on git pathspec glob support."""
        calls: list[list[str]] = []

        def fake_run(cmd, **_kwargs):
            """Return canned git outputs for the boundary check."""
            calls.append(cmd)
            if cmd == ["git", "ls-files", "--", "Plan/**", "Developer/**"]:
                return subprocess.CompletedProcess(cmd, 0, "", "")
            if cmd == ["git", "ls-files"]:
                return subprocess.CompletedProcess(cmd, 0, "Packages/Demo/Developer/meta.txt\n", "")
            raise AssertionError(cmd)

        with mock.patch.object(self.run_ci.subprocess, "run", side_effect=fake_run):
            self.assertFalse(self.run_ci._check_boundary())

        self.assertIn(["git", "ls-files"], calls)
        self.assertFalse(any(":(glob)" in " ".join(call) for call in calls))

    def test_boundary_check_rejects_root_developer_meta(self) -> None:
        """A root Unity folder companion must not bypass the private boundary."""

        def fake_run(cmd, **_kwargs):
            """Expose one tracked root Developer.meta file."""
            if cmd == ["git", "ls-files", "--", "Plan/**", "Developer/**"]:
                return subprocess.CompletedProcess(cmd, 0, "", "")
            if cmd == ["git", "ls-files"]:
                return subprocess.CompletedProcess(cmd, 0, "Developer.meta\n", "")
            raise AssertionError(cmd)

        with mock.patch.object(self.run_ci.subprocess, "run", side_effect=fake_run):
            self.assertFalse(self.run_ci._check_boundary())

    def test_repository_boundary_workflow_triggers_for_deep_private_paths(self) -> None:
        """GitHub must schedule the boundary job for private paths at any depth."""
        workflow = REPOSITORY_BOUNDARY_WORKFLOW_PATH.read_text(encoding="utf-8")
        event_blocks: dict[str, str] = {}
        for event in ("push", "pull_request"):
            match = re.search(
                rf"(?ms)^  {event}:\n(?P<body>.*?)(?=^  [a-z_]+:|^jobs:)",
                workflow,
            )
            self.assertIsNotNone(match, f"missing {event} event block")
            event_blocks[event] = match.group("body")

        required_patterns = {
            "Developer.meta": '      - "Developer.meta"',
            "Unity2Foxglove/Assets/Developer/private.md": '      - "**/Developer/**"',
            "Packages/A/B/Developer.meta": '      - "**/Developer.meta"',
        }
        for event, block in event_blocks.items():
            for private_path, required_pattern in required_patterns.items():
                self.assertIn(
                    required_pattern,
                    block,
                    f"{event} does not schedule the boundary job for {private_path}",
                )

        self.assertRegex(
            workflow,
            r"git ls-files -- [^\n]*'Developer\.meta'",
            "the remote boundary command does not inspect root Developer.meta",
        )
        phase16 = read_runtime_validation_source(PHASE16_VALIDATION_PATH)
        self.assertIn(
            '"Developer.meta"',
            phase16,
            "the default Phase16 boundary check does not inspect root Developer.meta",
        )

    def test_workflow_checkouts_never_persist_repository_credentials(self) -> None:
        """Read-only CI jobs must remove checkout credentials from every workspace."""

        for path in WORKFLOW_PATHS:
            workflow = path.read_text(encoding="utf-8")
            checkout_count = len(re.findall(r"uses:\s*actions/checkout@(?:v4|[0-9a-f]{40})(?:\s|#)", workflow))
            hardened_count = workflow.count("persist-credentials: false")
            self.assertGreater(checkout_count, 0, path.name)
            self.assertEqual(
                checkout_count,
                hardened_count,
                f"every checkout in {path.name} must disable credential persistence",
            )

    def test_ci_dotnet_restore_fails_when_the_only_feed_is_unavailable(self) -> None:
        """CI restore must not suppress failure of the repository's sole NuGet feed."""

        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertNotIn("--ignore-failed-sources", workflow)

    def test_local_restore_matches_remote_strict_source_semantics(self) -> None:
        """Local restore must not turn an unavailable feed into a cache-only green run."""
        with mock.patch.object(self.run_ci, "run", return_value=True) as run_command:
            self.assertTrue(
                self.run_ci.restore_with_ignoring_failed_sources(
                    "sample.csproj", "strict restore", fatal=False
                )
            )
        command = run_command.call_args.args[0]
        self.assertEqual(["dotnet", "restore", "sample.csproj"], command)
        self.assertNotIn("--ignore-failed-sources", command)

    def test_heavy_pull_request_workflows_cancel_superseded_runs(self) -> None:
        """Superseded dotnet and package runs should release hosted CI capacity."""

        for path in (DOTNET_WORKFLOW_PATH, PACKAGE_WORKFLOW_PATH):
            workflow = path.read_text(encoding="utf-8")
            self.assertRegex(workflow, r"(?m)^concurrency:\s*$")
            self.assertIn(
                "group: ${{ github.workflow }}-${{ github.ref }}",
                workflow,
            )
            self.assertIn("cancel-in-progress: true", workflow)

    def test_run_ci_includes_schema_generated_output_freshness(self) -> None:
        """Local CI should reject stale committed schema generator outputs."""
        self.assertEqual(
            "Scripts/schema/validate_schema_generated_outputs.py",
            self.run_ci.SCHEMA_GENERATED_OUTPUT_VALIDATOR,
        )

    def test_dotnet_workflow_runs_schema_generated_output_freshness(self) -> None:
        """Remote CI must compare outputs against its pinned Foxglove checkout."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        checkout = active_workflow_line_index(
            workflow,
            "repository: foxglove/foxglove-sdk",
        )
        pinned_ref = active_workflow_line_index(
            workflow,
            "ref: b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba",
            checkout + 1,
        )
        checkout_path = active_workflow_line_index(
            workflow,
            "path: third-party/foxglove-sdk",
            pinned_ref + 1,
        )
        validation = active_workflow_line_index(
            workflow,
            "run: python3 Scripts/schema/validate_schema_generated_outputs.py",
            checkout_path + 1,
        )

        self.assertLess(checkout, pinned_ref)
        self.assertLess(pinned_ref, checkout_path)
        self.assertLess(checkout_path, validation)

    def test_dotnet_workflow_runs_xunit_before_runtime_gate(self) -> None:
        """The unit-test gate must not be hidden behind an earlier runtime failure."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        xunit = active_workflow_line_index(workflow, "- name: Run xUnit unit tests")
        runtime = active_workflow_line_index(workflow, "- name: Run validation suite")
        self.assertLess(xunit, runtime)

    def test_dotnet_workflow_runs_package_sample_closure_lane(self) -> None:
        """The required workflow runs the package and sample closure lane."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        step = active_workflow_line_index(workflow, "- name: Run package and sample closure lane")
        command = active_workflow_line_index(workflow, "run: python3 -B Scripts/release/run_ci.py --only packages")
        self.assertLess(step, command)
        self.assertIn(
            "Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup",
            RUN_CI_PATH.read_text(encoding="utf-8"),
        )
        self.assertIn(
            "-m unittest Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup",
            workflow,
        )

    def test_dotnet_workflow_runs_workstream_f_runtime_closure_validations(self) -> None:
        """The required workflow runs every explicit Workstream F runtime selector."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        step = active_workflow_line_index(workflow, "- name: Run Workstream F explicit runtime closure validations")
        command = active_workflow_line_index(workflow, "dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- \"$flag\" || status=$?")
        self.assertLess(step, command)
        self.assertIn("status=0", workflow)
        self.assertIn('exit "$status"', workflow)
        for flag in ("--phase138l", "--phase138m", "--phase138p", "--phase163-54"):
            self.assertIn(flag, workflow)

    def test_dotnet_workflow_collects_independent_gate_results_after_early_failure(self) -> None:
        """Independent remote gates must execute after an earlier step fails."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        for step in (
            "- name: Run xUnit unit tests",
            "- name: Run FoxRun publish panel behavior tests",
            "- name: Validate source generator DLL freshness script",
            "- name: Validate generated ROS2 schema output freshness",
            "- name: Run validation suite",
            "- name: Run Phase179 ROS2 acceptance helper regressions",
            "- name: Run Phase181 custom ROS2 acceptance helper regressions",
            "- name: Run complete Python regression inventory",
            "- name: Run Phase186 Bridge tooling and package-composition gate",
            "- name: Run Phase192 base-head identity equivalence gate",
            "- name: Run official MCAP differential conformance",
            "- name: Validate local entrypoints",
        ):
            start = active_workflow_line_index(workflow, step)
            lines = workflow.splitlines()
            self.assertTrue(
                any(line.strip() == "if: always()" for line in lines[start + 1 : start + 4]),
                step,
            )

    def test_dotnet_workflow_identity_gate_uses_pull_request_base_and_head(self) -> None:
        """The required workflow must compare the actual PR base and head refs."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        start = active_workflow_line_index(
            workflow,
            "- name: Run Phase192 base-head identity equivalence gate",
        )
        joined = "\n".join(workflow.splitlines()[start : start + 30])
        self.assertIn("github.event.pull_request.base.sha", joined)
        self.assertIn("github.event.pull_request.head.sha", joined)
        self.assertIn("Scripts/phase192/compare_identity_surfaces.py", joined)
        self.assertIn("--compatibility", joined)
        self.assertIn("git archive", joined)
        self.assertIn("IDENTITY_GATE_BOOTSTRAP_REQUIRED", joined)
        self.assertNotIn("bootstrap_checker_sha", joined)
        self.assertNotIn("git show", joined)
        self.assertIn("trusted_root", joined)
        self.assertIn("PYTHONPATH", joined)
        self.assertIn("$RUNNER_TEMP", joined)
        self.assertIn("--repository .", joined)
        self.assertNotIn("python3 Scripts/phase192/compare_identity_surfaces.py", joined)
        self.assertNotIn("--strict", joined)

    def test_dotnet_workflow_cannot_waive_validator_initializer_changes(self) -> None:
        """The trusted-base compatibility fallback must not accept facade rebindings."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        start = workflow.index("          lyrical_validator_errors = [")
        end = workflow.index("          normalized_lines =", start)
        fallback = workflow[start:end]
        self.assertNotIn("PACKAGE_INITIALIZER_CHANGED", fallback)
        self.assertIn(
            "python3 Scripts/phase192/compare_identity_surfaces.py \\",
            workflow,
        )

    def test_dotnet_workflow_provisions_phase186_websocket_runtime_dependency(self) -> None:
        """The module-execution regression must run with its websocket dependency installed."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn(
            "python3 -m pip install --disable-pip-version-check --no-input "
            "psutil==7.0.0 websockets==15.0.1",
            workflow,
        )

    def test_dotnet_workflow_provisions_mcap_timestamp_runtime_dependency(self) -> None:
        """The MCAP timestamp regression must run with its pinned reader installed."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn(
            "mcap==1.3.1",
            workflow,
        )

    def test_dotnet_workflow_runs_xunit_before_panel_lane(self) -> None:
        """The unit-test gate must run even when the panel lane fails first."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        xunit = active_workflow_line_index(workflow, "- name: Run xUnit unit tests")
        panel = active_workflow_line_index(workflow, "- name: Run FoxRun publish panel behavior tests")
        self.assertLess(xunit, panel)

    def test_workflow_line_lookup_rejects_commented_steps(self) -> None:
        """A commented workflow step must not satisfy an active CI contract."""
        workflow = (
            "# repository: foxglove/foxglove-sdk\n"
            "  # ref: b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba\n"
        )

        with self.assertRaisesRegex(ValueError, "active workflow line is missing"):
            active_workflow_line_index(
                workflow,
                "repository: foxglove/foxglove-sdk",
            )

    def test_windows_workflow_executes_editor_restart_relay_process_tests(self) -> None:
        """The Windows-only restart behavior must not silently pass in an Ubuntu lane."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        windows_job = workflow.index("runs-on: windows-latest")
        adapter_property = workflow.index(
            "-p:IncludeRos2ForUnityAdapter=true",
            windows_job,
        )
        relay_filter = workflow.index(
            "FullyQualifiedName~Ros2ForUnityEditorRestartRelayTests",
            adapter_property,
        )

        self.assertLess(windows_job, adapter_property)
        self.assertLess(adapter_property, relay_filter)

    def test_validator_msbuild_args_keep_dash_prefixed_property_attached(self) -> None:
        """Argparse must receive dash-prefixed MSBuild properties as option values."""
        self.assertEqual(
            ["--msbuild-prop=-p:BaseOutputPath=C:/ci/bin/"],
            self.run_ci.validator_msbuild_args(["-p:BaseOutputPath=C:/ci/bin/"]),
        )

    def test_package_validators_use_current_python_executable(self) -> None:
        """Local CI should not depend on a bare python command existing."""
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture CI subprocess commands without executing them."""
            for _label, cmd in commands:
                calls.append(cmd)
            return {label: True for label, _cmd in commands}

        with mock.patch.object(self.run_ci, "run_parallel", side_effect=fake_run_parallel):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "packages"]):
                self.assertEqual(0, self.run_ci.main())

        python_calls = [cmd for cmd in calls if cmd and cmd[1].endswith(".py")]
        self.assertTrue(python_calls)
        self.assertTrue(all(cmd[0] == sys.executable for cmd in python_calls))

    def test_packages_lane_executes_every_r2fu_package_validator(self) -> None:
        """Local package CI must cover runtime and adapter artifacts for every distro."""
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture package subprocess commands without executing them."""
            calls.extend(command for _label, command in commands)
            return {label: True for label, _command in commands}

        with mock.patch.object(self.run_ci, "run_parallel", side_effect=fake_run_parallel):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "packages"]):
                self.assertEqual(0, self.run_ci.main())

        expected = {
            f"Scripts/ros2forunity/windows/{distro}/{validator}.py"
            for distro in ("humble", "jazzy", "lyrical")
            for validator in (
                "validate_r2fu_runtime_package",
                "validate_ros2forunity_package",
            )
        }
        actual = {
            command[1]
            for command in calls
            if len(command) == 2 and command[1] in expected
        }
        self.assertEqual(expected, actual)

    def test_package_workflow_executes_every_r2fu_package_validator(self) -> None:
        """Remote package CI must cover the same six distro and artifact validators."""
        workflow = PACKAGE_WORKFLOW_PATH.read_text(encoding="utf-8")

        for distro in ("humble", "jazzy", "lyrical"):
            for validator in (
                "validate_r2fu_runtime_package",
                "validate_ros2forunity_package",
            ):
                command = (
                    "python3 Scripts/ros2forunity/windows/"
                    f"{distro}/{validator}.py"
                )
                self.assertEqual(1, workflow.count(command), command)

    def test_packages_lane_executes_ros2_bridge_sample_regressions_and_drift_gate(self) -> None:
        """The package lane must execute both the sync helper tests and byte drift check."""
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture package subprocess commands without executing them."""
            calls.extend(command for _label, command in commands)
            return {label: True for label, _command in commands}

        with mock.patch.object(
            self.run_ci,
            "run_parallel",
            side_effect=fake_run_parallel,
        ):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "packages"],
            ):
                self.assertEqual(0, self.run_ci.main())

        self.assertIn(
            [
                sys.executable,
                "-m",
                "unittest",
                "Scripts.samples.regression_checks.test_sample_sync_tooling",
            ],
            calls,
        )
        self.assertIn(
            [
                sys.executable,
                "-m",
                "unittest",
                "Scripts.remotegateway.regression_checks.test_remote_gateway_tooling",
            ],
            calls,
        )
        self.assertIn(
            [
                sys.executable,
                "Scripts/samples/sync_ros2_bridge_sample.py",
                "--dry-run",
            ],
            calls,
        )
        self.assertIn(
            [sys.executable, "Scripts/samples/sync_maze_demo.py", "--dry-run"],
            calls,
        )

    def test_packages_lane_executes_all_maintained_python_regression_modules(self) -> None:
        """Default package CI must execute maintained regression modules, not only validators."""
        expected = (
            "Scripts.release.regression_checks.test_regression_inventory",
            "Scripts.native.regression_checks.test_native_sources",
            "Scripts.package.regression_checks.test_validate_local_entrypoints",
            "Scripts.package.regression_checks.test_validate_phase186_package_matrix",
            "Scripts.package.regression_checks.test_validate_unity_package",
            "Scripts.schema.regression_checks.test_schema_tooling",
            "Scripts.smoke.test_core_smoke_scripts",
            "Scripts.smoke.ros2.regression_checks.test_phase162_lyrical_zenoh_player_smoke",
            "Scripts.smoke.ros2.regression_checks.test_ros2_windows_env",
            "Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup",
            "Scripts.smoke.foxrun.regression_checks.test_phase185_foxrun_messagepack_probe",
            "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_manual",
            "Scripts.phase190.regression_checks.test_phase190_validators",
            "Scripts.phase190.regression_checks.test_phase190_script_documentation",
            "Scripts.phase190.regression_checks.test_run_conformance",
            "Scripts.phase192.regression_checks.test_identity_tooling",
            "Scripts.phase192.regression_checks.test_freeze_baseline",
            "Scripts.performance.regression_checks.test_phase188_replay",
            "Scripts.smoke.replay.regression_checks.test_phase188_deterministic_replay_acceptance",
        )
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture package subprocess commands without executing them."""
            calls.extend(command for _label, command in commands)
            return {label: True for label, _command in commands}

        with mock.patch.object(
            self.run_ci,
            "run_parallel",
            side_effect=fake_run_parallel,
        ):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "packages"],
            ):
                self.assertEqual(0, self.run_ci.main())

        self.assertIn(
            [sys.executable, "-m", "unittest", *expected],
            calls,
        )

    def test_required_test_context_aggregates_all_managed_lanes(self) -> None:
        """The protected test context must fail when any managed lane fails."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn("  linux-test:", workflow)
        aggregate = workflow[workflow.index("\n  test:\n", workflow.index("jobs:")) :]
        self.assertIn("if: always()", aggregate)
        for dependency in (
            "linux-test",
            "optional-ros2-adapter",
            "optional-ros2-native",
            "windows-parity",
            "analyzer-freshness",
        ):
            self.assertIn(f"- {dependency}", aggregate)
        self.assertIn("Validation aggregate failed.", aggregate)

    def test_unity_batch_gate_is_present_and_path_aware(self) -> None:
        """Unity changes expose a required, path-aware aggregate gate."""
        workflow = (ROOT / ".github" / "workflows" / "unity-compile.yml").read_text(encoding="utf-8")
        self.assertIn("name: Unity batch compile and path-aware gate", workflow)
        self.assertNotIn("continue-on-error:", workflow)
        self.assertIn("run_unity_batch_compile.py", workflow)
        self.assertIn("  push:\n    branches: [main]", workflow)
        self.assertIn("  pull_request:\n    branches: [main]", workflow)
        self.assertNotIn("pull_request_target", workflow)

        scope = workflow.split("  unity_scope:\n", 1)[1].split("  unity_compile:\n", 1)[0]
        self.assertIn("unity_required: ${{ steps.scope.outputs.unity_required }}", scope)
        self.assertIn("git diff --no-renames --name-only", scope)
        self.assertIn(r"Packages/[^/]+/(Runtime|Editor|Plugins|Tests/(Runtime|Unit))/", scope)
        self.assertIn(r"Packages/[^/]+/package\.json$", scope)
        self.assertIn(r"Unity2Foxglove/(Assets|Packages|ProjectSettings)/", scope)
        self.assertIn(r".*\.(asmdef|asmref|meta)$", scope)
        self.assertNotIn("actions/runners", workflow)
        self.assertNotIn("gh api", workflow)
        self.assertNotIn("GH_TOKEN", workflow)

        aggregate = workflow.split("  unity_required:\n", 1)[1]
        self.assertIn("always()", aggregate)
        self.assertIn("UNITY_GATE_NOT_REQUIRED", aggregate)
        self.assertIn("UNITY_SELF_HOSTED_RUNNER", aggregate)
        self.assertIn("UNITY_GATE_PASS", aggregate)
        self.assertIn("github.event.pull_request.head.repo.full_name", aggregate)
        self.assertNotIn("pull_request_target", workflow)

    def test_unity_scope_matches_import_sensitive_paths(self) -> None:
        """Unity import and project configuration changes must require the gate."""
        workflow = UNITY_COMPILE_WORKFLOW_PATH.read_text(encoding="utf-8")
        scope = workflow.split("  unity_scope:\n", 1)[1].split("  unity_compile:\n", 1)[0]
        match = re.search(r"grep -E '([^']+)' >/dev/null", scope)
        self.assertIsNotNone(match)
        assert match is not None
        path_pattern = match.group(1)
        for path in (
            "Packages/dev.unity2foxglove.sdk/Runtime/Foo.cs",
            "Packages/dev.unity2foxglove.sdk/Editor/Foo.cs",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Foo.cs",
            "Packages/dev.unity2foxglove.sdk/Tests/Unit/Foo.cs",
            "Packages/dev.unity2foxglove.sdk/Plugins/Google.Protobuf.dll",
            "Packages/dev.unity2foxglove.sdk/package.json",
            "Unity2Foxglove/Assets/Foo.cs",
            "Unity2Foxglove/Packages/manifest.json",
            "Unity2Foxglove/Packages/packages-lock.json",
            "Unity2Foxglove/ProjectSettings/ProjectVersion.txt",
            "Packages/dev.unity2foxglove.sdk/Foo.asmdef",
            "Packages/dev.unity2foxglove.sdk/Foo.asmref",
            "Packages/dev.unity2foxglove.sdk/Runtime/Foo.cs.meta",
        ):
            self.assertRegex(path, path_pattern, path)
        for path in ("README.md", "Scripts/release/run_ci.py", ".github/workflows/test.yml"):
            self.assertNotRegex(path, path_pattern, path)

        self.assertNotIn("grep -Eq", scope)

    def test_unity_scope_consumes_all_changed_paths_before_matching(self) -> None:
        """A large changed-path list must not abort on a short-circuiting grep pipe."""
        workflow = UNITY_COMPILE_WORKFLOW_PATH.read_text(encoding="utf-8")
        scope = workflow.split("  unity_scope:\n", 1)[1].split("  unity_compile:\n", 1)[0]
        self.assertIn("grep -E '", scope)
        self.assertIn("' >/dev/null", scope)
        self.assertNotIn("grep -Eq", scope)

        changed = "\n".join(
            ["Packages/dev.unity2foxglove.sdk/Runtime/Foo.cs"]
            + [f"docs/generated-{index}.md" for index in range(2000)]
        )
        script = """
set -euo pipefail
required=false
changed="$(cat "$1")"
if printf '%s\\n' "$changed" | grep -E '^(Packages/[^/]+/(Runtime|Editor|Plugins|Tests/(Runtime|Unit))/|Packages/[^/]+/package\\.json$|Unity2Foxglove/(Assets|Packages|ProjectSettings)/|.*\\.(asmdef|asmref|meta)$)' >/dev/null; then
  required=true
fi
printf '%s' "$required"
"""
        with tempfile.TemporaryDirectory() as temp:
            changed_path = Path(temp) / "changed.txt"
            changed_path.write_text(changed, encoding="utf-8")
            bash_executable = shutil.which("bash") or "bash"
            if os.name == "nt":
                git_bash = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git" / "bin" / "bash.exe"
                if git_bash.exists():
                    bash_executable = str(git_bash)
            result = subprocess.run(
                [bash_executable, "-c", script, "scope-test", changed_path.as_posix()],
                check=False,
                capture_output=True,
                text=True,
            )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, "true")

    def test_unity_required_gate_fails_closed_when_scope_needs_runner(self) -> None:
        """Unity changes must not bypass the required context when scheduling is disabled."""
        workflow = (ROOT / ".github" / "workflows" / "unity-compile.yml").read_text(encoding="utf-8")
        compile_job = workflow.split("  unity_compile:\n", 1)[1].split("  unity_required:\n", 1)[0]
        self.assertIn("needs.unity_scope.outputs.unity_required == 'true'", compile_job)
        self.assertNotIn("needs.unity_scope.outputs.runner_available", compile_job)
        self.assertIn("vars.UNITY_SELF_HOSTED_RUNNER == 'enabled'", compile_job)
        self.assertLess(compile_job.index("if:"), compile_job.index("runs-on:"))

    def test_unity_batch_gate_runs_only_on_an_enabled_trusted_runner(self) -> None:
        """Schedule the self-hosted job only when enabled, and never run fork pull requests on it."""
        workflow = (ROOT / ".github" / "workflows" / "unity-compile.yml").read_text(encoding="utf-8")
        job = workflow.split("  unity_compile:\n", 1)[1].split("    steps:\n", 1)[0]
        self.assertIn("vars.UNITY_SELF_HOSTED_RUNNER == 'enabled'", job)
        self.assertNotIn("needs.unity_scope.outputs.runner_available", job)
        self.assertNotIn("pull_request_target", job)
        self.assertIn("github.event_name != 'pull_request'", job)
        self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", job)
        self.assertLess(job.index("if:"), job.index("runs-on:"))

    def test_unity_scope_uses_pull_request_workflow_with_fork_guard(self) -> None:
        """Fork pull requests stay on the ordinary workflow and cannot use the self-hosted runner."""
        workflow = UNITY_COMPILE_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertNotIn("pull_request_target", workflow)
        self.assertIn("github.event.pull_request.head.repo.full_name", workflow)
        self.assertIn("github.event_name == 'pull_request'", workflow)
        self.assertNotIn("Checkout pull request head for read-only scope detection", workflow)
        self.assertNotIn("Fetch trusted pull request base", workflow)

    def test_unity_batch_gate_enables_git_long_paths_before_checkout(self) -> None:
        """The runner checkout must support the repository's longest tracked paths on Windows."""
        workflow = (ROOT / ".github" / "workflows" / "unity-compile.yml").read_text(encoding="utf-8")
        steps = workflow.split("  unity_compile:\n", 1)[1].split("  unity_required:\n", 1)[0]
        self.assertIn("git config --global core.longpaths true", steps)
        self.assertIn("GIT_CONFIG_GLOBAL=", steps)
        self.assertLess(
            steps.index("core.longpaths true"),
            steps.index("uses: actions/checkout@"),
        )

    def test_unity_runner_bootstraps_powershell_path_before_pwsh_steps(self) -> None:
        """The runner must expose the MSI PowerShell executable before shell resolution."""
        workflow = UNITY_COMPILE_WORKFLOW_PATH.read_text(encoding="utf-8")
        steps = workflow.split("  unity_compile:\n", 1)[1].split("  unity_required:\n", 1)[0]
        bootstrap = steps.split("      - name: Enable Git long-path support", 1)[0]
        self.assertIn("shell: cmd", bootstrap)
        self.assertIn(r"POWERSHELL7=C:\Program Files\PowerShell\7", bootstrap)
        self.assertIn(r"%POWERSHELL7%\pwsh.exe", bootstrap)
        self.assertIn("%GITHUB_PATH%", bootstrap)
        self.assertIn("where pwsh", bootstrap)
        self.assertLess(bootstrap.index("shell: cmd"), steps.index("shell: pwsh"))

    def test_unity_workflow_preserves_library_and_separates_manual_runs(self) -> None:
        """Keep the Unity Library cache and prevent push runs from cancelling dispatch runs."""
        workflow = UNITY_COMPILE_WORKFLOW_PATH.read_text(encoding="utf-8")
        compile_job = workflow.split("  unity_compile:\n", 1)[1].split("  unity_required:\n", 1)[0]
        self.assertIn("clean: false", compile_job)
        self.assertIn("git clean -ffdx -e Unity2Foxglove/Library/", compile_job)
        self.assertIn("--timeout-seconds 3600", compile_job)
        self.assertIn(
            "group: unity-compile-${{ github.event_name }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        self.assertIn("cancel-in-progress: ${{ github.event_name != 'workflow_dispatch' }}", workflow)

    def test_phase186_live_workflow_preserves_library_and_bootstraps_powershell(self) -> None:
        """The live certification lane must reuse the Unity cache and resolve PowerShell 7."""
        workflow = PHASE186_WINDOWS_LIVE_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn(r"POWERSHELL7=C:\Program Files\PowerShell\7", workflow)
        self.assertIn("where pwsh", workflow)
        self.assertIn("clean: false", workflow)
        self.assertIn("git clean -ffdx -e Unity2Foxglove/Library/", workflow)
        self.assertLess(workflow.index("Bootstrap PowerShell 7"), workflow.index("uses: actions/checkout@"))

    def test_unity_scope_disables_rename_detection(self) -> None:
        """Renames must report both paths so moving compiled code cannot bypass the gate."""
        workflow = UNITY_COMPILE_WORKFLOW_PATH.read_text(encoding="utf-8")
        scope = workflow.split("  unity_scope:\n", 1)[1].split("  unity_compile:\n", 1)[0]
        diff_lines = [line.strip() for line in scope.splitlines() if "git diff" in line]
        self.assertGreaterEqual(len(diff_lines), 3)
        for line in diff_lines:
            self.assertIn("--no-renames", line)

    def test_unity_project_bootstraps_r2fu_symbol_before_first_compile(self) -> None:
        """The Windows editor must see the optional R2FU assembly on its first import."""
        project_settings = (ROOT / "Unity2Foxglove" / "ProjectSettings" / "ProjectSettings.asset").read_text(
            encoding="utf-8"
        )
        self.assertRegex(
            project_settings,
            r"(?m)^\s*Standalone:.*\bUNITY2FOXGLOVE_ROS2_FOR_UNITY\b",
        )
        asmdef_paths = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Runtime"
            / "Native"
            / "Unity2Foxglove.Ros2ForUnity.Native.asmdef",
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Editor"
            / "Native"
            / "Unity2Foxglove.Ros2ForUnity.Native.Editor.asmdef",
        )
        for index, asmdef_path in enumerate(asmdef_paths):
            asmdef = json.loads(asmdef_path.read_text(encoding="utf-8"))
            self.assertIn("UNITY2FOXGLOVE_ROS2_FOR_UNITY", asmdef.get("defineConstraints", []))
            expected_platform_constraint = "UNITY_EDITOR_WIN" if index else "UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN"
            self.assertIn(expected_platform_constraint, asmdef.get("defineConstraints", []))

    def test_native_source_guards_defer_platform_authority_to_asmdefs(self) -> None:
        """Package native sources must not disappear when Unity compiles the Windows Editor assembly."""
        native_root = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Runtime" / "Native"
        native_sources = sorted(native_root.rglob("*.cs"))
        self.assertGreater(len(native_sources), 0)
        for source in native_sources:
            first_guard = next(
                (line.strip() for line in source.read_text(encoding="utf-8").splitlines() if line.strip().startswith("#if")),
                "",
            )
            if first_guard:
                self.assertEqual("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", first_guard, source)

        inspector = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Editor"
            / "Native"
            / "FoxRunRos2SubscriptionDiagnosticsInspector.cs"
        )
        self.assertEqual(
            "#if UNITY_EDITOR && UNITY2FOXGLOVE_ROS2_FOR_UNITY",
            next(line.strip() for line in inspector.read_text(encoding="utf-8").splitlines() if line.strip().startswith("#if")),
        )

        emitter_root = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Editor" / "Native" / "FoxRun"
        for name in ("Ros2CustomDtoMapperEmitter.cs", "Ros2CustomPublishEmitter.cs", "Ros2InputDispatchEmitter.cs"):
            emitter = (emitter_root / name).read_text(encoding="utf-8")
            self.assertIn("UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN", emitter)

    def test_package_release_gate_scans_the_full_event_range(self) -> None:
        """Candidate detection must not miss runtime changes in a multi-commit push."""
        workflow = PACKAGE_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn("fetch-depth: 0", workflow)
        self.assertIn('GITHUB_EVENT_PATH', workflow)
        self.assertIn('GITHUB_EVENT_NAME', workflow)
        self.assertIn('event["pull_request"]["base"]["sha"]', workflow)
        self.assertIn('event.get("before")', workflow)
        self.assertIn('os.environ["GITHUB_SHA"]', workflow)
        self.assertIn('"git", "diff", "--name-only", "--no-renames", before, after', workflow)
        self.assertIn('"git", "diff-tree", "--root", "--no-commit-id", "--name-only", "-r", "--no-renames", after', workflow)
        self.assertIn('inventory_path = Path(manifest["inventoryFile"])', workflow)
        self.assertIn('inventory_path = package / inventory_path', workflow)
        self.assertNotIn('package / "RuntimeSupport" / manifest["inventoryFile"]', workflow)
        self.assertNotIn('"HEAD^1", "HEAD"', workflow)

    def test_fatal_run_raises_after_printing_failure(self) -> None:
        """Fatal subprocess failures should abort at the point of failure."""
        failed = self.run_ci.OwnedCommandResult(returncode=7)
        with mock.patch.object(self.run_ci, "_run_owned_command", return_value=failed):
            self.assertFalse(self.run_ci.run(["tool"], "nonfatal", fatal=False))
            with self.assertRaises(SystemExit) as context:
                self.run_ci.run(["tool"], "fatal", fatal=True)
        self.assertEqual(7, context.exception.code)

    def test_run_reports_elapsed_seconds_on_success(self) -> None:
        """Direct command success output should include stable one-decimal elapsed time."""
        completed = subprocess.CompletedProcess(args=["tool"], returncode=0, stdout="", stderr="")

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[10.0, 11.24]):
            with mock.patch.object(self.run_ci, "_run_owned_command", return_value=self.run_ci.OwnedCommandResult(0)):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                    self.assertTrue(self.run_ci.run(["tool"], "timed success"))

        self.assertIn(f"{self.run_ci.PASS} timed success (1.2s)", stdout.getvalue())

    def test_run_reports_elapsed_seconds_on_nonzero_exit(self) -> None:
        """Direct command failures should retain their exit code and include elapsed time."""
        failed = subprocess.CompletedProcess(args=["tool"], returncode=7, stdout="", stderr="")

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[15.0, 16.24]):
            with mock.patch.object(self.run_ci, "_run_owned_command", return_value=self.run_ci.OwnedCommandResult(7)):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                    self.assertFalse(self.run_ci.run(["tool"], "timed failure"))

        self.assertIn(
            f"{self.run_ci.FAIL} timed failure (exit 7) (1.2s)",
            stdout.getvalue(),
        )

    def test_run_captured_returns_elapsed_seconds_on_success(self) -> None:
        """Captured package-validator results should carry elapsed time for ordered replay."""
        completed = subprocess.CompletedProcess(
            args=["tool"],
            returncode=0,
            stdout="validator output\n",
            stderr="",
        )

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[20.0, 21.26]):
            with mock.patch.object(
                self.run_ci,
                "_run_owned_command",
                return_value=self.run_ci.OwnedCommandResult(0, "validator output\n", ""),
            ):
                result = self.run_ci.run_captured(
                    ["tool"],
                    "captured validator",
                )

        self.assertIsInstance(result, self.run_ci.CapturedCommandResult)
        self.assertEqual("captured validator", result.label)
        self.assertTrue(result.ok)
        self.assertEqual(0, result.returncode)
        self.assertAlmostEqual(1.26, result.elapsed_seconds)
        self.assertEqual("validator output\n", result.stdout)
        self.assertEqual("", result.stderr)
        self.assertIsNone(result.timeout_seconds)

    def test_run_captured_preserves_nonzero_result_without_timeout(self) -> None:
        """Captured non-timeout failures should preserve their process result and elapsed time."""
        failed = subprocess.CompletedProcess(
            args=["tool"],
            returncode=9,
            stdout="validator stdout\n",
            stderr="validator stderr\n",
        )

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[25.0, 26.24]):
            with mock.patch.object(
                self.run_ci,
                "_run_owned_command",
                return_value=self.run_ci.OwnedCommandResult(9, "validator stdout\n", "validator stderr\n"),
            ):
                result = self.run_ci.run_captured(["tool"], "captured failure")

        self.assertIsInstance(result, self.run_ci.CapturedCommandResult)
        self.assertEqual("captured failure", result.label)
        self.assertFalse(result.ok)
        self.assertEqual(9, result.returncode)
        self.assertAlmostEqual(1.24, result.elapsed_seconds)
        self.assertEqual("validator stdout\n", result.stdout)
        self.assertEqual("validator stderr\n", result.stderr)
        self.assertIsNone(result.timeout_seconds)

    def test_run_captured_timeout_caches_its_effective_timeout(self) -> None:
        """Captured timeout diagnostics should use the one timeout value enforced by subprocess."""
        timeout = subprocess.TimeoutExpired(
            ["tool"],
            7,
            output="partial stdout\n",
            stderr="partial stderr\n",
        )

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[30.0, 31.24]):
            with mock.patch.object(
                self.run_ci,
                "command_timeout_seconds",
                side_effect=[7, 99],
            ) as command_timeout:
                with mock.patch.object(
                    self.run_ci,
                    "_run_owned_command",
                    return_value=self.run_ci.OwnedCommandResult(
                        124, "partial stdout\n", "partial stderr\n", timed_out=True
                    ),
                ) as run_process:
                    result = self.run_ci.run_captured(["tool"], "captured timeout")

        command_timeout.assert_called_once_with()
        self.assertEqual(7, run_process.call_args.kwargs["timeout_seconds"])
        self.assertIsInstance(result, self.run_ci.CapturedCommandResult)
        self.assertFalse(result.ok)
        self.assertEqual(124, result.returncode)
        self.assertAlmostEqual(1.24, result.elapsed_seconds)
        self.assertEqual(7, result.timeout_seconds)
        self.assertEqual("partial stdout\n", result.stdout)
        self.assertEqual("partial stderr\n", result.stderr)

    def test_run_captured_bounds_large_output_with_marker(self) -> None:
        """Captured command output is bounded and marked when truncated."""
        huge = "x" * (self.run_ci.MAX_CAPTURED_OUTPUT_CHARS + 4096)
        completed = subprocess.CompletedProcess(args=["tool"], returncode=0, stdout=huge, stderr=huge)
        with mock.patch.object(
            self.run_ci,
            "_run_owned_command",
            return_value=self.run_ci.OwnedCommandResult(0, huge, huge),
        ):
            result = self.run_ci.run_captured(["tool"], "large output")
        self.assertLessEqual(len(result.stdout), self.run_ci.MAX_CAPTURED_OUTPUT_CHARS)
        self.assertIn("output truncated at", result.stdout)
        self.assertLessEqual(len(result.stderr), self.run_ci.MAX_CAPTURED_OUTPUT_CHARS)

    def test_restore_fallback_retries_only_restore_state_failures(self) -> None:
        """Restore-state failures trigger exactly one fallback retry."""
        restore_failure = self.run_ci.CapturedCommandResult(
            "build", False, 1, 0.1, "error NETSDK1004: project.assets.json not found", ""
        )
        with mock.patch.object(self.run_ci, "run_captured", return_value=restore_failure):
            with mock.patch.object(self.run_ci, "run", return_value=True) as fallback:
                self.assertTrue(self.run_ci.run_with_restore_fallback(["first"], ["restore"], "build"))
        fallback.assert_called_once_with(["restore"], "build (retry with restore)", fatal=False)

    def test_restore_fallback_does_not_hide_non_restore_failure(self) -> None:
        """Compiler failures are returned without a misleading restore retry."""
        compiler_failure = self.run_ci.CapturedCommandResult(
            "build", False, 1, 0.1, "error CS1002: ; expected", ""
        )
        with mock.patch.object(self.run_ci, "run_captured", return_value=compiler_failure):
            with mock.patch.object(self.run_ci, "run") as fallback:
                self.assertFalse(self.run_ci.run_with_restore_fallback(["first"], ["restore"], "build"))
        fallback.assert_not_called()

    def test_run_parallel_replays_ordered_command_elapsed_time(self) -> None:
        """Parallel validator replay should retain labels, output order, return codes, and elapsed time."""
        captured_results = {
            "first validator": self.run_ci.CapturedCommandResult(
                label="first validator",
                ok=True,
                returncode=0,
                elapsed_seconds=1.2,
                stdout="first output\n",
                stderr="",
            ),
            "second validator": self.run_ci.CapturedCommandResult(
                label="second validator",
                ok=False,
                returncode=9,
                elapsed_seconds=4.6,
                stdout="",
                stderr="second error\n",
            ),
        }

        def fake_run_captured(_cmd: list[str], label: str):
            """Return complete captured command results without starting a subprocess."""
            return captured_results[label]

        with mock.patch.object(self.run_ci, "run_captured", side_effect=fake_run_captured):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
                    results = self.run_ci.run_parallel(
                        [
                            ("first validator", ["first"]),
                            ("second validator", ["second"]),
                        ]
                    )

        rendered_stdout = stdout.getvalue()
        self.assertEqual({"first validator": True, "second validator": False}, results)
        self.assertIn("--- first validator ---", rendered_stdout)
        self.assertIn("first output", rendered_stdout)
        self.assertIn(f"{self.run_ci.PASS} first validator (1.2s)", rendered_stdout)
        self.assertIn("--- second validator ---", rendered_stdout)
        self.assertIn(
            f"{self.run_ci.FAIL} second validator (exit 9) (4.6s)",
            rendered_stdout,
        )
        self.assertLess(rendered_stdout.index("--- first validator ---"), rendered_stdout.index("--- second validator ---"))
        self.assertLess(rendered_stdout.index("first output"), rendered_stdout.index("--- second validator ---"))
        self.assertIn("second error", stderr.getvalue())

    def test_run_parallel_replays_captured_timeout_once_in_declaration_order(self) -> None:
        """Captured timeouts should retain partial output and use one timeout-specific replay diagnostic."""
        captured_results = {
            "first validator": self.run_ci.CapturedCommandResult(
                label="first validator",
                ok=True,
                returncode=0,
                elapsed_seconds=1.2,
                stdout="first output\n",
                stderr="",
            ),
            "timeout validator": self.run_ci.CapturedCommandResult(
                label="timeout validator",
                ok=False,
                returncode=124,
                elapsed_seconds=4.6,
                stdout="partial timeout stdout\n",
                stderr="partial timeout stderr\n",
                timeout_seconds=7,
            ),
        }

        def fake_run_captured(_cmd: list[str], label: str):
            """Return captured results without starting a subprocess."""
            return captured_results[label]

        with mock.patch.object(self.run_ci, "run_captured", side_effect=fake_run_captured):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
                    results = self.run_ci.run_parallel(
                        [
                            ("first validator", ["first"]),
                            ("timeout validator", ["timeout"]),
                        ]
                    )

        rendered_stdout = stdout.getvalue()
        rendered_stderr = stderr.getvalue()
        rendered = rendered_stdout + rendered_stderr
        self.assertEqual({"first validator": True, "timeout validator": False}, results)
        self.assertLess(rendered_stdout.index("--- first validator ---"), rendered_stdout.index("--- timeout validator ---"))
        self.assertLess(rendered_stdout.index("first output"), rendered_stdout.index("--- timeout validator ---"))
        self.assertIn("partial timeout stdout", rendered_stdout)
        self.assertIn("partial timeout stderr", rendered_stderr)
        self.assertIn(
            f"{self.run_ci.FAIL} timeout validator timed out after 7s (4.6s)",
            rendered_stdout,
        )
        self.assertEqual(1, rendered.count(f"{self.run_ci.FAIL} timeout validator"))
        self.assertNotIn(f"{self.run_ci.FAIL} timeout validator (exit 124)", rendered)

    def test_run_timeout_reports_reason_and_elapsed_seconds(self) -> None:
        """Timed-out direct commands should retain their limit and show elapsed time."""
        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[30.0, 31.28]):
            with mock.patch.object(
                self.run_ci,
                "_run_owned_command",
                return_value=self.run_ci.OwnedCommandResult(124, timed_out=True),
            ):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                    self.assertFalse(self.run_ci.run(["tool"], "timeout", timeout_seconds=7))

        rendered = stdout.getvalue()
        self.assertIn(f"{self.run_ci.FAIL} timeout timed out after 7s", rendered)
        self.assertIn("(1.3s)", rendered)

    def test_run_ci_reports_timeout_without_hanging(self) -> None:
        """Subprocess timeouts should fail the command instead of hanging local CI."""
        with mock.patch.dict(os.environ, {"UNITY2FOXGLOVE_CI_TIMEOUT": "1"}):
            with mock.patch.object(
                self.run_ci,
                "_run_owned_command",
                return_value=self.run_ci.OwnedCommandResult(124, timed_out=True),
            ):
                self.assertFalse(self.run_ci.run(["tool"], "timeout", fatal=False))
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.run(["tool"], "fatal-timeout", fatal=True)
        self.assertEqual(124, context.exception.code)

    def test_run_can_disable_wall_clock_timeout(self) -> None:
        """Finite gates should be allowed to finish without a machine-specific deadline."""
        completed = subprocess.CompletedProcess(args=["tool"], returncode=0, stdout="", stderr="")

        with mock.patch.object(
            self.run_ci,
            "_run_owned_command",
            return_value=self.run_ci.OwnedCommandResult(0),
        ) as run_process:
            self.assertTrue(self.run_ci.run(["tool"], "finite gate", disable_timeout=True))

        self.assertIsNone(run_process.call_args.kwargs["timeout_seconds"])

    def test_run_timeout_terminates_descendant_process(self) -> None:
        """The uncaptured runner must terminate a child that outlives its parent."""
        self._assert_runner_timeout_terminates_descendant(captured=False)

    def test_run_captured_timeout_terminates_descendant_process(self) -> None:
        """The captured runner must terminate a child that outlives its parent."""
        self._assert_runner_timeout_terminates_descendant(captured=True)

    def test_owned_command_keeps_success_after_residual_tree_is_cleaned(self) -> None:
        """A transient descendant must not turn a successful command into exit 125 after cleanup."""
        process = mock.Mock(returncode=0)
        process.communicate.return_value = ("output\n", "")
        tree = mock.Mock(process=process)
        tree.terminate.return_value = []

        with mock.patch.object(self.run_ci, "start_owned_process", return_value=tree):
            with mock.patch.object(self.run_ci, "await_tree_quiescence", return_value=[424242]):
                result = self.run_ci._run_owned_command(
                    ["tool"], capture_output=True, timeout_seconds=10
                )

        self.assertEqual(0, result.returncode)
        self.assertEqual("output\n", result.stdout)
        self.assertEqual((), result.residual_pids)
        tree.terminate.assert_called_once_with()

    def test_owned_command_does_not_promote_unknown_returncode_to_success(self) -> None:
        """An unavailable process return code must not be rewritten as zero."""
        process = mock.Mock(returncode=None)
        process.communicate.return_value = ("output\n", "")
        tree = mock.Mock(process=process)
        tree.terminate.return_value = []

        with mock.patch.object(self.run_ci, "start_owned_process", return_value=tree):
            with mock.patch.object(self.run_ci, "await_tree_quiescence", return_value=[]):
                result = self.run_ci._run_owned_command(
                    ["tool"], capture_output=True, timeout_seconds=10
                )

        self.assertIsNone(result.returncode)

    def test_owned_command_fails_closed_when_cleanup_leaves_residual_tree(self) -> None:
        """A descendant that survives termination must retain the fail-closed result."""
        process = mock.Mock(returncode=0)
        process.communicate.return_value = ("output\n", "")
        tree = mock.Mock(process=process)
        tree.terminate.return_value = [424242]

        with mock.patch.object(self.run_ci, "start_owned_process", return_value=tree):
            with mock.patch.object(self.run_ci, "await_tree_quiescence", return_value=[424242]):
                result = self.run_ci._run_owned_command(
                    ["tool"], capture_output=True, timeout_seconds=10
                )

        self.assertEqual(125, result.returncode)
        self.assertEqual((424242,), result.residual_pids)

    def _assert_runner_timeout_terminates_descendant(self, *, captured: bool) -> None:
        """Verify timeout cleanup reaches descendants for both runner surfaces."""
        with tempfile.TemporaryDirectory() as temp:
            child_pid_path = Path(temp) / "child.pid"
            parent_code = (
                "import pathlib, subprocess, sys, time; "
                f"child=subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)']); "
                f"pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid), encoding='utf-8'); "
                "time.sleep(60)"
            )
            command = [sys.executable, "-c", parent_code]
            if captured:
                with mock.patch.object(self.run_ci, "command_timeout_seconds", return_value=5):
                    result = self.run_ci.run_captured(command, "captured process tree timeout")
                self.assertFalse(result.ok)
                self.assertEqual(124, result.returncode)
            else:
                result = self.run_ci.run(command, "process tree timeout", timeout_seconds=5)
                self.assertFalse(result)

            deadline = time.monotonic() + 4
            child_pid = None
            while time.monotonic() < deadline:
                if child_pid_path.is_file():
                    child_pid = int(child_pid_path.read_text(encoding="utf-8"))
                    if not self._pid_is_running(child_pid):
                        break
                time.sleep(0.05)
            self.assertIsNotNone(child_pid)
            self.assertFalse(self._pid_is_running(child_pid), f"descendant remained alive: {child_pid}")

    @staticmethod
    def _pid_is_running(pid: int) -> bool:
        """Return whether the operating-system process identifier is alive."""
        try:
            os.kill(pid, 0)
        except (OSError, ProcessLookupError):
            return False
        return True

    def test_default_ci_builds_independent_subcommand_jobs(self) -> None:
        """Default local CI should enqueue every dotnet lane as a self-subcommand."""
        args = types.SimpleNamespace(skip_analyzer=False)

        jobs = self.run_ci.build_default_ci_jobs(args)

        self.assertEqual(
            [
                "analyzer",
                "dotnet-runtime",
                "mediafoundation-native-smoke",
                "xunit",
                "xunit-adapter",
                "xunit-native",
                "xunit-manager-boundary",
                "ros2bridge-xunit",
                "performance-regression",
                "foxrun-publish-panel",
                "phase179-ros2-regression",
                "phase181-ros2-regression",
                "phase184-acceptance-tooling",
                "phase186-bridge-tooling",
                "mcap-conformance",
                "packages",
                "boundary",
            ],
            [job.name for job in jobs],
        )
        for job in jobs:
            self.assertEqual(
                [sys.executable, str(RUN_CI_PATH.resolve()), "--only", job.name],
                job.command,
        )
        self.assertEqual(
            {
                "phase184-acceptance-tooling",
            },
            {job.name for job in jobs if job.disable_timeout},
        )

    def test_phase184_acceptance_regression_module_constants_are_exact(self) -> None:
        """The tooling lane must name only the six maintained pure unittest modules."""
        expected = {
            name: module
            for name, module, _label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
        }

        self.assertEqual(
            expected,
            {name: getattr(self.run_ci, name, None) for name in expected},
        )

    def test_maintained_ci_dispatches_phase185_and_phase186_manual_regressions(self) -> None:
        """Maintained CI must execute the standalone Phase185 and Phase186-H suites."""
        self.assertIn(
            "Scripts.smoke.foxrun.regression_checks.test_phase185_foxrun_messagepack_probe",
            self.run_ci.PACKAGE_LANE_REGRESSION_MODULES,
        )
        self.assertIn(
            "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_manual",
            self.run_ci.PACKAGE_LANE_REGRESSION_MODULES,
        )

    def test_unity_build_requires_succeeded_build_result(self) -> None:
        """Unity build helper must gate success on BuildReport.summary.result."""
        source = (ROOT / "Unity2Foxglove" / "Assets" / "Editor" / "FoxgloveBuild.cs").read_text(encoding="utf-8")
        self.assertIn("report.summary.result == BuildResult.Succeeded", source)
        self.assertIn("report.summary.totalErrors == 0", source)

    def test_mcap_preflight_binds_latest_completion_and_clears_stale_evidence(self) -> None:
        """Replay preflight must not overwrite edits or expose evidence for a failed path."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "McapReplayPreflightDrawer.cs").read_text(encoding="utf-8")
        self.assertIn("_pendingLatestReplayPathSnapshot", source)
        self.assertIn("ClearCurrentEvidence", source)

    def test_latest_recording_scan_is_streaming_and_cancellable(self) -> None:
        """Latest-recording discovery must avoid eager path-array materialization."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "McapReplayPreflightDrawer.cs").read_text(encoding="utf-8")
        self.assertIn("Directory.EnumerateFiles", source)
        self.assertNotIn("Directory.GetFiles(recordingsDir, \"*.mcap\", SearchOption.AllDirectories)", source)

    def test_mcap_preflight_rejects_malformed_manifest_hash_identity(self) -> None:
        """Replay identity must not classify arbitrary text as a manifest hash."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "McapReplayPreflightDrawer.cs").read_text(encoding="utf-8")
        self.assertIn("IsValidFoxRunHash", source)
        self.assertIn("if (!IsValidFoxRunHash(hash))", source)
        self.assertIn("return string.Empty;", source)

    def test_encoding_inspectors_preserve_unsupported_serialized_values(self) -> None:
        """Enum inspectors must not rewrite forward-compatible raw values on repaint."""
        foxrun = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Shared" / "FoxRunEncodingEditorLabels.cs").read_text(encoding="utf-8")
        publisher = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Shared" / "PublisherEncodingEditorLabels.cs").read_text(encoding="utf-8")
        self.assertIn("EditorGUI.BeginChangeCheck", foxrun)
        self.assertIn("EditorGUI.EndChangeCheck", foxrun)
        self.assertIn("EditorGUI.BeginChangeCheck", publisher)

    def test_phase134_read_repo_text_rejects_missing_source(self) -> None:
        """Provider validation must fail closed when a required source file is absent."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase134_1Validation.cs").read_text(encoding="utf-8")
        self.assertIn("if (!File.Exists(path))", source)
        self.assertIn("throw new FileNotFoundException", source)

    def test_phase13_try_read_repo_text_fails_closed_on_missing_source(self) -> None:
        """Phase 13 validation must not turn missing source files into silent early returns."""
        source = read_runtime_validation_source(
            ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase13Validation.cs"
        )
        self.assertIn("if (!File.Exists(path))", source)
        self.assertIn("throw new FileNotFoundException", source)

    def test_phase13_recording_fixture_owns_and_cleans_temp_path(self) -> None:
        """Recording validation must retain the temp-path identity through cleanup."""
        source = read_runtime_validation_source(
            ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase13Validation.cs"
        )
        self.assertIn("var recordingPath = Path.GetTempFileName()", source)
        self.assertIn("File.Delete(recordingPath)", source)

    def test_editor_security_uses_canonical_loopback_host_policy(self) -> None:
        """Root-CA exposure warnings must normalize IPv4/IPv6 loopback forms."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "FoxgloveManagerEditor.Security.cs").read_text(encoding="utf-8")
        self.assertIn("FoxgloveManager.IsLoopbackHost(distributorHost)", source)

    def test_editor_path_picker_tolerates_malformed_existing_value(self) -> None:
        """Passive inspector rendering must survive malformed serialized paths."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "FoxgloveManagerEditor.Helpers.cs").read_text(encoding="utf-8")
        self.assertIn("catch (Exception)", source)
        self.assertIn("return GetDefaultDir();", source)

    def test_phase140_queue_stress_bounds_thread_joins(self) -> None:
        """Concurrent queue validation must not hang indefinitely on a stalled worker."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase140_7Validation.cs").read_text(encoding="utf-8")
        self.assertIn("producer.Join(TimeSpan.FromSeconds", source)
        self.assertIn("consumer.Join(TimeSpan.FromSeconds", source)

    def test_phase162_bounded_runner_does_not_turn_timeout_into_success(self) -> None:
        """A timed-out ROS2 helper must return a nonzero status even if the child lingers."""
        facade = ROOT / "Scripts" / "smoke" / "ros2" / "phase162_lyrical_zenoh_player_smoke.py"
        package = facade.with_suffix("")
        sources = [facade]
        if package.is_dir():
            sources.extend(sorted(package.glob("*.py")))
        source = "\n".join(path.read_text(encoding="utf-8") for path in sources)
        self.assertIn("if process.poll() is None", source)
        self.assertIn("returncode = 124", source)

    def test_phase184_acceptance_regressions_have_a_truthful_dedicated_lane(self) -> None:
        """The selector must execute exactly six pure unittest suites in locked order."""
        with mock.patch.object(self.run_ci, "run", return_value=True) as run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase184-acceptance-tooling"],
            ):
                self.assertEqual(0, self.run_ci.main())

        observed = [(call.args[0], call.args[1]) for call in run.call_args_list]
        self.assertEqual(
            [
                ([sys.executable, "-m", "unittest", module], label)
                for _name, module, label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
            ],
            observed,
        )
        self.assertTrue(
            all(
                call.kwargs.get("disable_timeout", False) is False
                for call in run.call_args_list
            )
        )

        phase184_modules = [
            module
            for _name, module, _label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
        ]
        self.assertTrue(
            all(
                command[:3] == [sys.executable, "-m", "unittest"]
                and len(command) == 4
                for command, _label in observed
            )
        )
        self.assertTrue(
            all(
                module.startswith("Scripts.smoke.foxrun.regression_checks.test_")
                for module in phase184_modules
            )
        )
        self.assertTrue(
            {
                "Scripts.smoke.foxrun.phase184_profile_acceptance",
                "Scripts.smoke.foxrun.phase184_foxglove_cli_install",
                "Scripts.smoke.foxrun.phase184_foxglove_desktop_live_acceptance",
            }.isdisjoint(phase184_modules)
        )

        with mock.patch.object(self.run_ci, "run", return_value=True) as phase181_run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase181-ros2-regression"],
            ):
                self.assertEqual(0, self.run_ci.main())

        phase181_commands = {
            tuple(call.args[0])
            for call in phase181_run.call_args_list
        }
        self.assertTrue(
            all(
                (sys.executable, "-m", "unittest", module)
                not in phase181_commands
                for module in phase184_modules
            )
        )

    def test_phase184_acceptance_tooling_lane_propagates_failure(self) -> None:
        """A failing Phase184 tooling suite must fail its dedicated selector."""

        with mock.patch.object(
            self.run_ci,
            "run",
            side_effect=(False, True, True, True, True, True),
        ):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase184-acceptance-tooling"],
            ):
                self.assertEqual(1, self.run_ci.main())

    def test_only_help_lists_phase184_acceptance_tooling_selector(self) -> None:
        """CLI help must name the dedicated Phase184 tooling lane honestly."""

        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        self.assertIn("phase184-acceptance-tooling", stdout.getvalue())

    def test_phase184_acceptance_tooling_command_is_exact(self) -> None:
        """The dedicated lane must remain a tooling test, not claim runtime execution."""

        jobs = self.run_ci.build_default_ci_jobs(
            types.SimpleNamespace(skip_analyzer=False)
        )
        job = next(
            candidate
            for candidate in jobs
            if candidate.name == "phase184-acceptance-tooling"
        )
        self.assertEqual(
            (
                sys.executable,
                str(RUN_CI_PATH.resolve()),
                "--only",
                "phase184-acceptance-tooling",
            ),
            tuple(job.command),
        )

    def test_phase186_bridge_tooling_selector_is_default_but_live_is_not(self) -> None:
        """Ordinary CI runs static tooling; provisioned live work stays explicit."""

        jobs = self.run_ci.build_default_ci_jobs(
            types.SimpleNamespace(skip_analyzer=False)
        )
        names = [job.name for job in jobs]
        self.assertIn("phase186-bridge-tooling", names)
        self.assertNotIn("phase186-bridge-windows-live", names)
        tooling = next(job for job in jobs if job.name == "phase186-bridge-tooling")
        self.assertFalse(tooling.disable_timeout)

    def test_phase186_bridge_tooling_selector_runs_exact_static_suites_and_matrix(self) -> None:
        """The tooling selector must never launch Unity, a sidecar, or a ROS peer."""

        with mock.patch.object(self.run_ci, "run", return_value=True) as run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase186-bridge-tooling"],
            ):
                self.assertEqual(0, self.run_ci.main())

        commands = [call.args[0] for call in run.call_args_list]
        self.assertEqual(
            [
                [sys.executable, "-m", "unittest", module]
                for module in self.PHASE186_BRIDGE_TOOLING_SUITES
            ]
            + [
                [sys.executable, "Scripts/package/validate_phase186_package_matrix.py"],
                [sys.executable, "-m", "Scripts.smoke.foxrun.phase186_provenance"],
            ],
            commands,
        )
        flattened = " ".join(part for command in commands for part in command)
        self.assertNotIn(
            [
                sys.executable,
                "-m",
                "Scripts.smoke.foxrun.phase186_bridge_certification",
            ],
            commands,
        )
        self.assertNotIn("phase186_bridge_acceptance --case", flattened)

        provenance_call = next(
            call
            for call in run.call_args_list
            if call.args[0][-1] == "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance"
        )
        self.assertEqual(self.run_ci.job_timeout_seconds(), provenance_call.kwargs["timeout_seconds"])
        for call in run.call_args_list:
            command = call.args[0]
            if command[-1] not in self.PHASE186_BRIDGE_TOOLING_SUITES or command[-1] == "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance":
                continue
            self.assertIsNot(True, call.kwargs.get("disable_timeout", False))

    def test_phase186_bridge_windows_live_selector_runs_exact_certification(self) -> None:
        """The live selector must bind certification identity to the current SHA."""

        head = "a" * 40
        with mock.patch.object(self.run_ci, "current_git_head", return_value=head):
            with mock.patch.object(self.run_ci, "run", return_value=True) as run:
                with mock.patch.object(
                    sys,
                    "argv",
                    ["run_ci.py", "--only", "phase186-bridge-windows-live"],
                ):
                    self.assertEqual(0, self.run_ci.main())

        self.assertEqual(1, run.call_count)
        command = run.call_args.args[0]
        self.assertEqual(
            [
                sys.executable,
                "-m",
                "Scripts.smoke.foxrun.phase186_bridge_certification",
                "--expected-head",
                head,
                "--output-root",
                "build/phase186/windows-live",
                "--run-id",
                self.run_ci.phase186_certification_run_id(head),
            ],
            command,
        )
        self.assertEqual(self.run_ci.job_timeout_seconds(), run.call_args.kwargs["timeout_seconds"])

    def test_phase186_bridge_windows_live_not_run_is_not_promoted_to_pass(self) -> None:
        """Any nonzero certification result, including NOT RUN, fails the selector."""

        with mock.patch.object(self.run_ci, "current_git_head", return_value="b" * 40):
            with mock.patch.object(self.run_ci, "run", return_value=False):
                with mock.patch.object(
                    sys,
                    "argv",
                    ["run_ci.py", "--only", "phase186-bridge-windows-live"],
                ):
                    self.assertEqual(1, self.run_ci.main())

    def test_phase186_selectors_are_named_in_help(self) -> None:
        """Both honest Phase186 lanes must be discoverable without reading the plan."""

        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        rendered = stdout.getvalue()
        self.assertIn("phase186-bridge-tooling", rendered)
        self.assertIn("phase186-bridge-windows-live", rendered)

    def test_phase186_workflows_keep_tooling_and_provisioned_live_separate(self) -> None:
        """Hosted CI runs tooling while live certification requires a labeled owner."""

        dotnet_workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn(
            "run_ci.py --only phase186-bridge-tooling",
            dotnet_workflow,
        )
        live_workflow = PHASE186_WINDOWS_LIVE_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", live_workflow)
        self.assertIn("unity2foxglove-phase186-live", live_workflow)
        self.assertIn("run_ci.py --only phase186-bridge-windows-live", live_workflow)
        self.assertIn("build/phase186/windows-live", live_workflow)
        self.assertNotRegex(live_workflow, r"(?m)^\s+push:")
        self.assertNotRegex(live_workflow, r"(?m)^\s+pull_request:")

    def test_only_help_lists_dotnet_lane_selectors(self) -> None:
        """CLI help should expose each direct dotnet lane selector."""
        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        for selector in ("dotnet-runtime", "xunit", "xunit-adapter", "xunit-native", "xunit-manager-boundary"):
            help_pattern = re.escape(selector).replace(r"\-", r"-\s*")
            self.assertRegex(stdout.getvalue(), help_pattern)

    def test_unknown_only_selector_is_a_usage_error(self) -> None:
        """A misspelled lane must never become a zero-work success."""
        with mock.patch.object(
            sys,
            "argv",
            ["run_ci.py", "--only", "pacakges"],
        ), mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
            with self.assertRaises(SystemExit) as context:
                self.run_ci.main()

        self.assertEqual(2, context.exception.code)
        self.assertIn("invalid choice", stderr.getvalue())

    def test_direct_analyzer_skip_is_a_usage_error(self) -> None:
        """The analyzer-only selector cannot claim success when its only lane is skipped."""
        with mock.patch.object(
            sys,
            "argv",
            ["run_ci.py", "--only", "analyzer", "--skip-analyzer"],
        ), mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
            with self.assertRaises(SystemExit) as context:
                self.run_ci.main()

        self.assertEqual(2, context.exception.code)
        self.assertIn("--skip-analyzer cannot be combined with --only analyzer", stderr.getvalue())

    def test_foxrun_command_rejects_duplicate_next_revision_options(self) -> None:
        """The FoxRun command must reject every duplicate --next-revision occurrence."""
        source = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Editor"
            / "Native"
            / "FoxRun"
            / "FoxRunRos2InterfacePackageCommand.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("nextRevisionOptionCount", source)
        self.assertIn("nextRevisionOptionCount > 1", source)

    def test_foxrun_command_has_no_exit_code_local_shadowing(self) -> None:
        """The batch argument error path must compile without local-name shadowing."""
        source = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Editor"
            / "Native"
            / "FoxRun"
            / "FoxRunRos2InterfacePackageCommand.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("var argumentErrorCode = ReportArgumentError", source)
        self.assertNotIn("var exitCode = ReportArgumentError", source)

    def test_all_ros2forunity_inspectors_handle_external_output_paths(self) -> None:
        """Every maintained distro inspector must report external outputs without raising."""
        for distro in ("humble", "jazzy", "lyrical"):
            source = (
                ROOT
                / "Scripts"
                / "ros2forunity"
                / "windows"
                / distro
                / "inspect_r2fu_runtime_artifact.py"
            ).read_text(encoding="utf-8")
            self.assertIn("display_output", source)
            self.assertIn("except ValueError", source)

    def test_empty_ci_result_summary_is_non_pass(self) -> None:
        """An aggregate with no executed lanes must fail instead of vacuously passing."""
        with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
            result = self.run_ci.report_ci_job_results({})

        self.assertEqual(1, result)
        self.assertIn("No CI checks were executed.", stdout.getvalue())

    def test_default_analyzer_skip_exposes_machine_readable_lane(self) -> None:
        """A permitted partial default run must identify its skipped analyzer lane explicitly."""
        with mock.patch.object(self.run_ci, "run_ci_jobs", return_value={"other": True}):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--skip-analyzer"],
            ), mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                result = self.run_ci.main()

        rendered = stdout.getvalue()
        self.assertEqual(0, result)
        self.assertIn("[SKIP] analyzer", rendered)
        self.assertIn("SKIPPED_LANES=analyzer", rendered)
        self.assertIn("All executed CI checks passed.", rendered)
        self.assertNotIn("All CI checks passed.", rendered)

    def test_default_ci_marks_resource_heavy_lanes_exclusive(self) -> None:
        """Resource-heavy lanes serialize so concurrent jobs cannot exhaust the host."""
        jobs = self.run_ci.build_default_ci_jobs(types.SimpleNamespace(skip_analyzer=False))

        self.assertEqual("dotnet", self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP)
        self.assertEqual(
            {
                "analyzer": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "dotnet-runtime": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "mediafoundation-native-smoke": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-adapter": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-native": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-manager-boundary": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "ros2bridge-xunit": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "performance-regression": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "foxrun-publish-panel": None,
                "phase179-ros2-regression": None,
                "phase181-ros2-regression": None,
                 "phase184-acceptance-tooling": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                 "phase186-bridge-tooling": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                 "mcap-conformance": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                 "packages": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "boundary": None,
            },
            {job.name: job.exclusive_group for job in jobs},
        )

    def test_main_dispatches_dotnet_parent_through_flat_parallel_jobs(self) -> None:
        """The dotnet parent selection should use the top-level lane scheduler."""
        observed: dict[str, object] = {}

        def fake_run_ci_jobs(jobs, max_workers):
            """Capture the flattened lane jobs without executing subprocesses."""
            observed["names"] = [job.name for job in jobs]
            observed["max_workers"] = max_workers
            return {job.name: True for job in jobs}

        with mock.patch.object(self.run_ci, "run_ci_jobs", side_effect=fake_run_ci_jobs):
            with mock.patch.object(self.run_ci, "restore_with_ignoring_failed_sources", return_value=True):
                with mock.patch.object(self.run_ci, "run_with_restore_fallback", return_value=True):
                    with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "dotnet", "--jobs", "2"]):
                        self.assertEqual(0, self.run_ci.main())

        self.assertEqual(
            [
                "dotnet-runtime",
                "mediafoundation-native-smoke",
                "xunit",
                "xunit-adapter",
                "xunit-native",
                "xunit-manager-boundary",
                "ros2bridge-xunit",
                "performance-regression",
            ],
            observed.get("names"),
        )
        self.assertEqual(2, observed.get("max_workers"))

    def _flat_dotnet_lane_cases(self):
        """Return the exact restore and command contracts for every flat dotnet lane."""
        return [
            (
                "dotnet-runtime",
                self.run_ci.RUNTIME_TESTS_PROJ,
                self.run_ci.RUNTIME_TEST_PROPS,
                "Restore runtime test project",
                [
                    "dotnet",
                    "run",
                    "--no-restore",
                    "--project",
                    self.run_ci.RUNTIME_TESTS_PROJ,
                    *self.run_ci.RUNTIME_TEST_PROPS,
                ],
                "Dotnet validation suite (default CI)",
                None,
                None,
            ),
            (
                "xunit",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_TEST_PROPS,
                "Restore xUnit unit test project",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_TEST_RESULTS_DIR),
                ],
                "xUnit unit tests",
                "unit-tests.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit",
            ),
            (
                "xunit-adapter",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_ADAPTER_TEST_PROPS,
                "Restore xUnit optional ROS2 adapter lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_ADAPTER_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-adapter.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_ADAPTER_TEST_RESULTS_DIR),
                ],
                "xUnit optional ROS2 adapter unit tests",
                "unit-tests-adapter.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit-adapter",
            ),
            (
                "xunit-native",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_NATIVE_TEST_PROPS,
                "Restore xUnit Native ROS2 compilation lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_NATIVE_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-native.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_NATIVE_TEST_RESULTS_DIR),
                ],
                "xUnit Native ROS2 compilation unit tests",
                "unit-tests-native.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit-native",
            ),
            (
                "xunit-manager-boundary",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_PROPS,
                "Restore Manager production-boundary xUnit lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-manager-boundary.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_RESULTS_DIR),
                ],
                "xUnit Manager production-boundary tests",
                "unit-tests-manager-boundary.trx",
                self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_RESULTS_DIR,
            ),
        ]

    def test_flat_dotnet_selectors_restore_and_run_isolated_lanes(self) -> None:
        """Each flat selector should restore its isolated lane then run it once without restore."""
        cases = self._flat_dotnet_lane_cases()
        xunit_artifacts: list[tuple[str, str]] = []
        lane_msbuild_roots: list[tuple[str, ...]] = []

        for (
            selector,
            project,
            props,
            restore_label,
            command,
            run_label,
            trx_name,
            results_dir,
        ) in cases:
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=True,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run", return_value=True) as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(0, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_called_once_with(command, run_label)
                fallback.assert_not_called()
                self.assertIn("--no-restore", command)
                isolated_roots = [
                    prop.split("=", 1)[1]
                    for prop in props
                    if prop.startswith(
                        (
                            "-p:BaseOutputPath=",
                            "-p:BaseIntermediateOutputPath=",
                            "-p:MSBuildProjectExtensionsPath=",
                            "-p:RestoreOutputPath=",
                        )
                    )
                ]
                self.assertEqual(4, len(isolated_roots))
                isolated_root = str(self.run_ci.ISOLATED_DOTNET_ROOT.resolve()).replace("\\", "/") + "/"
                self.assertTrue(all(root.startswith(isolated_root) for root in isolated_roots))
                lane_msbuild_roots.append(
                    tuple(root.replace("\\", "/").rstrip("/") for root in isolated_roots)
                )

                if selector == "xunit-adapter":
                    self.assertIn("-p:IncludeRos2ForUnityAdapter=true", props)
                if selector == "xunit-native":
                    self.assertIn("-p:IncludeRos2ForUnityNative=true", props)
                if selector == "xunit-manager-boundary":
                    self.assertIn("-p:IncludeManagerProductionBoundary=true", props)
                if trx_name is not None:
                    self.assertIsNotNone(results_dir)
                    self.assertIn(f"trx;LogFileName={trx_name}", command)
                    self.assertIn(str(results_dir), command)
                    xunit_artifacts.append((trx_name, str(results_dir)))

        self.assertEqual(4, len({trx_name for trx_name, _ in xunit_artifacts}))
        self.assertEqual(4, len({results_dir for _, results_dir in xunit_artifacts}))
        self.assertEqual(5, len(lane_msbuild_roots))
        self.assertEqual(5, len(set(lane_msbuild_roots)))

    def test_flat_dotnet_lane_failure_does_not_retry_with_restore(self) -> None:
        """Every failed lane should report failure after one no-restore command."""
        for (
            selector,
            project,
            props,
            restore_label,
            command,
            run_label,
            _trx_name,
            _results_dir,
        ) in self._flat_dotnet_lane_cases():
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=True,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run", return_value=False) as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(1, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_called_once_with(command, run_label)
                self.assertIn("--no-restore", command)
                fallback.assert_not_called()

    def test_flat_dotnet_lane_restore_failure_skips_the_no_restore_command(self) -> None:
        """Every failed explicit restore should fail its lane without running its test command."""
        for (
            selector,
            project,
            props,
            restore_label,
            _command,
            _run_label,
            _trx_name,
            _results_dir,
        ) in self._flat_dotnet_lane_cases():
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=False,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run") as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(1, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_not_called()
                fallback.assert_not_called()

    def test_run_ci_jobs_limits_active_jobs_until_release(self) -> None:
        """The real top-level scheduler should not start a third job before release."""
        jobs = [
            self.run_ci.CiJob("first", ["first"]),
            self.run_ci.CiJob("second", ["second"]),
            self.run_ci.CiJob("third", ["third"]),
        ]
        release = threading.Event()
        two_jobs_started = threading.Event()
        third_job_started = threading.Event()
        state_lock = threading.Lock()
        state = {"active": 0, "max_active": 0, "started_before_release": []}
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Hold worker slots until the test releases the synthetic jobs."""
            with state_lock:
                state["active"] += 1
                state["max_active"] = max(state["max_active"], state["active"])
                if not release.is_set():
                    state["started_before_release"].append(job.name)
                    if len(state["started_before_release"]) == 2:
                        two_jobs_started.set()
                    elif len(state["started_before_release"]) == 3:
                        third_job_started.set()
            try:
                release.wait()
                return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")
            finally:
                with state_lock:
                    state["active"] -= 1

        def run_scheduler() -> None:
            """Run the real scheduler without blocking the test thread."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(two_jobs_started.wait(timeout=2), "two workers did not start")
                        self.assertFalse(
                            third_job_started.wait(timeout=0.5),
                            "a third job started before a worker slot was released",
                        )
                        with state_lock:
                            self.assertEqual(2, len(state["started_before_release"]))
                            self.assertLessEqual(state["max_active"], 2)
                    finally:
                        release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after release")
        self.assertEqual([], worker_errors)
        self.assertEqual({job.name: True for job in jobs}, outcome["results"])

    def test_run_ci_jobs_admits_compatible_work_while_dotnet_group_is_active(self) -> None:
        """A blocked dotnet job should leave a worker free for an unrelated pending job."""
        jobs = [
            types.SimpleNamespace(
                name="dotnet-a",
                command=["dotnet-a"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="dotnet-b",
                command=["dotnet-b"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="other",
                command=["other"],
                disable_timeout=False,
                exclusive_group=None,
            ),
        ]
        dotnet_a_started = threading.Event()
        dotnet_a_release = threading.Event()
        dotnet_b_started = threading.Event()
        other_finished = threading.Event()
        started_names: list[str] = []
        state_lock = threading.Lock()
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Control only worker completion while retaining the real admission scheduler."""
            with state_lock:
                started_names.append(job.name)
            if job.name == "dotnet-a":
                dotnet_a_started.set()
                dotnet_a_release.wait()
            elif job.name == "dotnet-b":
                dotnet_b_started.set()
            elif job.name == "other":
                other_finished.set()
            return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")

        def run_scheduler() -> None:
            """Run the real scheduler off the test thread so it can await controlled workers."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(dotnet_a_started.wait(timeout=2), "dotnet-a did not start")
                        self.assertTrue(other_finished.wait(timeout=2), "compatible job did not start")
                        self.assertFalse(
                            dotnet_b_started.wait(timeout=0.5),
                            "second dotnet job started before the active group released",
                        )
                        with state_lock:
                            self.assertEqual({"dotnet-a", "other"}, set(started_names))
                        dotnet_a_release.set()
                        self.assertTrue(dotnet_b_started.wait(timeout=2), "dotnet-b did not start after release")
                    finally:
                        dotnet_a_release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after release")
        self.assertEqual([], worker_errors)
        self.assertEqual(
            [("dotnet-a", True), ("dotnet-b", True), ("other", True)],
            list(outcome["results"].items()),
        )

    def test_run_ci_jobs_releases_dotnet_group_after_normal_failure(self) -> None:
        """A normal failed grouped job should release its group for the next grouped job."""
        jobs = [
            types.SimpleNamespace(
                name="dotnet-a",
                command=["dotnet-a"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="dotnet-b",
                command=["dotnet-b"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
        ]
        dotnet_a_started = threading.Event()
        dotnet_a_release = threading.Event()
        dotnet_b_started = threading.Event()
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Return an ordinary failed result for the group owner after controlled release."""
            if job.name == "dotnet-a":
                dotnet_a_started.set()
                dotnet_a_release.wait()
                log_path = log_dir / f"{job.name}.log"
                log_path.write_text("synthetic normal failure\n", encoding="utf-8")
                return self.run_ci.CiJobResult(
                    job.name,
                    False,
                    1,
                    0.0,
                    log_path,
                )
            dotnet_b_started.set()
            return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")

        def run_scheduler() -> None:
            """Run the real scheduler off the test thread so it can await controlled workers."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(dotnet_a_started.wait(timeout=2), "dotnet-a did not start")
                        self.assertFalse(
                            dotnet_b_started.wait(timeout=0.5),
                            "dotnet-b started before the failed owner released its group",
                        )
                        dotnet_a_release.set()
                        self.assertTrue(dotnet_b_started.wait(timeout=2), "dotnet-b did not start after failure")
                    finally:
                        dotnet_a_release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after failure")
        self.assertEqual([], worker_errors)
        self.assertEqual(
            [("dotnet-a", False), ("dotnet-b", True)],
            list(outcome["results"].items()),
        )

    def test_mcap_conformance_disables_wall_clock_timeout(self) -> None:
        """The external differential gate should not assume how fast the host machine is."""
        observed: dict[str, object] = {}

        def fake_run(cmd, label, **kwargs):
            """Capture the dedicated conformance command without executing it."""
            observed["cmd"] = cmd
            observed["label"] = label
            observed["timeout_seconds"] = kwargs.get("timeout_seconds")
            return True

        with mock.patch.object(self.run_ci, "run", side_effect=fake_run):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "mcap-conformance"]):
                self.assertEqual(0, self.run_ci.main())

        self.assertIn("run_phase121_conformance.py", " ".join(observed["cmd"]))
        self.assertEqual("Official MCAP differential conformance", observed["label"])
        self.assertEqual(self.run_ci.job_timeout_seconds(), observed["timeout_seconds"])

    def test_parallel_mcap_job_disables_wall_clock_timeout(self) -> None:
        """The parent CI process must not reintroduce a deadline around the MCAP child."""
        command = [
            sys.executable,
            "-c",
            "import sys; sys.stdout.write('x' * 1048576); sys.stdout.flush()",
        ]
        with tempfile.TemporaryDirectory() as temp:
            job = self.run_ci.CiJob("mcap-conformance", command, disable_timeout=True)
            result = self.run_ci._run_ci_job(job, Path(temp))

        self.assertTrue(result.ok)
        self.assertEqual(0, result.returncode)

    def test_parallel_job_drains_noisy_child_before_wait_deadlock(self) -> None:
        """A noisy child must complete instead of filling the redirected pipe."""
        command = [
            sys.executable,
            "-c",
            "import sys; sys.stdout.write('x' * 1048576); sys.stdout.flush()",
        ]
        with tempfile.TemporaryDirectory() as temp:
            job = self.run_ci.CiJob("pipe-drain", command)
            with mock.patch.object(self.run_ci, "job_timeout_seconds", return_value=20):
                result = self.run_ci._run_ci_job(job, Path(temp))

        self.assertTrue(result.ok)
        self.assertEqual(0, result.returncode)
        self.assertLess(result.elapsed_seconds, 10)

    def test_main_dispatches_default_ci_through_parallel_jobs(self) -> None:
        """Without --only, CI should use the parallel job runner and aggregate job results."""
        observed: dict[str, object] = {}

        def fake_run_ci_jobs(jobs, max_workers):
            """Capture default CI jobs without executing subprocesses."""
            observed["names"] = [job.name for job in jobs]
            observed["max_workers"] = max_workers
            return {job.name: True for job in jobs}

        with mock.patch.object(self.run_ci, "run_ci_jobs", side_effect=fake_run_ci_jobs):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--jobs", "2"]):
                self.assertEqual(0, self.run_ci.main())

        self.assertEqual(
            [
                "analyzer",
                "dotnet-runtime",
                "mediafoundation-native-smoke",
                "xunit",
                "xunit-adapter",
                "xunit-native",
                "xunit-manager-boundary",
                "ros2bridge-xunit",
                "performance-regression",
                "foxrun-publish-panel",
                "phase179-ros2-regression",
                "phase181-ros2-regression",
                "phase184-acceptance-tooling",
                "phase186-bridge-tooling",
                "mcap-conformance",
                "packages",
                "boundary",
            ],
            observed["names"],
        )
        self.assertEqual(2, observed["max_workers"])

__all__ = ["RunCiTests"]
