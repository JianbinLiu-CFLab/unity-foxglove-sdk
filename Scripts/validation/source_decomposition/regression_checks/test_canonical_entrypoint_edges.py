from __future__ import annotations

import os
import shutil
import unittest
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces
from Scripts.validation.source_decomposition.compare_identity_surfaces import entrypoint_contracts
from Scripts.validation.source_decomposition.compare_identity_surfaces import entrypoint_migration_contracts
from Scripts.validation.source_decomposition.regression_checks.canonical_entrypoint_fixtures import (
    CanonicalEntrypointFixtureMixin,
)


class CanonicalEntrypointEdgeTests(CanonicalEntrypointFixtureMixin, unittest.TestCase):
    """Exercise hostile edge cases for canonical entrypoint compatibility."""

















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
