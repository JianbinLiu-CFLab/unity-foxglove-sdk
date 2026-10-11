from __future__ import annotations
from .discover_protocol_sources_and_phase186b_introduced_sources import *
from .foundation import __decomposed_authority_paths
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _source_lines(raw: bytes) -> list[str]:
    """Decode a source blob into its original physical lines."""
    return raw.decode("utf-8", errors="strict").splitlines()


def _is_source_wrapper_line(line: str) -> bool:
    """Identify syntax-only lines that should not count as implementation body."""
    stripped = line.strip()
    if not stripped:
        return True
    if stripped.startswith(("//", "/*", "*", "*/")):
        return True
    if stripped.startswith("using ") and stripped.endswith(";"):
        return True
    if stripped.startswith("namespace ") and "(" not in stripped:
        return True
    if stripped in {"{", "}", "};"}:
        return True
    declaration_words = {
        "public",
        "private",
        "protected",
        "internal",
        "file",
        "static",
        "abstract",
        "sealed",
        "unsafe",
        "partial",
    }
    words = stripped.split()
    if declaration_words.intersection(words[: len(words) - 1]) and any(
        token in words for token in ("class", "struct", "interface", "record", "enum")
    ):
        return True
    return False


def _validate_moved_fragment(
    original_lines: list[str],
    part_lines: list[str],
    start: int,
    end: int,
    path: str,
) -> list[str]:
    """Verify that a moved range maps exactly to the implementation in its part."""
    fragment = original_lines[start - 1 : end]
    hits = [
        offset
        for offset in range(len(part_lines) - len(fragment) + 1)
        if part_lines[offset : offset + len(fragment)] == fragment
    ]
    if len(hits) != 1:
        return [
            f"{path}: decomposed source movedRange does not identify one exact "
            f"source fragment (matches={len(hits)})"
        ]
    meaningful_fragment = [
        line for line in fragment if not _is_source_wrapper_line(line)
    ]
    meaningful_part = [line for line in part_lines if not _is_source_wrapper_line(line)]
    if not meaningful_fragment or meaningful_part != meaningful_fragment:
        return [
            f"{path}: decomposed source movedRange does not account for the "
            "part implementation body"
        ]
    return []


def _validate_decomposed_source_files(
    repository: pathlib.Path,
    payload: Mapping[str, object],
    discovered_sources: set[str],
    introduced_sources: Mapping[str, str],
) -> tuple[set[str], set[str], list[str]]:
    """Validate source-map parts and return their paths and canonical originals."""

    errors: list[str] = []
    part_paths: set[str] = set()
    original_paths: set[str] = set()
    records = payload.get("decomposedSources", [])
    if not isinstance(records, list):
        return part_paths, original_paths, errors

    for index, record in enumerate(records):
        if not isinstance(record, Mapping):
            continue
        original = record.get("originalPath")
        revision = record.get("sourceRevision")
        original_digest = record.get("originalSha256")
        parts = record.get("parts")
        if not isinstance(original, str) or not isinstance(revision, str):
            continue
        original_lines: list[str] = []
        decomposition_revision = record.get("decompositionRevision")
        try:
            original = _canonical_relative_path(
                original,
                label=f"decomposedSources[{index}].originalPath",
            )
        except ValueError as exc:
            errors.append(str(exc))
            continue
        original_paths.add(original)
        if original not in discovered_sources:
            errors.append(
                f"decomposed source original is not present in the working tree: {original}"
            )
        if original not in introduced_sources:
            errors.append(
                f"decomposed source original is outside the fixed Phase186B source set: {original}"
            )
        try:
            if _subprocess_lines(["git", "cat-file", "-t", revision], repository)[0] != "commit":
                errors.append(f"decomposed source revision is not a commit: {revision}")
            ancestor = subprocess.run(
                ["git", "merge-base", "--is-ancestor", revision, "HEAD"],
                cwd=repository,
                check=False,
                capture_output=True,
            )
            if ancestor.returncode != 0:
                errors.append(f"decomposed source revision is not an ancestor of HEAD: {revision}")
            original_bytes = _git_blob(repository, revision, original)
            if _sha256_bytes(_canonical_source_bytes(original_bytes)) != original_digest:
                errors.append(
                    f"{original}: decomposed source originalSha256 does not match "
                    f"{revision}:{original}"
                )
            original_lines = _source_lines(original_bytes)
            line_count = len(original_lines)
        except (IndexError, OSError, RuntimeError, UnicodeDecodeError, ValueError) as exc:
            errors.append(f"could not validate decomposed source baseline {original}: {exc}")
            line_count = 0
        if not isinstance(parts, list):
            continue
        historical_revision_is_valid = False
        if not isinstance(decomposition_revision, str):
            errors.append(
                "decomposed source decompositionRevision is required: "
                f"{original}"
            )
        else:
            try:
                if _subprocess_lines(
                    ["git", "cat-file", "-t", decomposition_revision],
                    repository,
                )[0] != "commit":
                    errors.append(
                        "decomposed source decompositionRevision is not a commit: "
                        f"{decomposition_revision}"
                    )
                else:
                    ancestor = subprocess.run(
                        ["git", "merge-base", "--is-ancestor", decomposition_revision, "HEAD"],
                        cwd=repository,
                        check=False,
                        capture_output=True,
                    )
                    if ancestor.returncode != 0:
                        errors.append(
                            "decomposed source decompositionRevision is not an ancestor of HEAD: "
                            f"{decomposition_revision}"
                        )
                    else:
                        historical_revision_is_valid = True
            except (IndexError, OSError, RuntimeError, ValueError) as exc:
                errors.append(
                    "could not validate decomposed source decomposition baseline "
                    f"{decomposition_revision}: {exc}"
                )
        for part_index, part in enumerate(parts):
            if not isinstance(part, Mapping):
                continue
            path = part.get("path")
            digest = part.get("sha256")
            moved_range = part.get("movedRange")
            if not isinstance(path, str):
                continue
            try:
                path = _canonical_relative_path(
                    path,
                    label=f"decomposedSources[{index}].parts[{part_index}].path",
                )
            except ValueError as exc:
                errors.append(str(exc))
                continue
            part_paths.add(path)
            if path not in discovered_sources:
                errors.append(f"decomposed source part is not discovered: {path}")
            raw: bytes | None = None
            try:
                raw = _resolve_regular_file_contained(
                    repository,
                    path,
                    label="decomposed source part",
                ).read_bytes()
                if _sha256_bytes(_canonical_source_bytes(raw)) != digest:
                    errors.append(f"{path}: decomposed source sha256 mismatch")
            except (OSError, ValueError) as exc:
                errors.append(str(exc))
            if isinstance(moved_range, Mapping):
                start = moved_range.get("startLine")
                end = moved_range.get("endLine")
                if (
                    type(start) is int
                    and type(end) is int
                    and (start < 1 or end < start or (line_count and end > line_count))
                ):
                    errors.append(f"{path}: decomposed source movedRange is outside the baseline")
                elif (
                    raw is not None
                    and original_lines
                    and type(start) is int
                    and type(end) is int
                    and historical_revision_is_valid
                ):
                    try:
                        historical_part = _git_blob(
                            repository,
                            decomposition_revision,
                            path,
                        )
                    except (OSError, RuntimeError, ValueError) as exc:
                        errors.append(
                            f"{path}: could not read decomposition baseline: {exc}"
                        )
                    else:
                        errors.extend(
                            _validate_moved_fragment(
                                original_lines,
                                _source_lines(historical_part),
                                start,
                                end,
                                path,
                            )
                        )

    unmapped = discovered_sources - part_paths - original_paths
    unexpected = sorted(unmapped - set(introduced_sources))
    if unexpected:
        errors.append(
            "protocol source files are outside the fixed Phase186B introduced set: "
            + ", ".join(unexpected)
        )
    return part_paths, original_paths, errors


def validate_repository_provenance(
    repository: pathlib.Path,
    reference_root: pathlib.Path,
    ledger_path: pathlib.Path,
) -> list[str]:
    """Validate the checked-out official reference and every ledgered implementation."""

    repository = repository.resolve()
    reference_root = reference_root.resolve()
    requested_ledger = _absolute_lexical_path(ledger_path)
    canonical_ledger = repository.joinpath(
        *pathlib.PurePosixPath(_LEDGER_RELATIVE).parts
    )
    if os.path.normcase(str(requested_ledger)) != os.path.normcase(
        str(canonical_ledger)
    ):
        return [
            "ledger path must be the canonical release authority: "
            f"{_LEDGER_RELATIVE}"
        ]
    try:
        ledger_path = _resolve_regular_file_contained(
            repository,
            _LEDGER_RELATIVE,
            label="canonical ledger",
        )
    except (OSError, ValueError) as exc:
        return [f"could not read provenance ledger: {exc}"]
    errors: list[str] = []
    if not ledger_path.is_relative_to(repository):
        return ["provenance ledger resolves outside repository"]
    try:
        payload = _read_json(ledger_path)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [f"could not read provenance ledger: {exc}"]

    try:
        git_top_level = pathlib.Path(
            _subprocess_lines(
                ["git", "rev-parse", "--show-toplevel"],
                reference_root,
            )[0]
        ).resolve(strict=True)
        if os.path.normcase(str(git_top_level)) != os.path.normcase(
            str(reference_root)
        ):
            return ["reference_root must be the exact Git top-level"]
    except (IndexError, OSError, RuntimeError) as exc:
        return [f"could not resolve reference Git top-level: {exc}"]

    try:
        revision_lines = _subprocess_lines(
            ["git", "rev-parse", "HEAD"],
            reference_root,
        )
        actual_revision = revision_lines[0]
        status = _subprocess_lines(
            ["git", "status", "--porcelain=v1", "--untracked-files=all"],
            reference_root,
        )
        if any(not line.startswith("?? ") for line in status):
            errors.append("reference checkout has tracked modifications")
        if any(line.startswith("?? ") for line in status):
            errors.append("reference checkout has untracked files")
        remotes = _subprocess_lines(
            ["git", "remote", "get-url", "origin"],
            reference_root,
        )
        if not remotes or remotes[0] != _REFERENCE_REMOTE:
            errors.append("reference origin is not the official ROS-TCP-Connector URL")
        actual_tree = _subprocess_lines(
            ["git", "show", "-s", "--format=%T", actual_revision],
            reference_root,
        )[0]
        actual_date = _subprocess_lines(
            ["git", "show", "-s", "--format=%cI", actual_revision],
            reference_root,
        )[0]
        actual_subject = _subprocess_lines(
            ["git", "show", "-s", "--format=%s", actual_revision],
            reference_root,
        )[0]
        if actual_tree != _REFERENCE_TREE:
            errors.append(
                f"reference tree mismatch: expected {_REFERENCE_TREE}, "
                f"observed {actual_tree}"
            )
        if actual_date != _REFERENCE_DATE:
            errors.append(
                f"reference commitDate mismatch: expected {_REFERENCE_DATE!r}, "
                f"observed {actual_date!r}"
            )
        if actual_subject != _REFERENCE_SUBJECT:
            errors.append(
                f"reference subject mismatch: expected {_REFERENCE_SUBJECT!r}, "
                f"observed {actual_subject!r}"
            )
    except (OSError, RuntimeError, IndexError) as exc:
        return errors + [f"could not inspect reference checkout: {exc}"]

    try:
        license_bytes = _git_blob(reference_root, actual_revision, "LICENSE")
        license_digest = _sha256_bytes(license_bytes)
        if license_digest != _REFERENCE_LICENSE_SHA256:
            errors.append(
                "reference LICENSE blob SHA-256 mismatch: "
                f"expected {_REFERENCE_LICENSE_SHA256}, observed {license_digest}"
            )
    except (OSError, RuntimeError, ValueError) as exc:
        errors.append(f"could not read pinned reference LICENSE blob: {exc}")

    reference = payload.get("reference")
    inspected = reference.get("inspectedFiles", []) if isinstance(reference, Mapping) else []
    reference_sources: dict[str, str] = {}
    for relative in inspected if isinstance(inspected, list) else []:
        if not isinstance(relative, str):
            continue
        try:
            canonical = _canonical_relative_path(
                relative,
                label="inspected reference file",
            )
            reference_sources[canonical] = _git_blob(
                reference_root,
                actual_revision,
                canonical,
            ).decode("utf-8", errors="strict")
        except (OSError, RuntimeError, UnicodeDecodeError, ValueError) as exc:
            errors.append(
                f"could not read pinned inspected reference file {relative}: {exc}"
            )

    discovered_sources, discovery_errors = _discover_protocol_sources(repository)
    errors.extend(discovery_errors)
    introduced_sources, introduced_errors = _phase186b_introduced_sources(repository)
    errors.extend(introduced_errors)
    errors.extend(
        _validate_canonical_ledger_schema(payload, introduced_sources)
    )
    part_paths, mapped_originals, source_map_errors = _validate_decomposed_source_files(
        repository,
        payload,
        discovered_sources,
        introduced_sources,
    )
    errors.extend(source_map_errors)
    canonical_discovered_sources = (
        discovered_sources - part_paths
    ) | mapped_originals
    if canonical_discovered_sources != set(introduced_sources):
        missing = sorted(set(introduced_sources) - canonical_discovered_sources)
        extra = sorted(canonical_discovered_sources - set(introduced_sources))
        if missing:
            errors.append(
                "fixed Phase186B protocol source files are missing: "
                + ", ".join(missing)
            )
        if extra:
            errors.append(
                "protocol source files are outside the fixed Phase186B introduced set: "
                + ", ".join(extra)
            )

    decomposed_authorities = __decomposed_authority_paths(repository)
    expected_implementation_paths = (
        canonical_discovered_sources
        | set(_REQUIRED_RECORDED_AUTHORITIES)
        | set(decomposed_authorities)
    )
    for relative in (
        *_REQUIRED_RECORDED_AUTHORITIES,
        *_REQUIRED_UNRECORDED_AUTHORITIES,
        *decomposed_authorities,
    ):
        try:
            _resolve_regular_file_contained(
                repository,
                relative,
                label="required Phase186B authority",
            )
        except (OSError, ValueError) as exc:
            errors.append(str(exc))

    implementation_sources: dict[str, str] = {}
    implementation_source_bytes: dict[str, bytes] = {}
    for relative in sorted(expected_implementation_paths):
        try:
            path = _resolve_regular_file_contained(
                repository,
                relative,
                label="implementation file",
            )
            raw = path.read_bytes()
            implementation_source_bytes[relative] = _canonical_source_bytes(raw)
            implementation_sources[relative] = raw.decode("utf-8", errors="strict")
        except (OSError, UnicodeDecodeError, ValueError) as exc:
            errors.append(f"could not read implementation file {relative}: {exc}")

    errors.extend(
        validate_ledger_payload(
            payload,
            actual_revision=actual_revision,
            implementation_sources=implementation_sources,
            reference_sources=reference_sources,
            implementation_bytes=implementation_source_bytes,
        )
    )
    records = payload.get("implementations")
    record_by_path = {
        str(record.get("path")): record
        for record in records
        if isinstance(record, Mapping) and isinstance(record.get("path"), str)
    } if isinstance(records, list) else {}
    for path, revision in introduced_sources.items():
        record = record_by_path.get(path)
        if record is not None and record.get("introducedIn") != revision:
            errors.append(
                f"{path}: introducedIn must be {revision} because diff-tree "
                "records it as added there"
            )
    for path, record in record_by_path.items():
        if path not in introduced_sources and "introducedIn" in record:
            errors.append(
                f"{path}: introducedIn is present but the fixed Phase186B "
                "diff-tree source set does not contain this path"
            )
    declared_commits = payload.get("introducedSourceCommits")
    expected_commits = [
        {
            "revision": revision,
            "subject": subject,
            "sourceCount": count,
        }
        for revision, subject, count in _PHASE186B_SOURCE_COMMITS
    ]
    if not _strict_json_equal(declared_commits, expected_commits):
        errors.append(
            "introducedSourceCommits must match the two fixed Phase186B Git commits"
        )
    if payload.get("ledgerPath") != _LEDGER_RELATIVE:
        errors.append(f"ledgerPath must be exactly {_LEDGER_RELATIVE!r}")
    errors.extend(
        _validate_v1_repository_authority(
            repository,
            payload,
            implementation_sources,
        )
    )
    return errors
def _git_tree_paths(repository: pathlib.Path, revision: str) -> list[str]:
    """List canonical paths stored in one immutable Git tree."""

    output = _subprocess_bytes(
        ["git", "ls-tree", "-r", "--name-only", "-z", revision],
        repository,
    )
    return [
        raw.decode("utf-8", errors="strict")
        for raw in output.split(b"\0")
        if raw
    ]
def _scope_paths(all_paths: Sequence[str], scope: Mapping[str, object]) -> list[str]:
    """Select inventory paths using canonical prefixes, exact paths, and globs."""

    prefixes = scope.get("prefixes", [])
    exact_paths = scope.get("exactPaths", [])
    globs = scope.get("globs", [])
    if not all(isinstance(value, list) for value in (prefixes, exact_paths, globs)):
        raise ValueError("inventory selectors must be arrays")
    normalized_prefixes, prefix_errors = _canonical_path_list(
        prefixes,
        label="inventory prefixes",
    )
    normalized_exact_list, exact_errors = _canonical_path_list(
        exact_paths,
        label="inventory exactPaths",
    )
    normalized_globs, glob_errors = _canonical_path_list(
        globs,
        label="inventory globs",
    )
    selector_errors = prefix_errors + exact_errors + glob_errors
    portable_selectors: dict[str, str] = {}
    for selector in normalized_prefixes + normalized_exact_list + normalized_globs:
        identity = unicodedata.normalize("NFC", selector).casefold()
        previous = portable_selectors.get(identity)
        if previous is not None:
            selector_errors.append(
                "inventory selectors have a case-insensitive duplicate: "
                f"{previous!r} and {selector!r}"
            )
        portable_selectors[identity] = selector
    if selector_errors:
        raise ValueError("; ".join(selector_errors))
    prefix_identities = tuple(prefix + "/" for prefix in normalized_prefixes)
    normalized_exact = set(normalized_exact_list)
    return sorted(
        path
        for path in all_paths
        if path in normalized_exact
        or any(path.startswith(prefix) for prefix in prefix_identities)
        or any(fnmatch.fnmatchcase(path, pattern) for pattern in normalized_globs)
    )


__all__ = [name for name in globals() if not name.startswith("__")]
