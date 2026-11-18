from __future__ import annotations

import os
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces
from Scripts.validation.source_decomposition.compare_identity_surfaces import entrypoint_contracts
from Scripts.validation.source_decomposition.regression_checks.canonical_entrypoint_fixtures import (
    CanonicalEntrypointFixtureMixin,
)

class CanonicalEntrypointContractTests(CanonicalEntrypointFixtureMixin, unittest.TestCase):
    """Exercise the Phase192 canonical entrypoint compatibility contract."""

    def test_standard_entrypoint_migration_is_compatible(self) -> None:
        """The five-file standard migration is compatible with the base surface."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            head = self._commit(repository, "standard entrypoint migration")
            self.assertEqual(
                0,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_preserves_exception_exit_contract(self) -> None:
        """The standard wrapper mechanically preserves legacy exit and stderr handling."""
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
        temporary, repository, base = self._create_repository(
            facade_source=facade_source,
            extra_source="VALUE = 0\n__all__ = []\n",
        )
        try:
            self._migrate_to_standard_entrypoint(repository)
            (repository / "Scripts/smoke/fixture/entrypoint.py").write_text(
                "def run_entrypoint():\n"
                "    import sys\n"
                "    from .live import main\n"
                "    try:\n"
                "        return main(sys.argv[1:])\n"
                "    except KeyboardInterrupt:\n"
                "        return 130\n"
                "    except Exception as exc:\n"
                "        print(f'[fixture] FAIL: {exc}', file=sys.stderr)\n"
                "        return 1\n"
                "\n"
                "__all__ = ['run_entrypoint']\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "preserve entrypoint exit contract")
            self.assertEqual(
                0,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
            self.assertTrue(
                entrypoint_contracts._approved_entrypoint_contract(
                    repository,
                    "Scripts/smoke/fixture.py",
                    base_root=repository,
                    base_relative="Scripts/smoke/fixture.py",
                )
            )
            (repository / "Scripts/smoke/fixture/extra.py").write_text(
                "# unrelated section edit\nVALUE = 0\n__all__ = []\n",
                encoding="utf-8",
            )
            unrelated = self._commit(repository, "edit unrelated section")
            self.assertEqual(
                0,
                compare_identity_surfaces.compare_revisions(repository, head, unrelated, strict=False),
            )
            entrypoint_path = repository / "Scripts/smoke/fixture/entrypoint.py"
            changed_prefix = entrypoint_path.read_text(encoding="utf-8").replace(
                "[fixture] FAIL",
                "[changed] FAIL",
            )
            entrypoint_path.write_text(changed_prefix, encoding="utf-8")
            prefix_changed = self._commit(repository, "change failure prefix")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, head, prefix_changed, strict=False),
            )
            entrypoint_path.write_text(
                "def run_entrypoint():\n"
                "    import sys\n"
                "    from .live import main\n"
                "    try:\n"
                "        return main(sys.argv[1:])\n"
                "    except Exception as exc:\n"
                "        print(f'[fixture] FAIL: {exc}', file=sys.stderr)\n"
                "        return 1\n"
                "\n"
                "__all__ = ['run_entrypoint']\n",
                encoding="utf-8",
            )
            rejected = self._commit(repository, "drop keyboard interrupt branch")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, head, rejected, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_existing_section_change(self) -> None:
        """A standard migration cannot hide a changed existing section."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            (repository / "Scripts/smoke/fixture/live.py").write_text(
                "def main():\n    return 1\n\n__all__ = ['main']\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "changed existing section")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_unrelated_section_byte_change(self) -> None:
        """A standard migration cannot hide a byte-only change in another section."""
        temporary, repository, base = self._create_repository(
            extra_source="VALUE = 0\n__all__ = []\n",
        )
        try:
            self._migrate_to_standard_entrypoint(repository)
            (repository / "Scripts/smoke/fixture/extra.py").write_text(
                "# changed during migration\nVALUE = 0\n__all__ = []\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "change unrelated section during migration")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_symlinked_forwarding_target(self) -> None:
        """A forwarding import cannot trust a symlinked target outside the package."""
        temporary, repository, base = self._create_repository()
        try:
            outside = Path(temporary.name) / "outside.py"
            outside.write_text("def main():\n    return 0\n", encoding="utf-8")
            target = repository / "Scripts/smoke/fixture/live.py"
            target.unlink()
            try:
                os.symlink(outside, target)
            except OSError as error:
                self.skipTest(f"symlink unavailable: {error}")
            self._migrate_to_standard_entrypoint(repository)
            head = self._commit(repository, "symlinked forwarding target")
            with self.assertRaises(RuntimeError):
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False)
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_noncall_delegate_and_keywords(self) -> None:
        """A standard forwarding body must call its delegate without keyword arguments."""
        variants = (
            (
                "import sys\n"
                "def run_entrypoint():\n"
                "    return sys\n"
                "\n"
                "__all__ = ['run_entrypoint']\n",
            ),
            (
                "def run_entrypoint():\n"
                "    from .live import main\n"
                "    return main(foo=1)\n"
                "\n"
                "__all__ = ['run_entrypoint']\n",
            ),
        )
        for (entrypoint_source,) in variants:
            temporary, repository, base = self._create_repository()
            try:
                self._migrate_to_standard_entrypoint(repository)
                (repository / "Scripts/smoke/fixture/entrypoint.py").write_text(
                    entrypoint_source,
                    encoding="utf-8",
                )
                head = self._commit(repository, "unsafe forwarding body")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
                )
            finally:
                temporary.cleanup()

    def test_standard_migration_rejects_new_section(self) -> None:
        """A standard migration cannot add a second non-entrypoint section."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            package = repository / "Scripts/smoke/fixture"
            (package / "extra.py").write_text("VALUE = 1\n__all__ = ['VALUE']\n", encoding="utf-8")
            head = self._commit(repository, "new section")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_facade_deviation(self) -> None:
        """A one-statement facade deviation remains incompatible."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            facade = repository / "Scripts/smoke/fixture.py"
            facade.write_text(facade.read_text(encoding="utf-8") + "_EXTRA = 1\n", encoding="utf-8")
            head = self._commit(repository, "facade deviation")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_directory_migration(self) -> None:
        """A standard entrypoint cannot be combined with a facade directory move."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            old_facade = repository / "Scripts/smoke/fixture.py"
            old_package = repository / "Scripts/smoke/fixture"
            new_facade = repository / "Scripts/phase192/fixture.py"
            new_package = repository / "Scripts/phase192/fixture"
            new_facade.parent.mkdir(parents=True)
            old_facade.replace(new_facade)
            old_package.replace(new_package)
            head = self._commit(repository, "directory migration")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_an_extra_module_entrypoint_block(self) -> None:
        """A standard migration cannot append a second module entrypoint block."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            module_entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            module_entrypoint.write_text(
                module_entrypoint.read_text(encoding="utf-8")
                + "if __name__ == '__main__':\n"
                + "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "extra module entrypoint block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_section_and_initializer_changes(self) -> None:
        """Main-block-only approval rejects any section or initializer change."""
        for target, content in (
            (
                "Scripts/smoke/fixture/live.py",
                "def main():\n    return 1\n\n__all__ = ['main']\n",
            ),
            (
                "Scripts/smoke/fixture/__init__.py",
                "from . import live as _phase192_section_0\nVALUE = 1\n",
            ),
        ):
            temporary, repository, base = self._create_repository()
            try:
                (repository / target).write_text(content, encoding="utf-8")
                (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                    "from . import *\n"
                    "if __name__ == '__main__':\n"
                    "    raise SystemExit(main())\n",
                    encoding="utf-8",
                )
                head = self._commit(repository, "invalid main block migration")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
                )
            finally:
                temporary.cleanup()

    def test_main_block_only_rejects_invalid_call_shapes(self) -> None:
        """Required, async, decorated, and missing calls remain incompatible."""
        variants = (
            (None, "missing()"),
            ("def main(required):\n    return required\n\n__all__ = ['main']\n", "main()"),
            (None, "main(1)"),
        )
        for live_source, call in variants:
            temporary, repository, base = self._create_repository(live_source=live_source)
            try:
                (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                    "from . import *\n"
                    "if __name__ == '__main__':\n"
                    f"    raise SystemExit({call})\n",
                    encoding="utf-8",
                )
                head = self._commit(repository, "invalid main call")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
                )
            finally:
                temporary.cleanup()

    def test_unlisted_relative_entrypoint_is_one_gate_error(self) -> None:
        """An unlisted relative entrypoint reports one gate error without a traceback."""
        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from .missing import *\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "unlisted entrypoint")
            output = StringIO()
            with redirect_stdout(output):
                result = compare_identity_surfaces.compare_revisions(repository, base, head, strict=False)
            self.assertEqual(1, result)
            self.assertIn("IDENTITY_CONTRACT_ERROR", output.getvalue())
            self.assertNotIn("Traceback", output.getvalue())
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_multiple_statements_and_else(self) -> None:
        """The approved main block has exactly one raise and no else branch."""
        variants = (
            (
                "if __name__ == '__main__':\n"
                "    print('extra')\n"
                "    raise SystemExit(main())\n"
            ),
            (
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n"
                "else:\n"
                "    pass\n"
            ),
        )
        for block in variants:
            temporary, repository, base = self._create_repository()
            try:
                (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                    "from . import *\n" + block,
                    encoding="utf-8",
                )
                head = self._commit(repository, "invalid main block shape")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
                )
            finally:
                temporary.cleanup()

    def test_main_block_only_rejects_async_and_decorated_section_function(self) -> None:
        """The called section function must be synchronous and undecorated."""
        variants = (
            "async def main():\n    return 0\n\n__all__ = ['main']\n",
            "def decorator(function):\n    return function\n\n@decorator\ndef main():\n    return 0\n\n__all__ = ['main']\n",
        )
        for section in variants:
            temporary, repository, base = self._create_repository(live_source=section)
            try:
                (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                    "from . import *\n"
                    "if __name__ == '__main__':\n"
                    "    raise SystemExit(main())\n",
                    encoding="utf-8",
                )
                head = self._commit(repository, "invalid section entrypoint")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
                )
            finally:
                temporary.cleanup()

    def test_stale_base_revision_fails(self) -> None:
        """A missing base revision remains a hard compatibility failure."""
        temporary, repository, base = self._create_repository()
        try:
            with self.assertRaises(RuntimeError):
                compare_identity_surfaces.compare_revisions(repository, "stale-base", base, strict=False)
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_an_existing_main_guard(self) -> None:
        """The exception cannot append a second module main guard."""
        temporary, repository, base = self._create_repository(
            main_source=(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n"
            )
        )
        try:
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.write_text(
                entrypoint.read_text(encoding="utf-8")
                + "if __name__ == '__main__':\n"
                + "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "duplicate main guard")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_requires_initializer_wildcard_export(self) -> None:
        """A wildcard module entrypoint requires the unchanged initializer to export its callable."""
        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/__init__.py").write_text(
                "__all__ = []\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "suppress package wildcard export")
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_conditional_or_unbound_exports(self) -> None:
        """Conditional definitions and literal-only initializer exports cannot satisfy wildcard resolution."""
        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/live.py").write_text(
                "if False:\n"
                "    def main():\n"
                "        return 0\n"
                "__all__ = ['main']\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "make section export conditional")
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append conditional main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/__init__.py").write_text(
                "from .live import main\n"
                "__all__ = ['main', 'missing']\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "declare incomplete initializer export")
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append incomplete main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/__init__.py").write_text(
                "from . import live as _phase192_section_0\n"
                "__all__ = ['main']\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "declare unbound initializer export")
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append unbound main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_alias_mismatch_and_unreviewed_dynamic_exports(self) -> None:
        """Aliased imports and unrelated dynamic export calls cannot satisfy the entrypoint contract."""
        temporary, repository, base = self._create_repository(main_source="from . import live as entrypoint\n")
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import live as entrypoint\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "call the source name instead of the alias")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_dynamic_export_mutations(self) -> None:
        """Mutating or deleting a dynamic export after synchronization is incompatible."""
        for mutation in ("del main\n", "_PHASE192_EXPORT_NAMES.clear()\n"):
            temporary, repository, base = self._create_repository()
            try:
                initializer = repository / "Scripts/smoke/fixture/__init__.py"
                initializer.write_text(
                    initializer.read_text(encoding="utf-8") + mutation,
                    encoding="utf-8",
                )
                base = self._commit(repository, "mutate dynamic exports")
                (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                    "from . import *\n"
                    "if __name__ == '__main__':\n"
                    "    raise SystemExit(main())\n",
                    encoding="utf-8",
                )
                head = self._commit(repository, "append main block")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(
                        repository,
                        base,
                        head,
                        strict=False,
                    ),
                )
            finally:
                temporary.cleanup()

    def test_main_block_only_rejects_empty_dynamic_section_tuple(self) -> None:
        """A dynamic export loop with no registered section cannot approve the package entrypoint."""
        temporary, repository, base = self._create_repository()
        try:
            initializer = repository / "Scripts/smoke/fixture/__init__.py"
            initializer.write_text(
                initializer.read_text(encoding="utf-8").replace(
                    "_PHASE192_SECTION_MODULES = (_phase192_section_0,)\n",
                    "_PHASE192_SECTION_MODULES = ()\n",
                ),
                encoding="utf-8",
            )
            base = self._commit(repository, "remove dynamic section membership")
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_dynamic_export_order_and_reset(self) -> None:
        """The export set must be initialized, filled, and sorted in that order exactly once."""
        initializer_mutations = (
            lambda source: source.replace(
                "__all__ = sorted(_PHASE192_EXPORT_NAMES)\n",
                "",
            ).replace(
                "_PHASE192_EXPORT_NAMES = set()\n",
                "_PHASE192_EXPORT_NAMES = set()\n__all__ = sorted(_PHASE192_EXPORT_NAMES)\n",
            ),
            lambda source: source + "_PHASE192_EXPORT_NAMES = set()\n",
        )
        for mutate in initializer_mutations:
            temporary, repository, base = self._create_repository()
            try:
                initializer = repository / "Scripts/smoke/fixture/__init__.py"
                initializer.write_text(mutate(initializer.read_text(encoding="utf-8")), encoding="utf-8")
                base = self._commit(repository, "break dynamic export order")
                (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                    "from . import *\n"
                    "if __name__ == '__main__':\n"
                    "    raise SystemExit(main())\n",
                    encoding="utf-8",
                )
                head = self._commit(repository, "append main block")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(
                        repository,
                        base,
                        head,
                        strict=False,
                    ),
                )
            finally:
                temporary.cleanup()

        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/live.py").write_text(
                "if False:\n"
                "    def main():\n"
                "        return 0\n"
                "__all__ = ['main']\n",
                encoding="utf-8",
            )
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from .live import main\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "create direct conditional import")
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from .live import main\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append direct main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

        temporary, repository, base = self._create_repository()
        try:
            initializer = repository / "Scripts/smoke/fixture/__init__.py"
            initializer.write_text(
                initializer.read_text(encoding="utf-8")
                + "getattr(_phase192_section_0, 'main')\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "add unrelated dynamic export call")
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_noncanonical_exported_callable(self) -> None:
        """A known exported helper cannot become the package entrypoint."""
        temporary, repository, base = self._create_repository(
            live_source=(
                "def main():\n"
                "    return 0\n"
                "\n"
                "def helper():\n"
                "    return 0\n"
                "\n"
                "__all__ = ['main', 'helper']\n"
            )
        )
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(helper())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append helper main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_new_unsafe_module_entrypoint_is_incompatible_without_a_base_entrypoint(self) -> None:
        """An unsafe newly added module entrypoint cannot bypass the compatibility gate."""
        temporary, repository, base = self._create_repository()
        try:
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.unlink()
            base = self._commit(repository, "remove base module entrypoint")
            entrypoint.write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(missing())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "add unsafe module entrypoint")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_missing_origin_main_revision_fails(self) -> None:
        """A fixture without origin/main cannot satisfy the compatibility gate."""
        temporary, repository, base = self._create_repository()
        try:
            with self.assertRaises(RuntimeError):
                compare_identity_surfaces.compare_revisions(repository, "origin/main", base, strict=False)
        finally:
            temporary.cleanup()

    def test_main_block_only_migration_is_compatible(self) -> None:
        """Appending the approved main block to __main__.py is compatible."""
        temporary, repository, base = self._create_repository()
        try:
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.write_text(
                "from . import *\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append main block")
            self.assertEqual(
                0,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_migration_rejects_missing_function(self) -> None:
        """A main block calling an unknown function remains incompatible."""
        temporary, repository, base = self._create_repository()
        try:
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.write_text(
                "from . import *\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(missing())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "invalid main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    base,
                    head,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()
