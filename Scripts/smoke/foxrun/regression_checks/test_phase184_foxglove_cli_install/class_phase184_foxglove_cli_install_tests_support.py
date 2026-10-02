from __future__ import annotations
from .fake_environment import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveCliInstallTests_support:
    """Decomposed Phase192 implementation component."""
    def setUp(self):
        """Prepare isolated test state."""

        if installer is None:
            self.fail("phase184_foxglove_cli_install is not implemented")
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.physical_root = pathlib.Path(self.temporary.name)
    def assert_provenance_failure(self, callback) -> protocol.AcceptanceFailure:
        """Handle the assert provenance failure step."""

        with self.assertRaises(protocol.AcceptanceFailure) as raised:
            callback()
        self.assertEqual(protocol.FAIL_CLI_PROVENANCE, raised.exception.code)
        self.assertLessEqual(
            len(raised.exception.message),
            protocol.MAX_DIAGNOSTIC_CHARACTERS,
        )
        return raised.exception
    def verify_cli(self, *args, **kwargs):
        """Handle the verify CLI step."""

        verifier = getattr(
            installer,
            "verify_installed_cli_provenance",
            None,
        )
        self.assertIsNotNone(
            verifier,
            "the public read-only CLI provenance verifier is required",
        )
        return verifier(*args, **kwargs)
    def prepare_verifier_fixture(
        self,
        environment: FakeEnvironment,
        *,
        binary_bytes: bytes = NEW_BYTES,
        receipt_argument: str = RECEIPT_PATH,
        receipt_overrides: dict[str, object] | None = None,
    ) -> tuple[str, dict[str, object]]:
        """Prepare verifier fixture."""

        environment.fs.write_bytes(INSTALL_PATH, binary_bytes)
        receipt_destination = installer._resolve_receipt_path(
            receipt_argument
        )
        digest = sha256_bytes(binary_bytes)
        receipt: dict[str, object] = {
            "schemaVersion": protocol.CLI_RECEIPT_SCHEMA_VERSION,
            "releaseTag": "v1.2.3",
            "releaseVersion": "1.2.3",
            "architecture": protocol.CLI_ARCHITECTURE,
            "assetName": protocol.CLI_ASSET_NAME,
            "assetUrl": OFFICIAL_ASSET_URL,
            "downloadSha256": digest,
            "downloadVersion": "1.2.3",
            "installedPath": INSTALL_PATH,
            "installedSha256": digest,
            "installedVersion": "1.2.3",
            "previousSha256": installer.NO_PREVIOUS_SHA256,
            "backupPath": (
                r"C:\Phase184Tests\backup\foxglove.previous.exe"
            ),
            "installedUtc": "2026-07-27T12:34:56Z",
        }
        if receipt_overrides is not None:
            receipt.update(receipt_overrides)
        environment.fs.write_receipt(receipt_destination, receipt)
        environment.events.clear()
        environment.fetch_urls.clear()
        return receipt_destination, receipt
    def assert_verifier_read_only(self, environment: FakeEnvironment) -> None:
        """Handle the assert verifier read only step."""

        mutation_events = {
            "copy-exclusive",
            "download",
            "ensure-parent",
            "publish-exclusive",
            "remove",
            "replace",
            "temp",
            "write-receipt",
        }
        self.assertFalse(
            any(event[0] in mutation_events for event in environment.events)
        )
        self.assertEqual([], environment.fetch_urls)
    def test_cli_contract_locks_endpoint_required_path_and_default_receipt(self):
        """Verify CLI contract locks endpoint required path and default receipt."""

        self.assertEqual(
            "https://api.github.com/repos/foxglove/foxglove-cli/releases/latest",
            installer.RELEASE_ENDPOINT,
        )
        parsed = installer.parse_args(["--install-path", INSTALL_PATH])
        self.assertEqual(INSTALL_PATH, parsed.install_path)
        self.assertEqual(
            ROOT
            / "build"
            / "phase184"
            / "tooling"
            / "foxglove-cli-install-receipt.json",
            pathlib.Path(parsed.receipt),
        )
        explicit = installer.parse_args(
            ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH]
        )
        self.assertEqual(RECEIPT_PATH, explicit.receipt)
        with (
            mock.patch("sys.stderr", new=io.StringIO()),
            self.assertRaises(SystemExit),
        ):
            installer.parse_args([])
    def test_direct_script_entrypoint_bootstraps_repository_imports(self):
        """Verify direct script entrypoint bootstraps repository imports."""

        environment = dict(os.environ)
        environment.pop("PYTHONHOME", None)
        environment.pop("PYTHONPATH", None)
        script = (
            ROOT
            / "Scripts"
            / "smoke"
            / "foxrun"
            / "phase184_foxglove_cli_install.py"
        )

        completed = subprocess.run(
            [sys.executable, str(script), "--help"],
            cwd=ROOT,
            env=environment,
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )

        self.assertEqual(
            0,
            completed.returncode,
            completed.stdout + completed.stderr,
        )
        self.assertIn("--install-path", completed.stdout)
        self.assertNotIn("ModuleNotFoundError", completed.stderr)
    def test_read_only_verifier_returns_immutable_exact_identity_and_document(self):
        """Verify read only verifier returns immutable exact identity and document."""

        environment = FakeEnvironment(self.physical_root)
        secret_backup = (
            r"C:\Phase184Tests\DO_NOT_REPORT_SECRET\foxglove.previous.exe"
        )
        receipt_destination, receipt = self.prepare_verifier_fixture(
            environment,
            receipt_overrides={"backupPath": secret_backup},
        )

        identity = self.verify_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            dependencies=environment.dependencies(),
        )

        identity_type = getattr(installer, "VerifiedCliIdentity", None)
        self.assertIsNotNone(identity_type)
        self.assertIsInstance(identity, identity_type)
        self.assertEqual(INSTALL_PATH, identity.installed_path)
        self.assertEqual("1.2.3", identity.installed_version)
        self.assertEqual(
            receipt["installedSha256"],
            identity.installed_sha256,
        )
        self.assertEqual("v1.2.3", identity.release_tag)
        self.assertEqual(OFFICIAL_ASSET_URL, identity.asset_url)
        self.assertEqual(protocol.CLI_ARCHITECTURE, identity.architecture)
        self.assertEqual(receipt_destination, identity.receipt_path)
        with self.assertRaises(dataclasses.FrozenInstanceError):
            setattr(identity, "installed_version", "9.9.9")

        document = identity.to_document()
        self.assertEqual(
            {
                "architecture": protocol.CLI_ARCHITECTURE,
                "assetUrl": OFFICIAL_ASSET_URL,
                "installedPath": INSTALL_PATH,
                "installedSha256": sha256_bytes(NEW_BYTES),
                "installedVersion": "1.2.3",
                "receiptPath": receipt_destination,
                "releaseTag": "v1.2.3",
            },
            document,
        )
        serialized = json.dumps(
            document,
            ensure_ascii=True,
            sort_keys=True,
        ).encode("utf-8")
        self.assertLessEqual(
            len(serialized),
            installer.MAX_VERIFIED_CLI_DOCUMENT_BYTES,
        )
        self.assertNotIn("DO_NOT_REPORT_SECRET", serialized.decode("utf-8"))
        self.assertNotIn("previousSha256", document)
        self.assertNotIn("backupPath", document)
        self.assertNotIn("installedUtc", document)
        self.assertEqual(
            2,
            sum(
                1
                for event in environment.events
                if event
                == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
            ),
        )
        self.assertEqual(
            1,
            sum(1 for event in environment.events if event[0] == "resolve"),
        )
        self.assert_verifier_read_only(environment)
    def test_read_only_verifier_accepts_scoped_relative_and_default_receipts(self):
        """Verify read only verifier accepts scoped relative and default receipts."""

        cases = (
            ("relative", RELATIVE_RECEIPT_PATH),
            ("default", str(installer.DEFAULT_RECEIPT_PATH)),
        )
        for name, receipt_argument in cases:
            with self.subTest(name=name):
                environment = FakeEnvironment(self.physical_root / name)
                receipt_destination, _ = self.prepare_verifier_fixture(
                    environment,
                    receipt_argument=receipt_argument,
                )

                identity = self.verify_cli(
                    INSTALL_PATH,
                    receipt_argument,
                    dependencies=environment.dependencies(),
                )

                self.assertEqual(receipt_destination, identity.receipt_path)
                self.assert_verifier_read_only(environment)
    def test_read_only_verifier_rejects_stale_dev_and_live_mismatches(self):
        """Verify read only verifier rejects stale dev and live mismatches."""

        cases = (
            "stale-path",
            "stale-hash",
            "stale-version",
            "dev-version",
            "resolver-path",
            "resolved-version",
            "resolved-hash",
            "installed-command",
            "resolver-command",
        )
        for case in cases:
            with self.subTest(case=case):
                environment = FakeEnvironment(self.physical_root / case)
                binary_bytes = OLD_BYTES if case == "dev-version" else NEW_BYTES
                receipt_overrides: dict[str, object] = {}
                if case == "stale-path":
                    receipt_overrides["installedPath"] = (
                        r"C:\Phase184Tests\stale\foxglove.exe"
                    )
                elif case == "stale-hash":
                    stale_hash = sha256_bytes(OLD_BYTES)
                    receipt_overrides.update(
                        {
                            "downloadSha256": stale_hash,
                            "installedSha256": stale_hash,
                        }
                    )
                self.prepare_verifier_fixture(
                    environment,
                    binary_bytes=binary_bytes,
                    receipt_overrides=receipt_overrides,
                )
                if case == "stale-version":
                    environment.installed_version = "9.9.9"
                elif case == "resolver-path":
                    environment.resolved_path = (
                        r"C:\Phase184Tests\other\foxglove.exe"
                    )
                    environment.fs.write_bytes(
                        environment.resolved_path,
                        NEW_BYTES,
                    )
                elif case == "resolved-version":
                    environment.resolved_version = "9.9.9"
                elif case == "resolved-hash":
                    environment.after_resolve_hook = (
                        lambda environment=environment:
                        environment.fs.write_bytes(
                            INSTALL_PATH,
                            b"resolved target changed",
                        )
                    )
                elif case == "installed-command":
                    environment.command_failure = RuntimeError(
                        "synthetic command failure with secret detail"
                    )
                elif case == "resolver-command":
                    environment.fail_resolver = True

                failure = self.assert_provenance_failure(
                    lambda environment=environment: self.verify_cli(
                        INSTALL_PATH,
                        RECEIPT_PATH,
                        dependencies=environment.dependencies(),
                    )
                )

                self.assertNotIn("secret detail", failure.message)
                self.assert_verifier_read_only(environment)
    def test_verifier_rejects_swapped_or_stale_candidate_before_execution(self):
        """Verify verifier rejects swapped or stale candidate before execution."""

        cases = ("swapped-bytes", "stale-path", "stale-hash")
        for case in cases:
            with self.subTest(case=case):
                environment = FakeEnvironment(self.physical_root / case)
                overrides: dict[str, object] = {}
                if case == "stale-path":
                    overrides["installedPath"] = (
                        r"C:\Phase184Tests\stale\foxglove.exe"
                    )
                elif case == "stale-hash":
                    overrides.update(
                        {
                            "downloadSha256": sha256_bytes(OLD_BYTES),
                            "installedSha256": sha256_bytes(OLD_BYTES),
                        }
                    )
                self.prepare_verifier_fixture(
                    environment,
                    receipt_overrides=overrides,
                )
                if case == "swapped-bytes":
                    environment.fs.write_bytes(INSTALL_PATH, OLD_BYTES)
                    environment.events.clear()

                self.assert_provenance_failure(
                    lambda environment=environment: self.verify_cli(
                        INSTALL_PATH,
                        RECEIPT_PATH,
                        dependencies=environment.dependencies(),
                    )
                )

                self.assertFalse(
                    any(event[0] == "run" for event in environment.events)
                )
                self.assert_verifier_read_only(environment)
    def test_candidate_version_children_receive_only_explicit_minimal_environment(self):
        """Verify candidate version children receive only explicit minimal environment."""

        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)

        identity = self.verify_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            dependencies=environment.dependencies(),
        )

        self.assertEqual("1.2.3", identity.installed_version)
        expected = {
            "PATH": r"C:\Windows\System32",
            "SystemRoot": r"C:\Windows",
            "TEMP": r"C:\Temp",
            "USERPROFILE": str(
                ROOT
                / "build"
                / "phase184"
                / "tooling"
                / "isolated-cli-user-profile"
            ),
        }
        self.assertEqual(
            [expected, expected],
            environment.command_environments,
        )
        self.assertEqual([expected], environment.resolver_environments)
        serialized = json.dumps(
            environment.command_environments
            + environment.resolver_environments,
            sort_keys=True,
        )
        for forbidden in (
            "GITHUB_TOKEN",
            "FOXGLOVE_API_KEY",
            "PHASE184G_TOKEN",
            "ROS_DISTRO",
            "RMW_IMPLEMENTATION",
            "UNRELATED",
            r"C:\Users\RealProfileWithCredentials",
        ):
            self.assertNotIn(forbidden, serialized)


__all__ = [name for name in globals() if not name.startswith("__")]
