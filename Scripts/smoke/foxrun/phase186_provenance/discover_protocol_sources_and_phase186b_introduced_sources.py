from __future__ import annotations
from .validate_ledger_payload_and_read_json import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _discover_protocol_sources(
    repository: pathlib.Path,
) -> tuple[set[str], list[str]]:
    """Discover maintained protocol sources without following unsafe entries."""

    discovered: set[str] = set()
    errors: list[str] = []
    for root_relative, pattern in _PROTOCOL_SOURCE_ROOTS:
        root_candidate = repository.joinpath(
            *pathlib.PurePosixPath(root_relative).parts
        )
        try:
            root_stat = root_candidate.lstat()
            if _is_reparse_point(root_stat):
                errors.append(
                    "protocol source directory must not be a symlink or "
                    f"reparse point: {root_relative}"
                )
                continue
            if not stat.S_ISDIR(root_stat.st_mode):
                errors.append(
                    f"protocol source root is not a directory: {root_relative}"
                )
                continue
            root = _resolve_contained(
                repository,
                root_relative,
                label="protocol source root",
            )
        except (OSError, ValueError) as exc:
            errors.append(str(exc))
            continue

        pending = [root]
        while pending:
            current = pending.pop()
            try:
                entries = list(os.scandir(current))
            except OSError as exc:
                errors.append(str(exc))
                continue
            for entry in entries:
                candidate = pathlib.Path(entry.path)
                try:
                    entry_stat = candidate.lstat()
                except OSError as exc:
                    errors.append(str(exc))
                    continue
                is_reparse = _is_reparse_point(entry_stat)
                if is_reparse:
                    relative = candidate.relative_to(repository).as_posix()
                    if entry.is_dir(follow_symlinks=True):
                        errors.append(
                            "protocol source directory must not be a symlink or "
                            f"reparse point: {relative}"
                        )
                    elif fnmatch.fnmatchcase(
                        entry.name.casefold(),
                        pattern.casefold(),
                    ):
                        try:
                            _resolve_contained(
                                repository,
                                relative,
                                label="protocol implementation path",
                            )
                        except (OSError, ValueError) as exc:
                            errors.append(str(exc))
                        else:
                            errors.append(
                                "protocol source entry must not be a symlink or "
                                f"reparse point: {relative}"
                            )
                    else:
                        errors.append(
                            "protocol source entry must not be a symlink or "
                            f"reparse point: {relative}"
                        )
                    continue
                if stat.S_ISDIR(entry_stat.st_mode):
                    pending.append(candidate)
                    continue
                if not fnmatch.fnmatchcase(
                    entry.name.casefold(),
                    pattern.casefold(),
                ):
                    continue
                relative = candidate.relative_to(repository).as_posix()
                try:
                    _resolve_contained(
                        repository,
                        relative,
                        label="protocol implementation path",
                    )
                except (OSError, ValueError) as exc:
                    errors.append(str(exc))
                    continue
                if is_reparse or not stat.S_ISREG(entry_stat.st_mode):
                    errors.append(
                        "protocol implementation file must be a regular "
                        f"non-symlink file: {relative}"
                    )
                    continue
                discovered.add(relative)
    return discovered, errors
def _phase186b_introduced_sources(
    repository: pathlib.Path,
) -> tuple[dict[str, str], list[str]]:
    """Collect sources introduced by the frozen Phase186B commit sequence."""

    introduced: dict[str, str] = {}
    errors: list[str] = []
    for revision, subject, expected_count in _PHASE186B_SOURCE_COMMITS:
        try:
            object_type = _subprocess_lines(
                ["git", "cat-file", "-t", revision],
                repository,
            )[0]
            if object_type != "commit":
                errors.append(f"Phase186B source revision {revision} is not a commit")
                continue
            actual_subject = _subprocess_lines(
                ["git", "show", "-s", "--format=%s", revision],
                repository,
            )[0]
            if actual_subject != subject:
                errors.append(
                    f"Phase186B source revision {revision} subject mismatch: "
                    f"{actual_subject!r}"
                )
            ancestor = subprocess.run(
                ["git", "merge-base", "--is-ancestor", revision, "HEAD"],
                cwd=repository,
                check=False,
                capture_output=True,
            )
            if ancestor.returncode != 0:
                errors.append(
                    f"Phase186B source revision {revision} is not an ancestor of HEAD"
                )
            added_raw = _subprocess_bytes(
                [
                    "git",
                    "diff-tree",
                    "--no-commit-id",
                    "--name-only",
                    "-r",
                    "--diff-filter=A",
                    "-z",
                    revision,
                ],
                repository,
            )
            added = [
                item.decode("utf-8", errors="strict")
                for item in added_raw.split(b"\0")
                if item
            ]
            sources = sorted(path for path in added if _is_protocol_source_path(path))
            if len(sources) != expected_count:
                errors.append(
                    f"Phase186B source revision {revision} introduced "
                    f"{len(sources)} protocol sources, expected {expected_count}"
                )
            for path in sources:
                previous = introduced.get(path)
                if previous is not None:
                    errors.append(
                        f"Phase186B protocol source {path} was introduced twice: "
                        f"{previous} and {revision}"
                    )
                introduced[path] = revision
        except (IndexError, OSError, RuntimeError, UnicodeDecodeError) as exc:
            errors.append(
                f"could not inspect Phase186B source revision {revision}: {exc}"
            )
    if len(introduced) != 14:
        errors.append(
            f"Phase186B fixed introduced source set has {len(introduced)} paths, "
            "expected 14"
        )
    return introduced, errors
def _validate_v1_repository_authority(
    repository: pathlib.Path,
    payload: Mapping[str, object],
    implementation_sources: Mapping[str, str],
) -> list[str]:
    """Validate the v1 compatibility record against frozen repository authority."""

    errors: list[str] = []
    compatibility = payload.get("v1Compatibility")
    if not isinstance(compatibility, Mapping):
        return ["provenance v1Compatibility must be an object"]
    expected = {
        "capturedFromHead": _V1_CAPTURE_COMMIT,
        "fixturePath": _FIXTURE_RELATIVE,
        "topLevelKeys": list(_V1_TOP_LEVEL_KEYS),
        "canonicalSha256": _V1_CANONICAL_SHA256,
    }
    for field, value in expected.items():
        if compatibility.get(field) != value:
            errors.append(
                f"v1Compatibility {field} must be exactly {value!r}; "
                f"observed {compatibility.get(field)!r}"
            )
    try:
        object_type = _subprocess_lines(
            ["git", "cat-file", "-t", _V1_CAPTURE_COMMIT],
            repository,
        )[0]
        if object_type != "commit":
            errors.append("frozen v1 capture object is not a commit")
            return errors
        frozen = _strict_json_loads(
            _git_blob(repository, _V1_CAPTURE_COMMIT, _FIXTURE_RELATIVE)
        )
        if list(frozen.keys()) != list(_V1_TOP_LEVEL_KEYS):
            errors.append("frozen v1 Git object has an unexpected top-level shape")
        frozen_subset = {key: frozen[key] for key in _V1_TOP_LEVEL_KEYS}
        frozen_digest = _canonical_json_sha256(frozen_subset)
        if frozen_digest != _V1_CANONICAL_SHA256:
            errors.append(
                "frozen v1 Git object canonical digest mismatch; "
                f"observed {frozen_digest}"
            )
        current = _strict_json_loads(
            implementation_sources[_FIXTURE_RELATIVE]
        )
        current_subset = {key: current[key] for key in _V1_TOP_LEVEL_KEYS}
        if current_subset != frozen_subset:
            errors.append("current fixture changed frozen v1 bytes or state")
        current_extra = set(current) - set(_V1_TOP_LEVEL_KEYS)
        if current_extra != {"v2"}:
            errors.append(
                "current fixture may add only the v2 top-level authority beside frozen v1"
            )
    except (
        IndexError,
        KeyError,
        OSError,
        RuntimeError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
    ) as exc:
        errors.append(f"could not validate frozen v1 Git authority: {exc}")
    return errors
def _validate_canonical_ledger_schema(
    payload: Mapping[str, object],
    introduced_sources: Mapping[str, str],
) -> list[str]:
    """Lock every object/key boundary in the canonical release ledger."""

    errors: list[str] = []
    top_level_keys = {
        "schemaVersion",
        "ledgerPath",
        "reference",
        "introducedSourceCommits",
        "v1Compatibility",
        "implementations",
    }
    observed_top_level_keys = set(payload)
    if observed_top_level_keys - {"decomposedSources"} != top_level_keys:
        errors.append(
            "canonical ledger top-level schema must contain exactly "
            + ", ".join(sorted(top_level_keys | {"decomposedSources"}))
        )

    reference = payload.get("reference")
    reference_keys = {
        "repository",
        "origin",
        "revision",
        "tree",
        "commitDate",
        "subject",
        "license",
        "licenseFile",
        "licenseSha256",
        "inspectedFiles",
        "ideasReviewed",
        "materialCopied",
    }
    if not isinstance(reference, Mapping) or set(reference) != reference_keys:
        errors.append(
            "canonical ledger reference schema must contain exactly the "
            "frozen reference metadata fields"
        )

    compatibility = payload.get("v1Compatibility")
    compatibility_keys = {
        "capturedFromHead",
        "fixturePath",
        "topLevelKeys",
        "canonicalSha256",
    }
    if (
        not isinstance(compatibility, Mapping)
        or set(compatibility) != compatibility_keys
    ):
        errors.append(
            "canonical ledger v1Compatibility schema must contain exactly "
            "capturedFromHead, fixturePath, topLevelKeys, and canonicalSha256"
        )

    declared_commits = payload.get("introducedSourceCommits")
    commit_keys = {"revision", "subject", "sourceCount"}
    if isinstance(declared_commits, list):
        for index, record in enumerate(declared_commits):
            if (
                not isinstance(record, Mapping)
                or set(record) != commit_keys
            ):
                errors.append(
                    "canonical ledger introducedSourceCommits"
                    f"[{index}] schema must contain exactly revision, subject, "
                    "and sourceCount"
                )
    else:
        errors.append(
            "canonical ledger introducedSourceCommits schema must be an array"
        )

    implementations = payload.get("implementations")
    implementation_paths: list[str] = []
    if not isinstance(implementations, list):
        return errors + [
            "canonical ledger implementation schema must be an array"
        ]
    base_keys = {"path", "sha256", "classification", "influence"}
    for index, record in enumerate(implementations):
        if not isinstance(record, Mapping):
            errors.append(
                f"canonical ledger implementation schema at index {index} "
                "must be an object"
            )
            continue
        path = record.get("path")
        classification = record.get("classification")
        if isinstance(path, str):
            implementation_paths.append(path)
        expected_keys = set(base_keys)
        if isinstance(path, str) and path in introduced_sources:
            expected_keys.add("introducedIn")
        if classification in {"inspired", "materially_copied"}:
            expected_keys.add("referenceFiles")
        if classification == "materially_copied":
            expected_keys.add("licenseNotice")
        if set(record) != expected_keys:
            errors.append(
                "canonical ledger implementation schema mismatch at "
                f"index {index}: expected exactly "
                + ", ".join(sorted(expected_keys))
            )
    if implementation_paths != sorted(implementation_paths):
        errors.append(
            "canonical ledger implementations must be sorted by ordinal path"
        )

    decomposed_sources = payload.get("decomposedSources", [])
    if not isinstance(decomposed_sources, list):
        errors.append("canonical ledger decomposedSources schema must be an array")
        return errors

    original_paths: list[str] = []
    part_paths: list[str] = []
    original_identities: dict[str, str] = {}
    part_identities: dict[str, str] = {}
    source_map_keys = {
        "originalPath",
        "sourceRevision",
        "originalSha256",
        "parts",
    }
    part_keys = {"path", "sha256", "movedRange"}
    range_keys = {"startLine", "endLine"}
    for index, record in enumerate(decomposed_sources):
        if not isinstance(record, Mapping) or set(record) != source_map_keys:
            errors.append(
                "canonical ledger decomposedSources schema mismatch at "
                f"index {index}: expected exactly "
                + ", ".join(sorted(source_map_keys))
            )
            continue
        original_path = record.get("originalPath")
        source_revision = record.get("sourceRevision")
        original_sha256 = record.get("originalSha256")
        parts = record.get("parts")
        if not isinstance(original_path, str) or not original_path:
            errors.append(
                f"decomposedSources[{index}].originalPath must be a non-empty string"
            )
        else:
            original_paths.append(original_path)
            identity = unicodedata.normalize("NFC", original_path).casefold()
            previous = original_identities.get(identity)
            if previous is not None:
                errors.append(
                    "canonical ledger decomposedSources originalPath values "
                    f"have a case-insensitive duplicate: {previous!r} and "
                    f"{original_path!r}"
                )
            else:
                original_identities[identity] = original_path
        if (
            not isinstance(source_revision, str)
            or re.fullmatch(r"[0-9a-f]{40}", source_revision) is None
        ):
            errors.append(
                f"decomposedSources[{index}].sourceRevision must be a full lowercase Git SHA"
            )
        if (
            not isinstance(original_sha256, str)
            or _SHA256_PATTERN.fullmatch(original_sha256) is None
        ):
            errors.append(
                f"decomposedSources[{index}].originalSha256 must be lowercase hex"
            )
        if not isinstance(parts, list) or not parts:
            errors.append(
                f"decomposedSources[{index}].parts must be a non-empty array"
            )
            continue
        ranges: list[tuple[int, int, int]] = []
        for part_index, part in enumerate(parts):
            if not isinstance(part, Mapping) or set(part) != part_keys:
                errors.append(
                    "canonical ledger decomposedSources part schema mismatch at "
                    f"{index}/{part_index}: expected exactly "
                    + ", ".join(sorted(part_keys))
                )
                continue
            path = part.get("path")
            digest = part.get("sha256")
            moved_range = part.get("movedRange")
            if not isinstance(path, str) or not path:
                errors.append(
                    f"decomposedSources[{index}].parts[{part_index}].path must be a non-empty string"
                )
            else:
                part_paths.append(path)
                identity = unicodedata.normalize("NFC", path).casefold()
                previous = part_identities.get(identity)
                if previous is not None:
                    errors.append(
                        "canonical ledger decomposedSources part paths have a "
                        f"case-insensitive duplicate: {previous!r} and {path!r}"
                    )
                else:
                    part_identities[identity] = path
            if not isinstance(digest, str) or _SHA256_PATTERN.fullmatch(digest) is None:
                errors.append(
                    f"decomposedSources[{index}].parts[{part_index}].sha256 must be lowercase hex"
                )
            if not isinstance(moved_range, Mapping) or set(moved_range) != range_keys:
                errors.append(
                    f"decomposedSources[{index}].parts[{part_index}].movedRange schema mismatch"
                )
            elif (
                type(moved_range.get("startLine")) is not int
                or type(moved_range.get("endLine")) is not int
                or moved_range["startLine"] < 1
                or moved_range["endLine"] < moved_range["startLine"]
            ):
                errors.append(
                    f"decomposedSources[{index}].parts[{part_index}].movedRange must be a positive inclusive range"
                )
            else:
                ranges.append(
                    (
                        moved_range["startLine"],
                        moved_range["endLine"],
                        part_index,
                    )
                )
        ranges.sort()
        for previous, current in zip(ranges, ranges[1:]):
            if current[0] <= previous[1]:
                errors.append(
                    "canonical ledger decomposedSources movedRange values may "
                    f"not overlap: parts {previous[2]} and {current[2]}"
                )
    if original_paths != sorted(original_paths):
        errors.append("canonical ledger decomposedSources must be sorted by originalPath")
    if part_paths != sorted(part_paths):
        errors.append("canonical ledger decomposedSources parts must be sorted by path")
    if len(set(original_paths)) != len(original_paths):
        errors.append("canonical ledger decomposedSources originalPath values must be unique")
    if len(set(part_paths)) != len(part_paths):
        errors.append("canonical ledger decomposedSources part paths must be unique")
    if set(original_paths) & set(part_paths):
        errors.append("canonical ledger decomposedSources cannot map an original onto itself")
    if set(original_identities) & set(part_identities):
        errors.append(
            "canonical ledger decomposedSources cannot map case-insensitive "
            "aliases of an original onto a part"
        )
    return errors


__all__ = [name for name in globals() if not name.startswith("__")]
