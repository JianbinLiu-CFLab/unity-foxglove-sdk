from .foundation import *


class SplitContractTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_identity_surface_rejects_missing_decomposed_manifest(self) -> None:
        """A discovered Decomposed facade must declare its section manifest."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            facade = root / "Scripts/ros2forunity/windows/jazzy/build.py"
            package = root / "Scripts/ros2forunity/windows/jazzy/Decomposed/build"
            package.mkdir(parents=True)
            facade.write_text("PUBLIC = 1\n", encoding="utf-8")
            (package / "01_core.py").write_text("VALUE = 1\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Missing source section manifest"):
                compare_identity_surfaces._split_facades(root)

    def test_identity_surface_accepts_dotted_manifest_section_names(self) -> None:
        """Manifest-backed sections may preserve legacy dotted filenames."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            facade = root / "Scripts/ros2forunity/windows/humble/phase160.py"
            package = root / "Scripts/ros2forunity/windows/humble/Decomposed/phase160"
            package.mkdir(parents=True)
            facade.write_text("PUBLIC = 1\n", encoding="utf-8")
            name = "phase160_r2fu_humble_windows_build.BuildPipeline.py"
            (package / "sections.manifest").write_text(name + "\n", encoding="utf-8")
            (package / name).write_text("VALUE = 1\n", encoding="utf-8")
            surfaces = compare_identity_surfaces._surfaces(root, strict=True)
            self.assertEqual(("phase160_r2fu_humble_windows_build.BuildPipeline",), surfaces[
                "Scripts/ros2forunity/windows/humble/phase160.py"
            ][1])

    def test_split_identity_surface_rejects_relative_star_chain_change(self) -> None:
        """Changing a section's relative star-import source changes runtime overwrite order."""
        with tempfile.TemporaryDirectory() as temp:
            roots = [Path(temp) / name for name in ("base", "head")]
            for root in roots:
                package = root / "Scripts/smoke/foxrun/fixture"
                package.mkdir(parents=True)
                (root / "Scripts/smoke/foxrun/fixture.py").write_text("PUBLIC = 1\n", encoding="utf-8")
                (package / "__init__.py").write_text(
                    "from . import a as _phase192_section_0\n"
                    "from . import c as _phase192_section_1\n"
                    "from . import b as _phase192_section_2\n"
                    "_PHASE192_SECTION_MODULES = (_phase192_section_0, _phase192_section_1, _phase192_section_2)\n",
                    encoding="utf-8",
                )
                for name, value in (("a", 1), ("c", 2)):
                    (package / f"{name}.py").write_text(
                        f"X = {value}\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                        encoding="utf-8",
                    )
                (package / "b.py").write_text(
                    "from .a import *\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                    encoding="utf-8",
                )
            (roots[1] / "Scripts/smoke/foxrun/fixture/b.py").write_text(
                "from .c import *\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                encoding="utf-8",
            )
            base = compare_identity_surfaces._surfaces(roots[0], strict=True)
            head = compare_identity_surfaces._surfaces(roots[1], strict=True)
            errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
            self.assertIn("RELATIVE_STAR_IMPORT_CHANGED Scripts/smoke/foxrun/fixture.py", errors)

    def test_split_identity_surface_rejects_missing_relative_facade_or_section_target(self) -> None:
        """Facade and section relative imports must resolve to real files and exports."""
        for location in ("facade", "section"):
            with self.subTest(location=location), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                package = root / "Scripts/smoke/foxrun/fixture"
                package.mkdir(parents=True)
                facade_source = "from .missing import X\n" if location == "facade" else "PUBLIC = 1\n"
                section_source = (
                    "from .missing import X\n"
                    "__all__ = [name for name in globals() if not name.startswith('__')]\n"
                    if location == "section"
                    else "LIVE = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n"
                )
                (root / "Scripts/smoke/foxrun/fixture.py").write_text(facade_source, encoding="utf-8")
                (package / "__init__.py").write_text("from . import live\n", encoding="utf-8")
                (package / "live.py").write_text(section_source, encoding="utf-8")
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._surfaces(root)

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

    def test_split_identity_surface_allows_added_relative_section_contract(self) -> None:
        """Adding a declared section preserves the baseline relative-import chain."""
        with tempfile.TemporaryDirectory() as temp:
            roots = [Path(temp) / name for name in ("base", "head")]
            for root in roots:
                package = root / "Scripts/smoke/foxrun/fixture"
                package.mkdir(parents=True)
                (root / "Scripts/smoke/foxrun/fixture.py").write_text(
                    "PUBLIC = 1\n", encoding="utf-8"
                )
                (package / "live.py").write_text(
                    "LIVE = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                    encoding="utf-8",
                )
                (package / "__init__.py").write_text(
                    "from . import live\n", encoding="utf-8"
                )
            head_package = roots[1] / "Scripts/smoke/foxrun/fixture"
            (head_package / "new.py").write_text(
                "NEW = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                encoding="utf-8",
            )
            (head_package / "__init__.py").write_text(
                "from . import live\nfrom . import new\n", encoding="utf-8"
            )
            base = compare_identity_surfaces._surfaces(roots[0], strict=False)
            head = compare_identity_surfaces._surfaces(roots[1], strict=False)
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

    def test_split_identity_surface_rejects_ephemeral_exception_alias_export(self) -> None:
        """An exception target is deleted after its handler and cannot be exported."""
        source = (
            "try:\n    raise Exception()\n"
            "except Exception as PUBLIC:\n    pass\n"
            "__all__ = ['PUBLIC']\n"
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(source, "fixture.py")
        self.assertNotIn(
            "PUBLIC",
            compare_identity_surfaces._symbols(source, "fixture.py", reject_conditional=False),
        )

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
            from Scripts.validation.source_decomposition.source_layout import section_modules
            with self.assertRaises(ValueError):
                section_modules(package)

    def test_split_identity_surface_rejects_explicit_relative_target_switch(self) -> None:
        """Changing an explicit relative import target changes runtime wiring."""
        with tempfile.TemporaryDirectory() as base_temp, tempfile.TemporaryDirectory() as head_temp:
            def make(root: Path, target: str) -> dict:
                """Internal helper for make."""
                package = root / "Scripts/smoke/foxrun/fixture"
                package.mkdir(parents=True)
                facade = root / "Scripts/smoke/foxrun/fixture.py"
                facade.write_text("PUBLIC = 1\n__all__ = ['PUBLIC']\n", encoding="utf-8")
                (package / "__init__.py").write_text("from . import a\nfrom . import b\nfrom . import c\n", encoding="utf-8")
                (package / "a.py").write_text("X = 1\n__all__ = ['X']\n", encoding="utf-8")
                (package / "b.py").write_text(
                    f"from .{target} import X\n__all__ = ['X']\n", encoding="utf-8"
                )
                (package / "c.py").write_text("X = 2\n__all__ = ['X']\n", encoding="utf-8")
                (package / "__main__.py").write_text("from . import *\n", encoding="utf-8")
                return {"Scripts/smoke/foxrun/fixture.py": compare_identity_surfaces._surface(root, facade)}
            base = make(Path(base_temp), "a")
            head = make(Path(head_temp), "c")
            errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
            self.assertIn("RELATIVE_STAR_IMPORT_CHANGED Scripts/smoke/foxrun/fixture.py", errors)

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

    def test_split_identity_surface_rejects_declared_section_importing_orphan(self) -> None:
        """A declared section cannot pull an unregistered sibling into its runtime surface."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Scripts/smoke/foxrun/fixture"
            package.mkdir(parents=True)
            (root / "Scripts/smoke/foxrun/fixture.py").write_text(
                "PUBLIC = 1\n", encoding="utf-8"
            )
            (package / "__init__.py").write_text(
                "from . import core\n", encoding="utf-8"
            )
            (package / "core.py").write_text(
                "from .evil import VALUE\nVALUE = VALUE\n"
                "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                encoding="utf-8",
            )
            (package / "evil.py").write_text("VALUE = 1\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                compare_identity_surfaces._surfaces(root, strict=False)


__all__ = ["SplitContractTests"]
