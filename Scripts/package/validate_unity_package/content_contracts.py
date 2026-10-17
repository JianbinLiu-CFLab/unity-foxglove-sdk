"""Public-content, sample-hygiene, and naming checks."""

from __future__ import annotations

from .foundation import *
def check_forbidden_public_content(
    results: list[CheckResult],
    samples_files: list[Path] | None = None,
    docs_files: list[Path] | None = None,
) -> None:
    """Scan public docs and samples for local-only markers."""
    samples_files = samples_files if samples_files is not None else list(iter_files(SAMPLES))
    docs_files = docs_files if docs_files is not None else list(iter_files(DOCS))
    package_readme = PACKAGE / "README.md"
    offenders: list[str] = []
    paths = samples_files + docs_files
    if package_readme.is_file():
        paths.append(package_readme)
    for path in paths:
        if path.suffix.lower() not in {".md", ".json", ".cs", ".unity", ".asset", ".inputactions", ".xml"}:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in FORBIDDEN_PUBLIC_SCAN_PATTERN.finditer(text):
            label = FORBIDDEN_PUBLIC_LABELS_BY_GROUP.get(match.lastgroup or "", "forbidden marker")
            offenders.append(f"{rel(path)} ({label})")
    add(
        results,
        "public docs/samples have no forbidden markers",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no forbidden markers found",
    )


def check_forbidden_sample_artifacts(results: list[CheckResult], samples_entries: list[Path] | None = None) -> None:
    """Reject generated, local, or benchmark files from package samples."""
    samples_entries = samples_entries if samples_entries is not None else list(SAMPLES.rglob("*"))
    offenders: set[Path] = set()
    for path in samples_entries:
        relative_parts = path.relative_to(SAMPLES).parts
        forbidden_index = next(
            (index for index, part in enumerate(relative_parts) if part in FORBIDDEN_SAMPLE_PARTS),
            None,
        )
        if forbidden_index is not None:
            offenders.add(SAMPLES.joinpath(*relative_parts[: forbidden_index + 1]))
            continue
        if path.name in FORBIDDEN_SAMPLE_NAMES:
            offenders.add(path)
            continue
        if any(pattern.match(path.name) for pattern in FORBIDDEN_SAMPLE_NAME_PATTERNS):
            offenders.add(path)
    offender_list = sorted(rel(path) for path in offenders)
    add(
        results,
        "samples contain no generated/local artifacts",
        not offender_list,
        "; ".join(offender_list[:MAX_REPORTED_OFFENDERS]) if offender_list else "no forbidden sample artifacts",
    )


def check_package_build_artifacts(results: list[CheckResult], package_entries: list[Path] | None = None) -> None:
    """Reject build/cache directories from the release package tree."""
    package_entries = package_entries if package_entries is not None else list(PACKAGE.rglob("*"))
    forbidden_dirs = PACKAGE_BUILD_DIRECTORY_NAMES
    offenders: list[str] = []
    for path in package_entries:
        if path.name.casefold() in forbidden_dirs and path.is_dir():
            offenders.append(rel(path))
    add(
        results,
        "package contains no build/cache directories",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no build/cache directories",
    )


def check_manual_phase_service_guards(results: list[CheckResult], demo_entries: list[Path] | None = None) -> None:
    """Reject active phase-only FoxService demo endpoints in committed Unity project scripts."""
    demo_entries = demo_entries if demo_entries is not None else list(UNITY_DEMO_SCRIPTS.rglob("*.cs"))
    offenders: list[str] = []
    for path in demo_entries:
        if not path.is_file():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for line_number, line in enumerate(text.splitlines(), start=1):
            stripped = line.lstrip()
            if stripped.startswith("//"):
                continue
            if '[FoxService("/phase141d/' in stripped:
                offenders.append(f"{rel(path)}:{line_number}")
    add(
        results,
        "manual phase FoxService demos stay disabled",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no active phase-only FoxService demos",
    )


def check_validation_naming(results: list[CheckResult], package_files: list[Path] | None = None) -> None:
    """Reject new Phase-number-prefixed runtime validation source filenames."""
    runtime_tests = PACKAGE / "Tests" / "Runtime"
    if package_files is None:
        runtime_files = list(iter_files(runtime_tests))
    else:
        runtime_files = [
            path
            for path in package_files
            if path.is_file() and path_is_relative_to(path, runtime_tests)
        ]

    offenders: list[str] = []
    for path in runtime_files:
        match = VALIDATION_PHASE_FILENAME_RE.match(path.name)
        if match is None:
            continue

        phase = int(match.group("phase"))
        trailing = match.group("trailing")
        index_match = VALIDATION_PHASE_FILENAME_INDEX_RE.match(trailing)
        index = int(index_match.group("index")) if index_match is not None else None
        if phase > LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE:
            offenders.append(rel(path))
        elif phase == LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE and (
            trailing == "" or index is None or index >= LEGACY_VALIDATION_FILENAME_CUTOFF_INDEX
        ):
            offenders.append(rel(path))

    add(
        results,
        "runtime validation source filenames are descriptive",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS])
        if offenders
        else "no new Phase-number-prefixed validation filenames",
    )


def check_google_protobuf_collision(results: list[CheckResult]) -> None:
    """Ensure Google.Protobuf plugin asmdefs do not collide with DLL names."""
    plugin_dir = PACKAGE / "Plugins" / "Google.Protobuf"
    dll_stems = {
        p.stem.casefold()
        for p in plugin_dir.iterdir()
        if p.is_file() and p.suffix.casefold() == ".dll"
    }
    asmdef_files = [
        p
        for p in plugin_dir.iterdir()
        if p.is_file() and p.suffix.casefold() == ".asmdef"
    ]
    filename_collisions = [
        rel(p) for p in asmdef_files if p.stem.casefold() in dll_stems
    ]

    name_collisions: list[str] = []
    for asmdef in asmdef_files:
        try:
            name = json.loads(asmdef.read_text(encoding="utf-8")).get("name")
        except Exception:
            continue
        if isinstance(name, str) and name.casefold() in dll_stems:
            name_collisions.append(f"{rel(asmdef)} name={name}")

    offenders = filename_collisions + name_collisions
    add(results, "Google.Protobuf DLL/asmdef naming", not offenders, "; ".join(offenders) if offenders else "no collision")



__all__ = sorted(name for name in globals() if not name.startswith("__"))
