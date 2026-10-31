from .foundation import *

class _UnityIl2CppBuildTestsPart1:
    """Decomposed UnityIl2CppBuildTests method group 1."""

    """Regression coverage for Unity build preflight checks."""

    def setUp(self) -> None:
        """Load a fresh unity_il2cpp module for each test."""
        self.unity_il2cpp = load_module("unity_il2cpp_under_test", UNITY_IL2CPP_PATH)

    def test_split_package_keeps_repository_root_and_public_signatures(self) -> None:
        """The facade and package share the root and callable signatures."""
        self.assertEqual(ROOT, self.unity_il2cpp.repo_root())
        self.assertEqual(3, self.unity_il2cpp.REPO_ROOT_PARENT_DEPTH)
        import unity_il2cpp.parts.foundation as foundation

        self.assertEqual(3, foundation.REPO_ROOT_PARENT_DEPTH)
        self.assertEqual(ROOT, foundation.repo_root())
        self.assertEqual(
            "(args: 'argparse.Namespace') -> 'Tuple[List[str], Path, Path, Path]'",
            str(inspect.signature(self.unity_il2cpp.build_command)),
        )
        import Scripts.unity_build.unity_il2cpp as namespaced

        self.assertIs(namespaced, module_registry().get("unity_il2cpp"))
        self.assertIs(
            module_registry().get("unity_il2cpp.implementation"),
            module_registry().get("Scripts.unity_build.unity_il2cpp.implementation"),
        )

    def test_direct_entrypoint_help_runs_in_a_clean_process(self) -> None:
        """The documented direct entrypoint must parse help before importing parts."""
        result = subprocess.run(
            [sys.executable, "-B", str(UNITY_IL2CPP_PATH), "--help"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("usage:", result.stdout)

    def test_split_package_reuses_nested_part_modules_across_import_prefixes(self) -> None:
        """Nested Unity parts keep one module object across import prefixes."""
        import Scripts.unity_build.unity_il2cpp as package
        import unity_il2cpp.parts.foundation as top_level
        import Scripts.unity_build.unity_il2cpp.parts.foundation as namespaced

        self.assertIs(package, module_registry().get("unity_il2cpp"))
        self.assertIs(top_level, namespaced)

    def test_generated_artifact_preflight_reports_missing_files(self) -> None:
        """Missing generated files should fail before Unity is started."""
        with tempfile.TemporaryDirectory() as temp:
            failures = self.unity_il2cpp.validate_generated_artifacts(Path(temp))

        self.assertTrue(failures)
        self.assertTrue(any("missing generated artifact" in failure for failure in failures))

    def _run_log_scenario(self, root: Path, at_start, polls=None):
        """Run the real controller against real files and a deterministic child seam."""
        tree = mock.Mock()
        # The controller observes descendants before its completion poll, so a
        # normal completed child produces one active observation followed by
        # the terminal zero return.
        tree.process.poll.side_effect = polls or [None, 0]
        tree.active_pids.return_value = []
        tree.close.return_value = None
        output, errors = io.StringIO(), io.StringIO()

        def launch(*_args):
            """Start the mocked owned process and capture its initial state."""
            at_start()
            return tree

        with mock.patch.object(self.unity_il2cpp, "start_owned_process", side_effect=launch):
            with mock.patch.object(self.unity_il2cpp, "LOG_POLL_SLEEP_SECONDS", 0):
                with contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
                    result = self.unity_il2cpp.run_with_progress(
                        ["controlled-child"], root, root / "unity.log", 1, 0
                    )
        self.assertEqual(0, result)
        tree.close.assert_called_once()
        return output.getvalue(), errors.getvalue()

    def test_log_reader_oversized_record_makes_bounded_progress(self) -> None:
        """A record larger than the read cap must not pin the reader's offset."""
        cap = self.unity_il2cpp.MAX_LOG_READ_BYTES
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "unity.log"
            payload = b"x" * (cap * 3) + b"\nerror CS1234: after oversized record\n"
            log.write_bytes(payload)
            offset, lines = 0, []
            for _ in range(5):
                previous = offset
                offset, chunk = self.unity_il2cpp.read_new_important_lines(log, offset)
                self.assertLessEqual(offset - previous, cap)
                if previous < len(payload):
                    self.assertGreater(offset, previous, "reader stalled on an incomplete oversized line")
                lines.extend(chunk)
            self.assertEqual(len(payload), offset)
            self.assertIn("error CS1234: after oversized record", lines)

    def test_log_controller_flushes_final_unterminated_error(self) -> None:
        """Root exit must not discard its final error just because LF is absent."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output, _ = self._run_log_scenario(
                root, lambda: (root / "unity.log").write_bytes(b"error CS7777: final")
            )
        self.assertEqual(1, output.count("[unity-log] error CS7777: final"))

    def test_log_controller_drains_multiple_chunks_after_exit(self) -> None:
        """Post-exit diagnostics must advance beyond the first two read chunks."""
        cap = self.unity_il2cpp.MAX_LOG_READ_BYTES
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            payload = b"ordinary\n" * (cap // 9 * 4) + b"error CS7778: late failure\n"
            output, _ = self._run_log_scenario(root, lambda: (root / "unity.log").write_bytes(payload))
        self.assertEqual(1, output.count("[unity-log] error CS7778: late failure"))

    def test_log_controller_rebinds_replacement_before_first_poll(self) -> None:
        """The prelaunch snapshot must identify a replacement, not just its old size."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            log = root / "unity.log"
            log.write_bytes(b"error CS0000: stale\n")

            def replace():
                """Replace the log atomically with fresh compiler output."""
                replacement = root / "replacement.log"
                replacement.write_bytes(b"error CS1111: fresh replacement starts here\n")
                replacement.replace(log)

            output, _ = self._run_log_scenario(root, replace)
        self.assertIn("[unity-log] error CS1111: fresh replacement starts here", output)
        self.assertNotIn("CS0000", output)

    def test_log_controller_preserves_split_utf8_append(self) -> None:
        """A partial multibyte diagnostic is buffered, decoded once, and not duplicated."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            log = root / "unity.log"
            payload = "error CS2222: 编译失败\n".encode("utf-8")

            def append():
                """Append the remaining UTF-8 payload to the tailed log."""
                with log.open("ab") as stream:
                    stream.write(payload[16:])
                return None

            steps = iter([append, lambda: 0])
            output, _ = self._run_log_scenario(
                root, lambda: log.write_bytes(payload[:16]), polls=lambda: next(steps)()
            )
        self.assertEqual(1, output.count("[unity-log] error CS2222: 编译失败"))
        self.assertNotIn("\ufffd", output)

    def test_log_controller_state_does_not_leak_between_invocations(self) -> None:
        """An earlier large log must not cause a later invocation to emit stale content."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            log = root / "unity.log"
            self._run_log_scenario(root, lambda: log.write_bytes(b"error CS3333: " + b"x" * 100 + b"\n"))
            log.write_bytes(b"error CS4444: stale second run\n")
            output, _ = self._run_log_scenario(root, lambda: None)
        self.assertNotIn("CS4444", output)

    def test_log_controller_discards_partial_record_on_truncation(self) -> None:
        """Truncation resets the pending bytes as well as the physical offset."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            log = root / "unity.log"

            def truncate():
                """Replace a partial log record with a fresh complete line."""
                log.write_bytes(b"error CS5555: fresh\n")
                return None

            steps = iter([truncate, lambda: 0])
            output, _ = self._run_log_scenario(
                root, lambda: log.write_bytes(b"error CS5550: old partial " + b"x" * 100),
                polls=lambda: next(steps)()
            )
        self.assertEqual(1, output.count("[unity-log] error CS5555: fresh"))
        self.assertNotIn("CS5550", output)

    def test_log_controller_drain_budget_is_explicit(self) -> None:
        """Final catch-up is finite and reports omitted diagnostics rather than hiding them."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with mock.patch.object(self.unity_il2cpp, "MAX_LOG_READ_BYTES", 64):
                with mock.patch.object(self.unity_il2cpp, "MAX_FINAL_LOG_CHUNKS", 2):
                    output, errors = self._run_log_scenario(
                        root, lambda: (root / "unity.log").write_bytes(b"normal\n" * 100 + b"error CS9999\n")
                    )
        self.assertNotIn("CS9999", output)
        self.assertIn("Final progress log budget exhausted", errors)

    def test_log_controller_reports_oversized_record_and_keeps_next_record(self) -> None:
        """Discard only the overlong record, then resume complete diagnostics."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with mock.patch.object(self.unity_il2cpp, "MAX_LOG_READ_BYTES", 64):
                output, errors = self._run_log_scenario(
                    root, lambda: (root / "unity.log").write_bytes(
                        b"error CS6660: " + b"x" * 150 + b"\nerror CS6661: next\n"
                    )
                )
        self.assertEqual(1, output.count("[unity-log] error CS6660:"))
        self.assertIn("[truncated]", output)
        self.assertEqual(1, output.count("[unity-log] error CS6661: next"))
        self.assertIn("Log record exceeded 64 bytes", errors)

    def test_log_controller_reports_read_failure_and_closes_descriptor(self) -> None:
        """I/O errors remain diagnostic and cannot leak the opened log descriptor."""
        real_fstat = os.fstat
        descriptors = []

        def fail(fd):
            """Inject a descriptor-stat failure while recording the handle."""
            descriptors.append(fd)
            raise OSError("controlled descriptor failure")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with mock.patch.object(self.unity_il2cpp.os, "fstat", side_effect=fail):
                _, errors = self._run_log_scenario(root, lambda: (root / "unity.log").write_bytes(b"normal\n"))
            self.assertTrue(descriptors)
            for fd in descriptors:
                with self.assertRaises(OSError):
                    real_fstat(fd)
        self.assertIn("Log read failed", errors)

    @unittest.skipUnless(os.name == "posix", "POSIX FIFO boundary")
    def test_log_controller_rejects_fifo_without_waiting_for_writer(self) -> None:
        """An unusable log is reported rather than silently disabling diagnostics."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._make_log_fifo(root / "unity.log")
            _, errors = self._run_log_scenario(root, lambda: None)
        self.assertIn("not a regular file", errors)

    def _make_log_fifo(self, path: Path) -> None:
        """Create a kernel FIFO even when a WSL-mounted Windows volume lacks mkfifo."""
        try:
            os.mkfifo(path)
        except OSError as exc:
            import errno
            if exc.errno != errno.EOPNOTSUPP or not Path("/proc/self/fd").is_dir():
                raise
            reader, writer = os.pipe()
            self.addCleanup(os.close, reader)
            os.close(writer)
            path.symlink_to(f"/proc/self/fd/{reader}")
            print("FIFO_FIXTURE=kernel_pipe_via_procfs; named_fifo_volume=EOPNOTSUPP")

    @unittest.skipUnless(os.name == "posix", "POSIX file replacement boundary")
    def test_log_controller_revalidates_opened_file_after_fifo_race(self) -> None:
        """A path replaced after stat must not block open or pass descriptor validation."""
        real_open = os.open
        replaced = False
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            log = root / "unity.log"

            def race(path, flags, *args, **kwargs):
                """Swap the log path during open to exercise replacement handling."""
                nonlocal replaced
                if Path(path) == log and not replaced:
                    replaced = True
                    log.unlink()
                    self._make_log_fifo(log)
                    # A regression must fail before trying a blocking FIFO open.
                    self.assertTrue(flags & os.O_NONBLOCK)
                return real_open(path, flags, *args, **kwargs)

            with mock.patch.object(self.unity_il2cpp.os, "open", side_effect=race):
                _, errors = self._run_log_scenario(root, lambda: log.write_bytes(b"normal\n"))
            self.assertTrue(replaced)
        self.assertIn("not a regular file", errors)

    def _populate_generated_artifacts(self, root: Path) -> None:
        """Write one nonempty regular file for every required generated artifact."""
        for relative in self.unity_il2cpp.REQUIRED_GENERATED_ARTIFACTS:
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("generated\n", encoding="utf-8")

    def test_generated_artifact_preflight_accepts_a_complete_regular_file_set(self) -> None:
        """A fully populated tree of nonempty regular files must preflight clean."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._populate_generated_artifacts(root)

            self.assertEqual([], self.unity_il2cpp.validate_generated_artifacts(root))

    def test_generated_artifact_preflight_rejects_a_directory_at_a_required_path(self) -> None:
        """exists() alone must not satisfy the preflight: a directory is not the artifact."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._populate_generated_artifacts(root)
            required = self.unity_il2cpp.REQUIRED_GENERATED_ARTIFACTS[0]
            occupied = root / required
            occupied.unlink()
            occupied.mkdir(parents=True)

            failures = self.unity_il2cpp.validate_generated_artifacts(root)

        self.assertTrue(any(required in failure for failure in failures))

    def test_generated_artifact_preflight_rejects_a_directory_symlink(self) -> None:
        """A directory symlink or reparse point at a required path is not the artifact."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._populate_generated_artifacts(root)
            required = self.unity_il2cpp.REQUIRED_GENERATED_ARTIFACTS[0]
            occupied = root / required
            occupied.unlink()
            target = root / "symlink-target-directory"
            target.mkdir()
            try:
                occupied.symlink_to(target, target_is_directory=True)
            except (OSError, NotImplementedError) as exc:
                self.skipTest(f"directory symlinks unavailable on this host: {exc}")

            failures = self.unity_il2cpp.validate_generated_artifacts(root)

        self.assertTrue(any(required in failure for failure in failures))

    def test_missing_project_pinned_unity_falls_back_to_hub_discovery(self) -> None:
        """Missing ProjectVersion editor should not block newer Hub editor discovery."""
        with tempfile.TemporaryDirectory() as temp:
            project = Path(temp) / "UnityProject"
            settings = project / "ProjectSettings"
            settings.mkdir(parents=True)
            (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.14f1\n", encoding="utf-8")

            with mock.patch.object(self.unity_il2cpp.platform, "system", return_value="Windows"):
                with mock.patch.dict(self.unity_il2cpp.os.environ, {"PROGRAMFILES": str(Path(temp) / "ProgramFiles")}, clear=False):
                    self.assertIsNone(self.unity_il2cpp.find_unity_from_project_version(project))

    def test_unity_version_key_ignores_patch_suffix_letters(self) -> None:
        """Unity Hub sorting should compare numeric version components only."""
        path = Path("C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor/Unity.exe")

        self.assertEqual((6000, 3, 14, 1), self.unity_il2cpp.unity_version_key(path))

    def test_default_build_dir_uses_utc_stamp(self) -> None:
        """Generated build directories should be timezone-stable in CI logs."""
        build_dir = self.unity_il2cpp.default_build_dir(Path("repo"), "win64")

        self.assertRegex(str(build_dir), r"win64-il2cpp-\d{8}-\d{6}Z-[0-9a-f]{8}$")

    def test_default_build_dir_reserves_distinct_concurrent_paths(self) -> None:
        """Concurrent invocations must not share the same default output tree."""
        first = self.unity_il2cpp.default_build_dir(Path("repo"), "win64")
        second = self.unity_il2cpp.default_build_dir(Path("repo"), "win64")
        self.assertNotEqual(first, second)

    def test_build_command_rejects_project_and_artifact_paths_outside_workspace(self) -> None:
        """CLI paths must not escape the repository authority boundary."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "repo"
            root.mkdir()
            (root / "Unity2Foxglove").mkdir()
            outside = Path(temp) / "outside"
            outside.mkdir()
            args = types.SimpleNamespace(
                project="../outside",
                build_dir="../outside/build",
                log="../outside/log.txt",
                output="../outside/player.exe",
                target="win64",
                unity=None,
                dry_run=False,
                allow_missing_unity=False,
            )
            with mock.patch.object(self.unity_il2cpp, "repo_root", return_value=root):
                with mock.patch.object(
                    self.unity_il2cpp, "resolve_unity_for_command", return_value=str(root / "Unity.exe")
                ):
                    with self.assertRaisesRegex(ValueError, "must remain inside"):
                        self.unity_il2cpp.build_command(args)

    def test_build_command_accepts_workspace_relative_paths(self) -> None:
        """Normal project and output paths remain accepted after boundary validation."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "repo"
            root.mkdir()
            (root / "Unity2Foxglove").mkdir()
            args = types.SimpleNamespace(
                project="Unity2Foxglove",
                build_dir="build/run",
                log="build/run/build.log",
                output="build/run/player.exe",
                target="win64",
                unity=None,
                dry_run=False,
                allow_missing_unity=False,
            )
            with mock.patch.object(self.unity_il2cpp, "repo_root", return_value=root):
                with mock.patch.object(
                    self.unity_il2cpp, "resolve_unity_for_command", return_value=str(root / "Unity.exe")
                ):
                    _, project, log, output = self.unity_il2cpp.build_command(args)
            self.assertEqual(root / "Unity2Foxglove", project)
            self.assertEqual(root / "build/run/build.log", log)
            self.assertEqual(root / "build/run/player.exe", output)

    def _write_unity_stand_in(self, path: Path, executable: bool = True) -> Path:
        """Create a candidate that a host would accept as an executable Unity."""
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"MZ\x90\x00stand-in unity executable")
        if executable and os.name != "nt":
            path.chmod(0o755)
        return path

    def _hub_layout(self, temp: Path) -> tuple[Path, Path]:
        """Return a Windows Hub editor path and the project that pins its version."""
        project = temp / "UnityProject"
        (project / "ProjectSettings").mkdir(parents=True)
        (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
            "m_EditorVersion: 2022.3.10f1\n", encoding="utf-8")
        unity = temp / "ProgramFiles" / "Unity" / "Hub" / "Editor" / "2022.3.10f1" / "Editor" / "Unity.exe"
        return project, unity

    def test_project_pinned_discovery_rejects_a_non_executable_candidate(self) -> None:
        """A file that merely occupies the Hub path is not a Unity editor."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project, unity = self._hub_layout(root)
            unity.parent.mkdir(parents=True)
            unity.write_text("NOT_A_UNITY_EXECUTABLE\n", encoding="utf-8")

            # DrvFs may grant X_OK to every regular file. Express the negative
            # permission boundary explicitly instead of assuming ext4 defaults.
            with mock.patch.object(self.unity_il2cpp.os, "access", return_value=False), \
                    mock.patch.object(self.unity_il2cpp.platform, "system", return_value="Windows"):
                with mock.patch.dict(self.unity_il2cpp.os.environ,
                                     {"PROGRAMFILES": str(root / "ProgramFiles"),
                                      "PROGRAMFILES(X86)": str(root / "missing")}, clear=False):
                    resolved = self.unity_il2cpp.find_unity_from_project_version(project)

        self.assertIsNone(resolved)

    def test_project_pinned_discovery_accepts_a_real_executable(self) -> None:
        """A genuine executable at the pinned Hub path must still be selected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project, unity = self._hub_layout(root)
            self._write_unity_stand_in(unity)

            with mock.patch.object(self.unity_il2cpp.platform, "system", return_value="Windows"):
                with mock.patch.dict(self.unity_il2cpp.os.environ,
                                     {"PROGRAMFILES": str(root / "ProgramFiles"),
                                      "PROGRAMFILES(X86)": str(root / "missing")}, clear=False):
                    resolved = self.unity_il2cpp.find_unity_from_project_version(project)

        self.assertIsNotNone(resolved)
        self.assertEqual(unity.resolve(), Path(resolved))

    def _pinned_project(self, temp: Path, editor_version: str) -> Path:
        """Write a project whose ProjectVersion.txt pins the given editor version value."""
        project = temp / "UnityProject"
        (project / "ProjectSettings").mkdir(parents=True, exist_ok=True)
        (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
            f"m_EditorVersion: {editor_version}\n", encoding="utf-8")
        return project

    def _resolve_pinned(self, temp: Path, project: Path):
        """Run project-pinned discovery with a controlled Windows Hub root."""
        with mock.patch.object(self.unity_il2cpp.platform, "system", return_value="Windows"):
            with mock.patch.dict(self.unity_il2cpp.os.environ,
                                 {"PROGRAMFILES": str(temp / "ProgramFiles"),
                                  "PROGRAMFILES(X86)": str(temp / "missing")}, clear=False):
                captured = io.StringIO()
                with contextlib.redirect_stdout(captured), contextlib.redirect_stderr(captured):
                    resolved = self.unity_il2cpp.find_unity_from_project_version(project)
        return resolved, captured.getvalue()

    def test_project_pinned_parent_traversal_is_rejected(self) -> None:
        """A pinned version with parent segments must not walk out of the Hub root."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "base"
            root.mkdir()
            project = self._pinned_project(root, "../../../../escaped")
            hub = root / "ProgramFiles" / "Unity" / "Hub" / "Editor"
            hub.mkdir(parents=True)
            escaped = (hub / "../../../../escaped" / "Editor" / "Unity.exe").resolve()
            self._write_unity_stand_in(escaped)

            resolved, _text = self._resolve_pinned(root, project)

        self.assertIsNone(resolved)

    def test_project_pinned_absolute_value_is_rejected(self) -> None:
        """An absolute pinned version discards the Hub prefix and must be rejected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            elsewhere = root / "elsewhere"
            self._write_unity_stand_in(elsewhere / "Editor" / "Unity.exe")
            project = self._pinned_project(root, str(elsewhere))
            (root / "ProgramFiles" / "Unity" / "Hub" / "Editor").mkdir(parents=True)

            resolved, _text = self._resolve_pinned(root, project)

        self.assertIsNone(resolved)

    def test_project_pinned_non_version_value_is_rejected(self) -> None:
        """A value that is not a Unity version component must not be joined at all."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = self._pinned_project(root, "not-a-version")
            planted = (root / "ProgramFiles" / "Unity" / "Hub" / "Editor"
                       / "not-a-version" / "Editor" / "Unity.exe")
            self._write_unity_stand_in(planted)

            resolved, _text = self._resolve_pinned(root, project)

        self.assertIsNone(resolved)

    def test_project_pinned_candidate_escaping_by_symlink_is_rejected(self) -> None:
        """Containment is checked after resolution, not on the unresolved join."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = self._pinned_project(root, "2022.3.10f1")
            version_root = (root / "ProgramFiles" / "Unity" / "Hub" / "Editor" / "2022.3.10f1")
            version_root.mkdir(parents=True)
            outside = self._write_unity_stand_in(root / "outside" / "Unity.exe")
            link = version_root / "Editor"
            try:
                link.symlink_to(outside.parent, target_is_directory=True)
            except (OSError, NotImplementedError) as exc:
                self.skipTest(f"directory symlinks unavailable on this host: {exc}")

            resolved, _text = self._resolve_pinned(root, project)

        self.assertIsNone(resolved)

    def test_project_pinned_valid_version_inside_hub_root_is_accepted(self) -> None:
        """A real editor under the pinned Hub version root must still be selected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = self._pinned_project(root, "2022.3.10f1")
            unity = (root / "ProgramFiles" / "Unity" / "Hub" / "Editor"
                     / "2022.3.10f1" / "Editor" / "Unity.exe")
            self._write_unity_stand_in(unity)

            resolved, _text = self._resolve_pinned(root, project)

        self.assertIsNotNone(resolved)
        self.assertEqual(unity.resolve(), Path(resolved))

    def test_hub_discovery_ignores_malformed_version_directories(self) -> None:
        """Hub fallback must not select trailing-junk or unknown-channel versions."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            good = root / "ProgramFiles" / "Unity" / "Hub" / "Editor" / "6000.3.14f1" / "Editor" / "Unity.exe"
            malformed = root / "ProgramFiles" / "Unity" / "Hub" / "Editor" / "6000.3.99z999" / "Editor" / "Unity.exe"
            backup = root / "ProgramFiles" / "Unity" / "Hub" / "Editor" / "6000.3.14f1-backup" / "Editor" / "Unity.exe"
            self._write_unity_stand_in(good)
            self._write_unity_stand_in(malformed)
            self._write_unity_stand_in(backup)
            with mock.patch.object(self.unity_il2cpp.platform, "system", return_value="Windows"):
                with mock.patch.dict(self.unity_il2cpp.os.environ,
                                     {"PROGRAMFILES": str(root / "ProgramFiles"),
                                      "PROGRAMFILES(X86)": str(root / "missing")}, clear=False):
                    resolved = self.unity_il2cpp.find_unity_from_hub()
        self.assertEqual(good.resolve(), Path(resolved))

__all__ = ["_UnityIl2CppBuildTestsPart1"]
