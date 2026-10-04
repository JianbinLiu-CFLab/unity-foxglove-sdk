from __future__ import annotations

import ast
import hashlib
import io
import tarfile
import tempfile
import unittest
from unittest import mock
from pathlib import Path

from Scripts.phase192 import compare_identity_surfaces, identity_contracts, sensitivity_proof, source_layout


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

    def test_identity_manifest_paths_stay_inside_root(self) -> None:
        """Identity row generation rejects rooted or escaping manifest paths."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paths = root / "paths.tsv"
            output = root / "rows.tsv"
            paths.write_text("\\outside.cs\n", encoding="utf-8")
            self.assertEqual(
                identity_contracts.main(
                    [
                        "managed",
                        "--root",
                        str(root),
                        "--paths",
                        str(paths),
                        "--output",
                        str(output),
                    ]
                ),
                1,
            )

    def test_source_map_rejects_unsupported_extensions(self) -> None:
        """Source maps are limited to recognized source-language extensions."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.txt"
            target = root / "segment.txt"
            source.write_text("Source\n", encoding="utf-8")
            target.write_text("Source\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.txt\tsegment.txt\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_symbol_only_in_comments_or_literals(self) -> None:
        """A source-map symbol must be a code identifier, not text in comments or strings."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            source.write_text(
                "// Source\nconst char* text = \"Source\";\n",
                encoding="utf-8",
            )
            target.write_text("public class Other {}\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tsegment.cs\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_same_resolved_path(self) -> None:
        """A moved-source row must not claim one file as both endpoints."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            source.write_text("class Source {}\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\t./source.cs\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_rooted_backslash_paths(self) -> None:
        """Source-map paths with a leading backslash are not repository-relative."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / "segment.cs"
            target.write_text("class Source {}\n", encoding="utf-8")
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"\\outside.cs\tsegment.cs\t{'0' * 64}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_absolute_paths(self) -> None:
        """Source-map entries must use repository-relative paths."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            source.write_text("class Source {}\n", encoding="utf-8")
            target.write_text("class Source {}\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"{source.resolve()}\tsegment.cs\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_python_comments_and_malformed_symbols(self) -> None:
        """Source maps must not certify Python comments or malformed symbol tokens."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.py"
            target = root / "segment.py"
            source.write_text("# Source\ndef other(): pass\n", encoding="utf-8")
            target.write_text("# Source\ndef other(): pass\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.py\tsegment.py\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.py\tsegment.py\t{digest}\t{{}}\n",
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
    def test_hash_manifest_rejects_empty_rows(self) -> None:
        """An empty generated hash manifest must not certify anything."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest = root / "generated.tsv"
            manifest.write_text("path\tsha256\n", encoding="utf-8")
            self.assertEqual(identity_contracts.check_hash_manifest(manifest, root), 1)

    def test_hash_manifest_rejects_paths_outside_root(self) -> None:
        """Generated hash entries must remain inside the checked root."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "root"
            root.mkdir()
            manifest = root / "generated.tsv"
            manifest.write_text(
                "path\tsha256\n../outside.cs\t" + "0" * 64 + "\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_hash_manifest(manifest, root), 1)

    def test_hash_manifest_rejects_noncanonical_alias_paths(self) -> None:
        """Hash manifests reject dot and parent path aliases inside the root."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            generated = root / "generated.cs"
            generated.write_text("class Generated {}\n", encoding="utf-8")
            digest = hashlib.sha256(generated.read_bytes()).hexdigest()
            for alias in ("./generated.cs", "nested/../generated.cs"):
                with self.subTest(alias=alias):
                    manifest = root / "generated.tsv"
                    manifest.write_text(
                        "path\tsha256\n" + f"{alias}\t{digest}\n",
                        encoding="utf-8",
                    )
                    self.assertEqual(identity_contracts.check_hash_manifest(manifest, root), 1)

    def test_hash_manifest_rejects_rooted_backslash_paths(self) -> None:
        """A leading backslash is rooted on Windows even without a drive."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest = root / "generated.tsv"
            manifest.write_text(
                "path\tsha256\n\\outside.cs\t" + "0" * 64 + "\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_hash_manifest(manifest, root), 1)

    def test_hash_manifest_rejects_absolute_paths(self) -> None:
        """Generated hash entries must use repository-relative paths."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            generated = root / "generated.cs"
            generated.write_text("class Generated {}\n", encoding="utf-8")
            manifest = root / "generated.tsv"
            digest = hashlib.sha256(generated.read_bytes()).hexdigest()
            manifest.write_text(
                "path\tsha256\n" + str(generated.resolve()) + "\t" + digest + "\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_hash_manifest(manifest, root), 1)

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
    def test_python_module_name_accepts_case_insensitive_extension(self) -> None:
        """Manifest paths with an uppercase Python suffix remain importable on Windows."""
        self.assertEqual(
            "Scripts.smoke.fixture",
            identity_contracts._module_name_for_path("Scripts/smoke/fixture.PY"),
        )

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

    def test_revision_archive_rejects_escape_and_symlink_members(self) -> None:
        """Revision extraction refuses traversal and symlink members on all Python versions."""
        for kind in ("escape", "symlink"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as temp:
                archive_path = Path(temp) / "fixture.tar"
                destination = Path(temp) / "tree"
                destination.mkdir()
                with tarfile.open(archive_path, "w") as archive:
                    if kind == "escape":
                        member = tarfile.TarInfo("../outside.txt")
                        payload = b"outside"
                        member.size = len(payload)
                        archive.addfile(member, io.BytesIO(payload))
                    else:
                        member = tarfile.TarInfo("link")
                        member.type = tarfile.SYMTYPE
                        member.linkname = "../outside.txt"
                        archive.addfile(member)
                with tarfile.open(archive_path, "r") as archive:
                    with self.assertRaises(RuntimeError):
                        compare_identity_surfaces._extract_archive_safely(archive, destination)

    def test_identity_surface_parses_all_current_split_sections(self) -> None:
        """Every checked-in split section passes the static identity parser."""
        surfaces = compare_identity_surfaces._surfaces(
            compare_identity_surfaces.REPOSITORY_ROOT
        )
        self.assertEqual(27, len(surfaces))

    def test_identity_surface_uses_revision_local_source_layout(self) -> None:
        """Base/head worktrees load their own section-order parser."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Scripts/smoke/foxrun/fixture"
            package.mkdir(parents=True)
            (package / "__init__.py").write_text(
                "from . import revision_live\n", encoding="utf-8"
            )
            (package / "revision_live.py").write_text(
                "VALUE = 1\n__all__ = ['VALUE']\n", encoding="utf-8"
            )
            parser_path = root / "Scripts/phase192/source_layout.py"
            parser_path.parent.mkdir(parents=True)
            parser_path.write_text(
                "raise RuntimeError('revision parser must not execute')\n",
                encoding="utf-8",
            )
            loaded = compare_identity_surfaces._section_modules_for_root(root)
            self.assertEqual((package / "revision_live.py",), loaded(package))

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
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("base-init",))
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live", "new"),
                ("base-init", "_added_init = 1"),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)
        self.assertIn("EXTRA_SECTIONS fixture.py: new", errors)
        self.assertIn("EXTRA_SYMBOLS fixture.py: NEW", errors)

    def test_split_identity_surface_allows_compatible_additions(self) -> None:
        """Normal CI must allow additive sections, symbols, and initializer edits."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("base-init",))
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live", "new"),
                ("base-init", "_added_init = 1"),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_allows_safe_initializer_insertions(self) -> None:
        """Safe imports and literals may be inserted without freezing the package."""
        base = {"fixture.py": (frozenset({"PUBLIC"}), (), ("PUBLIC = _external",))}
        head = {
            "fixture.py": (
                frozenset({"PUBLIC", "json"}),
                (),
                ("import json", "PUBLIC = _external"),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_rejects_initializer_reorder(self) -> None:
        """Reordering baseline initializer statements remains a compatibility break."""
        base = {
            "fixture.py": (
                frozenset({"PUBLIC"}),
                (),
                ("_external = 0", "PUBLIC = _external"),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"PUBLIC"}),
                (),
                ("PUBLIC = _external", "_external = 0"),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_initializer_removals(self) -> None:
        """Compatibility mode still rejects destructive initializer edits."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("keep", "remove"))
        }
        head = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("keep",))
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_destructive_initializer_additions(self) -> None:
        """Compatibility mode rejects additions that can erase package exports."""
        base = {
            "fixture.py": (frozenset({"PUBLIC"}), ("live",), ("keep", "export"))
        }
        for statement in ("__all__ = []", "del PUBLIC", "PUBLIC = 0"):
            with self.subTest(statement=statement):
                head = {
                    "fixture.py": (
                        frozenset({"PUBLIC"}),
                        ("live",),
                        ("keep", "export", statement),
                    )
                }
                errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
                self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_ignores_imports_and_private_names(self) -> None:
        """The compatibility surface contains declared public names only."""
        source = (
            "import json\n"
            "_private = 1\n"
            "PUBLIC = 2\n"
            "def visible(): pass\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        exports = compare_identity_surfaces._section_exports(source, "fixture.py")
        symbols = compare_identity_surfaces._symbols(source, "fixture.py")
        self.assertEqual(frozenset({"json", "_private", "PUBLIC", "visible"}), exports)
        self.assertEqual(frozenset({"json", "_private", "PUBLIC", "visible"}), symbols)

    def test_split_identity_surface_honors_explicit_imported_exports(self) -> None:
        """Explicit __all__ entries remain part of the runtime API even when imported."""
        source = "from dependency import Public\n__all__ = [\"Public\"]\n"
        self.assertEqual(
            frozenset({"Public"}),
            compare_identity_surfaces._section_exports(source, "fixture.py"),
        )

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
            surface = compare_identity_surfaces._surface(
                root, root / "Scripts/smoke/foxrun/fixture.py"
            )
            symbols = surface[0]
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
            (package / "live.py").write_text("LIVE = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n", encoding="utf-8")
            with self.assertRaises(FileNotFoundError):
                compare_identity_surfaces._surfaces(root)

    def test_source_layout_rejects_duplicate_declared_sections(self) -> None:
        """The shared AST authority must reject repeated section imports."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "fixture"
            package.mkdir()
            (package / "__init__.py").write_text(
                "from . import live\nfrom . import live\n", encoding="utf-8"
            )
            (package / "live.py").write_text("LIVE = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n", encoding="utf-8")
            from Scripts.phase192.source_layout import section_modules
            with self.assertRaises(ValueError):
                section_modules(package)

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
            (package / "live.py").write_text("LIVE = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n", encoding="utf-8")
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

    def test_repository_relative_paths_reject_windows_aliases(self) -> None:
        """Identity paths reject Windows ADS, device, control, and alias syntax."""
        for value in (
            "file:stream.cs",
            "file.cs.",
            "file.cs ",
            "CON.txt",
            "folder/NUL/data.cs",
            "folder/line\tbreak.cs",
        ):
            with self.subTest(value=value):
                self.assertFalse(identity_contracts.is_repository_relative(value))

    def test_hash_manifest_rejects_whitespace_and_duplicate_columns(self) -> None:
        """Hash manifests must preserve canonical fields and exact schemas."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            generated = root / "generated.cs"
            generated.write_text("generated\n", encoding="utf-8")
            digest = hashlib.sha256(generated.read_bytes()).hexdigest()
            whitespace = root / "whitespace.tsv"
            whitespace.write_text(
                "path\tsha256\n generated.cs\t" + digest + "\n",
                encoding="utf-8",
            )
            duplicate = root / "duplicate.tsv"
            duplicate.write_text(
                "path\tpath\tsha256\ngenerated.cs\tgenerated.cs\t" + digest + "\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_hash_manifest(whitespace, root), 1)
            self.assertEqual(identity_contracts.check_hash_manifest(duplicate, root), 1)

    def test_source_map_rejects_whitespace_duplicate_rows_and_duplicate_columns(self) -> None:
        """Source maps must have canonical fields and one row per endpoint pair."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            source.write_text("class Source {}\n", encoding="utf-8")
            target.write_text("class Source {}\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            row = f"source.cs\tsegment.cs\t{digest}\tSource\n"
            duplicate = root / "duplicate.tsv"
            duplicate.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n" + row + row,
                encoding="utf-8",
            )
            whitespace = root / "whitespace.tsv"
            whitespace.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f" source.cs\tsegment.cs\t{digest}\tSource\n",
                encoding="utf-8",
            )
            extra = root / "extra.tsv"
            extra.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\tmemo\n" + row,
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(duplicate, root), 1)
            self.assertEqual(identity_contracts.check_source_map(whitespace, root), 1)
            self.assertEqual(identity_contracts.check_source_map(extra, root), 1)

    def test_source_map_ignores_csharp_raw_literals_and_nested_comments(self) -> None:
        """C# literal/comment pseudo-code must not satisfy a moved-symbol row."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            source.write_text(
                'var text = """\nSource\n""";\n/* outer /* nested Source */ still */\n',
                encoding="utf-8",
            )
            target.write_text("class Other {}\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tsegment.cs\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_split_identity_surface_accepts_added_section_import(self) -> None:
        """Compatibility mode permits a new declared section without freezing the split."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}),
                ("live",),
                ("from . import live",),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live", "new"),
                ("from . import live", "from . import new"),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_rejects_initializer_collision_with_new_section_export(self) -> None:
        """A new initializer binding cannot shadow an export from a new section."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}),
                ("live",),
                ("from . import live",),
                frozenset({"LIVE"}),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live", "new"),
                ("from . import live", "from . import new", "NEW = 1"),
                frozenset({"LIVE", "NEW"}),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_section_reorder_and_duplicates(self) -> None:
        """Section order and duplicate imports are runtime-visible identity changes."""
        base = {"fixture.py": (frozenset({"LIVE"}), ("live", "other"), ())}
        for sections in (("other", "live"), ("live", "other", "other")):
            with self.subTest(sections=sections):
                head = {"fixture.py": (frozenset({"LIVE"}), sections, ())}
                errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
                self.assertIn("SECTION_ORDER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_unbound_explicit_exports(self) -> None:
        """An explicit __all__ entry must name a binding in the section."""
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "__all__ = ['Missing']\n", "fixture.py"
            )

    def test_split_identity_surface_rejects_noncanonical_dynamic_exports(self) -> None:
        """Only the generated public-globals __all__ template is accepted."""
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "__all__ = [name for name in globals() if False]\nPUBLIC = 1\n",
                "fixture.py",
            )

    def test_split_identity_surface_compatibility_allows_docstring_change(self) -> None:
        """Compatibility mode does not freeze a first-position documentation edit."""
        base = {"fixture.py": (frozenset(), (), ('"one"',))}
        head = {"fixture.py": (frozenset(), (), ('"two"',))}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertEqual([], errors)

    def test_split_identity_surface_strict_mode_detects_docstring_change(self) -> None:
        """Strict decomposition audits include initializer module documentation."""
        base = {"fixture.py": (frozenset(), (), ('"one"',))}
        head = {"fixture.py": (frozenset(), (), ('"two"',))}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_strict_mode_detects_initializer_comment_change(self) -> None:
        """Strict mode compares normalized initializer bytes, not only AST statements."""
        base = {"fixture.py": (frozenset(), (), ("VALUE = 1",), frozenset(), "base-digest")}
        head = {"fixture.py": (frozenset(), (), ("VALUE = 1",), frozenset(), "head-digest")}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_unsafe_initializer_additions(self) -> None:
        """Compatibility mode rejects side-effectful or rebinding initializer additions."""
        base = {"fixture.py": (frozenset(), (), ("_state = 1",))}
        for statement in (
            "import evil as _evil",
            "_state = 2",
            "@danger()\ndef _helper(): pass",
            "class _Helper(metaclass=danger): pass",
            "_annotated: evil() = 1",
        ):
            with self.subTest(statement=statement):
                head = {"fixture.py": (frozenset(), (), ("_state = 1", statement))}
                errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
                self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_identity_manifest_rejects_whitespace_and_duplicate_paths(self) -> None:
        """Identity path manifests preserve canonical spelling and uniqueness."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            source.write_text("class Source {}\n", encoding="utf-8")
            output = root / "rows.tsv"
            for content in (" source.cs\n", "source.cs\nsource.cs\n"):
                paths = root / "paths.tsv"
                paths.write_text(content, encoding="utf-8")
                self.assertEqual(
                    identity_contracts.main(
                        [
                            "managed",
                            "--root",
                            str(root),
                            "--paths",
                            str(paths),
                            "--output",
                            str(output),
                        ]
                    ),
                    1,
                )

    def test_split_identity_surface_rejects_all_mutation_forms(self) -> None:
        """__all__ mutation or conditional assignment cannot be hidden from the gate."""
        sources = (
            "PUBLIC = 1\n__all__ = ['PUBLIC']\n__all__ += ['MISSING']\n",
            "PUBLIC = 1\n__all__ = ['PUBLIC']\n__all__.append('MISSING')\n",
            "PUBLIC = 1\nif flag:\n    __all__ = []\n",
            "PUBLIC = 1\n__all__ = [MISSING]\n__all__ = ['PUBLIC']\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_indirect_namespace_mutations(self) -> None:
        """Namespace writes cannot hide a changed dynamic export surface."""
        sources = (
            "PUBLIC = 1\nglobals()[\"__all__\"] = []\n",
            "PUBLIC = 1\nglobals().update(__all__=[])\n",
            "PUBLIC = 1\nvars()[\"PUBLIC\"] = 0\n",
            "PUBLIC = 1\ng = globals\ng()[\"PUBLIC\"] = 0\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_compatibility_tracks_private_runtime_exports(self) -> None:
        """Removing a single-underscore dynamic export remains a missing-symbol failure."""
        base_exports = compare_identity_surfaces._section_exports(
            "_helper = 1\nPUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "fixture.py",
        )
        head_exports = compare_identity_surfaces._section_exports(
            "PUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "fixture.py",
        )
        base = {"fixture.py": (base_exports, ("live",), (), base_exports)}
        head = {"fixture.py": (head_exports, ("live",), (), head_exports)}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MISSING_SYMBOLS fixture.py: _helper", errors)

    def test_split_identity_surface_compatibility_allows_new_conditional_symbol(self) -> None:
        """Compatibility mode does not freeze a newly conditional public binding."""
        base = {"fixture.py": (frozenset({"LIVE"}), ("live",), ())}
        head = {"fixture.py": (frozenset({"LIVE", "NEW"}), ("live",), ())}
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_rejects_existing_symbol_becoming_conditional(self) -> None:
        """Compatibility mode rejects an existing runtime name made branch-dependent."""
        base = {
            "fixture.py": (
                frozenset({"PUBLIC", "_helper"}),
                ("live",),
                (),
                frozenset({"PUBLIC", "_helper"}),
                None,
                frozenset(),
                True,
            )
        }
        head = {
            "fixture.py": (
                frozenset({"PUBLIC", "_helper"}),
                ("live",),
                (),
                frozenset({"PUBLIC", "_helper"}),
                None,
                frozenset({"PUBLIC", "_helper"}),
                True,
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("CONDITIONAL_SYMBOL_CHANGED fixture.py: PUBLIC,_helper", errors)

    def test_split_identity_surface_rejects_removed_module_entrypoint(self) -> None:
        """Compatibility mode preserves an existing executable package entrypoint."""
        base = {"fixture.py": (frozenset(), (), (), frozenset(), None, frozenset(), True)}
        head = {"fixture.py": (frozenset(), (), (), frozenset(), None, frozenset(), False)}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MISSING_MODULE_ENTRYPOINT fixture.py", errors)

    def test_split_identity_surface_rejects_noncanonical_dynamic_exports_in_all_modes(self) -> None:
        """Noncanonical dynamic __all__ expressions cannot hide removed exports."""
        source = "PUBLIC = 1\nNEW = 2\n__all__ = [name for name in globals() if name != '__name__']\n"
        for reject_conditional in (True, False):
            with self.subTest(reject_conditional=reject_conditional):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source, "fixture.py", reject_conditional=reject_conditional
                    )

    def test_split_identity_surface_rejects_conditional_public_bindings(self) -> None:
        """Dynamic exports must not approximate branch-dependent module globals."""
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "if flag:\n    PUBLIC = 1\n"
                "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                "fixture.py",
            )

    def test_split_identity_surface_rejects_conditional_public_deletion(self) -> None:
        """A branch-dependent delete cannot be hidden by a unioned static surface."""
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "PUBLIC = 1\nif flag:\n    del PUBLIC\n"
                "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                "fixture.py",
            )

    def test_split_identity_surface_rejects_module_scope_binding_forms(self) -> None:
        """Unsupported module binding syntax must fail closed."""
        for source in (
            "(PUBLIC := 1)\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "match value:\n    case {'name': PUBLIC}: pass\n",
        ):
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_indirect_builtin_namespace_access(self) -> None:
        """Builtins and frame globals cannot rewrite a section behind the gate."""
        for source in (
            "import builtins\nbuiltins.globals()[\"PUBLIC\"] = 0\nPUBLIC = 1\n",
            "__builtins__[\"globals\"]()[\"PUBLIC\"] = 0\nPUBLIC = 1\n",
            "import sys\nsys._getframe(0).f_globals[\"PUBLIC\"] = 0\nPUBLIC = 1\n",
        ):
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")


    def test_split_identity_surface_requires_runtime_all_contract(self) -> None:
        """A split section without __all__ cannot be imported by the facade."""
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "PUBLIC = 1\n", "fixture.py"
            )

    def test_split_identity_surface_rejects_dynamic_execution_and_public_global(self) -> None:
        """Dynamic execution and public global writes must fail closed."""
        sources = (
            "from builtins import exec as _execute\n"
            "_execute(\"del PUBLIC\")\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import builtins as _builtins\n"
            "_builtins.exec(\"del PUBLIC\")\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "def rewrite():\n"
            "    global PUBLIC\n"
            "    PUBLIC = 0\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "sys.modules[__name__].__dict__[\"PUBLIC\"] = 0\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_allows_read_only_namespace_lookup(self) -> None:
        """A read-only globals lookup remains compatible with generated code."""
        source = (
            "PUBLIC = globals().get('PUBLIC', 1)\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        self.assertEqual(
            frozenset({"PUBLIC"}),
            compare_identity_surfaces._section_exports(source, "fixture.py"),
        )

    def test_split_identity_surface_compatibility_allows_added_symbol(self) -> None:
        """Compatibility mode allows additive section symbols while strict mode audits them."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}),
                ("live",),
                ("from . import live",),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live",),
                ("from . import live",),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))
        self.assertIn(
            "EXTRA_SYMBOLS fixture.py: NEW",
            compare_identity_surfaces._compare_surfaces(base, head, strict=True),
        )


    def test_split_identity_surface_allows_public_initializer_addition(self) -> None:
        """A new public initializer binding is compatible when the base is preserved."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("from . import live",)),
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live",),
                ("from . import live", "NEW = 1"),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_allows_safe_initializer_imports_and_definitions(self) -> None:
        """Safe imports and inert public definitions do not freeze compatibility mode."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("from . import live",)),
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW", "helper", "json"}),
                ("live", "new"),
                (
                    "from . import live",
                    "import json",
                    "from . import new",
                    "NEW = 1",
                    "def helper():\n    return NEW",
                ),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_preserves_module_docstring_position(self) -> None:
        base = {"Scripts/smoke/foxrun/demo.py": (frozenset({"run"}), ("live",), ('"doc"',), frozenset({"run"}))}
        moved = {"Scripts/smoke/foxrun/demo.py": (frozenset({"run"}), ("live",), ("pass", '"doc"'), frozenset({"run"}))}
        errors = compare_identity_surfaces._compare_surfaces(base, moved, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED Scripts/smoke/foxrun/demo.py", errors)

    def test_split_identity_surface_allows_annotated_and_relative_initializer_additions(self) -> None:
        """Compatibility mode allows inert annotations and valid relative aliases."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("from . import live",)),
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW", "alias", "Value", "json"}),
                ("live", "new"),
                (
                    "from . import live",
                    "from . import new as alias",
                    "from .live import Value",
                    "import json",
                    "NEW: str = 'x'",
                ),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_compatibility_allows_added_facade(self) -> None:
        """Compatibility mode does not freeze future split packages."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("from . import live",)),
        }
        head = {
            **base,
            "new_fixture.py": (frozenset({"NEW"}), ("new",), ("from . import new",)),
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))
        self.assertIn(
            "ADDED_FACADE new_fixture.py",
            compare_identity_surfaces._compare_surfaces(base, head, strict=True),
        )

    def test_split_identity_surface_rejects_new_section_export_collision(self) -> None:
        """A newly declared section cannot overwrite an existing runtime export."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}), ("live",), (), frozenset({"LIVE"}),
                None, frozenset(), True, (("live", frozenset({"LIVE"})),), frozenset(),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE"}), ("live", "new"), (), frozenset({"LIVE"}),
                None, frozenset(), True,
                (("live", frozenset({"LIVE"})), ("new", frozenset({"LIVE"}))),
                frozenset(),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertTrue(any(error.startswith("SECTION_EXPORT_COLLISION fixture.py:") and "new:LIVE" in error for error in errors))

    def test_split_identity_surface_rejects_new_namespace_risk(self) -> None:
        """Compatibility mode preserves the existing namespace-risk contract."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), (), frozenset({"LIVE"}),
                            None, frozenset(), True, (), frozenset({"Name(id='sys', ctx=Load())"}))
        }
        head = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), (), frozenset({"LIVE"}),
                            None, frozenset(), True, (), frozenset())
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("NAMESPACE_CONTRACT_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_unknown_initializer_import(self) -> None:
        """Compatibility mode does not admit arbitrary import side effects."""
        base = {"fixture.py": (frozenset(), (), ("pass",))}
        head = {"fixture.py": (frozenset({"evil"}), (), ("pass", "import evil"))}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_allows_inert_initializer_conditionals(self) -> None:
        """TYPE_CHECKING and false branches are safe additive initializer edits."""
        base = {"fixture.py": (frozenset(), (), ("from typing import TYPE_CHECKING",))}
        head = {
            "fixture.py": (
                frozenset({"TYPE_CHECKING", "NEW"}), (),
                ("from typing import TYPE_CHECKING", "if TYPE_CHECKING:\n    NEW = 1", "assert True"),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_rejects_forward_initializer_references(self) -> None:
        """Additive definitions cannot reference a binding declared later in the initializer."""
        base = {
            "fixture.py": (
                frozenset({"OLD"}), (), ("OLD = 1",), frozenset(), None, frozenset(),
                False, (), frozenset(), (), None, (),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"OLD", "NEW"}), (),
                ("def NEW(value=OLD): pass", "OLD = 1"),
                frozenset(), None, frozenset(), False, (), frozenset(), (), None, (),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_unbound_initializer_definition_names(self) -> None:
        """New initializer definitions must not introduce import-time NameErrors."""
        base = {"fixture.py": (frozenset(), (), ("pass",))}
        for statement in (
            "def NEW(value=Missing): pass",
            "def NEW(value: Missing): pass",
            "class NEW(Missing): pass",
            "NEW: Missing = 1",
        ):
            with self.subTest(statement=statement):
                head = {"fixture.py": (frozenset({"NEW"}), (), ("pass", statement))}
                errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
                self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_load_fresh_module_removes_failed_module_registration(self) -> None:
        """A failed fresh load must not poison a later test through sys.modules."""
        import sys
        with tempfile.TemporaryDirectory() as temp:
            facade = Path(temp) / "fixture.py"
            facade.write_text("raise RuntimeError('boom')\n", encoding="utf-8")
            sys.modules.pop("fixture", None)
            with self.assertRaisesRegex(RuntimeError, "boom"):
                source_layout.load_fresh_module("fixture", facade)
            self.assertNotIn("fixture", sys.modules)

    def test_initializer_future_import_is_not_a_public_surface_symbol(self) -> None:
        """Future imports do not create runtime package bindings."""
        tree = compare_identity_surfaces.ast.parse(
            "from __future__ import annotations\n"
        )
        self.assertEqual(
            set(),
            compare_identity_surfaces._top_level_names(tree, include_imports=True),
        )


    def test_source_map_requires_python_declarations_not_reference_text(self) -> None:
        """Python source maps require an AST declaration at both endpoints."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.py"
            target = root / "segment.py"
            source.write_text("def moved():\n    return 1\n", encoding="utf-8")
            target.write_text("moved()\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.py\tsegment.py\t{digest}\tmoved\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_ignores_csharp_verbatim_literal_declarations(self) -> None:
        """C# verbatim strings cannot satisfy a moved declaration row."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            source.write_text('var text = @"class Moved {}";\n', encoding="utf-8")
            target.write_text('var text = @"class Moved {}";\n', encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tsegment.cs\t{digest}\tMoved\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)


    def test_split_identity_surface_rejects_module_registry_alias_mutations(self) -> None:
        """Module registry writes remain rejected through aliases and helper calls."""
        sources = (
            "import sys as s\ns.modules[__name__].__setattr__('PUBLIC', 2)\nPUBLIC = 1\n",
            "import sys as s\ns.modules[__name__] = object()\nPUBLIC = 1\n",
            "import sys as s\ns.modules.pop(__name__, None)\nPUBLIC = 1\n",
            "import sys as s\ns.modules.update({__name__: object()})\nPUBLIC = 1\n",
            "import importlib\ns = importlib.import_module('sys')\ns.modules[__name__].__setattr__('PUBLIC', 2)\nPUBLIC = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source + "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                        "fixture.py",
                    )

    def test_split_identity_surface_rejects_indirect_module_registry_mutations(self) -> None:
        """Module registry writes remain rejected through reflection and namespace helpers."""
        sources = (
            "import sys\ngetattr(sys, \"modules\")[__name__].__setattr__(\"PUBLIC\", 2)\nPUBLIC = 1\n",
            "import sys\nsys.__getattribute__(\"modules\")[__name__].pop(__name__, None)\nPUBLIC = 1\n",
            "import sys\nvars(sys)[\"modules\"][__name__] = object()\nPUBLIC = 1\n",
            "import sys\nobject.__getattribute__(sys, \"modules\")[__name__].update({})\nPUBLIC = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source + "__all__ = [name for name in globals() if not name.startswith(\"__\")]\n",
                        "fixture.py",
                    )

    def test_split_identity_surface_rejects_module_registry_alias_escapes(self) -> None:
        """Registry mutations hidden in aliases, calls, closures, or comprehensions fail closed."""
        sources = (
            "from sys import modules as m\nt = m[__name__]\nt.PUBLIC = 2\nPUBLIC = 1\n",
            "import sys as s\nt = s.modules.get(__name__)\nt.PUBLIC = 2\nPUBLIC = 1\n",
            "import sys\ndef mutate(m):\n    m[__name__].pop('PUBLIC', None)\nmutate(sys.modules)\nPUBLIC = 1\n",
            "import sys\n(lambda m: m[__name__].pop('PUBLIC', None))(sys.modules)\nPUBLIC = 1\n",
            "import sys\n[m[__name__].pop('PUBLIC', None) for m in (sys.modules,)]\nPUBLIC = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source + "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                        "fixture.py",
                    )

    def test_split_identity_surface_rejects_dynamic_namespace_key_aliases(self) -> None:
        """Namespace and import aliases cannot hide dynamic execution from the gate."""
        sources = (
            "import __builtins__ as b\nb.exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "import __builtin__ as b\nb.exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "from __builtins__ import getattr as g\ng(object(), \"exec\")(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "from __builtin__ import getattr as g\ng(object(), \"exec\")(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "globals()[\"exec\"](\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "globals().get(\"exec\")(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "e = globals().get(\"exec\")\ne(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "from importlib import import_module as m\nm(\"builtins\").exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "import importlib as il\nil.import_module(\"__builtin__\").exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "import importlib as il\nil.import_module(name).exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "i = __import__\ni(\"builtins\").exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "__import__(\"__builtins__\").exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "__import__(\"builtins\").__getattribute__(\"exec\")(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "__import__(name).__getattribute__(\"eval\")(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "__import__(name).eval(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "vars().get(\"compile\")(\"PUBLIC = 2\", \"x\", \"exec\")\nPUBLIC = 1\n",
            "g = globals().get(\"getattr\")\ng(object(), \"exec\")(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "m = globals().get(\"import_module\")\nm(\"builtins\").exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "import importlib\ngetattr(importlib, \"import_module\")(\"builtins\").exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "import sys\nsys.modules[\"builtins\"].exec(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "import sys\nsys.modules[name].compile(\"PUBLIC = 2\", \"x\", \"exec\")\nPUBLIC = 1\n",
            "import sys\nsys.modules[\"__builtin__\"].eval(\"PUBLIC = 2\")\nPUBLIC = 1\n",
            "import sys\nsys.modules[__name__].__setattr__(\"PUBLIC\", 2)\nPUBLIC = 1\n",
            "import sys\nsys.modules[__name__].__delattr__(\"PUBLIC\")\nPUBLIC = 1\n",
            "import sys\nsetattr(sys.modules[__name__], \"PUBLIC\", 2)\nPUBLIC = 1\n",
            "import sys\ndelattr(sys.modules[__name__], \"PUBLIC\")\nPUBLIC = 1\n",
            "import sys\nobject.__setattr__(sys.modules[__name__], \"PUBLIC\", 2)\nPUBLIC = 1\n",
            "import sys\nsys.modules[__name__][\"PUBLIC\"] = 2\nPUBLIC = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source + "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                        "fixture.py",
                    )

    def test_source_map_does_not_treat_future_import_as_declaration(self) -> None:
        """Future-import names are compiler directives, not moved symbols."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.py"
            target = root / "segment.py"
            contents = "from __future__ import annotations\n"
            source.write_text(contents, encoding="utf-8")
            target.write_text(contents, encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.py\tsegment.py\t{digest}\tannotations\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_case_alias_of_same_endpoint(self) -> None:
        """Endpoint paths are compared canonically, including case."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            source.write_text("public class Source {}\n", encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tSOURCE.CS\t{digest}\tSource\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)


    def test_source_map_rejects_python_import_only_symbol(self) -> None:
        """An imported Python name is not a declaration moved by the split."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.py"
            target = root / "segment.py"
            contents = "from dependency import Moved\n"
            source.write_text(contents, encoding="utf-8")
            target.write_text(contents, encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.py\tsegment.py\t{digest}\tMoved\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)

    def test_source_map_rejects_csharp_using_alias(self) -> None:
        """A C# using alias is not a moved type or member declaration."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            target = root / "segment.cs"
            contents = "using Moved = Other;\n"
            source.write_text(contents, encoding="utf-8")
            target.write_text(contents, encoding="utf-8")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            ledger = root / "source-map.tsv"
            ledger.write_text(
                "old_path\tnew_path\toriginal_hash\tmoved_symbol\n"
                f"source.cs\tsegment.cs\t{digest}\tMoved\n",
                encoding="utf-8",
            )
            self.assertEqual(identity_contracts.check_source_map(ledger, root), 1)


    def test_identity_cli_defaults_to_strict_mode(self) -> None:
        """The command-line default remains the exact decomposition audit."""
        with mock.patch.object(compare_identity_surfaces, "compare_revisions", return_value=0) as compare:
            self.assertEqual(0, compare_identity_surfaces.main(["--base", "a", "--head", "b"]))
        self.assertEqual(True, compare.call_args.kwargs["strict"])

    def test_identity_cli_compatibility_mode_is_explicit(self) -> None:
        """The compatibility gate is selected only by its explicit flag."""
        with mock.patch.object(compare_identity_surfaces, "compare_revisions", return_value=0) as compare:
            self.assertEqual(0, compare_identity_surfaces.main(["--base", "a", "--head", "b", "--compatibility"]))
        self.assertEqual(False, compare.call_args.kwargs["strict"])

    def test_identity_cli_rejects_conflicting_modes(self) -> None:
        """Strict and compatibility modes cannot be combined."""
        with self.assertRaises(SystemExit) as error:
            compare_identity_surfaces.main(["--base", "a", "--head", "b", "--strict", "--compatibility"])
        self.assertEqual(2, error.exception.code)

    def test_split_identity_surface_rejects_facade_section_owner_collision(self) -> None:
        """A facade binding cannot shadow an existing section export in compatibility mode."""
        base = {
            "fixture.py": (
                frozenset({"X"}), ("a",), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset({"X"})),), frozenset(), (), None, (),
                (("X", ("section:a",)),),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"X"}), ("a",), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset({"X"})),), frozenset(), (), None, (),
                (("X", ("facade", "section:a")),),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("SYMBOL_OWNER_CHANGED fixture.py: X", errors)

    def test_split_identity_surface_strict_mode_detects_section_owner_swap(self) -> None:
        """Strict decomposition mode also compares section ownership."""
        base = {
            "fixture.py": (
                frozenset({"X"}), ("a", "b"), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset({"X"})), ("b", frozenset())), frozenset(), (), None, (),
                (("X", ("section:a",)),),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"X"}), ("a", "b"), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset()), ("b", frozenset({"X"}))), frozenset(), (), None, (),
                (("X", ("section:b",)),),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
        self.assertIn("SYMBOL_OWNER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_section_export_owner_swap(self) -> None:
        """Moving an existing export between sections changes facade resolution."""
        base = {
            "fixture.py": (
                frozenset({"X", "Y"}),
                ("a", "b"),
                (),
                frozenset({"X", "Y"}),
                None,
                frozenset(),
                True,
                (("a", frozenset({"X"})), ("b", frozenset({"Y"}))),
                frozenset(),
                (),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"X", "Y"}),
                ("a", "b"),
                (),
                frozenset({"X", "Y"}),
                None,
                frozenset(),
                True,
                (("a", frozenset({"Y"})), ("b", frozenset({"X"}))),
                frozenset(),
                (),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("SECTION_EXPORT_OWNER_CHANGED fixture.py: X,Y", errors)

    def test_conditional_contract_includes_body_and_duplicate_events(self) -> None:
        """Conditional contracts distinguish body changes and repeated writes."""
        first = compare_identity_surfaces._conditional_contract_signatures(
            compare_identity_surfaces.ast.parse("if FLAG:\n    _helper = 1\nif FLAG:\n    _helper = 2\n")
        )
        changed = compare_identity_surfaces._conditional_contract_signatures(
            compare_identity_surfaces.ast.parse("if FLAG:\n    _helper = 1\nif FLAG:\n    _helper = 3\n")
        )
        shortened = compare_identity_surfaces._conditional_contract_signatures(
            compare_identity_surfaces.ast.parse("if FLAG:\n    _helper = 1\n")
        )
        self.assertEqual(2, len(first))
        self.assertNotEqual(first, changed)
        self.assertNotEqual(first, shortened)

    def test_split_identity_surface_rejects_conditional_contract_change(self) -> None:
        """Changing an import-time condition is a compatibility break even if names match."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("base-condition",),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("head-condition",),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("CONDITIONAL_CONTRACT_CHANGED fixture.py", errors)

    def test_split_identity_surface_allows_new_conditional_names_but_not_existing_bindings(self) -> None:
        """New inert conditional names are additive; existing bindings retain their contract."""
        base = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE", "_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:base",), None, (),
            )
        }
        additive = {
            "fixture.py": (
                frozenset({"LIVE", "_helper", "NEW"}), ("live",), (), frozenset({"LIVE", "_helper", "NEW"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:base", "NEW:new"), None, (),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, additive, strict=False))
        changed = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE", "_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:base", "_helper:other"), None, (),
            )
        }
        self.assertIn(
            "CONDITIONAL_CONTRACT_CHANGED fixture.py",
            compare_identity_surfaces._compare_surfaces(base, changed, strict=False),
        )

    def test_split_identity_surface_rejects_scoped_conditional_binding_change(self) -> None:
        """Scoped section contracts must not hide a changed existing binding."""
        base = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(),
                ("section:live:_helper:base",), None, (),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(),
                ("section:live:_helper:changed",), None, (),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("CONDITIONAL_CONTRACT_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_conditional_contract_reorder(self) -> None:
        """Reordering conditional writes can change the final runtime binding."""
        base = {
            "fixture.py": (
                frozenset({"_helper"}), (), (), frozenset({"_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:first", "_helper:second"), None, (),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"_helper"}), (), (), frozenset({"_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:second", "_helper:first"), None, (),
            )
        }
        self.assertIn(
            "CONDITIONAL_CONTRACT_CHANGED fixture.py",
            compare_identity_surfaces._compare_surfaces(base, head, strict=False),
        )

    def test_conditional_contract_preserves_branch_and_handler_boundaries(self) -> None:
        """Control-flow branch/handler rearrangements change the contract."""
        base = ast.parse(
            "if flag:\n    _a = 1\nelse:\n    _b = 2\n"
            "try:\n    _c = 3\nexcept ValueError:\n    pass\nelse:\n    _d = 4\n"
        )
        head = ast.parse(
            "if flag:\n    _a = 1\n    _b = 2\n"
            "try:\n    _c = 3\n    _d = 4\nexcept ValueError:\n    pass\n"
        )
        self.assertNotEqual(
            compare_identity_surfaces._conditional_contract_signatures(base),
            compare_identity_surfaces._conditional_contract_signatures(head),
        )

    def test_split_identity_surface_rejects_module_entrypoint_replacement(self) -> None:
        """An existing -m wrapper cannot be replaced while retaining a relative import."""
        base = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(), (), None, ("base-import",)
            )
        }
        head = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(), (), None, ("head-import",)
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MODULE_ENTRYPOINT_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_entrypoint_call_addition(self) -> None:
        """An existing module entrypoint cannot gain an unreviewed executable call."""
        base = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(),
                (), "base-digest", ("delegate-call",),
            )
        }
        head = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(),
                (), "head-digest", ("delegate-call", "evil-call"),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MODULE_ENTRYPOINT_CHANGED fixture.py", errors)

    def test_entrypoint_rejects_wildcard_side_effect(self) -> None:
        """A wildcard protocol wrapper cannot execute unrelated import-time code."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "__main__.py"
            path.write_text(
                "from . import *\nimport os\nos.system('bad')\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_missing_relative_module(self) -> None:
        """A -m wrapper cannot delegate to a module absent from its package."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            path = package / "__main__.py"
            path.write_text(
                "from .missing import main\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_missing_package_alias(self) -> None:
        """A module-level package import must name an exported symbol."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "__main__.py"
            path.write_text(
                "from . import missing\nif __name__ == '__main__':\n    raise SystemExit(missing())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_misplaced_future_import(self) -> None:
        """Entrypoint syntax must be valid for Python, not only ast.parse."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "__main__.py"
            path.write_text(
                "value = 1\nfrom __future__ import annotations\nfrom . import main\n",
                encoding="utf-8",
            )
            with self.assertRaises(SyntaxError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_unknown_import_and_definition_side_effects(self) -> None:
        """Entrypoints reject unknown imports and import-time definition effects."""
        sources = (
            "from . import main\nimport evil\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
            "from . import main\ndef helper(value=evil()):\n    return value\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
            "from . import main\nclass Helper(metaclass=evil()):\n    pass\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
        )
        for source in sources:
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temp:
                path = Path(temp) / "__main__.py"
                path.write_text(source, encoding="utf-8")
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._entrypoint_contract(path)

    def test_split_identity_surface_rejects_added_module_entrypoint_in_strict_mode(self) -> None:
        """Strict decomposition identity does not allow a new executable entrypoint."""
        base = {"fixture.py": (frozenset(), (), (), frozenset(), None, frozenset(), False)}
        head = {"fixture.py": (frozenset(), (), (), frozenset(), None, frozenset(), True)}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
        self.assertIn("ADDED_MODULE_ENTRYPOINT fixture.py", errors)

    def test_split_identity_surface_rejects_indirect_module_registry_aliases(self) -> None:
        """Functions and object attributes cannot smuggle sys.modules into mutations."""
        sources = (
            "import sys\ndef registry():\n    return sys.modules\nregistry()[__name__].__setattr__('PUBLIC', 2)\nPUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\nclass Holder:\n    pass\nholder = Holder()\nholder.modules = sys.modules\nholder.modules[__name__].__setattr__('PUBLIC', 2)\nPUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_type_alias_bindings(self) -> None:
        """Unsupported Python 3.12 type aliases fail closed instead of hiding exports."""
        if not hasattr(ast, "TypeAlias"):
            self.skipTest("TypeAlias is unavailable on this Python version")
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "type PUBLIC = int\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                "fixture.py",
                reject_conditional=False,
            )

    def test_split_identity_surface_rejects_try_star_exports(self) -> None:
        """except* bindings are either tracked or rejected, never silently omitted."""
        if not hasattr(ast, "TryStar"):
            self.skipTest("TryStar is unavailable on this Python version")
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "try:\n    pass\nexcept* Exception:\n    PUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                "fixture.py",
            )

    def test_split_identity_surface_rejects_private_module_delete_bypass(self) -> None:
        """Private deletes can alter the dynamic export set and must fail closed."""
        source = (
            "_helper = 1\n"
            "del (_helper,)\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(source, "fixture.py")

if __name__ == "__main__":
    unittest.main()
