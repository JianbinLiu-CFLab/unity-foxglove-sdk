from __future__ import annotations
from .validate_repository_provenance_and_git_tree_paths import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def validate_pre_move_inventory(
    repository: pathlib.Path,
    inventory_path: pathlib.Path,
) -> list[str]:
    """Validate compact exact path counts and hashes for every pre-move ROS scope."""

    repository = repository.resolve()
    requested_inventory = _absolute_lexical_path(inventory_path)
    canonical_inventory = repository.joinpath(
        *pathlib.PurePosixPath(_INVENTORY_RELATIVE).parts
    )
    is_canonical_inventory = os.path.normcase(
        str(requested_inventory)
    ) == os.path.normcase(str(canonical_inventory))
    try:
        resolved_inventory = (
            _resolve_regular_file_contained(
                repository,
                _INVENTORY_RELATIVE,
                label="canonical inventory",
            )
            if is_canonical_inventory
            else requested_inventory.resolve(strict=True)
        )
        if not resolved_inventory.is_relative_to(repository):
            return ["pre-move inventory resolves outside repository"]
        payload = _read_json(resolved_inventory)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [f"could not read pre-move inventory: {exc}"]
    errors: list[str] = []
    schema_version = payload.get("schemaVersion")
    if type(schema_version) is not int or schema_version != 1:
        errors.append(
            "pre-move inventory schemaVersion must be exactly the JSON integer 1"
        )
    captured = payload.get("capturedFromHead")
    if (
        not isinstance(captured, str)
        or _FULL_OBJECT_ID_PATTERN.fullmatch(captured) is None
    ):
        return errors + [
            "capturedFromHead must be one full lowercase 40-hex commit object ID"
        ]
    try:
        object_type = _subprocess_lines(
            ["git", "cat-file", "-t", captured],
            repository,
        )[0]
        if object_type != "commit":
            return errors + [
                f"capturedFromHead must name a commit, observed {object_type!r}"
            ]
        resolved_commit = _subprocess_lines(
            ["git", "rev-parse", f"{captured}^{{commit}}"],
            repository,
        )[0]
        if resolved_commit != captured:
            errors.append(
                "capturedFromHead must be the exact commit ID, not an alias"
            )
        captured_tree = _subprocess_lines(
            ["git", "show", "-s", "--format=%T", captured],
            repository,
        )[0]
        if payload.get("capturedTree") != captured_tree:
            errors.append(
                "capturedTree mismatch: "
                f"expected {payload.get('capturedTree')!r}, observed {captured_tree!r}"
            )
        ancestor = subprocess.run(
            ["git", "merge-base", "--is-ancestor", captured, "HEAD"],
            cwd=repository,
            check=False,
            capture_output=True,
        )
        if ancestor.returncode != 0:
            errors.append("capturedFromHead is not an ancestor of current HEAD")
        all_paths = _git_tree_paths(repository, captured)
    except (IndexError, OSError, RuntimeError, UnicodeDecodeError) as exc:
        return errors + [f"could not inspect captured pre-move commit: {exc}"]

    inventory_relative = (
        _INVENTORY_RELATIVE
        if is_canonical_inventory
        else resolved_inventory.relative_to(repository).as_posix()
    )
    if inventory_relative == _INVENTORY_RELATIVE:
        if (
            set(payload) != _INVENTORY_TOP_LEVEL_KEYS
            or payload.get("purpose") != _INVENTORY_PURPOSE
        ):
            errors.append(
                "fixed inventory top-level authority mismatch: expected the "
                "exact keys and purpose"
            )
        if captured != _INVENTORY_CAPTURE_COMMIT:
            errors.append(
                f"capturedFromHead must be the fixed pre-move commit "
                f"{_INVENTORY_CAPTURE_COMMIT}"
            )
        if payload.get("capturedTree") != _INVENTORY_CAPTURE_TREE:
            errors.append(
                f"capturedTree must be the fixed pre-move tree "
                f"{_INVENTORY_CAPTURE_TREE}"
            )

    scopes = payload.get("scopes")
    if not isinstance(scopes, list) or not scopes:
        return errors + ["pre-move inventory scopes must be a non-empty list"]
    if (
        inventory_relative == _INVENTORY_RELATIVE
        and not _strict_json_equal(
            scopes,
            list(_INVENTORY_SCOPE_AUTHORITY),
        )
    ):
        errors.append(
            "fixed inventory scope authority mismatch: expected the exact "
            "ordered seven-scope identity/action/selector/count/digest ledger"
        )

    union: set[str] = set()
    scope_identities: dict[str, str] = {}
    for index, raw_scope in enumerate(scopes):
        if not isinstance(raw_scope, Mapping):
            errors.append(f"inventory scope {index} must be an object")
            continue
        scope_id = raw_scope.get("id")
        if not isinstance(scope_id, str) or not scope_id:
            errors.append(f"inventory scope {index} has no id")
            scope_id = str(index)
        else:
            identity = unicodedata.normalize("NFC", scope_id).casefold()
            previous = scope_identities.get(identity)
            if previous is not None:
                errors.append(
                    "duplicate inventory scope id: "
                    f"{previous!r} and {scope_id!r}"
                )
            else:
                scope_identities[identity] = scope_id
        try:
            paths = _scope_paths(all_paths, raw_scope)
        except ValueError as exc:
            errors.append(f"{scope_id}: {exc}")
            continue
        overlap = sorted(union.intersection(paths))
        if overlap:
            errors.append(
                f"{scope_id}: cross-scope overlap: " + ", ".join(overlap)
            )
        union.update(paths)
        actual_digest = path_inventory_digest(paths)
        actual_count = len(paths)
        declared_count = raw_scope.get("pathCount")
        if type(declared_count) is not int or declared_count != actual_count:
            errors.append(
                f"{scope_id}: pathCount mismatch; "
                f"expected {declared_count!r}, observed {actual_count}"
            )
        if raw_scope.get("pathDigestSha256") != actual_digest:
            errors.append(
                f"{scope_id}: path digest mismatch; observed {actual_digest}"
            )
        if raw_scope.get("action") not in {
            "move_to_bridge",
            "move_to_r2fu",
            "split_between_providers",
            "delete_from_sdk",
        }:
            errors.append(f"{scope_id}: action is not a recognized extraction action")

    actual_union_digest = path_inventory_digest(union)
    declared_total_count = payload.get("totalPathCount")
    if (
        type(declared_total_count) is not int
        or declared_total_count != len(union)
    ):
        errors.append(
            "totalPathCount mismatch; "
            f"expected {declared_total_count!r}, observed {len(union)}"
        )
    if payload.get("totalPathDigestSha256") != actual_union_digest:
        errors.append(
            "total path digest mismatch; " f"observed {actual_union_digest}"
        )
    if inventory_relative == _INVENTORY_RELATIVE:
        if (
            type(declared_total_count) is not int
            or declared_total_count != _INVENTORY_PATH_COUNT
        ):
            errors.append(
                "totalPathCount must remain exactly the JSON integer "
                f"{_INVENTORY_PATH_COUNT}"
            )
        if payload.get("totalPathDigestSha256") != _INVENTORY_PATH_DIGEST:
            errors.append(
                "totalPathDigestSha256 must remain the frozen pre-move digest"
            )
    return errors
def _default_paths(repository: pathlib.Path) -> tuple[pathlib.Path, pathlib.Path, pathlib.Path]:
    """Return default reference, ledger, and inventory paths."""

    return (
        repository / "third-party" / "ROS-TCP-Connector",
        repository
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "PROVENANCE.json",
        repository
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Tests"
        / "Unit"
        / "Phase186"
        / "Fixtures"
        / "pre_move_sdk_ros_inventory.json",
    )
def main(argv: Sequence[str] | None = None) -> int:
    """Validate repository provenance and report a process exit status."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=pathlib.Path, default=pathlib.Path.cwd())
    parser.add_argument("--reference-root", type=pathlib.Path)
    parser.add_argument("--ledger", type=pathlib.Path)
    parser.add_argument("--inventory", type=pathlib.Path)
    arguments = parser.parse_args(argv)
    repository = arguments.repository.resolve()
    default_reference, default_ledger, default_inventory = _default_paths(repository)
    errors = validate_repository_provenance(
        repository,
        arguments.reference_root or default_reference,
        arguments.ledger or default_ledger,
    )
    selected_inventory = arguments.inventory or default_inventory
    if arguments.inventory is not None and os.path.normcase(
        str(_absolute_lexical_path(selected_inventory))
    ) != os.path.normcase(str(default_inventory)):
        errors.append(
            "release inventory path must be the canonical authority: "
            f"{_INVENTORY_RELATIVE}"
        )
    errors.extend(
        validate_pre_move_inventory(
            repository,
            selected_inventory,
        )
    )
    if errors:
        for error in errors:
            print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("PASS: Phase186 provenance and pre-move SDK ROS inventory are exact.")
    return 0


__all__ = [name for name in globals() if not name.startswith("__")]
