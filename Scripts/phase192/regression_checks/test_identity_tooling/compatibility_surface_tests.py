from .foundation import *


class CompatibilitySurfaceTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
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
            "class _Invalid:\n    __slots__ = (1,)",
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
        """Compatibility mode permits removal of a single-underscore helper."""
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
        self.assertEqual([], errors)

    def test_split_identity_surface_compatibility_ignores_removed_module_import(self) -> None:
        """Compatibility mode permits removal of an unused absolute module import."""
        base = {
            "fixture.py": (
                frozenset({"contextlib", "PUBLIC"}),
                ("live",),
                (),
                frozenset({"contextlib", "PUBLIC"}),
                None,
                frozenset(),
                True,
                (),
                frozenset(),
                (),
                None,
                (),
                (),
                (),
                frozenset({"PUBLIC"}),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"PUBLIC"}),
                ("live",),
                (),
                frozenset({"PUBLIC"}),
                None,
                frozenset(),
                True,
                (),
                frozenset(),
                (),
                None,
                (),
                (),
                (),
                frozenset({"PUBLIC"}),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_compatibility_ignores_removed_from_import(self) -> None:
        """Compatibility mode does not freeze names bound by from-import statements."""
        base = {
            "fixture.py": (
                frozenset({"Imported", "PUBLIC"}),
                ("live",),
                (),
                frozenset({"Imported", "PUBLIC"}),
                None,
                frozenset(),
                True,
                (),
                frozenset(),
                (),
                None,
                (),
                (),
                (),
                frozenset({"PUBLIC"}),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"PUBLIC"}),
                ("live",),
                (),
                frozenset({"PUBLIC"}),
                None,
                frozenset(),
                True,
                (),
                frozenset(),
                (),
                None,
                (),
                (),
                (),
                frozenset({"PUBLIC"}),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_accepts_pending_public_removal_waiver(self) -> None:
        """A base waiver may remain pending until its public symbol is removed."""
        base = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        head = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        errors = compare_identity_surfaces._compare_surfaces(
            base,
            head,
            strict=False,
            waived_symbols={"fixture.py": frozenset({"PUBLIC"})},
        )
        self.assertEqual([], errors)

    def test_split_identity_surface_ignores_stale_waiver_for_removed_symbol(self) -> None:
        """A stale symbol waiver is inert for an existing facade."""
        base = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        head = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        errors = compare_identity_surfaces._compare_surfaces(
            base,
            head,
            strict=False,
            waived_symbols={"fixture.py": frozenset({"MISSING"})},
        )
        self.assertEqual([], errors)

    def test_split_identity_surface_stale_waiver_does_not_hide_active_removal(self) -> None:
        """An expired waiver cannot hide a different public removal."""
        base = {"fixture.py": (frozenset({"PUBLIC", "OTHER"}), ("live",), ())}
        head = {"fixture.py": (frozenset({"OTHER"}), ("live",), ())}
        errors = compare_identity_surfaces._compare_surfaces(
            base,
            head,
            strict=False,
            waived_symbols={"fixture.py": frozenset({"MISSING"})},
        )
        self.assertEqual(["MISSING_SYMBOLS fixture.py: PUBLIC"], errors)

    def test_split_identity_surface_strict_mode_ignores_waivers(self) -> None:
        """Strict mode rejects removals regardless of compatibility waivers."""
        base = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        head = {"fixture.py": (frozenset(), ("live",), ())}
        errors = compare_identity_surfaces._compare_surfaces(
            base,
            head,
            strict=True,
            waived_symbols={"fixture.py": frozenset({"PUBLIC"})},
        )
        self.assertEqual(["MISSING_SYMBOLS fixture.py: PUBLIC"], errors)

    def test_split_identity_surface_rejects_waiver_for_unknown_facade(self) -> None:
        """A waiver cannot silently target a facade absent from the base revision."""
        base = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        head = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        errors = compare_identity_surfaces._compare_surfaces(
            base,
            head,
            strict=False,
            waived_symbols={"missing.py": frozenset({"PUBLIC"})},
        )
        self.assertTrue(any("INVALID_IDENTITY_WAIVER missing.py:" in error for error in errors))

    def test_split_identity_surface_compatibility_honors_public_removal_waiver(self) -> None:
        """A reviewed waiver can intentionally permit one public symbol removal."""
        base = {"fixture.py": (frozenset({"PUBLIC"}), ("live",), ())}
        head = {"fixture.py": (frozenset(), ("live",), ())}
        errors = compare_identity_surfaces._compare_surfaces(
            base,
            head,
            strict=False,
            waived_symbols={"fixture.py": frozenset({"PUBLIC"})},
        )
        self.assertEqual([], errors)

    def test_split_identity_surface_rejects_unsafe_class_in_new_section(self) -> None:
        """New sections cannot execute class bodies during package import."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "fixture"
            package.mkdir()
            section = package / "new.py"
            source = "class NEW:\n    __slots__ = (1,)\n"
            section.write_text(source, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Unsafe import-time class definition"):
                compare_identity_surfaces._validate_import_time_section(
                    source,
                    section,
                    package,
                    frozenset({"new"}),
                )

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

    def test_split_identity_surface_allows_ordinary_dynamic_attribute_and_import_lookup(self) -> None:
        """Runtime object lookup is allowed when it is not a namespace escape."""
        source = (
            "import importlib\n"
            "def read_field(obj, field):\n"
            "    return getattr(obj, field, None)\n"
            "def load_module(name):\n"
            "    return importlib.import_module(name)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        exports = compare_identity_surfaces._section_exports(source, "fixture.py")
        self.assertIn("read_field", exports)
        self.assertIn("load_module", exports)

    def test_split_identity_surface_tracks_only_static_sensitive_import_roots(self) -> None:
        """Unknown import targets remain compatible while static sys aliases fail closed."""
        allowed = (
            "import importlib\n"
            "def load(name, key):\n"
            "    return getattr(importlib.import_module(name), key)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        exports = compare_identity_surfaces._section_exports(allowed, "fixture.py")
        self.assertIn("load", exports)
        rejected = (
            "import importlib\n"
            "name = 's' + 'ys'\n"
            "module = importlib.import_module(name)\n"
            "value = getattr(module, key)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(rejected, "fixture.py")

    def test_split_identity_surface_rejects_indirect_namespace_registry_aliases(self) -> None:
        """Indirect reflection helpers cannot expose or mutate sys.modules."""
        sources = (
            "import sys, operator\n"
            "get = operator.attrgetter('modules')\n"
            "registry = get(sys)\n"
            "registry[__name__].__setattr__('PUBLIC', 2)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "get = getattr\n"
            "registry = get(sys, 'modules')\n"
            "registry[__name__].__setattr__('PUBLIC', 2)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "get = object.__getattribute__\n"
            "registry = get(sys, 'modules')\n"
            "registry[__name__].__setattr__('PUBLIC', 2)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "registry = getattr(sys, 'mod' + 'ules')\n"
            "registry[__name__].__setattr__('PUBLIC', 2)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "key = 'mod' + 'ules'\n"
            "registry = sys.__getattribute__(key)\n"
            "registry[__name__].__setattr__('PUBLIC', 2)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "sys.__setattr__('modules', {})\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "key = 'modules'\n"
            "sys.__delattr__(key)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "key = 'mod' + 'ules'\n"
            "registry = object.__getattribute__(sys, key)\n"
            "registry[__name__] = object()\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\n"
            "_phase192_get = getattr\n"
            "registry = _phase192_get(sys, 'modules')\n"
            "registry[__name__].__setattr__('PUBLIC', 2)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import importlib\n"
            "loader = importlib.import_module\n"
            "module = loader('built' + 'ins')\n"
            "module.exec('PUBLIC = 2')\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_dynamic_namespace_subscript_reads(self) -> None:
        """Namespace subscripts cannot hide dynamic or dangerous key reads."""
        for source in (
            "value = globals()[key]\n__all__ = []\n",
            "value = globals()['__builtins__']\n__all__ = []\n",
            "value = locals()[key]\n__all__ = []\n",
        ):
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_phase192_dynamic_dangerous_key(self) -> None:
        """Private-looking dynamic keys cannot bypass dangerous namespace-key checks."""
        source = (
            "_phase192_key = '__builtins__'\n"
            "PUBLIC = globals().get(_phase192_key).get('exec')\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_namespace_read_aliases(self) -> None:
        """Namespace reads cannot be rebound to aliases before a dangerous lookup."""
        sources = (
            "get = globals().get\n"
            "get('__builtins__')['exec']('PUBLIC = 2')\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "get = globals().__getitem__\n"
            "get('__builtins__')['exec']('PUBLIC = 2')\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "get = globals().copy\n"
            "data = get()\n"
            "data['__builtins__']['exec']('PUBLIC = 2')\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "def read(get=globals().get):\n"
            "    return get('PUBLIC')\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_compatibility_rejects_assert_false_initializer(self) -> None:
        """An initializer assertion is never treated as a safe additive statement."""
        self.assertFalse(
            compare_identity_surfaces._initializer_is_additive(
                ("from . import live",),
                ("from . import live", "assert False"),
                frozenset({"live"}),
                frozenset(),
                {},
            )
        )


__all__ = ["CompatibilitySurfaceTests"]
