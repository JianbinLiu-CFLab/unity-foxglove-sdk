from .foundation import *


class LoaderIsolationTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
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

    def test_load_fresh_module_removes_new_local_modules_on_failure(self) -> None:
        """A failed load must remove newly imported modules from the facade directory."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            facade = root / "fixture.py"
            helper = root / "helper.py"
            helper.write_text("VALUE = 1\n", encoding="utf-8")
            facade.write_text("import helper\nraise RuntimeError('boom')\n", encoding="utf-8")
            sys.modules.pop("helper", None)
            try:
                with self.assertRaisesRegex(RuntimeError, "boom"):
                    source_layout.load_fresh_module("fixture", facade)
                self.assertNotIn("helper", sys.modules)
            finally:
                sys.modules.pop("helper", None)

    def test_load_fresh_module_removes_new_nested_local_modules_on_failure(self) -> None:
        """A failed load must remove newly imported nested packages from the facade directory."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            helper_dir = root / "helper"
            helper_dir.mkdir()
            (helper_dir / "__init__.py").write_text("\n", encoding="utf-8")
            (helper_dir / "sub.py").write_text("VALUE = 1\n", encoding="utf-8")
            facade = root / "fixture.py"
            facade.write_text(
                "import helper.sub\nraise RuntimeError('boom')\n",
                encoding="utf-8",
            )
            for name in ("helper", "helper.sub"):
                sys.modules.pop(name, None)
            try:
                with self.assertRaisesRegex(RuntimeError, "boom"):
                    source_layout.load_fresh_module("fixture", facade)
                self.assertNotIn("helper", sys.modules)
                self.assertNotIn("helper.sub", sys.modules)
            finally:
                for name in ("helper.sub", "helper"):
                    sys.modules.pop(name, None)

    def test_load_fresh_module_clears_preloaded_nested_local_modules(self) -> None:
        """A fresh load must not reuse preloaded nested packages from the facade directory."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            helper_dir = root / "helper"
            helper_dir.mkdir()
            (helper_dir / "__init__.py").write_text("\n", encoding="utf-8")
            sub_path = helper_dir / "sub.py"
            sub_path.write_text("VALUE = 'old'\n", encoding="utf-8")
            facade = root / "fixture.py"
            facade.write_text(
                "import helper.sub\nVALUE = helper.sub.VALUE\n",
                encoding="utf-8",
            )
            original_path = list(sys.path)
            sys.path.insert(0, str(root))
            try:
                import helper.sub

                sub_path.write_text("VALUE = 'new-value'\n", encoding="utf-8")
                module = source_layout.load_fresh_module("fixture", facade)
                self.assertEqual("new-value", module.VALUE)
            finally:
                sys.path[:] = original_path
                for name in ("fixture", "helper.sub", "helper"):
                    sys.modules.pop(name, None)

    def test_load_fresh_module_restores_path_for_import_setup_failures(self) -> None:
        """Import setup failures must restore the caller's sys.path exactly."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            facade = Path(temp) / "fixture.py"
            facade.write_text("VALUE = 1\n", encoding="utf-8")
            original_path = list(sys.path)
            cases = (
                (None, None),
                (types.SimpleNamespace(loader=None), None),
                (types.SimpleNamespace(loader=object()), RuntimeError("module init failed")),
            )
            for spec, module_error in cases:
                with self.subTest(spec=spec, module_error=module_error):
                    with mock.patch.object(
                        source_layout.importlib.util,
                        "spec_from_file_location",
                        return_value=spec,
                    ):
                        module_from_spec = mock.patch.object(
                            source_layout.importlib.util,
                            "module_from_spec",
                            side_effect=module_error,
                        )
                        with module_from_spec:
                            with self.assertRaises(RuntimeError):
                                source_layout.load_fresh_module("fixture", facade)
                    self.assertEqual(original_path, sys.path)

    def test_load_fresh_module_restores_cleared_modules_on_failure(self) -> None:
        """A failed fresh load must restore modules removed during isolation."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            facade = Path(temp) / "fixture.py"
            facade.write_text("raise RuntimeError('boom')\n", encoding="utf-8")
            sentinel = types.ModuleType("fixture")
            sys.modules["fixture"] = sentinel
            original_path = list(sys.path)
            try:
                with self.assertRaisesRegex(RuntimeError, "boom"):
                    source_layout.load_fresh_module("fixture", facade)
                self.assertIs(sentinel, sys.modules["fixture"])
                self.assertEqual(original_path, sys.path)
            finally:
                if sys.modules.get("fixture") is sentinel:
                    sys.modules.pop("fixture", None)

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
            "import sys\ndef leak(registry=sys.modules):\n    return registry\nPUBLIC = 1\n",
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

    def test_split_identity_surface_rejects_all_dunder_import_calls(self) -> None:
        """Direct dynamic imports cannot bypass the namespace escape checks."""
        sources = (
            "__import__('os').open('bad', 'w')\nPUBLIC = 1\n",
            "module_name = 'os'\n__import__(module_name).system('bad')\nPUBLIC = 1\n",
            "x = __import__('sys')\ngetattr(x, 'modules')[__name__] = object()\nPUBLIC = 1\n",
            "x = __import__('pathlib')\nx.open('bad', 'w')\nPUBLIC = 1\n",
            "x = __import__('pathlib', fromlist=['Path'])\ngetattr(x, key)('bad')\nPUBLIC = 1\n",
            "import importlib\n"
            "x = importlib.import_module('sys')\n"
            "key = 'modules'\n"
            "registry = getattr(x, key)\n"
            "PUBLIC = 1\n",
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


    def test_compatibility_revision_loads_base_and_head_non_strict(self) -> None:
        """An accepted additive head remains readable as the next compatibility baseline."""
        from contextlib import contextmanager

        @contextmanager
        def checkout(_repository, revision):
            """Internal helper for checkout."""
            yield Path(revision)

        with (
            mock.patch.object(compare_identity_surfaces, "_git", side_effect=["base\n", "head\n"]),
            mock.patch.object(compare_identity_surfaces, "_revision_checkout", side_effect=checkout),
            mock.patch.object(compare_identity_surfaces, "_surfaces", return_value={}) as surfaces,
        ):
            self.assertEqual(0, compare_identity_surfaces.compare_revisions(Path("."), "base", "head", strict=False))
        self.assertEqual(
            [mock.call(Path("base"), strict=False), mock.call(Path("head"), strict=False)],
            surfaces.call_args_list,
        )

    def test_compatibility_uses_base_revision_waiver_authority(self) -> None:
        """A head-only waiver cannot authorize a public-symbol removal in the same change."""
        base_surfaces = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        head_surfaces = {"fixture.py": (frozenset(), ("live",), ())}

        @contextmanager
        def checkout(_repository, revision):
            """Internal helper for checkout."""
            yield Path(revision)

        with (
            mock.patch.object(compare_identity_surfaces, "_git", side_effect=["base\n", "head\n"]),
            mock.patch.object(compare_identity_surfaces, "_revision_checkout", side_effect=checkout),
            mock.patch.object(
                compare_identity_surfaces,
                "_surfaces",
                side_effect=[base_surfaces, head_surfaces],
            ),
            mock.patch.object(
                compare_identity_surfaces,
                "_load_identity_waivers",
                side_effect=[{}, {"fixture.py": frozenset({"PUBLIC"})}],
            ),
            mock.patch.object(compare_identity_surfaces, "_validate_compatibility_new_sections"),
        ):
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    Path("."), "base", "head", strict=False
                ),
            )

    def test_compatibility_ignores_stale_base_waiver_during_cleanup(self) -> None:
        """Expired base waivers do not block their cleanup change."""
        base_surfaces = {"fixture.py": (frozenset({"OTHER"}), ("live",), ())}
        head_surfaces = {"fixture.py": (frozenset({"OTHER"}), ("live",), ())}

        @contextmanager
        def checkout(_repository, revision):
            """Provide a revision-labelled checkout for the compatibility seam."""
            yield Path(revision)

        with (
            mock.patch.object(compare_identity_surfaces, "_git", side_effect=["base\n", "head\n"]),
            mock.patch.object(compare_identity_surfaces, "_revision_checkout", side_effect=checkout),
            mock.patch.object(
                compare_identity_surfaces,
                "_surfaces",
                side_effect=[base_surfaces, head_surfaces],
            ),
            mock.patch.object(
                compare_identity_surfaces,
                "_load_identity_waivers",
                side_effect=[{"fixture.py": frozenset({"PUBLIC"})}, {}],
            ),
            mock.patch.object(compare_identity_surfaces, "_validate_compatibility_new_sections"),
        ):
            self.assertEqual(
                0,
                compare_identity_surfaces.compare_revisions(
                    Path("."), "base", "head", strict=False
                ),
            )

    def test_compatibility_surface_can_be_reused_after_conditional_and_orphan_additions(self) -> None:
        """Two compatibility rounds do not freeze an accepted conditional/orphan baseline."""
        with tempfile.TemporaryDirectory() as temp:
            roots = [Path(temp) / name for name in ("base", "head")]
            for root in roots:
                package = root / "Scripts/smoke/foxrun/fixture"
                package.mkdir(parents=True)
                (root / "Scripts/smoke/foxrun/fixture.py").write_text("PUBLIC = 1\n", encoding="utf-8")
                (package / "__init__.py").write_text("from . import live\n", encoding="utf-8")
                (package / "orphan.py").write_text("ORPHAN = 1\n", encoding="utf-8")
            (roots[0] / "Scripts/smoke/foxrun/fixture/live.py").write_text(
                "LIVE = 1\nif False:\n    CONDITIONAL = 1\n"
                "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                encoding="utf-8",
            )
            (roots[1] / "Scripts/smoke/foxrun/fixture/live.py").write_text(
                "LIVE = 1\nif False:\n    CONDITIONAL = 1\nif False:\n    LATER = 2\n"
                "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                encoding="utf-8",
            )
            base = compare_identity_surfaces._surfaces(roots[0], strict=False)
            head = compare_identity_surfaces._surfaces(roots[1], strict=False)
            self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))


__all__ = ["LoaderIsolationTests"]
