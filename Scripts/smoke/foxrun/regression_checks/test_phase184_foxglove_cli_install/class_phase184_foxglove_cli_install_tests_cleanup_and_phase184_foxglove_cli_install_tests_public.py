from __future__ import annotations
from .class_phase184_foxglove_cli_install_tests_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveCliInstallTests_cleanup:
    def test_interruptions_after_binary_replacement_restore_and_reraise(self):
        """Verify interruptions after binary replacement restore and reraise."""

        interruption_factories = (
            ("keyboard", lambda: KeyboardInterrupt("synthetic interrupt")),
            ("system-exit", lambda: SystemExit(73)),
        )
        previous_receipt = b"pre-existing receipt"
        for name, factory in interruption_factories:
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                )
                environment.fs.write_bytes(RECEIPT_PATH, previous_receipt)
                interruption = factory()
                environment.replacement_interruption = interruption

                with self.assertRaises(type(interruption)) as raised:
                    installer.main(
                        [
                            "--install-path",
                            INSTALL_PATH,
                            "--receipt",
                            RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )

                self.assertIs(interruption, raised.exception)
                if isinstance(interruption, SystemExit):
                    self.assertEqual(interruption.code, raised.exception.code)
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(INSTALL_PATH),
                )
                self.assertEqual(
                    previous_receipt,
                    environment.fs.read_bytes(RECEIPT_PATH),
                )
                self.assertGreaterEqual(
                    sum(
                        1
                        for event in environment.events
                        if event[0] == "replace"
                    ),
                    2,
                )
    def test_interruptions_after_receipt_write_restore_or_remove_receipt_and_reraise(self):
        """Verify interruptions after receipt write restore or remove receipt and reraise."""

        interruption_factories = (
            ("keyboard", lambda: KeyboardInterrupt("synthetic interrupt")),
            ("system-exit", lambda: SystemExit(74)),
        )
        for interruption_name, factory in interruption_factories:
            for receipt_preexisted in (False, True):
                with self.subTest(
                    interruption=interruption_name,
                    receipt_preexisted=receipt_preexisted,
                ):
                    environment = FakeEnvironment(
                        self.physical_root
                        / f"{interruption_name}-{receipt_preexisted}",
                        existing=OLD_BYTES,
                    )
                    previous_receipt = b"pre-existing receipt"
                    if receipt_preexisted:
                        environment.fs.write_bytes(
                            RECEIPT_PATH,
                            previous_receipt,
                        )
                    interruption = factory()
                    environment.fs.receipt_write_interruption = interruption

                    with self.assertRaises(type(interruption)) as raised:
                        installer.main(
                            [
                                "--install-path",
                                INSTALL_PATH,
                                "--receipt",
                                RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )

                    self.assertIs(interruption, raised.exception)
                    if isinstance(interruption, SystemExit):
                        self.assertEqual(interruption.code, raised.exception.code)
                    self.assertEqual(
                        OLD_BYTES,
                        environment.fs.read_bytes(INSTALL_PATH),
                    )
                    if receipt_preexisted:
                        self.assertEqual(
                            previous_receipt,
                            environment.fs.read_bytes(RECEIPT_PATH),
                        )
                    else:
                        self.assertFalse(
                            environment.fs.exists(RECEIPT_PATH)
                        )
    def test_interruption_rollback_failure_is_bounded_and_receipt_recovery_is_independent(self):
        """Verify interruption rollback failure is bounded and receipt recovery is independent."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        previous_receipt = b"pre-existing receipt"
        environment.fs.write_bytes(RECEIPT_PATH, previous_receipt)
        environment.fs.receipt_write_interruption = KeyboardInterrupt(
            "synthetic interrupt"
        )
        environment.rollback_failure = RuntimeError(
            "synthetic binary rollback failure"
        )

        try:
            failure = self.assert_provenance_failure(
                lambda: installer.main(
                    [
                        "--install-path",
                        INSTALL_PATH,
                        "--receipt",
                        RECEIPT_PATH,
                    ],
                    dependencies=environment.dependencies(),
                )
            )
        except (KeyboardInterrupt, SystemExit):
            self.fail("interruption bypassed rollback-failure classification")
        self.assertIn("rollback", failure.message.lower())
        self.assertEqual(
            previous_receipt,
            environment.fs.read_bytes(RECEIPT_PATH),
        )
        self.assertTrue(
            any(
                event[0] == "replace"
                and "receipt-rollback" in event[1]
                for event in environment.events
            )
        )
    def test_bounded_process_terminates_and_reaps_stdout_and_stderr_overflow(self):
        """Verify bounded process terminates and reaps stdout and stderr overflow."""

        runner = getattr(installer, "_run_bounded_process", None)
        self.assertIsNotNone(
            runner,
            "production adapters require one shared bounded process runner",
        )
        if runner is None:
            return

        helper = self.physical_root / "oversized_output_helper.py"
        helper.write_text(
            "import sys, time\n"
            "stream = sys.stdout.buffer if sys.argv[1] == 'stdout' "
            "else sys.stderr.buffer\n"
            f"stream.write(b'x' * ({installer.MAX_COMMAND_OUTPUT_BYTES} + 1))\n"
            "stream.flush()\n"
            "time.sleep(30)\n",
            encoding="utf-8",
        )
        real_popen = subprocess.Popen
        for stream_name in ("stdout", "stderr"):
            with self.subTest(stream=stream_name):
                captured: list[subprocess.Popen] = []

                def capture_process(*args, **kwargs):
                    """Capture process."""

                    process = real_popen(*args, **kwargs)
                    captured.append(process)
                    return process

                with mock.patch.object(
                    installer.subprocess,
                    "Popen",
                    side_effect=capture_process,
                ):
                    self.assert_provenance_failure(
                        lambda stream_name=stream_name: runner(
                            [sys.executable, str(helper), stream_name],
                            timeout_seconds=5,
                        )
                    )

                self.assertEqual(1, len(captured))
                process = captured[0]
                self.assertIsNotNone(process.returncode)
                self.assertIsNotNone(process.poll())
                self.assertTrue(process.stdout.closed)
                self.assertTrue(process.stderr.closed)
    def test_bounded_process_preserves_bounded_output_exit_code_and_backs_both_adapters(self):
        """Verify bounded process preserves bounded output exit code and backs both adapters."""

        runner = getattr(installer, "_run_bounded_process", None)
        result_type = getattr(installer, "_BoundedProcessResult", None)
        self.assertIsNotNone(runner)
        self.assertIsNotNone(result_type)
        if runner is None or result_type is None:
            return

        completed = runner(
            [
                sys.executable,
                "-c",
                (
                    "import sys; "
                    "sys.stdout.buffer.write(b'bounded-out'); "
                    "sys.stderr.buffer.write(b'bounded-err'); "
                    "raise SystemExit(7)"
                ),
            ],
            timeout_seconds=5,
        )
        self.assertEqual(7, completed.returncode)
        self.assertEqual(b"bounded-out", completed.stdout)
        self.assertEqual(b"bounded-err", completed.stderr)

        version_result = types.SimpleNamespace(
            returncode=0,
            stdout=b"1.2.3\n",
            stderr=b"bounded notice",
        )
        with mock.patch.object(
            installer,
            "_run_bounded_process",
            return_value=version_result,
        ) as bounded:
            self.assertEqual(
                "1.2.3\n",
                installer._run_command_production(
                    r"C:\Tools\foxglove.exe",
                    ("version",),
                ),
            )
            bounded.assert_called_once()

        resolver_result = types.SimpleNamespace(
            returncode=0,
            stdout=INSTALL_PATH.encode("utf-8"),
            stderr=b"",
        )
        with mock.patch.object(
            installer,
            "_run_bounded_process",
            return_value=resolver_result,
        ) as bounded:
            self.assertEqual(
                INSTALL_PATH,
                installer._resolve_command_production(),
            )
            bounded.assert_called_once()
    def test_bounded_process_timeout_terminates_and_reaps_child(self):
        """Verify bounded process timeout terminates and reaps child."""

        runner = getattr(installer, "_run_bounded_process", None)
        self.assertIsNotNone(runner)
        if runner is None:
            return

        real_popen = subprocess.Popen
        captured: list[subprocess.Popen] = []

        def capture_process(*args, **kwargs):
            """Capture process."""

            process = real_popen(*args, **kwargs)
            captured.append(process)
            return process

        with mock.patch.object(
            installer.subprocess,
            "Popen",
            side_effect=capture_process,
        ):
            failure = self.assert_provenance_failure(
                lambda: runner(
                    [
                        sys.executable,
                        "-c",
                        "import time; time.sleep(30)",
                    ],
                    timeout_seconds=0.05,
                )
            )

        self.assertIn("timed out", failure.message.lower())
        self.assertEqual(1, len(captured))
        process = captured[0]
        self.assertIsNotNone(process.returncode)
        self.assertIsNotNone(process.poll())
        self.assertTrue(process.stdout.closed)
        self.assertTrue(process.stderr.closed)
    def test_installer_delegates_windows_identity_to_protocol_helpers(self):
        """Verify installer delegates windows identity to protocol helpers."""

        source = read_installer_source()
        self.assertNotIn("def _path_key(", source)
        self.assertNotIn("startswith(\"\\\\\\\\?\\\\unc\\\\\")", source)
        self.assertIn("protocol.windows_path_key", source)
        self.assertIn("protocol.windows_paths_equal", source)
    def test_production_dependencies_fail_closed_off_windows_before_network(self):
        """Verify production dependencies fail closed off windows before network."""

        with (
            mock.patch.object(installer.os, "name", "posix"),
            mock.patch(
                "urllib.request.urlopen",
                side_effect=AssertionError("must fail before network"),
            ),
        ):
            self.assert_provenance_failure(
                lambda: installer.main(["--install-path", INSTALL_PATH])
            )
    def test_injected_run_never_mutates_real_user_path_and_keeps_diagnostics_stable(self):
        """Verify injected run never mutates real user path and keeps diagnostics stable."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        environment.download_version = "secret\r\n" + ("x" * 4096)
        failure = self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
                dependencies=environment.dependencies(),
            )
        )
        self.assertNotIn("secret", failure.message)
        flattened_events = repr(environment.events).lower()
        self.assertNotIn(r"c:\users\ljb\go\bin\foxglove.exe", flattened_events)
        self.assertFalse(
            (pathlib.Path.home() / "go" / "bin" / "foxglove.exe").is_relative_to(
                self.physical_root
            )
        )
class Phase184FoxgloveCliInstallTests(_Phase184FoxgloveCliInstallTests_support, _Phase184FoxgloveCliInstallTests_fixtures, _Phase184FoxgloveCliInstallTests_validation, _Phase184FoxgloveCliInstallTests_runtime, _Phase184FoxgloveCliInstallTests_cleanup, unittest.TestCase):
    """Exercise the phase184 foxglove CLI install tests behavior."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
