from __future__ import annotations
from .class_phase184_foxglove_cli_install_tests_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveCliInstallTests_runtime:
    """Decomposed Phase192 implementation component."""
    def test_receipt_windows_alias_of_backup_fails_without_overwriting_either_file(self):
        """Verify receipt windows alias of backup fails without overwriting either file."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        old_hash = sha256_bytes(OLD_BYTES)
        backup_path = installer.build_backup_path(
            INSTALL_PATH,
            "supplied revision",
            old_hash,
        )
        environment.fs.write_bytes(backup_path, OLD_BYTES)
        receipt_alias = backup_path.lower().replace("\\", "/")

        self.assert_provenance_failure(
            lambda: installer.install_cli(
                INSTALL_PATH,
                receipt_alias,
                environment.dependencies(),
                previous_revision="supplied revision",
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(backup_path))
        self.assertFalse(
            any(
                event[0] in {"replace", "write-receipt"}
                for event in environment.events
            )
        )
    def test_extended_receipt_alias_of_install_target_rejects_drive_and_unc_forms(self):
        """Verify extended receipt alias of install target rejects drive and unc forms."""

        variants = (
            ("drive-ordinary-target", INSTALL_PATH, extended_windows_path(INSTALL_PATH)),
            ("drive-extended-target", extended_windows_path(INSTALL_PATH), INSTALL_PATH),
            (
                "unc-ordinary-target",
                UNC_INSTALL_PATH,
                extended_windows_path(UNC_INSTALL_PATH),
            ),
            (
                "unc-extended-target",
                extended_windows_path(UNC_INSTALL_PATH),
                UNC_INSTALL_PATH,
            ),
        )
        for name, install_path, receipt_alias in variants:
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                    install_path=install_path,
                )
                self.assert_provenance_failure(
                    lambda environment=environment,
                    install_path=install_path,
                    receipt_alias=receipt_alias: installer.main(
                        [
                            "--install-path",
                            install_path,
                            "--receipt",
                            receipt_alias,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(install_path),
                )
                self.assertFalse(
                    any(
                        event[0] in {"replace", "write-receipt"}
                        for event in environment.events
                    )
                )
    def test_extended_receipt_alias_of_backup_rejects_drive_and_unc_forms(self):
        """Verify extended receipt alias of backup rejects drive and unc forms."""

        variants = (
            ("drive", INSTALL_PATH),
            ("drive-extended-target", extended_windows_path(INSTALL_PATH)),
            ("unc", UNC_INSTALL_PATH),
            ("unc-extended-target", extended_windows_path(UNC_INSTALL_PATH)),
        )
        for name, install_path in variants:
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                    install_path=install_path,
                )
                backup_path = installer.build_backup_path(
                    install_path,
                    "supplied revision",
                    sha256_bytes(OLD_BYTES),
                )
                environment.fs.write_bytes(backup_path, OLD_BYTES)
                if backup_path.lower().startswith("\\\\?\\"):
                    receipt_alias = ordinary_windows_path(backup_path)
                else:
                    receipt_alias = extended_windows_path(backup_path)

                self.assert_provenance_failure(
                    lambda environment=environment,
                    install_path=install_path,
                    receipt_alias=receipt_alias: installer.install_cli(
                        install_path,
                        receipt_alias,
                        environment.dependencies(),
                        previous_revision="supplied revision",
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(install_path),
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(backup_path),
                )
                self.assertFalse(
                    any(
                        event[0] in {"replace", "write-receipt"}
                        for event in environment.events
                    )
                )
    def test_extended_receipt_alias_of_download_temp_rejects_drive_and_unc_forms(self):
        """Verify extended receipt alias of download temp rejects drive and unc forms."""

        for name, install_path in (
            ("drive", INSTALL_PATH),
            ("unc", UNC_INSTALL_PATH),
        ):
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                    install_path=install_path,
                )
                download_temp = ntpath.join(
                    ntpath.dirname(install_path),
                    ".foxglove.forced-download.tmp",
                )
                environment.fs.forced_temps["download"] = download_temp
                receipt_alias = extended_windows_path(download_temp)

                self.assert_provenance_failure(
                    lambda environment=environment,
                    install_path=install_path,
                    receipt_alias=receipt_alias: installer.main(
                        [
                            "--install-path",
                            install_path,
                            "--receipt",
                            receipt_alias,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(install_path),
                )
                self.assertFalse(
                    any(
                        event[0] in {"download", "replace", "write-receipt"}
                        for event in environment.events
                    )
                )
    def test_relative_receipt_resolves_against_repository_root_for_locked_command(self):
        """Verify relative receipt resolves against repository root for locked command."""

        repository_root = pathlib.PureWindowsPath(r"C:\Phase184Repository")
        expected_receipt = ntpath.normpath(
            ntpath.join(str(repository_root), RELATIVE_RECEIPT_PATH)
        )
        environment = FakeEnvironment(self.physical_root)

        with mock.patch.object(
            installer,
            "REPOSITORY_ROOT",
            repository_root,
        ):
            result = installer.main(
                [
                    "--install-path",
                    INSTALL_PATH,
                    "--receipt",
                    RELATIVE_RECEIPT_PATH,
                ],
                dependencies=environment.dependencies(),
            )

        self.assertEqual(0, result)
        self.assertTrue(environment.fs.exists(expected_receipt))
        self.assertEqual(
            expected_receipt,
            next(
                event[1]
                for event in environment.events
                if event[0] == "write-receipt"
            ),
        )
        self.assertFalse(environment.fs.exists(RELATIVE_RECEIPT_PATH))
    def test_download_version_failure_happens_before_replacement(self):
        """Verify download version failure happens before replacement."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        environment.download_version = "9.9.9"

        self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
                dependencies=environment.dependencies(),
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertFalse(any(event[0] == "replace" for event in environment.events))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))
    def test_post_replace_path_version_and_hash_failures_restore_old_binary(self):
        """Verify post replace path version and hash failures restore old binary."""

        cases = (
            "resolver",
            "installed-version",
            "resolved-version",
            "hash",
            "replace-raises-after-mutation",
        )
        for case in cases:
            with self.subTest(case=case):
                case_root = self.physical_root / case
                environment = FakeEnvironment(case_root, existing=OLD_BYTES)
                if case == "resolver":
                    environment.resolved_path = (
                        r"C:\Phase184Tests\other\foxglove.exe"
                    )
                    environment.fs.write_bytes(
                        environment.resolved_path,
                        NEW_BYTES,
                    )
                elif case == "installed-version":
                    environment.installed_version = "9.9.9"
                elif case == "resolved-version":
                    environment.resolved_version = "9.9.9"
                else:
                    if case == "hash":
                        def tamper_after_replace(_source, destination):
                            """Handle the tamper after replace step."""

                            environment.fs.write_bytes(destination, b"tampered")

                        environment.after_replace_hook = tamper_after_replace
                    else:
                        environment.raise_after_replace = True

                self.assert_provenance_failure(
                    lambda environment=environment: installer.main(
                        [
                            "--install-path",
                            INSTALL_PATH,
                            "--receipt",
                            RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(INSTALL_PATH),
                )
                self.assertFalse(environment.fs.exists(RECEIPT_PATH))
    def test_post_replace_failure_for_new_install_removes_new_target(self):
        """Verify post replace failure for new install removes new target."""

        environment = FakeEnvironment(self.physical_root)
        environment.fail_resolver = True
        self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
                dependencies=environment.dependencies(),
            )
        )
        self.assertFalse(environment.fs.exists(INSTALL_PATH))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))
    def test_receipt_write_or_revalidation_failure_rolls_back_and_prevents_success(self):
        """Verify receipt write or revalidation failure rolls back and prevents success."""

        failure_points = (
            "writer",
            "revalidation",
            "target-mutated-after-write",
            "resolver-changed-after-write",
            "preexisting-receipt-restored",
        )
        for failure_point in failure_points:
            with self.subTest(failure_point=failure_point):
                environment = FakeEnvironment(
                    self.physical_root / failure_point,
                    existing=OLD_BYTES,
                )
                previous_receipt = None
                if failure_point == "writer":
                    environment.fs.raise_after_receipt_write = True
                elif failure_point == "revalidation":
                    environment.fs.corrupt_loaded_receipt = True
                elif failure_point == "target-mutated-after-write":
                    environment.fs.after_receipt_write = (
                        lambda _path, _payload: environment.fs.write_bytes(
                            INSTALL_PATH,
                            b"post-write target mutation",
                        )
                    )
                elif failure_point == "resolver-changed-after-write":
                    alternate = r"C:\Phase184Tests\other\foxglove.exe"

                    def change_resolution(_path, _payload):
                        """Handle the change resolution step."""

                        environment.resolved_path = alternate
                        environment.fs.write_bytes(alternate, NEW_BYTES)

                    environment.fs.after_receipt_write = change_resolution
                else:
                    previous_receipt = b"unrelated prior receipt bytes"
                    environment.fs.write_bytes(RECEIPT_PATH, previous_receipt)
                    environment.fs.after_receipt_write = (
                        lambda _path, _payload: environment.fs.write_bytes(
                            INSTALL_PATH,
                            b"post-write target mutation",
                        )
                    )
                self.assert_provenance_failure(
                    lambda environment=environment: installer.main(
                        [
                            "--install-path",
                            INSTALL_PATH,
                            "--receipt",
                            RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(INSTALL_PATH),
                )
                if previous_receipt is None:
                    self.assertFalse(environment.fs.exists(RECEIPT_PATH))
                else:
                    self.assertEqual(
                        previous_receipt,
                        environment.fs.read_bytes(RECEIPT_PATH),
                    )
                if failure_point == "revalidation":
                    self.assertTrue(
                        any(
                            event[0] == "load-receipt"
                            for event in environment.events
                        )
                    )


__all__ = [name for name in globals() if not name.startswith("__")]
