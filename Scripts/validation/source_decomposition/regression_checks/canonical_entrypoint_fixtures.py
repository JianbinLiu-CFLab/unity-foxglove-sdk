from __future__ import annotations

import re
import subprocess
import tempfile
from pathlib import Path

_REPOSITORY_ROOT = Path(__file__).resolve().parents[4]


class CanonicalEntrypointFixtureMixin:
    """Provide hermetic repositories for canonical entrypoint tests."""

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
        facade_source: str | None = None,
        extra_source: str | None = None,
        live_source: str | None = None,
        main_source: str | None = None,
    ) -> Path:
        """Create a phase184-shaped package with a section-owned main function."""
        package = repository / "Scripts/smoke/fixture"
        package.mkdir(parents=True)
        (repository / "Scripts/smoke/fixture.py").write_text(
            facade_source
            or (
                "from __future__ import annotations\n"
                "import sys as _phase192_sys\n"
                "if __package__:\n"
                "    from .fixture import *\n"
                "else:\n"
                "    from fixture import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(main())\n"
            ),
            encoding="utf-8",
        )
        if extra_source is not None:
            (package / "extra.py").write_text(extra_source, encoding="utf-8")
        section_imports = "from . import live as _phase192_section_0\n"
        section_tuple = "_PHASE192_SECTION_MODULES = (_phase192_section_0,)\n"
        if extra_source is not None:
            section_imports += "from . import extra as _phase192_section_1\n"
            section_tuple = "_PHASE192_SECTION_MODULES = (_phase192_section_0, _phase192_section_1)\n"
        (package / "__init__.py").write_text(
            section_imports
            + section_tuple
            + "_PHASE192_EXPORT_NAMES = set()\n"
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
        facade_source: str | None = None,
        extra_source: str | None = None,
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
            facade_source=facade_source,
            extra_source=extra_source,
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
        initializer_source = initializer.read_text(encoding="utf-8")
        section_indices = [
            int(value)
            for value in re.findall(r"_phase192_section_(\d+)", initializer_source)
        ]
        next_index = max(section_indices, default=-1) + 1
        alias = f"_phase192_section_{next_index}"
        initializer_source = initializer_source.replace(
            "_PHASE192_SECTION_MODULES = (",
            f"from . import entrypoint as {alias}\n\n_PHASE192_SECTION_MODULES = (",
            1,
        )
        tuple_start = initializer_source.index("_PHASE192_SECTION_MODULES = ")
        tuple_end = initializer_source.index(")", tuple_start)
        tuple_body = initializer_source[tuple_start:tuple_end]
        tuple_insertion = (
            f"    {alias},\n"
            if "\n" in tuple_body
            else f" {alias}" if tuple_body.rstrip().endswith(",") else f", {alias}"
        )
        initializer_source = (
            initializer_source[:tuple_end]
            + tuple_insertion
            + initializer_source[tuple_end:]
        )
        initializer.write_text(initializer_source, encoding="utf-8")
