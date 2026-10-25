"""Deterministic identity surfaces used by the Phase192 split gates."""

from __future__ import annotations

import argparse
import unicodedata
from pathlib import Path

from .manifest_checks import check_hash_manifest, check_source_map, compare_tsv, write_rows
from .path_identity import is_repository_relative
from .surface_rows import csharp_test_rows, cpp_rows, managed_rows, python_annotation_rows, python_surface, python_test_rows

__all__ = ["main"]

def main(argv: list[str] | None = None) -> int:
    """Phase192 main."""
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("managed", "python", "python-annotations", "cpp", "tests"):
        command = sub.add_parser(name)
        command.add_argument("--root", type=Path, default=Path("."))
        command.add_argument("--paths", type=Path, required=True)
        command.add_argument("--output", type=Path, required=True)
    compare = sub.add_parser("compare")
    compare.add_argument("--expected", type=Path, required=True)
    compare.add_argument("--actual", type=Path, required=True)
    generated = sub.add_parser("generated")
    generated.add_argument("--manifest", type=Path, required=True)
    generated.add_argument("--root", type=Path, default=Path("."))
    source_map = sub.add_parser("source-map")
    source_map.add_argument("--input", type=Path, required=True)
    source_map.add_argument("--root", type=Path, default=Path("."))
    args = parser.parse_args(argv)
    if args.command == "compare":
        return compare_tsv(args.expected, args.actual)
    if args.command == "generated":
        return check_hash_manifest(args.manifest, args.root)
    if args.command == "source-map":
        return check_source_map(args.input, args.root)
    try:
        root = args.root.resolve(strict=True)
    except (OSError, RuntimeError):
        print("IDENTITY_PATH_DIFF root")
        return 1
    paths: list[str] = []
    for line in args.paths.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        if line != line.strip():
            print(f"IDENTITY_PATH_DIFF {line}")
            return 1
        paths.append(line)
    rows: list[list[str]] = []
    seen_paths: set[str] = set()
    for path in paths:
        if not is_repository_relative(path):
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        path_key = unicodedata.normalize("NFC", path).casefold()
        if path_key in seen_paths:
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        seen_paths.add(path_key)
        try:
            item = (root / path).resolve(strict=True)
        except (OSError, RuntimeError):
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        if not item.is_relative_to(root) or not item.is_file():
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        if args.command == "managed":
            rows.extend(managed_rows(path, item.read_text(encoding="utf-8", errors="replace")))
        elif args.command == "python":
            rows.extend(python_surface(path, item.read_text(encoding="utf-8", errors="replace")))
        elif args.command == "python-annotations":
            rows.extend(python_annotation_rows(path, item.read_text(encoding="utf-8", errors="replace")))
        elif args.command == "cpp":
            rows.extend(cpp_rows(path, item.read_text(encoding="utf-8", errors="replace")))
        else:
            text = item.read_text(encoding="utf-8", errors="replace")
            rows.extend(csharp_test_rows(path, text) if path.casefold().endswith(".cs") else python_test_rows(path, text, root))
    headers = {
        "managed": ["path", "line", "visibility", "declaration"],
        "python": ["path", "all", "exports"],
        "python-annotations": ["path", "symbol", "kind", "detail"],
        "cpp": ["path", "line", "symbol", "declaration"],
        "tests": ["path", "test_name", "framework"],
    }
    write_rows(args.output, headers[args.command], rows)
    print(f"IDENTITY_WRITTEN command={args.command} rows={len(rows)}")
    return 0
