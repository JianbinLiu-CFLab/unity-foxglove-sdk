from __future__ import annotations
from .reject_json_constant_and_strict_json_loads import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def validate_ledger_payload(
    payload: Mapping[str, object],
    *,
    actual_revision: str,
    implementation_sources: Mapping[str, str],
    reference_sources: Mapping[str, str],
    implementation_bytes: Mapping[str, bytes] | None = None,
) -> list[str]:
    """Validate one already-loaded ledger and its bounded source corpus."""

    errors: list[str] = []
    schema_version = payload.get("schemaVersion")
    if type(schema_version) is not int or schema_version != 1:
        errors.append(
            "provenance schemaVersion must be exactly the JSON integer 1"
        )

    reference = payload.get("reference")
    if not isinstance(reference, Mapping):
        return errors + ["provenance reference must be an object"]
    expected_reference_metadata = {
        "repository": _REFERENCE_REMOTE,
        "origin": _REFERENCE_REMOTE,
        "revision": _REFERENCE_REVISION,
        "tree": _REFERENCE_TREE,
        "commitDate": _REFERENCE_DATE,
        "subject": _REFERENCE_SUBJECT,
        "license": "Apache-2.0",
        "licenseFile": "LICENSE",
        "licenseSha256": _REFERENCE_LICENSE_SHA256,
    }
    for field, expected in expected_reference_metadata.items():
        if reference.get(field) != expected:
            errors.append(
                f"reference {field} must be exactly {expected!r}; "
                f"observed {reference.get(field)!r}"
            )
    if reference.get("revision") != actual_revision:
        errors.append(
            "reference revision mismatch: "
            f"expected {reference.get('revision')!r}, observed {actual_revision!r}"
        )

    inspected, inspected_errors = _canonical_path_list(
        reference.get("inspectedFiles"),
        label="reference inspectedFiles",
        require_nonempty=True,
    )
    errors.extend(inspected_errors)
    if inspected != list(_REFERENCE_FILES):
        errors.append(
            "reference inspectedFiles must match the locked upstream file list"
        )
    ideas = reference.get("ideasReviewed")
    if ideas != list(_REFERENCE_IDEAS):
        errors.append("reference ideasReviewed must match the locked clean-room ideas")
    if not isinstance(reference.get("materialCopied"), bool):
        errors.append("reference materialCopied must be a Boolean")

    expected_reference_paths = set(inspected)
    observed_reference_paths, reference_path_errors = _canonical_path_list(
        list(reference_sources.keys()),
        label="reference source paths",
    )
    errors.extend(reference_path_errors)
    missing_reference = sorted(
        expected_reference_paths - set(observed_reference_paths)
    )
    if missing_reference:
        errors.append(
            "missing inspected reference files: " + ", ".join(missing_reference)
        )

    implementations = payload.get("implementations")
    if not isinstance(implementations, list) or not implementations:
        return errors + ["provenance implementations must be a non-empty list"]

    records: dict[str, Mapping[str, object]] = {}
    portable_record_paths: dict[str, str] = {}
    has_material_copy = False
    for index, raw_record in enumerate(implementations):
        if not isinstance(raw_record, Mapping):
            errors.append(f"implementation record {index} must be an object")
            continue
        path_value = raw_record.get("path")
        try:
            path = _canonical_relative_path(
                path_value,
                label=f"implementation record {index} path",
            )
        except ValueError as exc:
            errors.append(str(exc))
            continue
        portable_identity = unicodedata.normalize("NFC", path).casefold()
        previous = portable_record_paths.get(portable_identity)
        if previous is not None:
            errors.append(
                "case-insensitive duplicate implementation provenance path: "
                f"{previous!r} and {path!r}"
            )
            continue
        portable_record_paths[portable_identity] = path
        if path in records:
            errors.append(f"duplicate implementation provenance path: {path}")
            continue
        records[path] = raw_record
        digest = raw_record.get("sha256")
        if not isinstance(digest, str) or _SHA256_PATTERN.fullmatch(digest) is None:
            errors.append(f"{path}: sha256 must be exactly 64 lowercase hex digits")
        classification = raw_record.get("classification")
        if classification not in _CLASSIFICATIONS:
            errors.append(f"{path}: unknown classification {classification!r}")
        influence = raw_record.get("influence")
        if not isinstance(influence, str) or not influence.strip():
            errors.append(f"{path}: influence must be non-empty")
        if classification in {"inspired", "materially_copied"}:
            references = raw_record.get("referenceFiles")
            reference_paths, reference_errors = _canonical_path_list(
                references,
                label=f"{path} referenceFiles",
                require_nonempty=True,
            )
            errors.extend(reference_errors)
            if any(item not in expected_reference_paths for item in reference_paths):
                errors.append(f"{path}: referenceFiles must name inspected upstream files")
        if classification == "materially_copied":
            has_material_copy = True
            notice = raw_record.get("licenseNotice")
            if not isinstance(notice, str) or not notice.strip():
                errors.append(f"{path}: materially_copied content requires licenseNotice")
            elif "Apache-2.0" not in notice:
                errors.append(
                    f"{path}: materially_copied licenseNotice must name Apache-2.0"
                )
        elif "licenseNotice" in raw_record:
            errors.append(
                f"{path}: licenseNotice is only valid for materially_copied content"
            )

    if reference.get("materialCopied") is not has_material_copy:
        errors.append(
            "reference materialCopied is inconsistent with per-record classifications"
        )

    observed_implementation_paths, implementation_path_errors = _canonical_path_list(
        list(implementation_sources.keys()),
        label="implementation source paths",
    )
    errors.extend(implementation_path_errors)
    missing_implementation = sorted(
        set(records) - set(observed_implementation_paths)
    )
    if missing_implementation:
        errors.append(
            "missing implementation files: " + ", ".join(missing_implementation)
        )

    reference_windows: dict[tuple[str, ...], str] = {}
    for reference_path, source in reference_sources.items():
        for window in _distinctive_windows(source):
            reference_windows.setdefault(window, _normal_path(reference_path))

    for implementation_path, source in implementation_sources.items():
        try:
            path = _canonical_relative_path(
                implementation_path,
                label="implementation source path",
            )
        except ValueError:
            continue
        record = records.get(path)
        if record is None:
            errors.append(f"implementation file has no provenance record: {path}")
            continue
        source_bytes = (
            implementation_bytes.get(path)
            if implementation_bytes is not None
            else source.encode("utf-8")
        )
        if source_bytes is None:
            errors.append(f"{path}: exact implementation bytes were not supplied")
        else:
            observed_digest = _sha256_bytes(source_bytes)
            if record.get("sha256") != observed_digest:
                errors.append(
                    f"{path}: sha256 mismatch; observed {observed_digest}"
                )
        if record.get("classification") == "materially_copied":
            continue
        for window in _distinctive_windows(source):
            reference_path = reference_windows.get(window)
            if reference_path is not None:
                errors.append(
                    f"{path}: unexplained distinctive overlap with {reference_path}"
                )
                break

    limit_authority, error_authority, document_authority_errors = (
        _fixture_document_authority(implementation_sources)
    )
    errors.extend(document_authority_errors)
    for document_path in _PROTOCOL_DOCS:
        source = implementation_sources.get(document_path)
        if source is None:
            continue
        try:
            visible_source = _visible_markdown_source(source)
        except ValueError as exc:
            errors.append(f"{document_path}: {exc}")
            continue
        try:
            _, authority_section = _visible_markdown_authority_section(source)
        except ValueError as exc:
            errors.append(f"{document_path}: {exc}")
            authority_section = ""
        lower_source = visible_source.casefold()
        required_anchors = (
            (_REFERENCE_REMOTE, _REFERENCE_REMOTE),
            (_REFERENCE_REVISION, _REFERENCE_REVISION),
            (_REFERENCE_DATE, _REFERENCE_DATE),
            (_REFERENCE_SUBJECT, _REFERENCE_SUBJECT),
            ("Apache-2.0", "Apache-2.0"),
            ("original", "original"),
            ("no implementation code or comments were copied", "no implementation code"),
            ("PROVENANCE.json", "PROVENANCE.json"),
            (
                "production Bridge runtime consumes this v2 session",
                "production Bridge runtime consumes this v2 session",
            ),
        )
        for anchor, diagnostic in required_anchors:
            if anchor.casefold() not in lower_source:
                errors.append(
                    f"{document_path}: protocol document must contain {diagnostic!r}"
                )
        for code in _ERROR_CODES:
            if f"`{code}`" not in visible_source:
                errors.append(
                    f"{document_path}: protocol document is missing error code {code!r}"
                )
        for limit in _LIMIT_NAMES:
            if f"`{limit}`" not in visible_source:
                errors.append(
                    f"{document_path}: protocol document is missing limit {limit!r}"
                )
        expected_link = _PROTOCOL_DOC_LEDGER_LINKS[document_path]
        if f"]({expected_link})" not in visible_source:
            errors.append(
                f"{document_path}: protocol document must link {expected_link!r}"
            )
        if limit_authority:
            try:
                observed_limit_rows = _markdown_table_rows(
                    authority_section,
                    header="| Limit | Value |",
                    column_count=2,
                    table_name="visible authority limit table",
                )
                expected_limit_rows = [
                    [f"`{name}`", value] for name, value in limit_authority
                ]
                if observed_limit_rows != expected_limit_rows:
                    errors.append(
                        f"{document_path}: limit table mismatch; expected the "
                        "exact ordered 27-row fixture authority with no extras "
                        "or duplicates"
                    )
            except ValueError as exc:
                errors.append(f"{document_path}: {exc}")
            errors.extend(
                f"{document_path}: {error}"
                for error in _validate_authority_table_row_uniqueness(
                    authority_section,
                    identifiers=_LIMIT_NAMES,
                    table_name="visible authority limit table",
                )
            )
        if error_authority:
            try:
                observed_error_rows = _markdown_table_rows(
                    authority_section,
                    header=(
                        "| Error code | Terminal | Allowed wire response |"
                    ),
                    column_count=3,
                    table_name="visible authority error table",
                )
                expected_error_rows = [list(row) for row in error_authority]
                if observed_error_rows != expected_error_rows:
                    errors.append(
                        f"{document_path}: error table mismatch; expected the "
                        "exact ordered 23-row fixture mapping with no extras "
                        "or duplicates"
                    )
            except ValueError as exc:
                errors.append(f"{document_path}: {exc}")
            errors.extend(
                f"{document_path}: {error}"
                for error in _validate_authority_table_row_uniqueness(
                    authority_section,
                    identifiers=_ERROR_CODES,
                    table_name="visible authority error table",
                )
            )

    compatibility = payload.get("v1Compatibility")
    if compatibility is not None:
        if not isinstance(compatibility, Mapping):
            errors.append("v1Compatibility must be an object")
        else:
            fixture_path = compatibility.get("fixturePath")
            source = (
                implementation_sources.get(fixture_path)
                if isinstance(fixture_path, str)
                else None
            )
            keys = compatibility.get("topLevelKeys")
            if not isinstance(keys, list) or any(
                not isinstance(key, str) for key in keys
            ):
                errors.append("v1Compatibility topLevelKeys must be a string list")
            elif source is None:
                errors.append("v1Compatibility fixture is missing from implementations")
            else:
                try:
                    current_fixture = _strict_json_loads(source)
                    subset = {key: current_fixture[key] for key in keys}
                    observed_digest = _canonical_json_sha256(subset)
                    if observed_digest != compatibility.get("canonicalSha256"):
                        errors.append(
                            "frozen v1 canonical state mismatch; "
                            f"observed {observed_digest}"
                        )
                except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
                    errors.append(f"could not validate frozen v1 fixture: {exc}")

    return errors
def _read_json(path: pathlib.Path) -> Mapping[str, object]:
    """Read one strict JSON object from disk."""

    payload = _strict_json_loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError(f"{path} must contain one JSON object")
    return payload
def _subprocess_bytes(command: Sequence[str], cwd: pathlib.Path) -> bytes:
    """Run a command and return its raw stdout bytes."""

    completed = subprocess.run(
        list(command),
        cwd=cwd,
        check=False,
        capture_output=True,
    )
    if completed.returncode != 0:
        detail = completed.stderr.decode("utf-8", errors="replace").strip()
        raise RuntimeError(
            detail or f"command failed with exit code {completed.returncode}"
        )
    return completed.stdout
def _git_blob(repository: pathlib.Path, revision: str, path: str) -> bytes:
    """Read one canonical repository path from an immutable Git revision."""

    path = _canonical_relative_path(path, label="Git blob path")
    return _subprocess_bytes(
        ["git", "cat-file", "blob", f"{revision}:{path}"],
        repository,
    )
def _resolve_contained(
    root: pathlib.Path,
    relative: str,
    *,
    label: str,
) -> pathlib.Path:
    """Resolve a required path while rejecting escapes from the given root."""

    canonical = _canonical_relative_path(relative, label=label)
    candidate = root.joinpath(*pathlib.PurePosixPath(canonical).parts)
    resolved = candidate.resolve(strict=True)
    if not resolved.is_relative_to(root):
        raise ValueError(f"{label} resolves outside repository: {relative!r}")
    return resolved
def _resolve_regular_file_contained(
    root: pathlib.Path,
    relative: str,
    *,
    label: str,
) -> pathlib.Path:
    """Resolve one lexical regular file without traversing reparse aliases."""

    canonical = _canonical_relative_path(relative, label=label)
    current = root
    parts = pathlib.PurePosixPath(canonical).parts
    for index, part in enumerate(parts):
        current = current / part
        result = current.lstat()
        if _is_reparse_point(result):
            raise ValueError(
                f"{label} must be a regular non-symlink file and must not "
                f"traverse reparse points: {relative!r}"
            )
        if index < len(parts) - 1 and not stat.S_ISDIR(result.st_mode):
            raise ValueError(
                f"{label} parent component is not a directory: {relative!r}"
            )
        if index == len(parts) - 1 and not stat.S_ISREG(result.st_mode):
            raise ValueError(
                f"{label} must be a regular non-symlink file: {relative!r}"
            )
    resolved = current.resolve(strict=True)
    if not resolved.is_relative_to(root):
        raise ValueError(f"{label} resolves outside repository: {relative!r}")
    return resolved
def _is_protocol_source_path(path: str) -> bool:
    """Return whether a canonical path belongs to the protocol source scope."""

    pure = pathlib.PurePosixPath(path)
    for root, pattern in _PROTOCOL_SOURCE_ROOTS:
        if pure.parent.as_posix() == root and fnmatch.fnmatchcase(pure.name, pattern):
            return True
    return False


__all__ = [name for name in globals() if not name.startswith("__")]
