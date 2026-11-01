from .foundation import *

class _RunCiTestsPart2:
    """Decomposed RunCiTests method group 2."""

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
            "Scripts.phase192.regression_checks.test_u2r2_protocol_limits",
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

__all__ = ["_RunCiTestsPart2"]
