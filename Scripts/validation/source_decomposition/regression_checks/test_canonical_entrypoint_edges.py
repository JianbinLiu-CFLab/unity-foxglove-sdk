from __future__ import annotations

import os
import shutil
import unittest
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces
from Scripts.validation.source_decomposition.compare_identity_surfaces import entrypoint_contracts
from Scripts.validation.source_decomposition.compare_identity_surfaces import foundation
from Scripts.validation.source_decomposition.compare_identity_surfaces import entrypoint_migration_contracts
from Scripts.validation.source_decomposition.regression_checks.canonical_entrypoint_fixtures import (
    CanonicalEntrypointFixtureMixin,
)


class CanonicalEntrypointEdgeTests(CanonicalEntrypointFixtureMixin, unittest.TestCase):
    """Exercise hostile edge cases for canonical entrypoint compatibility."""

    def test_standard_migration_accepts_one_leading_run_entrypoint_docstring(self) -> None:
        """A single leading string docstring is ignored in the mechanical body comparison."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            entrypoint = repository / "Scripts/smoke/fixture/entrypoint.py"
            entrypoint.write_text(
                entrypoint.read_text(encoding="utf-8").replace(
                    "def run_entrypoint():\n",
                    'def run_entrypoint():\n    """Run the legacy delegate."""\n',
                    1,
                ),
                encoding="utf-8",
            )
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertTrue(
                    entrypoint_contracts._approved_entrypoint_contract(
                        repository,
                        "Scripts/smoke/fixture.py",
                        base_root=base_root,
                        base_relative="Scripts/smoke/fixture.py",
                    )
                )
        finally:
            temporary.cleanup()

    def test_already_standard_migration_accepts_one_leading_run_entrypoint_docstring(self) -> None:
        """A standard-base wrapper may add one leading string docstring without changing its body."""
        temporary, repository, _ = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            base = self._commit(repository, "standard entrypoint")
            entrypoint = repository / "Scripts/smoke/fixture/entrypoint.py"
            entrypoint.write_text(
                entrypoint.read_text(encoding="utf-8").replace(
                    "def run_entrypoint():\n",
                    'def run_entrypoint():\n    """Run the standard delegate."""\n',
                    1,
                ),
                encoding="utf-8",
            )
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertTrue(
                    entrypoint_contracts._approved_entrypoint_contract(
                        repository,
                        "Scripts/smoke/fixture.py",
                        base_root=base_root,
                        base_relative="Scripts/smoke/fixture.py",
                    )
                )
        finally:
            temporary.cleanup()

    def test_standard_docstring_baseline_remains_compatible(self) -> None:
        """A trusted standard wrapper with a leading docstring remains compatible unchanged."""
        temporary, repository, _ = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            entrypoint = repository / "Scripts/smoke/fixture/entrypoint.py"
            entrypoint.write_text(
                entrypoint.read_text(encoding="utf-8").replace(
                    "def run_entrypoint():\n",
                    'def run_entrypoint():\n    """Run the trusted delegate."""\n',
                    1,
                ),
                encoding="utf-8",
            )
            base = self._commit(repository, "standard entrypoint with docstring")
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertTrue(
                    entrypoint_contracts._approved_entrypoint_contract(
                        repository,
                        "Scripts/smoke/fixture.py",
                        base_root=base_root,
                        base_relative="Scripts/smoke/fixture.py",
                    )
                )
        finally:
            temporary.cleanup()

    def test_legacy_guard_string_and_run_entrypoint_docstring_are_compatible(self) -> None:
        """A trusted legacy guard string does not block a leading run_entrypoint docstring."""
        temporary, repository, base = self._create_repository(
            facade_source=(
                "from __future__ import annotations\n"
                "import sys as _phase192_sys\n"
                "if __package__:\n"
                "    from .fixture import *\n"
                "else:\n"
                "    from fixture import *\n"
                "if __name__ == '__main__':\n"
                "    \"\"\"Legacy guard note.\"\"\"\n"
                "    raise SystemExit(main())\n"
            )
        )
        try:
            self._migrate_to_standard_entrypoint(repository)
            entrypoint = repository / "Scripts/smoke/fixture/entrypoint.py"
            entrypoint.write_text(
                entrypoint.read_text(encoding="utf-8").replace(
                    "def run_entrypoint():\n",
                    'def run_entrypoint():\n    """Run the legacy delegate."""\n',
                    1,
                ),
                encoding="utf-8",
            )
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertTrue(
                    entrypoint_contracts._approved_entrypoint_contract(
                        repository,
                        "Scripts/smoke/fixture.py",
                        base_root=base_root,
                        base_relative="Scripts/smoke/fixture.py",
                    )
                )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_nonleading_or_nonstring_run_entrypoint_docstrings(self) -> None:
        """Only one exact leading string expression is accepted as a run_entrypoint docstring."""
        cases = (
            (
                "after import",
                "    from .live import main\n    \"\"\"late\"\"\"\n    return main()\n",
            ),
            (
                "after statement",
                "    from .live import main\n    return main()\n    \"\"\"late\"\"\"\n",
            ),
            (
                "consecutive strings",
                "    \"\"\"first\"\"\"\n    \"\"\"second\"\"\"\n    from .live import main\n    return main()\n",
            ),
            (
                "bytes",
                "    b'bytes'\n    from .live import main\n    return main()\n",
            ),
            (
                "f-string",
                "    f'formatted'\n    from .live import main\n    return main()\n",
            ),
            (
                "binary operation",
                "    'a' + 'b'\n    from .live import main\n    return main()\n",
            ),
            (
                "only docstring",
                "    \"\"\"only\"\"\"\n",
            ),
        )
        for name, body in cases:
            with self.subTest(name=name):
                temporary, repository, base = self._create_repository()
                try:
                    self._migrate_to_standard_entrypoint(repository)
                    entrypoint = repository / "Scripts/smoke/fixture/entrypoint.py"
                    source = entrypoint.read_text(encoding="utf-8")
                    function_start = source.index("def run_entrypoint():")
                    function_end = source.index("\n\n__all__", function_start)
                    entrypoint.write_text(
                        source[:function_start]
                        + "def run_entrypoint():\n"
                        + body
                        + source[function_end:],
                        encoding="utf-8",
                    )
                    with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                        self.assertFalse(
                            entrypoint_contracts._approved_entrypoint_contract(
                                repository,
                                "Scripts/smoke/fixture.py",
                                base_root=base_root,
                                base_relative="Scripts/smoke/fixture.py",
                            )
                        )
                finally:
                    temporary.cleanup()

    def test_namespace_change_rejects_symlinked_existing_section(self) -> None:
        """A migrated package cannot replace an existing section with an external symlink."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            package = repository / "Scripts/smoke/fixture"
            section = package / "live.py"
            outside = Path(temporary.name).parent / f"{Path(temporary.name).name}_live.py"
            outside.write_bytes(section.read_bytes())
            section.unlink()
            try:
                os.symlink(outside, section)
            except OSError as error:
                self.skipTest(f"symlink unavailable: {error}")
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertFalse(
                    foundation._approved_namespace_change(
                        "Scripts/smoke/fixture.py",
                        base_root,
                        repository,
                    )
                )
        finally:
            temporary.cleanup()

    def test_namespace_change_rejects_symlinked_package_subdirectory(self) -> None:
        """A migrated package cannot contain a symlinked child directory."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            package = repository / "Scripts/smoke/fixture"
            outside = Path(temporary.name).parent / f"{Path(temporary.name).name}_nested"
            outside.mkdir()
            try:
                os.symlink(outside, package / "nested", target_is_directory=True)
            except OSError as error:
                self.skipTest(f"symlink unavailable: {error}")
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertFalse(
                    foundation._approved_namespace_change(
                        "Scripts/smoke/fixture.py",
                        base_root,
                        repository,
                    )
                )
        finally:
            temporary.cleanup()

















    def test_standard_migration_rejects_symlinked_entrypoint_section(self) -> None:
        """A symlink cannot provide the standard forwarding section implementation."""
        temporary, repository, base = self._create_repository()
        try:
            target = repository / "Scripts/smoke/fixture/entrypoint_target.py"
            target.write_text(
                "def run_entrypoint():\n"
                "    from .live import main\n"
                "    return main()\n\n"
                "__all__ = ['run_entrypoint']\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "add forwarding target")
            self._migrate_to_standard_entrypoint(repository)
            entrypoint = repository / "Scripts/smoke/fixture/entrypoint.py"
            entrypoint.unlink()
            try:
                os.symlink(target, entrypoint)
            except OSError as error:
                self.skipTest(f"symlink unavailable: {error}")
            self.assertFalse(entrypoint_contracts._approved_entrypoint_section(entrypoint))
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_symlinked_facade(self) -> None:
        """A symlink cannot provide the standard forwarding facade implementation."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            facade = repository / "Scripts/smoke/fixture.py"
            outside = Path(temporary.name) / "outside_fixture.py"
            outside.write_text(facade.read_text(encoding="utf-8"), encoding="utf-8")
            facade.unlink()
            try:
                os.symlink(outside, facade)
            except OSError as error:
                self.skipTest(f"symlink unavailable: {error}")
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertFalse(
                    entrypoint_contracts._approved_entrypoint_contract(
                        repository,
                        "Scripts/smoke/fixture.py",
                        base_root=base_root,
                        base_relative="Scripts/smoke/fixture.py",
                    )
                )
        finally:
            temporary.cleanup()

    def test_main_block_only_accepts_phase184_shape(self) -> None:
        """The reviewed Phase184 package accepts its exact appended module guard."""
        root = Path(__file__).resolve().parents[4]
        relative_facade = Path(
            "Scripts/smoke/foxrun/phase184_foxglove_desktop_live_acceptance.py"
        )
        relative_package = relative_facade.with_suffix("")
        temporary = self._temporary_repository()
        try:
            base_root = Path(temporary.name)
            (base_root / relative_facade.parent).mkdir(parents=True, exist_ok=True)
            shutil.copy2(root / relative_facade, base_root / relative_facade)
            shutil.copytree(
                root / relative_package,
                base_root / relative_package,
                ignore=shutil.ignore_patterns("__pycache__", "*.pyc"),
            )
            import ast

            base_main = base_root / relative_package / "__main__.py"
            base_tree = ast.parse(base_main.read_text(encoding="utf-8"))
            base_main.write_text(
                ast.unparse(
                    ast.Module(body=base_tree.body[:-1], type_ignores=[])
                )
                + "\n",
                encoding="utf-8",
            )
            head_root = base_root / "head"
            shutil.copytree(base_root / "Scripts", head_root / "Scripts")
            module_entrypoint = head_root / relative_package / "__main__.py"
            module_entrypoint.write_text(
                module_entrypoint.read_text(encoding="utf-8")
                + "\nif __name__ == '__main__':\n"
                + "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            self.assertTrue(
                entrypoint_migration_contracts._approved_main_block_only_contract(
                    base_root,
                    head_root,
                    relative_facade.as_posix(),
                    relative_facade.as_posix(),
                )
            )
        finally:
            temporary.cleanup()

    def test_module_entrypoint_full_guard_matches_trusted_facade(self) -> None:
        """A package module guard may mirror the unchanged trusted facade guard."""
        facade_source = (
            "from __future__ import annotations\n"
            "import sys as _phase192_sys\n"
            "if __package__:\n"
            "    from .fixture import *\n"
            "else:\n"
            "    from fixture import *\n"
            "if __name__ == '__main__':\n"
            "    try:\n"
            "        raise SystemExit(main(_phase192_sys.argv[1:]))\n"
            "    except KeyboardInterrupt:\n"
            "        raise SystemExit(130)\n"
            "    except Exception as exc:\n"
            "        print(f'[fixture] FAIL: {exc}', file=_phase192_sys.stderr)\n"
            "        raise SystemExit(1)\n"
        )
        base_main = (
            "from __future__ import annotations\n"
            "import sys\n"
            "from . import main\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(main(sys.argv[1:]))\n"
        )
        head_main = base_main.replace(
            "    raise SystemExit(main(sys.argv[1:]))\n",
            "    try:\n"
            "        raise SystemExit(main(sys.argv[1:]))\n"
            "    except KeyboardInterrupt:\n"
            "        raise SystemExit(130)\n"
            "    except Exception as exc:\n"
            "        print(f'[fixture] FAIL: {exc}', file=sys.stderr)\n"
            "        raise SystemExit(1)\n",
        )
        temporary, repository, base = self._create_repository(
            facade_source=facade_source,
            main_source=base_main,
            live_source="def main(argv):\n    return 0\n\n__all__ = ['main']\n",
        )
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(head_main, encoding="utf-8")
            head = self._commit(repository, "mirror facade module guard")
            self.assertEqual(
                0,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_module_entrypoint_full_guard_rejects_mismatch_and_symlink(self) -> None:
        """A mirrored module guard must match the trusted guard and regular package files."""
        facade_source = (
            "from __future__ import annotations\n"
            "import sys as _phase192_sys\n"
            "if __package__:\n"
            "    from .fixture import *\n"
            "else:\n"
            "    from fixture import *\n"
            "if __name__ == '__main__':\n"
            "    try:\n"
            "        raise SystemExit(main(_phase192_sys.argv[1:]))\n"
            "    except KeyboardInterrupt:\n"
            "        raise SystemExit(130)\n"
            "    except Exception as exc:\n"
            "        print(f'[fixture] FAIL: {exc}', file=_phase192_sys.stderr)\n"
            "        raise SystemExit(1)\n"
        )
        base_main = (
            "from __future__ import annotations\n"
            "import sys\n"
            "from . import main\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(main(sys.argv[1:]))\n"
        )
        head_main = base_main.replace(
            "    raise SystemExit(main(sys.argv[1:]))\n",
            "    try:\n"
            "        raise SystemExit(main(sys.argv[2:]))\n"
            "    except KeyboardInterrupt:\n"
            "        raise SystemExit(130)\n"
            "    except Exception as exc:\n"
            "        print(f'[fixture] FAIL: {exc}', file=sys.stderr)\n"
            "        raise SystemExit(1)\n",
        )
        temporary, repository, base = self._create_repository(
            facade_source=facade_source,
            main_source=base_main,
            live_source="def main(argv):\n    return 0\n\n__all__ = ['main']\n",
        )
        try:
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.write_text(head_main, encoding="utf-8")
            head = self._commit(repository, "mismatch facade module guard")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
            entrypoint.write_text(
                head_main.replace("sys.argv[2:]", "_phase192_sys.argv[1:]"),
                encoding="utf-8",
            )
            alias_head = self._commit(repository, "use an unbound module sys alias")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, alias_head, strict=False),
            )
            entrypoint.write_text(head_main, encoding="utf-8")
            package = repository / "Scripts/smoke/fixture"
            section = package / "live.py"
            outside = Path(temporary.name).parent / f"{Path(temporary.name).name}_live.py"
            outside.write_bytes(section.read_bytes())
            section.unlink()
            try:
                os.symlink(outside, section)
            except OSError as error:
                self.skipTest(f"symlink unavailable: {error}")
            with compare_identity_surfaces._revision_checkout(repository, base) as base_root:
                self.assertFalse(
                    entrypoint_migration_contracts._approved_module_entrypoint_guard_contract(
                        base_root,
                        repository,
                        "Scripts/smoke/fixture.py",
                        "Scripts/smoke/fixture.py",
                    )
                )
        finally:
            temporary.cleanup()
