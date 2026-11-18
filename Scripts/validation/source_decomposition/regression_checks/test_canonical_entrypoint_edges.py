from __future__ import annotations

import os
import unittest

from Scripts.validation.source_decomposition import compare_identity_surfaces
from Scripts.validation.source_decomposition.compare_identity_surfaces import entrypoint_contracts
from Scripts.validation.source_decomposition.compare_identity_surfaces import entrypoint_migration_contracts
from Scripts.validation.source_decomposition.regression_checks.canonical_entrypoint_fixtures import (
    CanonicalEntrypointFixtureMixin,
)


class CanonicalEntrypointEdgeTests(CanonicalEntrypointFixtureMixin, unittest.TestCase):
    """Exercise hostile edge cases for canonical entrypoint compatibility."""

    def test_main_block_only_rejects_section_all_mutations(self) -> None:
        """A section __all__ binding cannot be read or mutated after assignment."""
        for mutation in (
            "__all__.append('missing')\n",
            "__all__ = ['main']\n",
            "__all__ += ['missing']\n",
            "del __all__\n",
            "value = __all__\n",
        ):
            temporary, repository, base = self._create_repository()
            try:
                section = repository / "Scripts/smoke/fixture/live.py"
                section.write_text(
                    section.read_text(encoding="utf-8") + mutation,
                    encoding="utf-8",
                )
                base = self._commit(repository, "mutate section exports")
                entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
                entrypoint.write_text(
                    "from . import *\n"
                    "if __name__ == '__main__':\n"
                    "    raise SystemExit(main())\n",
                    encoding="utf-8",
                )
                head = self._commit(repository, "append main block")
                with (
                    compare_identity_surfaces._revision_checkout(repository, base) as base_root,
                    compare_identity_surfaces._revision_checkout(repository, head) as head_root,
                ):
                    self.assertFalse(
                        entrypoint_migration_contracts._approved_main_block_only_contract(
                            base_root,
                            head_root,
                            "Scripts/smoke/fixture.py",
                            "Scripts/smoke/fixture.py",
                        )
                    )
            finally:
                temporary.cleanup()

    def test_main_block_only_rejects_conditional_initializer_all(self) -> None:
        """A conditional package __all__ assignment cannot be treated as literal exports."""
        temporary, repository, base = self._create_repository()
        try:
            initializer = repository / "Scripts/smoke/fixture/__init__.py"
            initializer.write_text(
                "from .live import main\n"
                "__all__ = ['main']\n"
                "if True:\n"
                "    __all__ = ['other']\n",
                encoding="utf-8",
            )
            base = self._commit(repository, "add conditional package exports")
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append main block")
            with (
                compare_identity_surfaces._revision_checkout(repository, base) as base_root,
                compare_identity_surfaces._revision_checkout(repository, head) as head_root,
            ):
                self.assertFalse(
                    entrypoint_migration_contracts._approved_main_block_only_contract(
                        base_root,
                        head_root,
                        "Scripts/smoke/fixture.py",
                        "Scripts/smoke/fixture.py",
                    )
                )
        finally:
            temporary.cleanup()

    def test_main_block_only_rejects_unresolved_secondary_import(self) -> None:
        """Every relative import in the legacy module entrypoint must resolve."""
        temporary, repository, base = self._create_repository(
            main_source="from .live import main\nfrom .missing import helper\n",
        )
        try:
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.write_text(
                entrypoint.read_text(encoding="utf-8")
                + "if __name__ == '__main__':\n"
                + "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "append main block")
            with (
                compare_identity_surfaces._revision_checkout(repository, base) as base_root,
                compare_identity_surfaces._revision_checkout(repository, head) as head_root,
            ):
                self.assertFalse(
                    entrypoint_migration_contracts._approved_main_block_only_contract(
                        base_root,
                        head_root,
                        "Scripts/smoke/fixture.py",
                        "Scripts/smoke/fixture.py",
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

    def test_new_exported_helper_entrypoint_is_incompatible_without_a_base_entrypoint(self) -> None:
        """A newly added module entrypoint cannot dispatch an exported helper."""
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
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.unlink()
            base = self._commit(repository, "remove base module entrypoint")
            entrypoint.write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(helper())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "add exported helper entrypoint")
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

    def test_new_wildcard_entrypoint_requires_effective_initializer_exports(self) -> None:
        """A wildcard entrypoint requires main in the initializer's effective exports."""
        temporary, repository, base = self._create_repository()
        try:
            initializer = repository / "Scripts/smoke/fixture/__init__.py"
            initializer.write_text(
                "from .live import main\n"
                "__all__ = []\n",
                encoding="utf-8",
            )
            (repository / "Scripts/smoke/fixture/__main__.py").unlink()
            base = self._commit(repository, "remove wildcard main export")
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "add wildcard main entrypoint")
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

    def test_new_entrypoint_requires_zero_arg_callable_main(self) -> None:
        """A newly added main entrypoint must resolve to a zero-argument callable."""
        for live_source in (
            "main = 1\n__all__ = ['main']\n",
            "def main(required):\n    return required\n\n__all__ = ['main']\n",
        ):
            temporary, repository, base = self._create_repository(live_source=live_source)
            try:
                entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
                entrypoint.unlink()
                base = self._commit(repository, "remove base module entrypoint")
                entrypoint.write_text(
                    "from . import *\n"
                    "if __name__ == '__main__':\n"
                    "    raise SystemExit(main())\n",
                    encoding="utf-8",
                )
                head = self._commit(repository, "add invalid main entrypoint")
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

    def test_new_main_entrypoint_is_compatible_without_a_base_entrypoint(self) -> None:
        """A newly added module entrypoint may dispatch the canonical package main."""
        temporary, repository, base = self._create_repository()
        try:
            entrypoint = repository / "Scripts/smoke/fixture/__main__.py"
            entrypoint.unlink()
            base = self._commit(repository, "remove base module entrypoint")
            entrypoint.write_text(
                "from . import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "add main entrypoint")
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
