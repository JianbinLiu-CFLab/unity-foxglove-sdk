from .foundation import *

class _RunCiTestsPart1:
    """Decomposed RunCiTests method group 1."""

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

__all__ = ["_RunCiTestsPart1"]
