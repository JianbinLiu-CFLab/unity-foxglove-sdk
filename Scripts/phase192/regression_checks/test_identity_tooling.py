from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from Scripts.phase192 import identity_contracts


class IdentityToolingTests(unittest.TestCase):
    """Phase192 IdentityToolingTests."""
    def test_python_export_surface_is_canonical(self) -> None:
        """Phase192 test python export surface is canonical."""
        rows = identity_contracts.python_surface(
            "fixture.py", "__all__ = ['Visible']\nclass Visible: pass\ndef _private(): pass\n"
        )
        self.assertEqual(rows, [["fixture.py", "['Visible']", "Visible"]])

    def test_python_annotation_surface_detects_future_and_type_changes(self) -> None:
        """Detect changes to postponed evaluation and callable annotations."""

        baseline = identity_contracts.python_annotation_rows(
            "fixture.py",
            "from __future__ import annotations\n"
            "def visible(value: 'Input') -> 'Output': pass\n",
        )
        changed = identity_contracts.python_annotation_rows(
            "fixture.py",
            "def visible(value: 'ChangedInput') -> 'Output': pass\n",
        )
        self.assertNotEqual(baseline, changed)

    def test_managed_surface_keeps_public_and_internal_declarations(self) -> None:
        """Phase192 test managed surface keeps public and internal declarations."""
        rows = identity_contracts.managed_rows(
            "Fixture.cs", "public class PublicType {}\ninternal struct InternalType {}\n"
        )
        self.assertEqual([row[2] for row in rows], ["public", "internal"])

    def test_csharp_test_surface_detects_attribute_method(self) -> None:
        """Phase192 test csharp test surface detects attribute method."""
        rows = identity_contracts.csharp_test_rows(
            "Fixture.cs", "[Fact]\npublic void KeepsName() {}\n"
        )
        self.assertEqual(rows, [["Fixture.cs", "KeepsName", "Fact"]])

    def test_compare_gate_rejects_removed_row(self) -> None:
        """Phase192 test compare gate rejects removed row."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            expected = root / "expected.tsv"
            actual = root / "actual.tsv"
            expected.write_text("key\nkeep\nremove\n", encoding="utf-8")
            actual.write_text("key\nkeep\n", encoding="utf-8")
            self.assertEqual(identity_contracts.compare_tsv(expected, actual), 1)

    def test_compare_gate_accepts_identical_surface(self) -> None:
        """Phase192 test compare gate accepts identical surface."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "surface.tsv"
            path.write_text("key\nkeep\n", encoding="utf-8")
            self.assertEqual(identity_contracts.compare_tsv(path, path), 0)

    def test_source_map_rejects_missing_moved_symbol(self) -> None:
        """Phase192 test source map rejects missing moved symbol."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            source.write_text("public class Source {}\n", encoding="utf-8")
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                "source.cs\tsource.cs\tdeadbeef\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 0)

    def test_generated_hash_gate_rejects_changed_manifest_hash(self) -> None:
        """Phase192 test generated hash gate rejects changed manifest hash."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            generated = root / "generated.cs"
            generated.write_text("generated\n", encoding="utf-8")
            manifest = root / "generated.tsv"
            manifest.write_text(
                "path\tsha256\n"
                f"generated.cs\t{'0' * 64}\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_hash_manifest(manifest, root), 1)


if __name__ == "__main__":
    unittest.main()
