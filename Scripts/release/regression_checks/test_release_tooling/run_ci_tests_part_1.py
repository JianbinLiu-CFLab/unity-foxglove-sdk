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

__all__ = ["_RunCiTestsPart1"]
