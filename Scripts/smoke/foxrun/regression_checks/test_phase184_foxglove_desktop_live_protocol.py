#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure regressions for the Phase184-H Foxglove tooling protocol."""

from __future__ import annotations

import copy
import dataclasses
import hashlib
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


def valid_barrier_config(
    output_root: pathlib.Path,
    *,
    run_id: str = "phase184g-20260727-desktop01",
    token: str = "p184g_A1b2C3d4E5f6",
    positive_seconds: int = 1,
) -> dict[str, object]:
    return {
        "runId": run_id,
        "token": token,
        "outputRoot": str(output_root),
        "observationWindows": {"positiveSeconds": positive_seconds},
    }


def valid_barrier_document(config: dict[str, object]) -> dict[str, object]:
    token = config["token"]
    assert isinstance(token, str)
    return {
        "schemaVersion": 1,
        "runId": config["runId"],
        "tokenDigest": hashlib.sha256(token.encode("utf-8")).hexdigest().upper(),
        "state": "desktop-client-proved",
        "acceptedClients": 1,
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

    def assert_client_failure(self, callback, token: str) -> protocol.AcceptanceFailure:
        with self.assertRaises(protocol.AcceptanceFailure) as raised:
            callback()
        self.assertEqual(protocol.FAIL_CLIENT, raised.exception.code)
        self.assertNotIn(token, str(raised.exception))
        self.assertLessEqual(
            len(raised.exception.message),
            protocol.MAX_DIAGNOSTIC_CHARACTERS,
        )
        return raised.exception

    def assert_evidence_failure(
        self,
        callback,
        token: str,
    ) -> protocol.AcceptanceFailure:
        with self.assertRaises(protocol.AcceptanceFailure) as raised:
            callback()
        self.assertEqual(protocol.FAIL_EVIDENCE, raised.exception.code)
        self.assertNotIn(token, str(raised.exception))
        self.assertLessEqual(
            len(raised.exception.message),
            protocol.MAX_DIAGNOSTIC_CHARACTERS,
        )
        return raised.exception

    def test_desktop_client_barrier_path_and_document_are_exact_and_token_bound(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            document = valid_barrier_document(config)
            barrier = protocol.resolve_desktop_client_barrier_path(output)
            barrier.write_text(json.dumps(document), encoding="utf-8")

            self.assertEqual(
                output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME,
                barrier,
            )
            self.assertEqual(
                document,
                protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: 0.0,
                    sleep=lambda _: self.fail("visible barrier must not sleep"),
                ),
            )

    def test_desktop_client_barrier_rejects_non_owned_paths_and_traversal(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-path-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            valid = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            invalid = (
                output / "other.json",
                output / "nested" / ".." / protocol.DESKTOP_CLIENT_BARRIER_FILENAME,
                output.parent / protocol.DESKTOP_CLIENT_BARRIER_FILENAME,
                pathlib.Path(protocol.DESKTOP_CLIENT_BARRIER_FILENAME),
            )
            for path in invalid:
                with self.subTest(path=path):
                    self.assert_client_failure(
                        lambda path=path: protocol.wait_for_desktop_barrier(
                            config,
                            path,
                            clock=lambda: 0.0,
                            sleep=lambda _: None,
                            deadline=0.0,
                        ),
                        str(config["token"]),
                    )
            self.assertEqual(
                valid,
                protocol.resolve_desktop_client_barrier_path(output),
            )

    def test_desktop_client_barrier_rejects_every_malformed_schema_shape_immediately(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-json-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            token = str(config["token"])
            valid = valid_barrier_document(config)
            duplicate = (
                b'{"schemaVersion":1,"schemaVersion":1,'
                + json.dumps(
                    {
                        "runId": valid["runId"],
                        "tokenDigest": valid["tokenDigest"],
                        "state": valid["state"],
                        "acceptedClients": valid["acceptedClients"],
                    }
                )[1:].encode("utf-8")
            )
            mutations: dict[str, bytes] = {
                "empty": b"",
                "malformed": b'{"schemaVersion":',
                "invalid-utf8": b"\xff",
                "root-array": b"[]",
                "duplicate": duplicate,
                "extra": json.dumps(dict(valid, extra=True)).encode("utf-8"),
                "schema-bool": json.dumps(dict(valid, schemaVersion=True)).encode(
                    "utf-8"
                ),
                "schema-string": json.dumps(dict(valid, schemaVersion="1")).encode(
                    "utf-8"
                ),
                "schema-zero": json.dumps(dict(valid, schemaVersion=0)).encode(
                    "utf-8"
                ),
                "run-type": json.dumps(dict(valid, runId=1)).encode("utf-8"),
                "run-stale": json.dumps(
                    dict(valid, runId=str(config["runId"]) + "-stale")
                ).encode("utf-8"),
                "digest-type": json.dumps(dict(valid, tokenDigest=1)).encode("utf-8"),
                "digest-lower": json.dumps(
                    dict(valid, tokenDigest=str(valid["tokenDigest"]).lower())
                ).encode("utf-8"),
                "digest-short": json.dumps(dict(valid, tokenDigest="A" * 63)).encode(
                    "utf-8"
                ),
                "digest-mismatch": json.dumps(
                    dict(valid, tokenDigest="A" * 64)
                ).encode("utf-8"),
                "state-type": json.dumps(dict(valid, state=1)).encode("utf-8"),
                "state-wrong": json.dumps(
                    dict(valid, state="desktop-started")
                ).encode("utf-8"),
                "accepted-bool": json.dumps(
                    dict(valid, acceptedClients=True)
                ).encode("utf-8"),
                "accepted-string": json.dumps(
                    dict(valid, acceptedClients="1")
                ).encode("utf-8"),
                "accepted-zero": json.dumps(
                    dict(valid, acceptedClients=0)
                ).encode("utf-8"),
                "accepted-two": json.dumps(
                    dict(valid, acceptedClients=2)
                ).encode("utf-8"),
                "oversize": b"x"
                * (protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES + 1),
            }
            for key in valid:
                incomplete = dict(valid)
                del incomplete[key]
                mutations[f"missing-{key}"] = json.dumps(incomplete).encode("utf-8")

            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            for label, payload in mutations.items():
                with self.subTest(label=label):
                    barrier.write_bytes(payload)
                    sleep = mock.Mock()
                    self.assert_client_failure(
                        lambda: protocol.wait_for_desktop_barrier(
                            config,
                            barrier,
                            clock=lambda: 0.0,
                            sleep=sleep,
                        ),
                        token,
                    )
                    sleep.assert_not_called()

    def test_desktop_client_barrier_missing_wait_uses_injected_bounded_deadline(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-wait-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [10.0]
            sleeps: list[float] = []

            def clock() -> float:
                return now[0]

            def sleep(seconds: float) -> None:
                sleeps.append(seconds)
                now[0] += seconds

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=clock,
                    sleep=sleep,
                    deadline=10.75,
                ),
                str(config["token"]),
            )
            self.assertGreater(len(sleeps), 1)
            self.assertAlmostEqual(10.75, now[0])
            self.assertTrue(
                all(
                    0 < value <= protocol.DESKTOP_CLIENT_BARRIER_POLL_SECONDS
                    for value in sleeps
                )
            )

    def test_desktop_client_barrier_default_deadline_uses_window_plus_allowance(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-window-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output, positive_seconds=2)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [100.0]

            def clock() -> float:
                return now[0]

            def sleep(seconds: float) -> None:
                now[0] += seconds

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=clock,
                    sleep=sleep,
                ),
                str(config["token"]),
            )
            self.assertAlmostEqual(
                100.0
                + 2
                + protocol.DESKTOP_CLIENT_BARRIER_STARTUP_ALLOWANCE_SECONDS,
                now[0],
            )

    def test_desktop_client_barrier_polls_then_accepts_atomic_document(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-appears-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            document = valid_barrier_document(config)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [0.0]
            sleeps: list[float] = []

            def sleep(seconds: float) -> None:
                sleeps.append(seconds)
                now[0] += seconds
                protocol.write_json_atomic(
                    barrier,
                    document,
                    max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
                )

            self.assertEqual(
                document,
                protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: now[0],
                    sleep=sleep,
                    deadline=1.0,
                ),
            )
            self.assertEqual(1, len(sleeps))

    def test_desktop_client_barrier_rejects_valid_document_created_after_deadline(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-late-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            document = valid_barrier_document(config)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [0.0]
            sleeps: list[float] = []

            def sleep(seconds: float) -> None:
                sleeps.append(seconds)
                now[0] = 1.25
                protocol.write_json_atomic(
                    barrier,
                    document,
                    max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
                )

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: now[0],
                    sleep=sleep,
                    deadline=1.0,
                ),
                str(config["token"]),
            )
            self.assertEqual(1, len(sleeps))
            self.assertGreater(sleeps[0], 0)
            self.assertLessEqual(
                sleeps[0],
                protocol.DESKTOP_CLIENT_BARRIER_POLL_SECONDS,
            )
            self.assertTrue(barrier.is_file())

    def test_desktop_client_barrier_at_exact_deadline_times_out_before_read(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-equal-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            protocol.write_json_atomic(
                barrier,
                valid_barrier_document(config),
                max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
            )

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: 5.0,
                    sleep=lambda _: self.fail("expired barrier must not sleep"),
                    deadline=5.0,
                ),
                str(config["token"]),
            )

    def test_desktop_client_barrier_clock_failure_precedes_visible_document(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_ClockFailureRawToken77"
        with tempfile.TemporaryDirectory(prefix="barrier-clock-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output, token=token)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            protocol.write_json_atomic(
                barrier,
                valid_barrier_document(config),
                max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
            )
            clocks = (
                mock.Mock(side_effect=(0.0, float("nan"))),
                mock.Mock(side_effect=(0.0, RuntimeError(token))),
            )
            for clock in clocks:
                with self.subTest(clock=clock):
                    self.assert_client_failure(
                        lambda clock=clock: protocol.wait_for_desktop_barrier(
                            config,
                            barrier,
                            clock=clock,
                            sleep=lambda _: self.fail("clock failure must not sleep"),
                            deadline=1.0,
                        ),
                        token,
                    )

    def test_desktop_client_barrier_rejects_symlink_or_reparse_alias(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-link-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            target = output / "atomic-target.json"
            target.write_text(
                json.dumps(valid_barrier_document(config)),
                encoding="utf-8",
            )
            try:
                barrier.symlink_to(target)
            except OSError:
                self.skipTest("This Windows identity cannot create a test symlink.")
            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: 0.0,
                    sleep=lambda _: None,
                ),
                str(config["token"]),
            )

    def test_transport_client_marker_parser_accepts_only_exact_bounded_envelopes(self):
        token = "p184g_A1b2C3d4E5f6"
        case = "foxglove-profile"
        normal = protocol.parse_transport_client_marker(
            f"{protocol.TRANSPORT_CLIENTS_MARKER} case={case} token={token} "
            "active=0 accepted=0",
            case=case,
            token=token,
        )
        overflow = protocol.parse_transport_client_marker(
            f"{protocol.TRANSPORT_CLIENTS_OVERFLOW_MARKER} case={case} "
            f"token={token} active=8 accepted=13",
            case=case,
            token=token,
        )
        self.assertFalse(normal.overflow)
        self.assertEqual((0, 0), (normal.active, normal.accepted))
        self.assertTrue(overflow.overflow)
        self.assertEqual((8, 13), (overflow.active, overflow.accepted))
        self.assertEqual(
            {"active": 0, "accepted": 0},
            normal.to_document(),
        )
        with self.assertRaises(dataclasses.FrozenInstanceError):
            normal.active = 9

    def test_transport_client_marker_parser_rejects_every_malformed_envelope(self):
        token = "p184g_UniqueRawToken77"
        case = "foxglove-profile"
        marker = protocol.TRANSPORT_CLIENTS_MARKER
        valid = f"{marker} case={case} token={token} active=1 accepted=1"
        invalid = (
            "",
            f"PHASE184H_TRANSPORT_CLIENT case={case} token={token} active=1 accepted=1",
            f"{marker} token={token} active=1 accepted=1",
            f"{marker} case={case} case={case} token={token} active=1 accepted=1",
            f"{marker} case={case} token={token} active=1 accepted=1 extra=1",
            f"{marker} token={token} case={case} active=1 accepted=1",
            f"{marker} case=other token={token} active=1 accepted=1",
            f"{marker} case={case} token=stale active=1 accepted=1",
            f"{marker} case={case} token={token} active=True accepted=1",
            f"{marker} case={case} token={token} active=-1 accepted=1",
            f"{marker} case={case} token={token} active=01 accepted=1",
            f"{marker} case={case} token={token} active=1 accepted=False",
            f"{marker} case={case} token={token} active=1 accepted=-1",
            f"{marker} case={case} token={token} active=1 accepted=01",
            f"{marker} case={case} token={token} "
            f"active={protocol.MAX_TRANSPORT_CLIENT_COUNT + 1} accepted=1",
            f"{marker} case={case} token={token} active=1 "
            f"accepted={protocol.MAX_TRANSPORT_CLIENT_COUNT + 1}",
            valid + "\n",
            valid + ("x" * protocol.MAX_TRANSPORT_CLIENT_MARKER_BYTES),
        )
        for line in invalid:
            with self.subTest(line=line[:80]):
                self.assert_evidence_failure(
                    lambda line=line: protocol.parse_transport_client_marker(
                        line,
                        case=case,
                        token=token,
                    ),
                    token,
                )

    def test_transport_client_markers_require_zero_one_two_in_strict_order(self):
        token = "p184g_A1b2C3d4E5f6"
        case = "foxglove-profile"
        lines = [
            "unrelated log line",
            f"PHASE184H_TRANSPORT_CLIENTS case={case} token={token} active=0 accepted=0",
            f"PHASE184H_TRANSPORT_CLIENTS case={case} token={token} active=0 accepted=0",
            f"PHASE184H_TRANSPORT_CLIENTS case={case} token={token} active=1 accepted=1",
            f"PHASE184H_TRANSPORT_CLIENTS case={case} token={token} active=2 accepted=3",
        ]
        markers = protocol.validate_transport_client_transition_order(
            lines,
            case=case,
            token=token,
        )
        self.assertIsInstance(markers, tuple)
        self.assertEqual([0, 1, 2], [marker.active for marker in markers])
        self.assertEqual([0, 1, 3], [marker.accepted for marker in markers])

    def test_transport_client_markers_reject_overflow_wrong_identity_and_order(self):
        token = "p184g_A1b2C3d4E5f6"
        case = "foxglove-profile"
        zero = (
            f"{protocol.TRANSPORT_CLIENTS_MARKER} case={case} token={token} "
            "active=0 accepted=0"
        )
        one = (
            f"{protocol.TRANSPORT_CLIENTS_MARKER} case={case} token={token} "
            "active=1 accepted=1"
        )
        two = (
            f"{protocol.TRANSPORT_CLIENTS_MARKER} case={case} token={token} "
            "active=2 accepted=2"
        )
        invalid_sets = (
            [],
            [one, two],
            [zero, two, one],
            [zero, one],
            [
                zero,
                f"{protocol.TRANSPORT_CLIENTS_MARKER} case=other token={token} "
                "active=1 accepted=1",
                two,
            ],
            [
                zero,
                f"{protocol.TRANSPORT_CLIENTS_MARKER} case={case} token=stale "
                "active=1 accepted=1",
                two,
            ],
            [
                zero,
                one,
                f"{protocol.TRANSPORT_CLIENTS_OVERFLOW_MARKER} case={case} "
                f"token={token} active=2 accepted=2",
            ],
        )
        for lines in invalid_sets:
            with self.subTest(lines=lines):
                self.assert_evidence_failure(
                    lambda lines=lines: protocol.validate_transport_client_transition_order(
                        lines,
                        case=case,
                        token=token,
                    ),
                    token,
                )

    def test_transport_chronology_rejects_regression_and_nonconsecutive_reappearance(self):
        token = "p184g_A1b2C3d4E5f6"
        case = "foxglove-profile"

        def marker(active: int, accepted: int) -> str:
            return (
                f"{protocol.TRANSPORT_CLIENTS_MARKER} "
                f"case={case} token={token} "
                f"active={active} accepted={accepted}"
            )

        invalid_sets = (
            [
                marker(0, 0),
                marker(1, 1),
                marker(0, 0),
                marker(2, 2),
            ],
            [
                marker(0, 0),
                marker(1, 1),
                marker(0, 0),
                marker(1, 1),
                marker(2, 2),
            ],
        )
        for lines in invalid_sets:
            with self.subTest(lines=lines):
                self.assert_evidence_failure(
                    lambda lines=lines: (
                        protocol.validate_transport_client_transition_order(
                            lines,
                            case=case,
                            token=token,
                        )
                    ),
                    token,
                )

    def test_protocol_has_no_ambient_environment_network_or_process_access(self):
        source = inspect.getsource(protocol)
        self.assertNotIn("os.environ", source)
        self.assertNotIn("os.getenv", source)
        self.assertNotIn("subprocess", source)
        self.assertNotIn("socket", source)
        self.assertNotIn("websockets", source)
        self.assertNotIn("requests", source)
        self.assertNotIn("urllib.request", source)


if __name__ == "__main__":
    unittest.main()
