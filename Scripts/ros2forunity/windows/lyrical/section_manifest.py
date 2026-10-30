"""Validate manifest-backed Python source sections."""

from __future__ import annotations

from pathlib import Path


def load_section_names(section_dir: Path) -> tuple[str, ...]:
    """Return the manifest order after validating the exact section file set."""
    manifest_path = section_dir / "sections.manifest"
    if not manifest_path.is_file():
        raise FileNotFoundError(f"Missing runtime package section manifest: {manifest_path}")
    names = tuple(
        line.strip()
        for line in manifest_path.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    )
    folded = {name.casefold() for name in names}
    if not names or len(names) != len(folded) or any(
        not name.endswith(".py")
        or Path(name).name != name
        or name in {"__init__.py", "__main__.py"}
        for name in names
    ):
        raise ValueError(f"Invalid runtime package section manifest: {manifest_path}")
    actual_names = tuple(
        sorted(
            path.name
            for path in section_dir.glob("*.py")
            if path.name not in {"__init__.py", "__main__.py"}
        )
    )
    if set(actual_names) != set(names):
        raise ValueError(
            "Runtime package section manifest mismatch: "
            f"missing={sorted(set(names) - set(actual_names))}; "
            f"unexpected={sorted(set(actual_names) - set(names))}"
        )
    return names
