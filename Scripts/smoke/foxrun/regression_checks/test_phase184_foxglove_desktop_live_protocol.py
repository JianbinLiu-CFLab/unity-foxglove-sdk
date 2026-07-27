#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure regressions for the Phase184-H Foxglove tooling protocol."""

from __future__ import annotations

import copy
import inspect
import json
import os
import pathlib
import tempfile
import unittest
from unittest import mock

from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_protocol as protocol


ROOT = pathlib.Path(__file__).resolve().parents[4]
TEST_ROOT = ROOT / "build" / "Tests" / "Phase184HProtocol"


def valid_receipt() -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "releaseTag": "v1.2.3",
        "releaseVersion": "1.2.3",
        "architecture": "windows-amd64",
        "assetName": "foxglove-windows-amd64.exe",
        "assetUrl": (
            "https://github.com/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe"
        ),
        "downloadSha256": "A" * 64,
        "downloadVersion": "v1.2.3",
        "installedPath": r"C:\Users\Tester\go\bin\foxglove.exe",
        "installedSha256": "A" * 64,
        "installedVersion": "1.2.3",
        "previousSha256": "B" * 64,
        "backupPath": r"C:\Users\Tester\go\bin\foxglove.dev-BBBBBBBB.exe",
        "installedUtc": "2026-07-27T12:34:56Z",
    }


class Phase184FoxgloveDesktopLiveProtocolTests(unittest.TestCase):
    def assert_provenance_failure(self, callback) -> protocol.AcceptanceFailure:
        with self.assertRaises(protocol.AcceptanceFailure) as raised:
            callback()
        self.assertEqual(protocol.FAIL_CLI_PROVENANCE, raised.exception.code)
        self.assertLessEqual(
            len(raised.exception.message),
            protocol.MAX_DIAGNOSTIC_CHARACTERS,
        )
        return raised.exception

    def test_public_constants_lock_receipt_and_terminal_protocol(self):
        self.assertEqual(1, protocol.CLI_RECEIPT_SCHEMA_VERSION)
        self.assertEqual("windows-amd64", protocol.CLI_ARCHITECTURE)
        self.assertEqual(
            "foxglove-windows-amd64.exe",
            protocol.CLI_ASSET_NAME,
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
                "FAIL_FOXRUN_CHILD",
                "FAIL_EVIDENCE",
                "FAIL_CLEANUP",
            },
            protocol.TERMINAL_FAILURE_CODES,
        )

    def test_acceptance_failure_keeps_stable_code_and_bounds_one_line_message(self):
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
        for tag in ("v1.2.3", "1.2.3"):
            with self.subTest(tag=tag):
                url = (
                    "https://github.com/foxglove/foxglove-cli/releases/download/"
                    f"{tag}/foxglove-windows-amd64.exe"
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
        )
        for value in invalid:
            with self.subTest(value=value):
                self.assert_provenance_failure(
                    lambda value=value: protocol.validate_official_asset_url(
                        value,
                        expected_release_version="1.2.3",
                    )
                )

    def test_valid_receipt_matches_live_cli_with_windows_path_normalization(self):
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

    def test_receipt_rejects_drive_and_unc_namespace_aliases_in_both_directions(self):
        baseline = valid_receipt()
        aliases = (
            (
                r"C:\Tools\foxglove.exe",
                r"\\?\C:\Tools\foxglove.exe",
            ),
            (
                r"\\?\C:\Tools\foxglove.exe",
                r"C:\Tools\foxglove.exe",
            ),
            (
                r"\\server\share\foxglove.exe",
                r"\\?\UNC\server\share\foxglove.exe",
            ),
            (
                r"\\?\UNC\server\share\foxglove.exe",
                r"\\server\share\foxglove.exe",
            ),
        )
        for installed_path, backup_path in aliases:
            with self.subTest(
                installed_path=installed_path,
                backup_path=backup_path,
            ):
                receipt = dict(
                    baseline,
                    installedPath=installed_path,
                    backupPath=backup_path,
                )
                self.assert_provenance_failure(
                    lambda receipt=receipt, installed_path=installed_path: (
                        protocol.validate_cli_receipt(
                            receipt,
                            installed_path,
                            baseline["installedVersion"],
                            baseline["installedSha256"],
                        )
                    )
                )

    def test_public_windows_path_identity_normalizes_aliases_case_and_separators(self):
        path_key = getattr(protocol, "windows_path_key", None)
        paths_equal = getattr(protocol, "windows_paths_equal", None)
        self.assertTrue(callable(path_key))
        self.assertTrue(callable(paths_equal))

        aliases = (
            (
                r"C:\Tools\Foxglove.exe",
                r"\\?\c:/tools/foxglove.EXE",
                r"c:\tools\foxglove.exe",
            ),
            (
                r"\\Server\Share\Foxglove.exe",
                r"//?/UNC/server/share/foxglove.EXE",
                r"\\server\share\foxglove.exe",
            ),
        )
        for ordinary, extended, expected_key in aliases:
            with self.subTest(ordinary=ordinary, extended=extended):
                self.assertEqual(expected_key, path_key(ordinary))
                self.assertEqual(expected_key, path_key(extended))
                self.assertTrue(paths_equal(ordinary, extended))
                self.assertTrue(paths_equal(extended, ordinary))

        for invalid in (
            "foxglove.exe",
            r"C:relative\foxglove.exe",
            r"\root-relative\foxglove.exe",
            r"\\?\relative\foxglove.exe",
            r"\\?\UNC\server-only",
            "/usr/local/bin/foxglove",
        ):
            with self.subTest(invalid=invalid):
                self.assert_provenance_failure(
                    lambda invalid=invalid: path_key(invalid)
                )

    def test_receipt_requires_absolute_windows_paths_and_exact_live_path(self):
        baseline = valid_receipt()
        unc_receipt = dict(
            baseline,
            installedPath=r"\\server\share\foxglove.exe",
            backupPath=r"\\server\share\foxglove.dev-BBBBBBBB.exe",
        )
        protocol.validate_cli_receipt(
            unc_receipt,
            "//SERVER/share/FOXGLOVE.exe",
            baseline["installedVersion"],
            baseline["installedSha256"],
        )

        invalid_paths = (
            "foxglove.exe",
            r"C:relative\foxglove.exe",
            r"\root-relative\foxglove.exe",
            "/usr/local/bin/foxglove",
            "",
        )
        for key in ("installedPath", "backupPath"):
            for value in invalid_paths:
                with self.subTest(field=key, value=value):
                    receipt = dict(baseline, **{key: value})
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
                r"C:\Other\foxglove.exe",
                baseline["installedVersion"],
                baseline["installedSha256"],
            )
        )
        same_backup = dict(
            baseline,
            backupPath=r"c:/users/tester/go/bin/FOXGLOVE.exe",
        )
        self.assert_provenance_failure(
            lambda: protocol.validate_cli_receipt(
                same_backup,
                baseline["installedPath"],
                baseline["installedVersion"],
                baseline["installedSha256"],
            )
        )

    def test_receipt_accepts_canonical_utc_timestamps_and_rejects_malformed_values(self):
        baseline = valid_receipt()
        for value in (
            "2026-07-27T12:34:56Z",
            "2026-07-27T12:34:56.123456Z",
        ):
            with self.subTest(valid=value):
                receipt = dict(baseline, installedUtc=value)
                protocol.validate_cli_receipt(
                    receipt,
                    baseline["installedPath"],
                    baseline["installedVersion"],
                    baseline["installedSha256"],
                )

        invalid = (
            "",
            "2026-02-30T12:34:56Z",
            "2026-07-27 12:34:56Z",
            "2026-07-27T12:34:56",
            "2026-07-27T12:34:56+00:00",
            "2026-07-27T12:34:56.1234567Z",
            "2026-07-27T12:34:56z",
            None,
        )
        for value in invalid:
            with self.subTest(invalid=value):
                receipt = dict(baseline, installedUtc=value)
                self.assert_provenance_failure(
                    lambda receipt=receipt: protocol.validate_cli_receipt(
                        receipt,
                        baseline["installedPath"],
                        baseline["installedVersion"],
                        baseline["installedSha256"],
                    )
                )

    def test_bounded_receipt_loader_rejects_malformed_duplicate_and_oversize_json(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="load-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            valid_path = root / "receipt.json"
            valid_path.write_text(json.dumps(valid_receipt()), encoding="utf-8")
            self.assertEqual(valid_receipt(), protocol.load_cli_receipt(valid_path))

            invalid_payloads = {
                "empty.json": b"",
                "array.json": b"[]",
                "malformed.json": b'{"schemaVersion":',
                "duplicate.json": b'{"schemaVersion":1,"schemaVersion":1}',
                "utf8.json": b"\xff",
                "deep.json": (b"[" * 2000) + (b"]" * 2000),
                "integer.json": b'{"schemaVersion":' + (b"1" * 5000) + b"}",
                "oversize.json": b"{" + (b"x" * protocol.MAX_RECEIPT_BYTES) + b"}",
            }
            for name, payload in invalid_payloads.items():
                with self.subTest(name=name):
                    path = root / name
                    path.write_bytes(payload)
                    failure = self.assert_provenance_failure(
                        lambda path=path: protocol.load_cli_receipt(path)
                    )
                    self.assertNotIn("x" * 128, failure.message)

    def test_atomic_json_writer_is_deterministic_bounded_and_uses_sibling_replace(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="write-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            target = root / "nested" / "receipt.json"
            real_replace = os.replace
            replacements: list[tuple[pathlib.Path, pathlib.Path]] = []

            def recording_replace(source, destination):
                replacements.append((pathlib.Path(source), pathlib.Path(destination)))
                real_replace(source, destination)

            with mock.patch.object(
                protocol.os,
                "replace",
                side_effect=recording_replace,
            ):
                protocol.write_json_atomic(target, {"b": 2, "a": 1})
            first = target.read_bytes()
            protocol.write_json_atomic(target, {"a": 1, "b": 2})
            second = target.read_bytes()

            self.assertEqual(b'{"a":1,"b":2}\n', first)
            self.assertEqual(first, second)
            self.assertEqual(target, replacements[0][1])
            self.assertEqual(target.parent, replacements[0][0].parent)
            self.assertTrue(replacements[0][0].name.startswith(target.name + "."))
            self.assertTrue(replacements[0][0].name.endswith(".tmp"))
            self.assertEqual([], list(target.parent.glob("*.tmp")))

            previous = target.read_bytes()
            self.assert_provenance_failure(
                lambda: protocol.write_json_atomic(
                    target,
                    {"oversize": "x" * protocol.MAX_RECEIPT_BYTES},
                )
            )
            self.assertEqual(previous, target.read_bytes())
            self.assertEqual([], list(target.parent.glob("*.tmp")))

            self.assert_provenance_failure(
                lambda: protocol.write_json_atomic(
                    target,
                    {"invalid": float("nan")},
                )
            )
            self.assertEqual(previous, target.read_bytes())
            self.assertEqual([], list(target.parent.glob("*.tmp")))

    def test_protocol_has_no_ambient_environment_network_or_process_access(self):
        source = inspect.getsource(protocol)
        self.assertNotIn("os.environ", source)
        self.assertNotIn("os.getenv", source)
        self.assertNotIn("subprocess", source)
        self.assertNotIn("urllib.request", source)


if __name__ == "__main__":
    unittest.main()
