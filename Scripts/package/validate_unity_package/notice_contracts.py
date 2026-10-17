"""Third-party notice and binary inventory checks."""

from __future__ import annotations

from .foundation import *
def _third_party_notice_sections(notices: str) -> tuple[str, ...]:
    """Split Markdown notices into attributable level-two-heading sections."""
    headings = list(re.finditer(r"(?m)^##\s+", notices))
    if not headings:
        return (notices,)
    return tuple(
        notices[start.start() : end.start() if end is not None else None]
        for start, end in zip(headings, headings[1:] + [None])
    )


def check_third_party_notices(
    results: list[CheckResult],
    package_entries: list[Path] | None = None,
) -> None:
    """Ensure every bundled binary dependency has a matching license notice."""
    if not THIRD_PARTY_NOTICES.exists():
        add(results, "third-party notices exist", False, rel(THIRD_PARTY_NOTICES))
        notices = ""
    else:
        notices = THIRD_PARTY_NOTICES.read_text(encoding="utf-8", errors="replace")
    notice_sections = _third_party_notice_sections(notices)
    missing: list[str] = []
    absent_artifacts: list[str] = []
    for artifact, required_tokens in THIRD_PARTY_NOTICE_REQUIREMENTS:
        if not artifact.exists():
            absent_artifacts.append(rel(artifact))
            continue
        absent = [token for token in required_tokens if token not in notices]
        if absent:
            missing.append(f"{rel(artifact)} missing {', '.join(absent)}")
        elif not any(all(token in section for token in required_tokens) for section in notice_sections):
            missing.append(f"{rel(artifact)} tokens lack one attributable notice section")

    add(
        results,
        "third-party notice artifact scope visible",
        not absent_artifacts,
        "all listed artifacts are bundled" if not absent_artifacts else "not bundled: " + "; ".join(absent_artifacts),
    )

    add(
        results,
        "third-party notices cover bundled binaries",
        not missing,
        "; ".join(missing) if missing else "all bundled binary notices present",
    )

    package_entries = package_entries if package_entries is not None else list(PACKAGE.rglob("*"))
    analyzer_root = PACKAGE / "Editor" / "SourceGenerators" / "analyzers"
    discovered: dict[str, str] = {}
    for path in package_entries:
        if not path.is_file() or path.suffix.casefold() != ".dll":
            continue
        if path_is_relative_to(path, analyzer_root):
            continue
        if not path_is_relative_to(path, PACKAGE):
            continue
        relative = path.relative_to(PACKAGE).as_posix()
        discovered.setdefault(relative.casefold(), relative)

    declared: set[str] = set()
    for artifact, _required_tokens in THIRD_PARTY_NOTICE_REQUIREMENTS:
        if path_is_relative_to(artifact, PACKAGE):
            declared.add(artifact.relative_to(PACKAGE).as_posix().casefold())
    unlisted = [discovered[key] for key in sorted(set(discovered) - declared)]
    add(
        results,
        "third-party notice inventory closed",
        not unlisted,
        "all regular package DLLs are notice-listed or first-party analyzers"
        if not unlisted
        else "unlisted regular DLLs: " + "; ".join(unlisted),
    )


__all__ = [
    '_third_party_notice_sections',
    'check_third_party_notices',
]
