from .foundation import *


class SurfaceRowsTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
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

    def test_cpp_surface_detects_exported_symbol_rename(self) -> None:
        """C++ identity rows must change when an exported function is renamed."""
        source = 'extern "C" __declspec(dllexport) int U2FDracoGetVersion(\n    int value\n)\n{\n    return value;\n}\n'
        changed = source.replace("U2FDracoGetVersion", "U2FDracoGetVersionX", 1)
        rows = identity_contracts.cpp_rows("fixture.cpp", source)
        self.assertEqual(1, len(rows))
        self.assertEqual("U2FDracoGetVersion", rows[0][2])
        self.assertNotIn("return value", rows[0][3])
        self.assertNotEqual(
            rows,
            identity_contracts.cpp_rows("fixture.cpp", changed),
        )

    def test_cpp_surface_ignores_function_body_changes(self) -> None:
        """C++ identity remains stable when only an implementation body changes."""
        source = "int Foo() {\n    return 1;\n}\n"
        changed = source.replace("return 1", "return 2")
        self.assertEqual(
            identity_contracts.cpp_rows("fixture.cpp", source),
            identity_contracts.cpp_rows("fixture.cpp", changed),
        )

    def test_cpp_surface_does_not_record_calls_inside_function_bodies(self) -> None:
        """Calls nested in a function body are not C++ interface declarations."""
        source = "int Foo() {\n    return helper();\n}\n"
        rows = identity_contracts.cpp_rows("fixture.cpp", source)
        self.assertEqual(["Foo"], [row[2] for row in rows])

    def test_cpp_surface_ignores_comments_and_strings(self) -> None:
        """C++ comments and string literals cannot introduce identity symbols."""
        source = (
            "/*\n"
            "int FakeComment(int value);\n"
            "*/\n"
            'const char* text = "int FakeString(int value);";\n'
            "int RealExport(int value);\n"
        )
        rows = identity_contracts.cpp_rows("fixture.cpp", source)
        self.assertEqual(["RealExport"], [row[2] for row in rows])

    def test_cpp_surface_records_special_member_declarations(self) -> None:
        """Constructors and destructors remain visible C++ identity rows."""
        source = (
            "Widget::Widget(int value) : value_(value) {}\n"
            "Widget::~Widget() {}\n"
        )
        rows = identity_contracts.cpp_rows("fixture.cpp", source)
        self.assertEqual(2, len(rows))
        self.assertEqual(["Widget", "~Widget"], [row[2] for row in rows])

    def test_cpp_surface_does_not_record_constructor_body_calls(self) -> None:
        """Constructor initializer lists do not expose body calls as declarations."""
        source = (
            "Widget::Widget(int value) : value_(value) {\n"
            "    Initialize();\n"
            "}\n"
        )
        rows = identity_contracts.cpp_rows("fixture.cpp", source)
        self.assertEqual(["Widget"], [row[2] for row in rows])

    def test_source_sensitivity_uses_real_source_mutations(self) -> None:
        """Sensitivity proof must expose real source mutation helpers."""
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "fixture.cpp"
            source.write_text('int Exported(int value) { return value; }\n', encoding="utf-8")
            self.assertEqual(
                sensitivity_proof.source_mutation_result(
                    "cpp", source, lambda text: text.replace("Exported", "Renamed", 1)
                ),
                1,
            )

    def test_source_sensitivity_covers_all_four_source_surfaces(self) -> None:
        """Managed, Python, C++, and test source mutations must all be RED."""
        targets = sensitivity_proof._real_source_mutation_targets(Path.cwd())
        self.assertEqual({"managed", "python", "cpp", "tests"}, {kind for kind, _, _ in targets})
        for kind, source, mutate in targets:
            with self.subTest(kind=kind):
                self.assertEqual(1, sensitivity_proof.source_mutation_result(kind, source, mutate))

    def test_source_sensitivity_compares_frozen_identity_rows(self) -> None:
        """Sensitivity proof must reject a mutation against its frozen TSV baseline."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "fixture.cpp"
            original = "int Exported(int value) { return value; }\n"
            source.write_text(original, encoding="utf-8")
            identity = root / "identity"
            identity.mkdir()
            baseline = identity_contracts.cpp_rows("fixture.cpp", original)
            identity_contracts.write_rows(
                identity / "cpp-symbol-surface.tsv",
                ["path", "line", "symbol", "declaration"],
                baseline,
            )
            self.assertEqual(
                1,
                sensitivity_proof.frozen_identity_mutation_result(
                    "cpp", source, lambda text: text.replace("Exported", "Renamed", 1), identity, root
                ),
            )
            with self.assertRaises(FileNotFoundError):
                sensitivity_proof.frozen_identity_mutation_result(
                    "cpp", source, lambda text: text.replace("Exported", "Renamed", 1), root / "missing", root
                )

    def test_frozen_identity_compares_only_target_baseline_rows(self) -> None:
        """Frozen identity checks ignore unrelated files but reject target mutations."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "fixture.cpp"
            original = "int Exported(int value) { return value; }\n"
            source.write_text(original, encoding="utf-8")
            identity = root / "identity"
            identity.mkdir()
            rows = identity_contracts.cpp_rows("fixture.cpp", original)
            rows.append(["other.cpp", "1", "Other", "int Other()"])
            identity_contracts.write_rows(
                identity / "cpp-symbol-surface.tsv",
                ["path", "line", "symbol", "declaration"],
                rows,
            )
            self.assertEqual(
                0,
                sensitivity_proof.frozen_identity_mutation_result(
                    "cpp", source, lambda text: text, identity, root
                ),
            )
            self.assertEqual(
                1,
                sensitivity_proof.frozen_identity_mutation_result(
                    "cpp", source, lambda text: text.replace("Exported", "Renamed", 1), identity, root
                ),
            )

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

    def test_python_test_rows_uses_out_of_tree_package_context(self) -> None:
        """Out-of-tree discovery resolves absolute and relative imports from its root."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "fixture_pkg/regression_checks"
            package.mkdir(parents=True)
            (root / "fixture_pkg/__init__.py").write_text(
                "ROOT_VALUE = 'supplied-root'\n", encoding="utf-8"
            )
            (package / "__init__.py").write_text("", encoding="utf-8")
            (package / "helpers.py").write_text(
                "VALUE = 'relative-root'\n", encoding="utf-8"
            )
            fixture = package / "test_sample.py"
            fixture.write_text(
                "from fixture_pkg import ROOT_VALUE\n"
                "from .helpers import VALUE\n"
                "import unittest\n"
                "class Attached(unittest.TestCase):\n"
                "    def test_imports(self):\n"
                "        self.assertEqual(ROOT_VALUE, 'supplied-root')\n"
                "        self.assertEqual(VALUE, 'relative-root')\n",
                encoding="utf-8",
            )
            rows = identity_contracts.python_test_rows(
                "fixture_pkg/regression_checks/test_sample.py",
                fixture.read_text(encoding="utf-8"),
                root,
            )
            self.assertEqual(
                rows,
                [[
                    "fixture_pkg/regression_checks/test_sample.py",
                    "fixture_pkg.regression_checks.test_sample.Attached.test_imports",
                    "unittest",
                ]],
            )

    def test_release_tooling_identity_rows_load_facade_without_section_recursion(self) -> None:
        """Release tooling identity inventory loads the facade and skips nested sections."""
        root = compare_identity_surfaces.REPOSITORY_ROOT
        facade = root / "Scripts/release/regression_checks/test_release_tooling.py"
        rows = identity_contracts.python_test_rows(
            facade.relative_to(root).as_posix(),
            facade.read_text(encoding="utf-8"),
            root,
        )
        self.assertEqual(206, len(rows))
        package = facade.with_suffix("")
        for section in sorted(package.glob("*.py")):
            if section.name == "__init__.py":
                continue
            with self.subTest(section=section.name):
                self.assertEqual(
                    [],
                    identity_contracts.python_test_rows(
                        section.relative_to(root).as_posix(),
                        section.read_text(encoding="utf-8"),
                        root,
                    ),
                )

    def test_real_surface_mutation_noop_is_not_red(self) -> None:
        """A no-op extractor mutation cannot pass because unrelated rows differ."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "fixture.cpp"
            original = "int Exported(int value) { return value; }\n"
            source.write_text(original, encoding="utf-8")
            identity = root / "identity"
            identity.mkdir()
            (identity / "cpp-paths.tsv").write_text(
                "path\nfixture.cpp\nother.cpp\n", encoding="utf-8"
            )
            rows = identity_contracts.cpp_rows("fixture.cpp", original)
            rows.append(["other.cpp", "1", "Other", "int Other()"])
            identity_contracts.write_rows(
                identity / "cpp-symbol-surface.tsv",
                ["path", "line", "symbol", "declaration"],
                rows,
            )
            with mock.patch.object(sensitivity_proof, "_mutate_source", lambda text, label: text):
                self.assertEqual(
                    0,
                    sensitivity_proof._run_real_surface_mutation(
                        "cpp-symbol", identity, root, root / "temp"
                    ),
                )

    def test_real_surface_mutation_rejects_baseline_mismatch(self) -> None:
        """A stale frozen row must fail before a mutation result is accepted."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "fixture.cpp"
            source.write_text("int Exported(int value) { return value; }\n", encoding="utf-8")
            identity = root / "identity"
            identity.mkdir()
            (identity / "cpp-paths.tsv").write_text("path\nfixture.cpp\n", encoding="utf-8")
            identity_contracts.write_rows(
                identity / "cpp-symbol-surface.tsv",
                ["path", "line", "symbol", "declaration"],
                [["fixture.cpp", "1", "Wrong", "int Wrong()"]],
            )
            with self.assertRaisesRegex(AssertionError, "frozen identity baseline mismatch"):
                with mock.patch.object(sensitivity_proof, "_mutate_source", lambda text, label: text):
                    sensitivity_proof._run_real_surface_mutation(
                        "cpp-symbol", identity, root, root / "temp"
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


__all__ = ["SurfaceRowsTests"]
