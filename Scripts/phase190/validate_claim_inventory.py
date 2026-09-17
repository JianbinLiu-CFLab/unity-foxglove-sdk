#!/usr/bin/env python3
"""Validate the Phase190 claim inventory against the architecture document."""
from __future__ import annotations

import argparse
import csv
import re
import sys
from pathlib import Path

REQUIRED = {
    "claim_id",
    "area",
    "invariant",
    "source_section",
    "applicability",
    "environment",
    "test_entrypoint",
    "positive_control",
    "negative_control",
}


def fail(message: str, code: int = 1) -> int:
    """Emit an inventory failure and return its command exit status."""
    print(f"CLAIM_INVENTORY_INVALID {message}")
    return code


def main(argv: list[str] | None = None) -> int:
    """Validate claim fields, unique IDs and architecture section coverage."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--architecture", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        with args.input.open("r", encoding="utf-8", newline="") as handle:
            reader = csv.DictReader(handle, delimiter="\t")
            if not reader.fieldnames:
                return fail("missing_header")
            missing = REQUIRED.difference(reader.fieldnames)
            if missing:
                return fail("missing_columns=" + ",".join(sorted(missing)))
            rows = list(reader)
        if not rows:
            return fail("empty")
        ids = [row["claim_id"].strip() for row in rows]
        if any(not value for value in ids):
            return fail("blank_claim_id")
        if len(set(ids)) != len(ids):
            return fail("duplicate_ids=" + str(len(ids) - len(set(ids))))
        for index, row in enumerate(rows, 2):
            for key in REQUIRED:
                if not row.get(key, "").strip():
                    return fail(f"blank_{key}_line={index}")
            if row["applicability"].strip().lower() not in {
                "required",
                "optional-but-applicable",
                "not-applicable",
            }:
                return fail(f"invalid_applicability_line={index}")
        architecture = args.architecture.read_text(encoding="utf-8")
        sections = set(re.findall(r"^##\s+([1-8])\.", architecture, re.MULTILINE))
        mapped = set()
        for row in rows:
            mapped.update(part.strip() for part in row["source_section"].split(","))
        unmapped = sorted(sections.difference(mapped), key=int)
        if unmapped:
            return fail("unmapped_in_scope=" + ",".join(unmapped))
        print(
            "CLAIM_INVENTORY_VALID "
            f"count={len(rows)} duplicate_ids=0 "
            f"unmapped_in_scope={len(unmapped)}"
        )
        return 0
    except FileNotFoundError as exc:
        print(f"CLAIM_INVENTORY_INVALID missing={exc.filename}")
        return 2
    except (OSError, UnicodeError, csv.Error) as exc:
        print(f"CLAIM_INVENTORY_INVALID parse_error={exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
