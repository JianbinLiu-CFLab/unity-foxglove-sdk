from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveDesktopLiveProtocolTests_support:
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
    def test_public_constants_lock_receipt_and_terminal_protocol(self):
        """Verify public constants lock receipt and terminal protocol."""

        self.assertEqual(1, protocol.CLI_RECEIPT_SCHEMA_VERSION)
        self.assertEqual("windows-amd64", protocol.CLI_ARCHITECTURE)
        self.assertEqual(
            "foxglove-windows-amd64.exe",
            protocol.CLI_ASSET_NAME,
        )
        self.assertEqual(
            frozenset(
                {
                    "foxglove-windows-amd64",
                    "foxglove-windows-amd64.exe",
                }
            ),
            protocol.CLI_ASSET_NAMES,
        )
        self.assertEqual(
            frozenset(
                {
                    "schemaVersion",
                    "releaseTag",
                    "releaseVersion",
                    "architecture",
                    "assetName",
                    "assetUrl",
                    "downloadSha256",
                    "downloadVersion",
                    "installedPath",
                    "installedSha256",
                    "installedVersion",
                    "previousSha256",
                    "backupPath",
                    "installedUtc",
                }
            ),
            protocol.CLI_RECEIPT_KEYS,
        )
        self.assertEqual(
            {
                "FAIL_CLI_PROVENANCE",
                "FAIL_DESKTOP_PREFLIGHT",
                "FAIL_DESKTOP_START",
                "FAIL_DESKTOP_IDENTITY",
                "FAIL_DESKTOP_CONNECTION",
                "FAIL_CLIENT",
                "FAIL_FOXRUN_CHILD",
                "FAIL_EVIDENCE",
                "FAIL_CLEANUP",
            },
            protocol.TERMINAL_FAILURE_CODES,
        )
        self.assertEqual(
            "desktop-client-barrier.json",
            protocol.DESKTOP_CLIENT_BARRIER_FILENAME,
        )
        self.assertEqual(1, protocol.DESKTOP_CLIENT_BARRIER_SCHEMA_VERSION)
        self.assertEqual(
            "desktop-client-proved",
            protocol.DESKTOP_CLIENT_BARRIER_STATE,
        )
        self.assertGreater(protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES, 0)
        self.assertGreater(
            protocol.DESKTOP_CLIENT_BARRIER_STARTUP_ALLOWANCE_SECONDS,
            0,
        )
        self.assertEqual(
            120.0,
            protocol.DESKTOP_CLIENT_BARRIER_STARTUP_ALLOWANCE_SECONDS,
        )
    def test_acceptance_failure_keeps_stable_code_and_bounds_one_line_message(self):
        """Verify acceptance failure keeps stable code and bounds one line message."""

        failure = protocol.AcceptanceFailure(
            protocol.FAIL_CLI_PROVENANCE,
            "unsafe\r\n" + ("x" * (protocol.MAX_DIAGNOSTIC_CHARACTERS * 2)),
        )
        self.assertEqual(protocol.FAIL_CLI_PROVENANCE, failure.code)
        self.assertLessEqual(
            len(failure.message),
            protocol.MAX_DIAGNOSTIC_CHARACTERS,
        )
        self.assertNotIn("\r", failure.message)
        self.assertNotIn("\n", failure.message)
        self.assertTrue(str(failure).startswith("FAIL_CLI_PROVENANCE: "))
    def test_semantic_version_normalizes_only_one_stable_version(self):
        """Verify semantic version normalizes only one stable version."""

        for value in ("1.2.3", "v1.2.3", "  v184.0.27\r\n"):
            with self.subTest(value=value):
                expected = value.strip().removeprefix("v")
                self.assertEqual(
                    expected,
                    protocol.normalize_semantic_version(value),
                )

        invalid = (
            "",
            " ",
            "dev",
            "vdev",
            "1.2",
            "1.2.3.4",
            "01.2.3",
            "1.02.3",
            "1.2.03",
            "V1.2.3",
            "version 1.2.3",
            "1.2.3 1.2.4",
            "1.2.3\n1.2.4",
            "1.2.3-dev",
            "1.2.3-rc.1",
            "1.2.3+build.7",
            "v1.2.3-beta+build",
            None,
        )
        for value in invalid:
            with self.subTest(value=value):
                self.assert_provenance_failure(
                    lambda value=value: protocol.normalize_semantic_version(value)
                )
    def test_sha256_helpers_use_exact_uppercase_hex(self):
        """Verify SHA-256 helpers use exact uppercase hex."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="sha-", dir=TEST_ROOT) as raw:
            path = pathlib.Path(raw) / "payload.bin"
            path.write_bytes(b"phase184h\n")
            digest = protocol.sha256_file(path)

        self.assertEqual(64, len(digest))
        self.assertRegex(digest, r"\A[0-9A-F]{64}\Z")
        self.assertEqual(digest, protocol.validate_sha256(digest))
        for value in (
            digest.lower(),
            digest[:-1],
            digest + "0",
            "G" * 64,
            " " + digest,
            digest + "\n",
            None,
        ):
            with self.subTest(value=value):
                self.assert_provenance_failure(
                    lambda value=value: protocol.validate_sha256(value)
                )
    def test_official_asset_url_requires_exact_origin_repo_route_tag_and_asset(self):
        """Verify official asset url requires exact origin repo route tag and asset."""

        for tag in ("v1.2.3", "1.2.3"):
            for asset_name in (
                "foxglove-windows-amd64",
                "foxglove-windows-amd64.exe",
            ):
                with self.subTest(tag=tag, asset_name=asset_name):
                    url = (
                        "https://github.com/foxglove/foxglove-cli/"
                        f"releases/download/{tag}/{asset_name}"
                    )
                    self.assertEqual(
                        "1.2.3",
                        protocol.validate_official_asset_url(
                            url,
                            expected_release_version="v1.2.3",
                        ),
                    )

        invalid = (
            "http://github.com/foxglove/foxglove-cli/releases/download/v1.2.3/"
            "foxglove-windows-amd64.exe",
            "https://github.com.evil.example/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe",
            "https://github.com@evil.example/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe",
            "https://github.com:443/foxglove/foxglove-cli/releases/download/v1.2.3/"
            "foxglove-windows-amd64.exe",
            "https://github.com/Foxglove/foxglove-cli/releases/download/v1.2.3/"
            "foxglove-windows-amd64.exe",
            "https://github.com/foxglove/foxglove-cli-lookalike/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe",
            "https://github.com/foxglove/foxglove-cli/releases/download/v1.2.3/"
            "foxglove-linux-amd64",
            "https://github.com/foxglove/foxglove-cli/releases/download/v1.2.3/"
            "foxglove-windows-amd64.exe?token=secret",
            "https://github.com/foxglove/foxglove-cli/releases/download/v1.2.3/"
            "foxglove-windows-amd64.exe#fragment",
            "https://github.com/%66oxglove/foxglove-cli/releases/download/v1.2.3/"
            "foxglove-windows-amd64.exe",
            "https://github.com/foxglove/foxglove-cli/releases/download/v1.2.4/"
            "foxglove-windows-amd64.exe",
            "\x00https://github.com/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe",
            "HTTPS://github.com/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe",
            "https://git\thub.com/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe",
            "https://github.com/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe?",
            "https://github.com/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe#",
        )
        for value in invalid:
            with self.subTest(value=value):
                self.assert_provenance_failure(
                    lambda value=value: protocol.validate_official_asset_url(
                        value,
                        expected_release_version="1.2.3",
                    )
                )
    def test_receipt_accepts_each_exact_asset_alias_only_when_url_matches(self):
        """Verify receipt accepts each exact asset alias only when url matches."""

        for asset_name in (
            "foxglove-windows-amd64",
            "foxglove-windows-amd64.exe",
        ):
            with self.subTest(asset_name=asset_name):
                receipt = valid_receipt()
                receipt["assetName"] = asset_name
                receipt["assetUrl"] = (
                    "https://github.com/foxglove/foxglove-cli/releases/"
                    f"download/v1.2.3/{asset_name}"
                )
                self.assertEqual(
                    receipt,
                    protocol.validate_cli_receipt(
                        receipt,
                        receipt["installedPath"],
                        receipt["installedVersion"],
                        receipt["installedSha256"],
                    ),
                )

        mismatched = valid_receipt()
        mismatched["assetName"] = "foxglove-windows-amd64"
        with self.assertRaises(protocol.AcceptanceFailure):
            protocol.validate_cli_receipt(
                mismatched,
                mismatched["installedPath"],
                mismatched["installedVersion"],
                mismatched["installedSha256"],
            )
    def test_valid_receipt_matches_live_cli_with_windows_path_normalization(self):
        """Verify valid receipt matches live CLI with windows path normalization."""

        receipt = valid_receipt()
        original = copy.deepcopy(receipt)
        validated = protocol.validate_cli_receipt(
            receipt,
            installed_path="c:/users/tester/go/bin/FOXGLOVE.exe",
            installed_version="v1.2.3",
            installed_sha256="A" * 64,
        )
        self.assertEqual(original, receipt)
        self.assertEqual(receipt, validated)
    def test_receipt_rejects_every_missing_key_and_any_extra_or_secret_field(self):
        """Verify receipt rejects every missing key and any extra or secret field."""

        baseline = valid_receipt()
        for key in protocol.CLI_RECEIPT_KEYS:
            with self.subTest(missing=key):
                receipt = dict(baseline)
                receipt.pop(key)
                self.assert_provenance_failure(
                    lambda receipt=receipt: protocol.validate_cli_receipt(
                        receipt,
                        baseline["installedPath"],
                        baseline["installedVersion"],
                        baseline["installedSha256"],
                    )
                )

        for key in ("unexpected", "environment", "password", "authorization"):
            with self.subTest(extra=key):
                receipt = dict(baseline, **{key: "must-not-persist"})
                self.assert_provenance_failure(
                    lambda receipt=receipt: protocol.validate_cli_receipt(
                        receipt,
                        baseline["installedPath"],
                        baseline["installedVersion"],
                        baseline["installedSha256"],
                    )
                )
    def test_receipt_rejects_schema_architecture_asset_and_official_url_drift(self):
        """Verify receipt rejects schema architecture asset and official url drift."""

        mutations = {
            "schema": ("schemaVersion", 2),
            "boolean-schema": ("schemaVersion", True),
            "architecture": ("architecture", "linux-amd64"),
            "asset": ("assetName", "foxglove-windows-arm64.exe"),
            "host": (
                "assetUrl",
                "https://example.com/foxglove/foxglove-cli/releases/download/"
                "v1.2.3/foxglove-windows-amd64.exe",
            ),
        }
        baseline = valid_receipt()
        for label, (key, value) in mutations.items():
            with self.subTest(label=label):
                receipt = dict(baseline, **{key: value})
                self.assert_provenance_failure(
                    lambda receipt=receipt: protocol.validate_cli_receipt(
                        receipt,
                        baseline["installedPath"],
                        baseline["installedVersion"],
                        baseline["installedSha256"],
                    )
                )
    def test_receipt_rejects_every_version_or_hash_mismatch(self):
        """Verify receipt rejects every version or hash mismatch."""

        baseline = valid_receipt()
        for key in (
            "releaseTag",
            "releaseVersion",
            "downloadVersion",
            "installedVersion",
        ):
            with self.subTest(version_field=key):
                receipt = dict(baseline, **{key: "1.2.4"})
                self.assert_provenance_failure(
                    lambda receipt=receipt: protocol.validate_cli_receipt(
                        receipt,
                        baseline["installedPath"],
                        baseline["installedVersion"],
                        baseline["installedSha256"],
                    )
                )

        self.assert_provenance_failure(
            lambda: protocol.validate_cli_receipt(
                baseline,
                baseline["installedPath"],
                "1.2.4",
                baseline["installedSha256"],
            )
        )
        for key in ("downloadSha256", "installedSha256"):
            with self.subTest(hash_field=key):
                receipt = dict(baseline, **{key: "C" * 64})
                self.assert_provenance_failure(
                    lambda receipt=receipt: protocol.validate_cli_receipt(
                        receipt,
                        baseline["installedPath"],
                        baseline["installedVersion"],
                        baseline["installedSha256"],
                    )
                )
        receipt = dict(baseline, previousSha256="b" * 64)
        self.assert_provenance_failure(
            lambda: protocol.validate_cli_receipt(
                receipt,
                baseline["installedPath"],
                baseline["installedVersion"],
                baseline["installedSha256"],
            )
        )
        self.assert_provenance_failure(
            lambda: protocol.validate_cli_receipt(
                baseline,
                baseline["installedPath"],
                baseline["installedVersion"],
                "C" * 64,
            )
        )


__all__ = [name for name in globals() if not name.startswith("__")]
