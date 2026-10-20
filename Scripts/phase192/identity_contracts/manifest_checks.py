"""Manifest, hash, and source-map validation for Phase192."""

from __future__ import annotations

import csv
import hashlib
import re
import unicodedata
from pathlib import Path

from .path_identity import is_repository_relative
from .source_parsing import (
    SOURCE_EXTENSIONS,
    SOURCE_SYMBOL,
    SOURCE_SYMBOL_PYTHON,
    declares_source_symbol,
    source_without_non_code,
)

def write_rows(path: Path, header: list[str], rows: list[list[str]]) -> None:
    """Phase192 write rows."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle, delimiter="\t")
        writer.writerow(header)
        writer.writerows(sorted(rows))


def compare_tsv(expected: Path, actual: Path) -> int:
    """Phase192 compare tsv."""
    expected_text = expected.read_text(encoding="utf-8")
    actual_text = actual.read_text(encoding="utf-8")
    if expected_text == actual_text:
        print("IDENTITY_EQUAL")
        return 0
    print("IDENTITY_DIFF")
    return 1


def _read_table(path: Path, expected_fields: frozenset[str]) -> tuple[list[str], list[dict[str, str]]] | None:
    """Read a strict TSV table, rejecting duplicate or extra columns."""
    with path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        fieldnames = reader.fieldnames
        if (
            fieldnames is None
            or len(fieldnames) != len(set(fieldnames))
            or set(fieldnames) != set(expected_fields)
        ):
            return None
        rows = list(reader)
    if any(None in row for row in rows):
        return None
    return fieldnames, rows


def _strict_field(row: dict[str, str], name: str) -> str | None:
    """Return a nonempty field without silently normalizing its identity."""
    value = row.get(name)
    if not isinstance(value, str) or not value or value != value.strip():
        return None
    return value


def _resolved_identity(path: Path) -> str:
    """Return a case-folded NFC path identity for duplicate detection."""
    return unicodedata.normalize("NFC", str(path)).casefold()


def check_hash_manifest(manifest: Path, root: Path) -> int:
    """Validate every nonempty, repository-relative generated-file hash row."""
    table = _read_table(manifest, frozenset({"path", "sha256"}))
    if table is None:
        print("GENERATED_HASH_SCHEMA_DIFF")
        return 1
    _, rows = table
    if not rows:
        print("GENERATED_HASH_EMPTY")
        return 1
    try:
        root_resolved = root.resolve(strict=True)
    except (OSError, RuntimeError):
        print("GENERATED_HASH_ROOT_DIFF")
        return 1
    seen: set[str] = set()
    resolved_seen: set[str] = set()
    for row in rows:
        relative = _strict_field(row, "path")
        expected = _strict_field(row, "sha256")
        relative = relative or ""
        expected = expected or ""
        relative_path = Path(relative)
        if (
            not is_repository_relative(relative)
            or not re.fullmatch(r"[0-9a-fA-F]{64}", expected)
        ):
            print(f"GENERATED_HASH_SCHEMA_DIFF {relative or '-'}")
            return 1
        try:
            path = (root / relative_path).resolve(strict=False)
        except (OSError, RuntimeError):
            print(f"GENERATED_HASH_PATH_DIFF {relative}")
            return 1
        path_key = unicodedata.normalize("NFC", relative).casefold()
        resolved_key = _resolved_identity(path)
        if (
            not path.is_relative_to(root_resolved)
            or path_key in seen
            or resolved_key in resolved_seen
        ):
            print(f"GENERATED_HASH_PATH_DIFF {relative}")
            return 1
        seen.add(path_key)
        resolved_seen.add(resolved_key)
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest().upper() != expected.upper():
            print(f"GENERATED_HASH_DIFF {relative}")
            return 1
    print(f"GENERATED_HASH_EQUAL count={len(rows)}")
    return 0


def check_source_map(source_map: Path, root: Path) -> int:
    """Validate source-map paths, hashes, and code-level moved symbols."""
    table = _read_table(
        source_map,
        frozenset({"old_path", "new_path", "original_hash", "moved_symbol"}),
    )
    if table is None:
        print("SOURCE_MAP_SCHEMA_DIFF")
        return 1
    _, rows = table
    if not rows:
        print("SOURCE_MAP_EMPTY")
        return 1
    try:
        root_resolved = root.resolve(strict=True)
    except (OSError, RuntimeError):
        print("SOURCE_MAP_PATH_DIFF root")
        return 1
    seen_pairs: set[tuple[str, str]] = set()
    for row in rows:
        old_name = _strict_field(row, "old_path") or ""
        new_name = _strict_field(row, "new_path") or ""
        original_hash = _strict_field(row, "original_hash") or ""
        moved_symbol = _strict_field(row, "moved_symbol") or ""
        old_relative = Path(old_name)
        new_relative = Path(new_name)
        if (
            not old_name
            or not new_name
            or not is_repository_relative(old_name)
            or not is_repository_relative(new_name)
            or not original_hash
            or not moved_symbol
        ):
            print("SOURCE_MAP_EMPTY_FIELD")
            return 1
        lexical_pair = (
            unicodedata.normalize("NFC", old_name).casefold(),
            unicodedata.normalize("NFC", new_name).casefold(),
        )
        if lexical_pair in seen_pairs:
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        seen_pairs.add(lexical_pair)
        old_path = root / old_relative
        new_path = root / new_relative
        try:
            old_path = old_path.resolve(strict=True)
            new_path = new_path.resolve(strict=True)
        except (OSError, RuntimeError):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if old_path == new_path or _resolved_identity(old_path) == _resolved_identity(new_path):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if (not old_path.is_file() or not new_path.is_file()
                or not old_path.is_relative_to(root_resolved)
                or not new_path.is_relative_to(root_resolved)):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        old_suffix = old_path.suffix.casefold()
        new_suffix = new_path.suffix.casefold()
        if (
            old_suffix not in SOURCE_EXTENSIONS
            or new_suffix not in SOURCE_EXTENSIONS
            or old_suffix != new_suffix
        ):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if not re.fullmatch(r"[0-9a-fA-F]{64}", original_hash):
            print(f"SOURCE_MAP_HASH_DIFF {old_name}")
            return 1
        actual_hash = hashlib.sha256(old_path.read_bytes()).hexdigest()
        if actual_hash.casefold() != original_hash.casefold():
            print(f"SOURCE_MAP_HASH_DIFF {old_name}")
            return 1
        old_raw = old_path.read_text(encoding="utf-8", errors="replace")
        target_raw = new_path.read_text(encoding="utf-8", errors="replace")
        if source_without_non_code(old_path, old_raw) is None or source_without_non_code(new_path, target_raw) is None:
            print(f"SOURCE_MAP_SYNTAX_DIFF {old_name} {new_name}")
            return 1
        symbol_expression = (
            SOURCE_SYMBOL_PYTHON
            if old_path.suffix.casefold() == ".py"
            else SOURCE_SYMBOL
        )
        if not symbol_expression.fullmatch(moved_symbol):
            print(f"SOURCE_MAP_SYMBOL_DIFF {old_name} {new_name} {moved_symbol}")
            return 1
        if not declares_source_symbol(old_path, old_raw, moved_symbol) or not declares_source_symbol(new_path, target_raw, moved_symbol):
            print(f"SOURCE_MAP_SYMBOL_DIFF {old_name} {new_name} {moved_symbol}")
            return 1
    print(f"SOURCE_MAP_VALID rows={len(rows)}")
    return 0


def files_from_manifest(path: Path) -> list[str]:
    """Phase192 files from manifest."""
    with path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        return [row["path"] for row in reader]
