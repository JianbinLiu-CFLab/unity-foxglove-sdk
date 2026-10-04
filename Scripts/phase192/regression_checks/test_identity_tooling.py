from __future__ import annotations

import hashlib
import tempfile
import unittest
from unittest import mock
from pathlib import Path

from Scripts.phase192 import compare_identity_surfaces, identity_contracts, sensitivity_proof


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

    def test_source_map_accepts_matching_hash_and_moved_symbol(self) -> None:
        """Phase192 test source map accepts matching hash and moved symbol."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            source.write_text("public class Source {}\n", encoding="utf-8")
            target.write_text("public class Source {}\n", encoding="utf-8")
            source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tsegment.cs\t{source_hash}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 0)

    def test_source_map_rejects_empty_table(self) -> None:
        """Phase192 test source map rejects an empty table."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_hash_mismatch(self) -> None:
        """Phase192 test source map rejects a hash mismatch."""
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
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_missing_moved_symbol(self) -> None:
        """Phase192 test source map rejects a missing moved symbol."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            source.write_text("public class Source {}\n", encoding="utf-8")
            source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
            target = root / "segment.cs"
            target.write_text("public class Other {}\n", encoding="utf-8")
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tsegment.cs\t{source_hash}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_missing_old_moved_symbol(self) -> None:
        """A moved symbol must be present in both source-map endpoints."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            source.write_text("public class Other {}\n", encoding="utf-8")
            target.write_text("public class Source {}\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tsegment.cs\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)
    def test_generated_byte_mutation_runs_hash_gate_on_isolated_copy(self) -> None:
        """Phase192 test generated mutation invokes the real hash gate."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            generated = root / "generated.cs"
            generated.write_text("generated\n", encoding="utf-8")
            manifest = root / "generated.tsv"
            digest = hashlib.sha256(generated.read_bytes()).hexdigest().upper()
            manifest.write_text(
                "path\tsha256\n"
                f"generated.cs\t{digest}\n",
                encoding="utf-8",
            )
            with mock.patch.object(
                identity_contracts,
                "check_hash_manifest",
                wraps=identity_contracts.check_hash_manifest,
            ) as hash_gate:
                self.assertEqual(
                    sensitivity_proof.generated_byte_mutation(manifest, root),
                    1,
                )
            self.assertEqual(hash_gate.call_count, 2)
            self.assertEqual(hash_gate.call_args_list[0].args[0], manifest)
            self.assertNotEqual(hash_gate.call_args_list[1].args[1], root)
    def test_python_test_rows_uses_actual_unittest_discovery(self) -> None:
        """Phase192 test identity excludes AST-only orphan methods."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            fixture = root / "test_fixture.py"
            fixture.write_text(
                "import unittest\n"
                "class Attached(unittest.TestCase):\n"
                "    def test_attached(self):\n"
                "        pass\n"
                "class OrphanMixin:\n"
                "    def test_orphan(self):\n"
                "        pass\n",
                encoding="utf-8",
            )
            rows = identity_contracts.python_test_rows(
                "test_fixture.py",
                fixture.read_text(encoding="utf-8"),
                root,
            )
            self.assertEqual(
                rows,
                [["test_fixture.py", "test_fixture.Attached.test_attached", "unittest"]],
            )

    def test_identity_surface_scans_all_split_roots(self) -> None:
        """The gate includes every decomposed smoke-source root."""
        facades = compare_identity_surfaces._split_facades(
            compare_identity_surfaces.REPOSITORY_ROOT
        )
        relative = {
            path.relative_to(compare_identity_surfaces.REPOSITORY_ROOT).as_posix()
            for path in facades
        }
        self.assertIn(
            "Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/websocket/phase189_component_messagepack_probe.py",
            relative,
        )
        self.assertEqual(27, len(relative))

    def test_split_identity_surface_rejects_added_surface(self) -> None:
        """Identity equivalence rejects additions as well as removals."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), "init-digest")
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live", "new"),
                "changed-init",
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)
        self.assertIn("EXTRA_SECTIONS fixture.py: new", errors)
        self.assertIn("EXTRA_SYMBOLS fixture.py: NEW", errors)

    def test_split_identity_surface_compatibility_allows_additions(self) -> None:
        """Compatibility mode permits additive surfaces and initializer changes."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), "base-init")
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live", "new"),
                "head-init",
            ),
            "added.py": (frozenset({"ADDED"}), ("added",), "added-init"),
        }
        self.assertEqual(
            [], compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        )

    def test_split_identity_surface_compatibility_rejects_removals(self) -> None:
        """Compatibility mode still rejects removed facades, sections, and symbols."""
        base = {
            "fixture.py": (
                frozenset({"LIVE", "KEEP"}),
                ("live", "keep"),
                "base-init",
            )
        }
        head = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), "head-init")
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MISSING_SECTIONS fixture.py: keep", errors)
        self.assertIn("MISSING_SYMBOLS fixture.py: KEEP", errors)
        errors = compare_identity_surfaces._compare_surfaces(
            {**base, "removed.py": base["fixture.py"]}, head, strict=False
        )
        self.assertIn("REMOVED_FACADE removed.py", errors)

    def test_split_identity_surface_compatibility_allows_safe_initializer_additions(self) -> None:
        """Compatibility mode allows inert imports and literal bindings after the base contract."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}),
                ("live",),
                ("from . import live", "VALUE = 1"),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "json", "NEW"}),
                ("live", "new"),
                ("import json", "from . import live", "VALUE = 1", "NEW = 2"),
            )
        }
        self.assertEqual(
            [], compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        )

    def test_split_identity_surface_compatibility_allows_safe_function_and_class_additions(self) -> None:
        """Compatibility mode allows undecorated definitions with inert class bodies."""
        base = {"fixture.py": (frozenset({"LIVE"}), ("live",), ("VALUE = 1",))}
        head = {
            "fixture.py": (
                frozenset({"LIVE", "helper", "Helper"}),
                ("live",),
                (
                    "VALUE = 1",
                    "def helper():\n    return 1",
                    "class Helper:\n    pass",
                ),
            )
        }
        self.assertEqual(
            [], compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        )

    def test_split_identity_surface_compatibility_rejects_initializer_removals_and_reorder(self) -> None:
        """Compatibility mode preserves base initializer order and statements."""
        base = {"fixture.py": (frozenset({"LIVE"}), ("live",), ("KEEP = 1", "KEEP2 = 2"))}
        for initializer in (("KEEP2 = 2", "KEEP = 1"), ("KEEP = 1",)):
            with self.subTest(initializer=initializer):
                errors = compare_identity_surfaces._compare_surfaces(
                    base,
                    {"fixture.py": (frozenset({"LIVE"}), ("live",), initializer)},
                    strict=False,
                )
                self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_compatibility_rejects_destructive_initializer_changes(self) -> None:
        """Compatibility mode rejects rebinding and deleting existing initializer names."""
        base = {"fixture.py": (frozenset({"LIVE"}), ("live",), ("VALUE = 1",))}
        for statement in ("VALUE = 2", "del VALUE", "__all__ = []"):
            with self.subTest(statement=statement):
                errors = compare_identity_surfaces._compare_surfaces(
                    base,
                    {
                        "fixture.py": (
                            frozenset({"LIVE"}),
                            ("live",),
                            ("VALUE = 1", statement),
                        )
                    },
                    strict=False,
                )
                self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_compatibility_rejects_section_reorder(self) -> None:
        """Compatibility mode preserves the order of existing sections."""
        base = {"fixture.py": (frozenset({"LIVE"}), ("live", "other"), "init")}
        errors = compare_identity_surfaces._compare_surfaces(
            base,
            {"fixture.py": (frozenset({"LIVE"}), ("other", "live", "new"), "changed")},
            strict=False,
        )
        self.assertIn("SECTION_ORDER_CHANGED fixture.py", errors)

    def test_split_identity_surface_compatibility_rejects_removed_entrypoint(self) -> None:
        """Compatibility mode preserves an existing package module entrypoint."""
        base = {
            "fixture.py": (frozenset(), (), "init", (), True)
        }
        head = {
            "fixture.py": (frozenset(), (), "init", (), False)
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MISSING_MODULE_ENTRYPOINT fixture.py", errors)

    def test_identity_cli_defaults_to_strict_and_accepts_compatibility_flag(self) -> None:
        """The CLI keeps strict mode as the default and exposes compatibility explicitly."""
        with mock.patch.object(
            compare_identity_surfaces, "compare_revisions", return_value=0
        ) as compare:
            self.assertEqual(
                0, compare_identity_surfaces.main(["--base", "a", "--head", "b"])
            )
            self.assertTrue(compare.call_args.kwargs["strict"])
            self.assertEqual(
                0,
                compare_identity_surfaces.main(
                    ["--base", "a", "--head", "b", "--compatibility"]
                ),
            )
            self.assertFalse(compare.call_args.kwargs["strict"])

    def test_split_identity_surface_honors_section_exports(self) -> None:
        """The identity surface must follow a section's runtime __all__ contract."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Scripts/smoke/foxrun/fixture"
            package.mkdir(parents=True)
            (root / "Scripts/smoke/foxrun/fixture.py").write_text(
                "class Public: pass\n", encoding="utf-8"
            )
            (package / "__init__.py").write_text(
                "from . import live\n", encoding="utf-8"
            )
            (package / "live.py").write_text(
                "LIVE = 1\n__all__ = []\n", encoding="utf-8"
            )
            symbols, _, _ = compare_identity_surfaces._surface(
                root, root / "Scripts/smoke/foxrun/fixture.py"
            )
            self.assertNotIn("LIVE", symbols)

    def test_split_identity_surface_rejects_missing_declared_section(self) -> None:
        """A declared section cannot silently disappear from a split package."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Scripts/smoke/foxrun/fixture"
            package.mkdir(parents=True)
            (root / "Scripts/smoke/foxrun/fixture.py").write_text(
                "class Public: pass\n", encoding="utf-8"
            )
            (package / "__init__.py").write_text(
                "from . import live\nfrom . import missing\n", encoding="utf-8"
            )
            (package / "live.py").write_text("LIVE = 1\n", encoding="utf-8")
            with self.assertRaises(FileNotFoundError):
                compare_identity_surfaces._surfaces(root)
            with self.assertRaises(FileNotFoundError):
                compare_identity_surfaces._surfaces(root, strict=False)

    def test_split_identity_surface_rejects_orphan_section(self) -> None:
        """The base/head surface loader must reject a package section omitted from __init__."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Scripts/smoke/foxrun/fixture"
            package.mkdir(parents=True)
            (root / "Scripts/smoke/foxrun/fixture.py").write_text(
                "class Public: pass\n", encoding="utf-8"
            )
            (package / "__init__.py").write_text(
                "from . import live\n", encoding="utf-8"
            )
            (package / "live.py").write_text("LIVE = 1\n", encoding="utf-8")
            (package / "orphan.py").write_text("ORPHAN = 1\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                compare_identity_surfaces._surfaces(root)
            surfaces = compare_identity_surfaces._surfaces(root, strict=False)
            self.assertIn("Scripts/smoke/foxrun/fixture.py", surfaces)
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
