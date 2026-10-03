from __future__ import annotations
from .class_phase184_foxglove_desktop_live_protocol_tests_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveDesktopLiveProtocolTests_fixtures:
    """Decomposed Phase192 implementation component."""
    def test_receipt_rejects_drive_and_unc_namespace_aliases_in_both_directions(self):
        """Verify receipt rejects drive and unc namespace aliases in both directions."""

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
        """Verify public windows path identity normalizes aliases case and separators."""

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
        """Verify receipt requires absolute windows paths and exact live path."""

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
        """Verify receipt accepts canonical UTC timestamps and rejects malformed values."""

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
        """Verify bounded receipt loader rejects malformed duplicate and oversize JSON."""

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
        """Verify atomic JSON writer is deterministic bounded and uses sibling replace."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="write-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            target = root / "nested" / "receipt.json"
            real_replace = os.replace
            replacements: list[tuple[pathlib.Path, pathlib.Path]] = []

            def recording_replace(source, destination):
                """Handle the recording replace step."""

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
        """Handle the assert client failure step."""

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
        """Handle the assert evidence failure step."""

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
        """Verify desktop client barrier path and document are exact and token bound."""

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
        """Verify desktop client barrier rejects non owned paths and traversal."""

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


__all__ = [name for name in globals() if not name.startswith("__")]
