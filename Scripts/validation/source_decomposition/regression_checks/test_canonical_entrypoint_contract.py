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

    def test_standard_migration_rejects_delegate_import_from_wrong_section(self) -> None:
        """The migrated wrapper must import the legacy delegate from its unique owner section."""
        extra_source = (
            "def main():\n    return 0\n\n"
            "def image_republisher_main():\n    return 0\n\n"
            "def sanitize_name():\n    return 0\n\n"
            "def wait_for_raw_image():\n    return 0\n\n"
            "__all__ = ['main', 'image_republisher_main', 'sanitize_name', 'wait_for_raw_image']\n"
        )
        variants = (
            "from .extra import main\n",
            "from .extra import image_republisher_main as main\n",
            "from .extra import sanitize_name as main\n",
        )
        for import_line in variants:
            with self.subTest(import_line=import_line):
                temporary, repository, base = self._create_repository(extra_source=extra_source)
                try:
                    self._migrate_to_standard_entrypoint(repository)
                    (repository / "Scripts/smoke/fixture/entrypoint.py").write_text(
                        "def run_entrypoint():\n"
                        f"    {import_line}"
                        "    return main()\n\n"
                        "__all__ = ['run_entrypoint']\n",
                        encoding="utf-8",
                    )
                    head = self._commit(repository, "change delegate import owner")
                    self.assertEqual(
                        1,
                        compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
                    )
                finally:
                    temporary.cleanup()

    def test_already_migrated_entrypoint_rejects_delegate_import_change(self) -> None:
        """A later change cannot replace the trusted delegate import in a migrated wrapper."""
        temporary, repository, base = self._create_repository(
            extra_source="def main():\n    return 0\n\n__all__ = ['main']\n",
        )
        try:
            self._migrate_to_standard_entrypoint(repository)
            migrated = self._commit(repository, "standard entrypoint migration")
            entrypoint = repository / "Scripts/smoke/fixture/entrypoint.py"
            entrypoint.write_text(
                entrypoint.read_text(encoding="utf-8").replace(".live", ".extra"),
                encoding="utf-8",
            )
            changed = self._commit(repository, "change migrated delegate import")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(
                    repository,
                    migrated,
                    changed,
                    strict=False,
                ),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_unresolved_imported_delegate(self) -> None:
        """A section import without a target binding cannot authorize the delegate."""
        temporary, repository, base = self._create_repository(
            live_source="from .extra import main\n__all__ = ['main']\n",
            extra_source="VALUE = 0\n__all__ = []\n",
        )
        try:
            self._migrate_to_standard_entrypoint(repository)
            head = self._commit(repository, "migrate unresolved imported delegate")
            with (
                compare_identity_surfaces._revision_checkout(repository, base) as base_root,
                compare_identity_surfaces._revision_checkout(repository, head) as head_root,
            ):
                self.assertFalse(
                    entrypoint_contracts._approved_entrypoint_contract(
                        head_root,
                        "Scripts/smoke/fixture.py",
                        base_root=base_root,
                        base_relative="Scripts/smoke/fixture.py",
                    )
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
            live_source="def main(argv):\n    return 0\n\n__all__ = ['main']\n",
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

    def test_standard_migration_rejects_unimported_body_binding(self) -> None:
        """The mechanical import contract rejects body names without a section owner."""
        facade_source = (
            "from __future__ import annotations\n"
            "import sys as _phase192_sys\n"
            "if __package__:\n"
            "    from .fixture import *\n"
            "else:\n"
            "    from fixture import *\n"
            "DEFAULT_CONFIG = 0\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(main(DEFAULT_CONFIG))\n"
        )
        temporary, repository, base = self._create_repository(facade_source=facade_source)
        try:
            self._migrate_to_standard_entrypoint(repository)
            (repository / "Scripts/smoke/fixture/entrypoint.py").write_text(
                "def run_entrypoint():\n"
                "    from .live import main\n"
                "    return main(DEFAULT_CONFIG)\n\n"
                "__all__ = ['run_entrypoint']\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "use unimported body binding")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
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

    def test_main_block_only_rejects_guard_mismatch(self) -> None:
        """The appended guard must equal the trusted facade guard exactly."""
        for block in (
            "if __name__ == '__main__':\n    raise SystemExit(main(1))\n",
            "if __name__ == '__main__':\n    raise SystemExit(main() or 0)\n",
            "if __name__ == '__main__':\n    print('extra')\n    raise SystemExit(main())\n",
        ):
            temporary, repository, base = self._create_repository()
            try:
                (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                    "from . import *\n" + block,
                    encoding="utf-8",
                )
                head = self._commit(repository, "mismatch facade main guard")
                self.assertEqual(
                    1,
                    compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
                )
            finally:
                temporary.cleanup()

    def test_main_block_only_rejects_main_import_change(self) -> None:
        """The base module entrypoint import must remain the package wildcard."""
        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from .live import main\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "change module entrypoint import")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_facade_main_binding(self) -> None:
        """The facade cannot bind the delegated name outside its package star import."""
        facade_source = (
            "from __future__ import annotations\n"
            "import sys as _phase192_sys\n"
            "if __package__:\n"
            "    from .fixture import *\n"
            "else:\n"
            "    from fixture import *\n"
            "main = 0\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(main())\n"
        )
        temporary, repository, base = self._create_repository(facade_source=facade_source)
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "bind facade main locally")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_star_import_after_facade_guard(self) -> None:
        """The trusted facade must bind the delegated name before its guard runs."""
        facade_source = (
            "if __name__ == '__main__':\n"
            "    raise SystemExit(main())\n"
            "from .fixture import *\n"
        )
        temporary, repository, base = self._create_repository(facade_source=facade_source)
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "move facade star import after guard")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_standard_migration_rejects_symlinked_facade(self) -> None:
        """A migrated facade must be a regular file inside the checkout."""
        temporary, repository, base = self._create_repository()
        try:
            self._migrate_to_standard_entrypoint(repository)
            facade = repository / "Scripts/smoke/fixture.py"
            facade_source = facade.read_text(encoding="utf-8")
            facade.unlink()
            outside = repository / "external_facade.py"
            outside.write_text(facade_source, encoding="utf-8")
            try:
                os.symlink(outside, facade)
            except OSError as error:
                self.skipTest(f"symlink unavailable: {error}")
            self._commit(repository, "symlink migrated facade")
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

    def test_main_block_only_rejects_package_file_change(self) -> None:
        """A main-block-only change cannot modify another package file."""
        temporary, repository, base = self._create_repository()
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            (repository / "Scripts/smoke/fixture/live.py").write_text(
                "def main():\n    return 1\n\n__all__ = ['main']\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "change package section with main block")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_noncanonical_base_module(self) -> None:
        """A pre-existing module entrypoint with extra statements cannot be extended."""
        temporary, repository, base = self._create_repository(
            main_source="from . import *\nvalue = 1\n",
        )
        try:
            (repository / "Scripts/smoke/fixture/__main__.py").write_text(
                "from . import *\n"
                "value = 1\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "extend noncanonical module entrypoint")
            self.assertEqual(
                1,
                compare_identity_surfaces.compare_revisions(repository, base, head, strict=False),
            )
        finally:
            temporary.cleanup()
