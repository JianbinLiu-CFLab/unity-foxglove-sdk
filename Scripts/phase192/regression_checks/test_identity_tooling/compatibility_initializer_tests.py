from .foundation import *


class CompatibilityInitializerTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_compatibility_rejects_import_time_side_effect_section(self) -> None:
        """New sections reject top-level raises and calls before compatibility approval."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "live.py").write_text("LIVE = 1\n", encoding="utf-8")
            for source in (
                "raise RuntimeError('boom')\nNEW = 1\n",
                "import os\nos.remove('boom')\nNEW = 1\n",
                "boom()\nNEW = 1\n",
            ):
                with self.subTest(source=source):
                    section = package / "new_section.py"
                    section.write_text(source, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        compare_identity_surfaces._validate_import_time_section(
                            source,
                            section,
                            package,
                            frozenset({"new_section"}),
                        )

    def test_compatibility_rejects_import_time_annotation_execution(self) -> None:
        """New sections cannot execute calls or resolve unknown annotations at import time."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            sources = (
                "VALUE: missing_type() = 1\n",
                "def read(value: MissingType):\n    return value\n",
                "def read(value=[item for item in range(1)]):\n    return value\n",
                "class Record:\n    value: MissingType = 1\n",
                "class Record:\n    value: MissingType() = 1\n",
                "import typing\nclass Record(typing.MissingType):\n    pass\n",
            )
            for index, source in enumerate(sources):
                with self.subTest(index=index):
                    section = package / f"new_{index}.py"
                    section.write_text(source, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        compare_identity_surfaces._validate_import_time_section(
                            source,
                            section,
                            package,
                            frozenset({section.stem}),
                        )

            postponed = (
                "from __future__ import annotations\n"
                "def read(value: MissingType):\n"
                "    return value\n"
            )
            section = package / "postponed.py"
            section.write_text(postponed, encoding="utf-8")
            compare_identity_surfaces._validate_import_time_section(
                postponed,
                section,
                package,
                frozenset({section.stem}),
            )

    def test_compatibility_requires_bound_type_checking_names(self) -> None:
        """TYPE_CHECKING guards must use an imported, statically known name."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            unbound = "if TYPE_CHECKING:\n    VALUE = 1\n"
            section = package / "unbound.py"
            section.write_text(unbound, encoding="utf-8")
            with self.assertRaises(ValueError):
                compare_identity_surfaces._validate_import_time_section(
                    unbound, section, package, frozenset({section.stem})
                )

            qualified_unbound = "if typing.TYPE_CHECKING:\n    VALUE = 1\n"
            section = package / "qualified_unbound.py"
            section.write_text(qualified_unbound, encoding="utf-8")
            with self.assertRaises(ValueError):
                compare_identity_surfaces._validate_import_time_section(
                    qualified_unbound, section, package, frozenset({section.stem})
                )

            bound = "import typing\nif typing.TYPE_CHECKING:\n    VALUE = 1\n"
            section = package / "bound.py"
            section.write_text(bound, encoding="utf-8")
            compare_identity_surfaces._validate_import_time_section(
                bound, section, package, frozenset({section.stem})
            )

    def test_compatibility_rejects_broken_added_facade_initializer(self) -> None:
        """A newly added facade cannot hide an import-breaking package initializer."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Scripts/smoke/foxrun/new_fixture"
            package.mkdir(parents=True)
            (root / "Scripts/smoke/foxrun/new_fixture.py").write_text(
                "NEW = 1\n", encoding="utf-8"
            )
            (package / "live.py").write_text(
                "LIVE = 1\n__all__ = ['LIVE']\n", encoding="utf-8"
            )
            (package / "__init__.py").write_text(
                "raise RuntimeError('boom')\nfrom . import live\n", encoding="utf-8"
            )
            surfaces = compare_identity_surfaces._surfaces(root, strict=False)
            with self.assertRaises(ValueError):
                compare_identity_surfaces._validate_compatibility_new_sections(
                    root / "base",
                    root,
                    {},
                    surfaces,
                )

    def test_split_identity_surface_rejects_operator_and_partial_registry_aliases(self) -> None:
        """Other standard-library helper combinators cannot hide registry access."""
        sources = (
            "import sys, operator\n"
            "get = operator.methodcaller('__getattribute__', 'modules')\n"
            "registry = get(sys)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import builtins, functools\n"
            "get = functools.partial(getattr, 'modules')\n"
            "registry = get(builtins)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_reflection_helper_import_aliases(self) -> None:
        """Reflection helpers imported directly cannot evade alias tracking."""
        sources = (
            "from sys import __getattribute__ as get\n"
            "import sys\n"
            "registry = get(sys, 'modules')\n"
            "PUBLIC = 1\n",
            "from operator import attrgetter as get\n"
            "import sys\n"
            "registry = get('modules')(sys)\n"
            "PUBLIC = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source + "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                        "fixture.py",
                    )

    def test_split_identity_surface_rejects_importlib_result_side_effects(self) -> None:
        """Dynamic import results cannot reach process, file, or reflection side effects."""
        sources = (
            "import importlib\n"
            "importlib.import_module('os').system('bad')\n"
            "PUBLIC = 1\n",
            "import importlib as _loader\n"
            "_loader.import_module('os').remove('bad')\n"
            "PUBLIC = 1\n",
            "import importlib\n"
            "module = importlib.import_module(name)\n"
            "module.system('bad')\n"
            "PUBLIC = 1\n",
            "import importlib\n"
            "module = importlib.import_module(name)\n"
            "getattr(module, 'system')('bad')\n"
            "PUBLIC = 1\n",
            "import importlib\n"
            "getattr(importlib, key)('os')\n"
            "PUBLIC = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source + "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                        "fixture.py",
                    )

    def test_split_identity_surface_rejects_nested_namespace_escapes(self) -> None:
        """Namespace dictionaries cannot escape through containers, returns, or defaults."""
        sources = (
            "def consume(value):\n"
            "    value[0]['PUBLIC'] = 2\n"
            "consume((globals(),))\n"
            "PUBLIC = 1\n",
            "def consume(value=None):\n"
            "    value['PUBLIC'] = 2\n"
            "consume(value=globals())\n"
            "PUBLIC = 1\n",
            "def leak():\n"
            "    return globals()\n"
            "PUBLIC = 1\n",
            "def leak(value=globals()):\n"
            "    return value\n"
            "PUBLIC = 1\n",
            "values = [value for value in (globals(),)]\n"
            "PUBLIC = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(
                        source + "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                        "fixture.py",
                    )

    def test_split_identity_surface_allows_dynamic_import_loader_function(self) -> None:
        """Existing runtime loaders may return an imported module without reflecting on it."""
        source = (
            "import importlib\n"
            "def load_module(name):\n"
            "    return importlib.import_module(name)\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        exports = compare_identity_surfaces._section_exports(source, "fixture.py")
        self.assertIn("load_module", exports)

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


    def test_split_identity_surface_allows_section_tuple_addition(self) -> None:
        """Adding a declared section preserves the generated initializer contract."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}),
                ("live",),
                (
                    "from . import live as _phase192_section_0",
                    "_PHASE192_SECTION_MODULES = (_phase192_section_0,)",
                ),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "NEW"}),
                ("live", "new"),
                (
                    "from . import live as _phase192_section_0",
                    "from . import new as _phase192_section_1",
                    "_PHASE192_SECTION_MODULES = (_phase192_section_0, _phase192_section_1)",
                ),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, head, strict=False))

    def test_split_identity_surface_rejects_unbound_section_tuple_alias(self) -> None:
        """The generated section registry cannot reference an undeclared module alias."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}),
                ("live",),
                (
                    "from . import live as _phase192_section_0",
                    "_PHASE192_SECTION_MODULES = (_phase192_section_0,)",
                ),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE"}),
                ("live",),
                (
                    "from . import live as _phase192_section_0",
                    "_PHASE192_SECTION_MODULES = (_phase192_section_0, _phase192_section_fake)",
                ),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_missing_relative_initializer_member(self) -> None:
        """Relative initializer imports must name an export of the referenced section."""
        base = {
            "fixture.py": (frozenset({"LIVE"}), ("live",), ("from . import live",)),
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "Missing"}),
                ("live",),
                ("from . import live", "from .live import Missing"),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

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
        """Internal helper for test_split_identity_surface_preserves_module_docstring_position."""
        base = {"Scripts/smoke/foxrun/demo.py": (frozenset({"run"}), ("live",), ('"doc"',), frozenset({"run"}))}
        moved = {"Scripts/smoke/foxrun/demo.py": (frozenset({"run"}), ("live",), ("pass", '"doc"'), frozenset({"run"}))}
        errors = compare_identity_surfaces._compare_surfaces(base, moved, strict=False)
        self.assertIn("PACKAGE_INITIALIZER_CHANGED Scripts/smoke/foxrun/demo.py", errors)

    def test_split_identity_surface_allows_annotated_and_relative_initializer_additions(self) -> None:
        """Compatibility mode allows inert annotations and valid relative aliases."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}), ("live",), ("from . import live",),
                frozenset({"LIVE"}), None, frozenset(), False,
                (("live", frozenset()),), frozenset(), (), None, (), (),
            ),
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
                frozenset({"LIVE", "Value"}), None, frozenset(), False,
                (("live", frozenset({"Value"})), ("new", frozenset())),
                frozenset(), (), None, (), (),
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

    def test_split_identity_surface_rejects_unresolvable_safe_root_imports(self) -> None:
        """Allowlisted standard-library roots still require real modules and members."""
        base = {"fixture.py": (frozenset(), (), ("pass",))}
        for statement in ("import json.no_such", "from json import NO_SUCH"):
            with self.subTest(statement=statement):
                head = {"fixture.py": (frozenset(), (), ("pass", statement))}
                errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
                self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

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

    def test_split_identity_surface_rejects_assert_and_non_definite_initializer_references(self) -> None:
        """Compatibility additions cannot add failing asserts or use conditional/ephemeral names."""
        base_cases = (
            (("pass",), ("pass", "assert False")),
            (("if False:\n    PRIVATE = 1",), ("if False:\n    PRIVATE = 1", "def NEW(value=PRIVATE): pass")),
            (("try:\n    raise Exception()\nexcept Exception as TEMP:\n    pass",), ("try:\n    raise Exception()\nexcept Exception as TEMP:\n    pass", "def NEW(value=TEMP): pass")),
            (("OLD = 1", "del OLD"), ("OLD = 1", "del OLD", "def NEW(value=OLD): pass")),
        )
        for base_initializer, head_initializer in base_cases:
            with self.subTest(head_initializer=head_initializer):
                base = {"fixture.py": (frozenset(), (), base_initializer)}
                head = {"fixture.py": (frozenset({"NEW"}), (), head_initializer)}
                errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
                self.assertIn("PACKAGE_INITIALIZER_CHANGED fixture.py", errors)

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


__all__ = ["CompatibilityInitializerTests"]
