from __future__ import annotations
from .class_phase184_foxglove_cli_install_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveCliInstallTests_validation:
    def test_successful_new_install_verifies_fresh_resolution_then_writes_receipt(self):
        """Verify successful new install verifies fresh resolution then writes receipt."""

        environment = FakeEnvironment(self.physical_root)
        with mock.patch(
            "urllib.request.urlopen",
            side_effect=AssertionError("tests must not reach the network"),
        ):
            result = installer.main(
                [
                    "--install-path",
                    INSTALL_PATH,
                    "--receipt",
                    RECEIPT_PATH,
                ],
                dependencies=environment.dependencies(),
            )

        self.assertEqual(0, result)
        self.assertEqual([installer.RELEASE_ENDPOINT], environment.fetch_urls)
        self.assertEqual(NEW_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        receipt = environment.fs.load_receipt(RECEIPT_PATH)
        installed_hash = sha256_bytes(NEW_BYTES)
        self.assertEqual(
            {
                "schemaVersion": protocol.CLI_RECEIPT_SCHEMA_VERSION,
                "releaseTag": "v1.2.3",
                "releaseVersion": "1.2.3",
                "architecture": protocol.CLI_ARCHITECTURE,
                "assetName": protocol.CLI_ASSET_NAME,
                "assetUrl": OFFICIAL_ASSET_URL,
                "downloadSha256": installed_hash,
                "downloadVersion": "1.2.3",
                "installedPath": INSTALL_PATH,
                "installedSha256": installed_hash,
                "installedVersion": "1.2.3",
                "previousSha256": installer.NO_PREVIOUS_SHA256,
                "backupPath": installer.build_backup_path(
                    INSTALL_PATH,
                    "none",
                    installer.NO_PREVIOUS_SHA256,
                ),
                "installedUtc": "2026-07-27T12:34:56Z",
            },
            receipt,
        )

        replace_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "replace"
            and event[2] == ntpath.normpath(INSTALL_PATH)
        )
        exact_run_index = next(
            index
            for index, event in enumerate(environment.events)
            if index > replace_index
            and event == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
        )
        resolve_indexes = [
            index
            for index, event in enumerate(environment.events)
            if event
            == ("resolve", "Get-Command foxglove -CommandType Application")
        ]
        write_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "write-receipt"
        )
        post_write_run_index = next(
            index
            for index, event in enumerate(environment.events)
            if index > write_index
            and event == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
        )
        load_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "load-receipt"
        )
        self.assertEqual(2, len(resolve_indexes))
        self.assertLess(exact_run_index, resolve_indexes[0])
        self.assertLess(resolve_indexes[0], write_index)
        self.assertLess(write_index, post_write_run_index)
        self.assertLess(post_write_run_index, resolve_indexes[1])
        self.assertLess(resolve_indexes[1], load_index)
    def test_install_receipt_records_the_exact_selected_official_asset_alias(self):
        """Verify install receipt records the exact selected official asset alias."""

        environment = FakeEnvironment(self.physical_root)
        environment.release = {
            "tag_name": "v1.2.3",
            "assets": [
                official_asset(
                    CURRENT_OFFICIAL_ASSET_NAME,
                    CURRENT_OFFICIAL_ASSET_URL,
                ),
                {
                    "name": "foxglove-windows-arm64",
                    "browser_download_url": (
                        "https://github.com/foxglove/foxglove-cli/releases/"
                        "download/v1.2.3/foxglove-windows-arm64"
                    ),
                },
            ],
        }

        installer.main(
            ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
            dependencies=environment.dependencies(),
        )

        receipt = environment.fs.load_receipt(RECEIPT_PATH)
        self.assertEqual(CURRENT_OFFICIAL_ASSET_NAME, receipt["assetName"])
        self.assertEqual(CURRENT_OFFICIAL_ASSET_URL, receipt["assetUrl"])
    def test_replacement_preserves_revision_and_hash_qualified_backup(self):
        """Verify replacement preserves revision and hash qualified backup."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        installer.main(
            ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
            dependencies=environment.dependencies(),
        )

        old_hash = sha256_bytes(OLD_BYTES)
        expected_backup = installer.build_backup_path(
            INSTALL_PATH,
            environment.old_revision,
            old_hash,
        )
        self.assertIn("dev-184-local", ntpath.basename(expected_backup))
        self.assertIn(old_hash[:12], ntpath.basename(expected_backup))
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(expected_backup))
        receipt = environment.fs.load_receipt(RECEIPT_PATH)
        self.assertEqual(old_hash, receipt["previousSha256"])
        self.assertEqual(expected_backup, receipt["backupPath"])
    def test_supplied_old_revision_changes_only_backup_name(self):
        """Verify supplied old revision changes only backup name."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        receipt = installer.install_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            environment.dependencies(),
            previous_revision="supplied revision",
        )
        expected_backup = installer.build_backup_path(
            INSTALL_PATH,
            "supplied revision",
            sha256_bytes(OLD_BYTES),
        )
        self.assertEqual(expected_backup, receipt["backupPath"])
        self.assertEqual("1.2.3", receipt["releaseVersion"])
        replacement_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "replace"
        )
        self.assertFalse(
            any(
                event
                == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
                for event in environment.events[:replacement_index]
            )
        )
    def test_backup_creation_interruptions_clean_owned_temp_and_reraise(self):
        """Verify backup creation interruptions clean owned temp and reraise."""

        interruption_factories = (
            ("keyboard", lambda: KeyboardInterrupt("synthetic interrupt")),
            ("system-exit", lambda: SystemExit(75)),
        )
        failure_points = (
            "copy",
            "hash",
            "publication-before",
            "publication-after",
        )
        for failure_point in failure_points:
            for interruption_name, factory in interruption_factories:
                with self.subTest(
                    failure_point=failure_point,
                    interruption=interruption_name,
                ):
                    environment = FakeEnvironment(
                        self.physical_root
                        / f"{failure_point}-{interruption_name}",
                        existing=OLD_BYTES,
                    )
                    old_hash = sha256_bytes(OLD_BYTES)
                    owned_backup = installer.build_backup_path(
                        INSTALL_PATH,
                        environment.old_revision,
                        old_hash,
                    )
                    unrelated_bytes = b"unrelated preserved backup"
                    unrelated_backup = installer.build_backup_path(
                        INSTALL_PATH,
                        "unrelated-backup",
                        sha256_bytes(unrelated_bytes),
                    )
                    environment.fs.write_bytes(
                        unrelated_backup,
                        unrelated_bytes,
                    )
                    interruption = factory()
                    if failure_point == "copy":
                        environment.fs.backup_copy_interruption = interruption
                    elif failure_point == "hash":
                        environment.fs.backup_hash_interruption = interruption
                    elif failure_point.startswith("publication"):
                        environment.fs.backup_publication_interruption = (
                            interruption
                        )
                        environment.fs.backup_publication_interrupt_after = (
                            failure_point == "publication-after"
                        )

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
                    self.assertEqual(
                        OLD_BYTES,
                        environment.fs.read_bytes(INSTALL_PATH),
                    )
                    if failure_point == "publication-after":
                        self.assertEqual(
                            OLD_BYTES,
                            environment.fs.read_bytes(owned_backup),
                        )
                    else:
                        self.assertFalse(
                            environment.fs.exists(owned_backup)
                        )
                    self.assertEqual(
                        unrelated_bytes,
                        environment.fs.read_bytes(unrelated_backup),
                    )
                    backup_temps = [
                        event[2]
                        for event in environment.events
                        if event[0:2] == ("temp", "backup")
                    ]
                    self.assertEqual(1, len(backup_temps))
                    self.assertFalse(
                        environment.fs.exists(backup_temps[0])
                    )
                    self.assertFalse(environment.fs.exists(RECEIPT_PATH))
                    self.assertFalse(
                        any(
                            event[0] in {"replace", "write-receipt"}
                            for event in environment.events
                        )
                    )
    def test_competitor_winning_backup_publication_is_never_unlinked(self):
        """Verify competitor winning backup publication is never unlinked."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        old_hash = sha256_bytes(OLD_BYTES)
        deterministic_backup = installer.build_backup_path(
            INSTALL_PATH,
            environment.old_revision,
            old_hash,
        )
        competitor_bytes = b"competitor-owned backup"
        environment.fs.backup_publication_competitor = competitor_bytes

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

        self.assertIn("backup", failure.message.lower())
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertEqual(
            competitor_bytes,
            environment.fs.read_bytes(deterministic_backup),
        )
        backup_temps = [
            event[2]
            for event in environment.events
            if event[0:2] == ("temp", "backup")
        ]
        self.assertEqual(1, len(backup_temps))
        self.assertFalse(environment.fs.exists(backup_temps[0]))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))
        self.assertEqual(
            1,
            sum(
                1
                for event in environment.events
                if event[0] == "publish-exclusive"
            ),
        )
        self.assertFalse(
            any(event[0] == "replace" for event in environment.events)
        )
    def test_local_backup_publication_has_no_overwrite_semantics(self):
        """Verify local backup publication has no overwrite semantics."""

        publisher = getattr(
            installer.LocalFilesystem(),
            "publish_exclusive",
            None,
        )
        self.assertIsNotNone(
            publisher,
            "production backup publication requires a no-overwrite primitive",
        )
        if publisher is None:
            return

        source = self.physical_root / "owned-backup.tmp"
        destination = self.physical_root / "competitor-backup.exe"
        source.write_bytes(OLD_BYTES)
        destination.write_bytes(b"competitor")

        with self.assertRaises(FileExistsError):
            publisher(str(source), str(destination))

        self.assertEqual(OLD_BYTES, source.read_bytes())
        self.assertEqual(b"competitor", destination.read_bytes())
    def test_backup_collision_never_overwrites_different_existing_file(self):
        """Verify backup collision never overwrites different existing file."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        old_hash = sha256_bytes(OLD_BYTES)
        collision = installer.build_backup_path(
            INSTALL_PATH,
            "supplied revision",
            old_hash,
        )
        environment.fs.write_bytes(collision, b"different backup")

        self.assert_provenance_failure(
            lambda: installer.install_cli(
                INSTALL_PATH,
                RECEIPT_PATH,
                environment.dependencies(),
                previous_revision="supplied revision",
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertEqual(b"different backup", environment.fs.read_bytes(collision))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))
    def test_receipt_windows_alias_of_install_target_fails_before_mutation(self):
        """Verify receipt windows alias of install target fails before mutation."""

        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        receipt_alias = INSTALL_PATH.lower().replace("\\", "/")

        self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", receipt_alias],
                dependencies=environment.dependencies(),
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertFalse(
            any(
                event[0] in {"replace", "write-receipt"}
                for event in environment.events
            )
        )


__all__ = [name for name in globals() if not name.startswith("__")]
