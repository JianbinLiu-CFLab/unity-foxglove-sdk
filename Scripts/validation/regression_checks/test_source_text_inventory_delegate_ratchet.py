"""Regression tests for the delegate-target manifest ratchet (Rule R)."""

from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from Scripts.validation.source_text_inventory_delegate_ratchet import (
    DELEGATE_BOOTSTRAP_ENV,
    DELEGATE_MANIFEST_PATH,
    _delegate_manifest_failures,
    _delegate_manifest_problems,
)
from Scripts.validation.source_text_inventory_ratchet_helpers import (
    _apply_ratchet_replacements,
    _load_ratchet_replacements,
)

from .test_source_text_inventory_semantics import _FixtureSupport

ROW_FILE = "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
NO_ROW_FILE = "Packages/fixture/Tests/Runtime/NoRows.cs"
VALIDATE = "M:FixtureValidation.Validate"
HELPER = "M:FixtureValidation.Helper"


def _line(member: str, target: str, file: str = ROW_FILE, variant: str = "runtime", count: str = "1") -> str:
    """Return one manifest line."""
    return "\t".join((variant, file, member, target, count))


def _manifest(*lines: str) -> str:
    """Render manifest lines in the committed sorted LF form."""
    return "".join(line + "\n" for line in sorted(lines))


def _signature(*lines: str) -> str:
    """Return the Rule R signature of one key's entry lines."""
    return hashlib.sha256(_manifest(*lines).encode("utf-8")).hexdigest()


def _replacement(member: str, base: str, head: str, variant: str = "runtime", **overrides: object) -> dict[str, object]:
    """Return one DELEGATE replacement entry."""
    entry: dict[str, object] = {
        "file": ROW_FILE,
        "validation_flag": f"{variant}|{member}",
        "class": "DELEGATE",
        "base_signature": base,
        "head_signature": head,
        "reason": "The reviewed method-group target was renamed in place.",
        "replacement": "renamed-target",
    }
    entry.update(overrides)
    return entry


INVALID_MANIFESTS = {
    "fields": "runtime\tA.cs\tM:A.B\tM:A.C\n",
    "empty-field": "runtime\t\tM:A.B\tM:A.C\t1\n",
    "count": "runtime\tA.cs\tM:A.B\tM:A.C\t0\n",
    "count-negative": "runtime\tA.cs\tM:A.B\tM:A.C\t-1\n",
    "count-leading-zero": "runtime\tA.cs\tM:A.B\tM:A.C\t01\n",
    "count-text": "runtime\tA.cs\tM:A.B\tM:A.C\tx\n",
    "variant": "runtime-native\tA.cs\tM:A.B\tM:A.C\t1\n",
    "member": "runtime\tA.cs\tA.B\tM:A.C\t1\n",
    "target": "runtime\tA.cs\tM:A.B\tT:A.C\t1\n",
    "duplicate": "runtime\tA.cs\tM:A.B\tM:A.C\t1\nruntime\tA.cs\tM:A.B\tM:A.C\t2\n",
    "unsorted": "unit\tA.cs\tM:A.B\tM:A.C\t1\nruntime\tA.cs\tM:A.B\tM:A.C\t1\n",
    "cr": "runtime\tA.cs\tM:A.B\tM:A.C\t1\r\n",
    "final-lf": "runtime\tA.cs\tM:A.B\tM:A.C\t1",
}
INVALID_REASONS = {
    "count-negative": "count",
    "count-leading-zero": "count",
    "count-text": "count",
}


class DelegateManifestRatchetTests(_FixtureSupport, unittest.TestCase):
    """Verify Rule R through the real source-text inventory CLI."""

    def _fixture(
        self,
        directory: str,
        base_lines: tuple[str, ...] | None,
        replacements: list[dict[str, object]] | None = None,
        base_text: str | None = None,
    ) -> Path:
        """Commit a trusted base manifest, optional replacements and a row-free source file."""
        root = self._git_fixture(directory)
        manifest = root / DELEGATE_MANIFEST_PATH
        if base_lines is None and base_text is None:
            manifest.unlink()
        else:
            manifest.write_bytes((base_text if base_text is not None else _manifest(*base_lines)).encode("utf-8"))
        no_rows = root / NO_ROW_FILE
        no_rows.write_text("public static class NoRows { public static void Run() {} }\n", encoding="utf-8")
        if replacements is not None:
            (root / "Scripts/validation/source_text_ratchet_replacements.json").write_text(
                json.dumps({"replacements": replacements}) + "\n", encoding="utf-8"
            )
        subprocess.run(["git", "-C", str(root), "config", "core.autocrlf", "false"], check=True)
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        subprocess.run(["git", "-C", str(root), "commit", "-qm", "trusted-delegate-manifest"], check=True)
        return root

    def _head(self, root: Path, *lines: str) -> None:
        """Write the head manifest into the working tree."""
        (root / DELEGATE_MANIFEST_PATH).write_bytes(_manifest(*lines).encode("utf-8"))

    def _gate(self, root: Path, bootstrap: str | None = None) -> tuple[int, str]:
        """Run the real CLI with an explicit bootstrap pin environment."""
        environment = {DELEGATE_BOOTSTRAP_ENV: bootstrap} if bootstrap is not None else {}
        with mock.patch.dict(os.environ, environment):
            if bootstrap is None:
                os.environ.pop(DELEGATE_BOOTSTRAP_ENV, None)
            return self._run_fixture_gate(root)

    def test_unchanged_manifest_passes(self):
        """Pass when every base key keeps its exact entry lines."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            code, output = self._gate(root)
            self.assertEqual(0, code, output)
            self.assertNotIn("DELEGATE", output)

    def test_target_swap_in_existing_member_fails(self):
        """Fail when an existing member's recorded target changes."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Other"))
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn(
                "SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED variant=runtime "
                f"file={ROW_FILE} member={VALIDATE} "
                f"base={_signature(_line(VALIDATE, 'M:FixtureValidation.Used'))} "
                f"head={_signature(_line(VALIDATE, 'M:FixtureValidation.Other'))}",
                output,
            )

    def test_count_change_in_existing_member_fails(self):
        """Fail when an existing member's occurrence count changes."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Used", count="2"))
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED", output)

    def test_removing_existing_member_entries_fails(self):
        """Treat an absent head key as an empty entry set."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            self._head(root)
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn(f"member={VALIDATE} base=", output)
            self.assertIn(f"head={hashlib.sha256(b'').hexdigest()}", output)

    def test_removal_is_not_hidden_by_a_new_member_in_the_same_file(self):
        """Check the removed key on its own when the head adds another member."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            self._head(root, _line(HELPER, "M:FixtureValidation.Used"))
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn(f"SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED variant=runtime file={ROW_FILE} member={VALIDATE} ", output)
            self.assertNotIn(f"member={HELPER}", output)

    def test_variants_are_separate_keys(self):
        """Fail a change in one variant while the other variant is unchanged."""
        with tempfile.TemporaryDirectory() as directory:
            base = (_line(VALIDATE, "M:FixtureValidation.Used"), _line(VALIDATE, "M:FixtureValidation.Used", variant="runtime-adapter"))
            root = self._fixture(directory, base)
            self._head(root, base[0], _line(VALIDATE, "M:FixtureValidation.Other", variant="runtime-adapter"))
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED variant=runtime-adapter ", output)
            self.assertNotIn("CHANGED variant=runtime file", output)

    def test_new_member_passes(self):
        """Allow keys that exist only in the head."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Used"), _line(HELPER, "M:FixtureValidation.Other"))
            code, output = self._gate(root)
            self.assertEqual(0, code, output)

    def test_file_without_rows_is_ignored(self):
        """Leave members of row-free files to compile-time exactness."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line("M:NoRows.Run", "M:NoRows.Run", file=NO_ROW_FILE),))
            self._head(root)
            code, output = self._gate(root)
            self.assertEqual(0, code, output)
            self.assertNotIn("DELEGATE_TARGETS_CHANGED", output)

    def test_file_deleted_from_head_defers_to_the_count_ratchet(self):
        """Do not report Rule R for a file that no longer exists in the head tree."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            (root / ROW_FILE).unlink()
            self._head(root)
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertNotIn("DELEGATE_TARGETS_CHANGED", output)

    def test_case_only_row_file_rename_does_not_skip_the_key(self):
        """Keep checking a key whose file only changed case, as a case-sensitive head tree sees it."""
        with tempfile.TemporaryDirectory() as directory:
            moved = "Packages/fixture/Tests/Runtime/Moved.cs"
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used", file=moved),))
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Other", file=moved.upper()))
            failures = _delegate_manifest_failures(root, "HEAD", [{"file": moved}], [{"file": moved.upper()}])
            self.assertEqual(1, len(failures), failures)
            self.assertTrue(failures[0].startswith(f"SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED variant=runtime file={moved} "))

    def test_exact_trusted_delegate_replacement_clears_only_its_key(self):
        """Clear one changed key with an exact trusted approval and keep the other failing."""
        with tempfile.TemporaryDirectory() as directory:
            base = (_line(VALIDATE, "M:FixtureValidation.Used"), _line(HELPER, "M:FixtureValidation.Used"))
            approved = _replacement(VALIDATE, _signature(base[0]), _signature(_line(VALIDATE, "M:FixtureValidation.Renamed")))
            root = self._fixture(directory, base, [approved])
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Renamed"), _line(HELPER, "M:FixtureValidation.Other"))
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn(f"SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_APPROVED variant=runtime file={ROW_FILE} member={VALIDATE} ", output)
            self.assertIn(f"SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED variant=runtime file={ROW_FILE} member={HELPER} ", output)
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Renamed"), base[1])
            code, output = self._gate(root)
            self.assertEqual(0, code, output)

    def test_stale_or_head_only_delegate_replacement_clears_nothing(self):
        """Ignore approvals whose signatures differ or that exist only in the head."""
        with tempfile.TemporaryDirectory() as directory:
            base = (_line(VALIDATE, "M:FixtureValidation.Used"),)
            stale = _replacement(VALIDATE, _signature(base[0]), _signature(_line(VALIDATE, "M:FixtureValidation.Planned")))
            root = self._fixture(directory, base, [stale])
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Renamed"))
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED", output)
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, base)
            head = _line(VALIDATE, "M:FixtureValidation.Renamed")
            (root / "Scripts/validation/source_text_ratchet_replacements.json").write_text(
                json.dumps({"replacements": [_replacement(VALIDATE, _signature(base[0]), _signature(head))]}) + "\n",
                encoding="utf-8",
            )
            self._head(root, head)
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_CHANGED", output)
            self.assertNotIn("DELEGATE_TARGETS_APPROVED", output)

    def test_missing_head_manifest_fails(self):
        """Fail when the reviewed head drops the manifest."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            (root / DELEGATE_MANIFEST_PATH).unlink()
            code, output = self._gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_DELEGATE_MANIFEST_MISSING side=head", output)

    def test_missing_base_manifest_bootstraps_only_at_the_pinned_base(self):
        """Skip Rule R only when the base lacking a manifest is the pinned introduction base."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, None)
            self._head(root, _line(VALIDATE, "M:FixtureValidation.Used"))
            base = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
            code, output = self._gate(root, bootstrap=base)
            self.assertEqual(0, code, output)
            self.assertIn(f"SOURCE_TEXT_DELEGATE_MANIFEST_BOOTSTRAP base={base}", output)
            for pin in (None, "", "0" * 40, base[:12]):
                with self.subTest(pin=pin):
                    code, output = self._gate(root, bootstrap=pin)
                    self.assertEqual(1, code, output)
                    self.assertIn("SOURCE_TEXT_RATCHET_DELEGATE_MANIFEST_MISSING side=base", output)
                    self.assertNotIn("BOOTSTRAP", output)

    def test_unresolvable_base_revision_fails(self):
        """Report a base error instead of treating an unknown revision as bootstrap."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
            failures = _delegate_manifest_failures(root, "f" * 40, [], [])
            self.assertEqual([f"SOURCE_TEXT_RATCHET_BASE_ERROR revision={'f' * 40}"], failures)

    def test_invalid_head_manifest_fails_for_every_rule(self):
        """Fail closed on each head well-formedness violation."""
        for name, text in INVALID_MANIFESTS.items():
            with self.subTest(rule=name), tempfile.TemporaryDirectory() as directory:
                root = self._fixture(directory, (_line(VALIDATE, "M:FixtureValidation.Used"),))
                (root / DELEGATE_MANIFEST_PATH).write_bytes(text.encode("utf-8"))
                code, output = self._gate(root)
                self.assertEqual(1, code, output)
                self.assertRegex(
                    output,
                    rf"SOURCE_TEXT_RATCHET_DELEGATE_MANIFEST_INVALID side=head line=\d+ reason={INVALID_REASONS.get(name, name)}\b",
                )

    def test_invalid_base_manifest_fails_and_never_bootstraps(self):
        """Fail closed on each base violation even at the pinned bootstrap base."""
        for name, text in INVALID_MANIFESTS.items():
            with self.subTest(rule=name), tempfile.TemporaryDirectory() as directory:
                root = self._fixture(directory, (), base_text=text)
                self._head(root)
                base = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
                code, output = self._gate(root, bootstrap=base)
                self.assertEqual(1, code, output)
                self.assertRegex(
                    output,
                    rf"SOURCE_TEXT_RATCHET_DELEGATE_MANIFEST_INVALID side=base line=\d+ reason={INVALID_REASONS.get(name, name)}\b",
                )
                self.assertNotIn("BOOTSTRAP", output)

    def test_manifest_problems_match_the_analyzer_rules(self):
        """Accept the empty and the well-formed manifest and report line numbers for violations."""
        self.assertEqual([], _delegate_manifest_problems(b""))
        self.assertEqual([], _delegate_manifest_problems(_manifest(_line(VALIDATE, "L:M:FixtureValidation.Validate|Local")).encode()))
        self.assertEqual([(2, "duplicate")], _delegate_manifest_problems(INVALID_MANIFESTS["duplicate"].encode()))
        self.assertEqual([(0, "encoding")], _delegate_manifest_problems(b"\xff\n"))


class DelegateReplacementSchemaTests(unittest.TestCase):
    """Verify the DELEGATE replacement class in the trusted replacement loader."""

    def _load(self, entries: list[dict[str, object]]) -> tuple[list[dict[str, object]], list[str]]:
        """Load replacement entries from a temporary working tree."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Scripts/validation/source_text_ratchet_replacements.json"
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({"replacements": entries}), encoding="utf-8")
            return _load_ratchet_replacements(Path(directory))

    def test_delegate_class_is_accepted_and_unknown_classes_are_rejected(self):
        """Accept DELEGATE next to C and READER and keep other classes invalid."""
        entries, errors = self._load([_replacement(VALIDATE, "a" * 64, "b" * 64)])
        self.assertEqual([], errors)
        self.assertEqual("DELEGATE", entries[0]["class"])
        _, errors = self._load([_replacement(VALIDATE, "a" * 64, "b" * 64, **{"class": "DELEGATES"})])
        self.assertIn("replacement 0 has invalid class", errors)

    def test_malformed_delegate_entries_are_rejected(self):
        """Reject malformed flags, signatures, duplicates and short reasons."""
        cases = {
            "flag-without-variant": _replacement(VALIDATE, "a" * 64, "b" * 64, validation_flag="--phase-fixture"),
            "flag-unknown-variant": _replacement(VALIDATE, "a" * 64, "b" * 64, variant="runtime-native"),
            "flag-bad-member": _replacement("FixtureValidation.Validate", "a" * 64, "b" * 64),
            "flag-not-string": _replacement(VALIDATE, "a" * 64, "b" * 64, validation_flag=7),
            "equal-signatures": _replacement(VALIDATE, "a" * 64, "a" * 64),
            "uppercase-signature": _replacement(VALIDATE, "A" * 64, "b" * 64),
            "short-signature": _replacement(VALIDATE, "a" * 63, "b" * 64),
            "short-reason": _replacement(VALIDATE, "a" * 64, "b" * 64, reason="renamed"),
        }
        for name, entry in cases.items():
            with self.subTest(case=name):
                entries, errors = self._load([entry])
                self.assertEqual([], entries)
                self.assertTrue(errors)
        entries, errors = self._load([_replacement(VALIDATE, "a" * 64, "b" * 64)] * 2)
        self.assertIn("replacement 1 is duplicated", errors)

    def test_class_c_replacement_application_ignores_delegate_entries(self):
        """Keep the Class C replacement path blind to DELEGATE entries."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Scripts/validation/source_text_ratchet_replacements.json"
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({"replacements": [_replacement(VALIDATE, "a" * 64, "b" * 64)]}), encoding="utf-8")
            failures = [f"SOURCE_TEXT_RATCHET_ASSERTION_CHANGED file={ROW_FILE.casefold()} flag=runtime|{VALIDATE} class=C"]
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                errors = _apply_ratchet_replacements(Path(directory), None, failures, ({}, {}), ({}, {}))
            self.assertEqual([], errors)
            self.assertEqual(1, len(failures))
            self.assertEqual("", output.getvalue())


if __name__ == "__main__":
    unittest.main()
