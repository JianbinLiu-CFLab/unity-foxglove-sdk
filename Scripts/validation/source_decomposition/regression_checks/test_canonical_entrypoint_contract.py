from __future__ import annotations

import subprocess
import tempfile
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces


_REPOSITORY_ROOT = Path(__file__).resolve().parents[4]


class CanonicalEntrypointContractTests(unittest.TestCase):
    """Exercise the Phase192 canonical entrypoint compatibility contract."""

    def _temporary_repository(self) -> tempfile.TemporaryDirectory[str]:
        """Return a fixture directory rooted under the repository build tree."""
        temporary_root = _REPOSITORY_ROOT / "build" / "tmp"
        temporary_root.mkdir(parents=True, exist_ok=True)
        return tempfile.TemporaryDirectory(dir=temporary_root)

    def _commit(self, repository: Path, message: str) -> str:
        """Commit the fixture tree and return its revision."""
        subprocess.run(["git", "add", "-A"], cwd=repository, check=True)
        subprocess.run(
            [
                "git",
                "-c",
                "user.name=fixture",
                "-c",
                "user.email=fixture@example.invalid",
                "commit",
                "-q",
                "-m",
                message,
            ],
            cwd=repository,
            check=True,
        )
        return subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()

    def _create_phase184_fixture(
        self,
        repository: Path,
        *,
        live_source: str | None = None,
        main_source: str | None = None,
    ) -> Path:
        """Create a phase184-shaped package with a section-owned main function."""
        package = repository / "Scripts/smoke/fixture"
        package.mkdir(parents=True)
        (repository / "Scripts/smoke/fixture.py").write_text(
            "from __future__ import annotations\n"
            "import sys as _phase192_sys\n"
            "if __package__:\n"
            "    from .fixture import *\n"
            "else:\n"
            "    from fixture import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(main())\n",
            encoding="utf-8",
        )
        (package / "__init__.py").write_text(
            "from . import live as _phase192_section_0\n"
            "_PHASE192_SECTION_MODULES = (_phase192_section_0,)\n"
            "_PHASE192_EXPORT_NAMES = set()\n"
            "for _phase192_section in _PHASE192_SECTION_MODULES:\n"
            "    for _phase192_name in _phase192_section.__all__:\n"
            "        globals()[_phase192_name] = getattr(_phase192_section, _phase192_name)\n"
            "        _PHASE192_EXPORT_NAMES.add(_phase192_name)\n"
            "__all__ = sorted(_PHASE192_EXPORT_NAMES)\n",
            encoding="utf-8",
        )
        (package / "live.py").write_text(
            live_source
            or (
                "def main():\n"
                "    return 0\n"
                "\n"
                "__all__ = ['main']\n"
            ),
            encoding="utf-8",
        )
        (package / "__main__.py").write_text(
            main_source or "from . import *\n",
            encoding="utf-8",
        )
        return repository / "Scripts/smoke/fixture.py"

    def _create_repository(
        self,
        *,
        live_source: str | None = None,
        main_source: str | None = None,
    ) -> tuple[tempfile.TemporaryDirectory[str], Path, str]:
        """Create and commit a phase184-shaped repository fixture."""
        temporary = self._temporary_repository()
        repository = Path(temporary.name)
        subprocess.run(["git", "init", "-q"], cwd=repository, check=True)
        subprocess.run(["git", "config", "core.autocrlf", "false"], cwd=repository, check=True)
        self._create_phase184_fixture(
            repository,
            live_source=live_source,
            main_source=main_source,
        )
        base = self._commit(repository, "base")
        return temporary, repository, base

    def _migrate_to_standard_entrypoint(self, repository: Path) -> None:
        """Apply the approved standard entrypoint migration to the fixture."""
        facade = repository / "Scripts/smoke/fixture.py"
        facade.write_text(
            "from pathlib import Path as _Phase192Path\n"
            "import sys as _phase192_sys\n"
            "_PHASE192_REPOSITORY_ROOT = _Phase192Path(__file__).resolve().parents[2]\n"
            "if str(_PHASE192_REPOSITORY_ROOT) not in _phase192_sys.path:\n"
            "    _phase192_sys.path.insert(0, str(_PHASE192_REPOSITORY_ROOT))\n"
            "from Scripts.smoke.fixture.entrypoint import run_entrypoint\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(run_entrypoint())\n",
            encoding="utf-8",
        )
        package = repository / "Scripts/smoke/fixture"
        (package / "entrypoint.py").write_text(
            "def run_entrypoint():\n"
            "    from .live import main\n"
            "    return main()\n"
            "\n"
            "__all__ = ['run_entrypoint']\n",
            encoding="utf-8",
        )
        (package / "__main__.py").write_text(
            "from .entrypoint import run_entrypoint\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(run_entrypoint())\n",
            encoding="utf-8",
        )
        initializer = package / "__init__.py"
        initializer.write_text(
            initializer.read_text(encoding="utf-8")
            .replace(
                "from . import live as _phase192_section_0\n",
                "from . import live as _phase192_section_0\n"
                "from . import entrypoint as _phase192_section_1\n",
            )
            .replace(
                "_PHASE192_SECTION_MODULES = (_phase192_section_0,)\n",
                "_PHASE192_SECTION_MODULES = (_phase192_section_0, _phase192_section_1)\n",
            ),
            encoding="utf-8",
        )

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
