#!/usr/bin/env python3
"""Validate finding-level Phase190 adjudication and claim mapping."""
from __future__ import annotations

import argparse
import csv
from pathlib import Path

DISPOSITIONS = {
    "CONFIRMED",
    "REFUTED",
    "SATISFIED_BY_EXISTING",
    "BLOCKED",
    "ENHANCEMENT",
    "EVIDENCE_CONFLICT",
}
REQUIRED = {"finding_id", "claim_id", "root_cluster", "disposition", "evidence"}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--adjudication", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        with args.inventory.open("r", encoding="utf-8", newline="") as handle:
            inventory = list(csv.DictReader(handle, delimiter="\t"))
        with args.adjudication.open("r", encoding="utf-8", newline="") as handle:
            reader = csv.DictReader(handle, delimiter="\t")
            if not reader.fieldnames:
                print("ADJUDICATION_INVALID missing_header")
                return 1
            missing = REQUIRED.difference(reader.fieldnames)
            if missing:
                print("ADJUDICATION_INVALID missing_columns=" + ",".join(sorted(missing)))
                return 1
            rows = list(reader)
        claims = {row.get("claim_id", "").strip() for row in inventory}
        finding_ids = [row["finding_id"].strip() for row in rows]
        if not rows or any(not value for value in finding_ids):
            print("ADJUDICATION_INVALID blank_finding_id")
            return 1
        if len(set(finding_ids)) != len(finding_ids):
            print("ADJUDICATION_INVALID duplicate_finding_ids")
            return 1
        roots = set()
        for row in rows:
            claim = row["claim_id"].strip()
            disposition = row["disposition"].strip()
            roots.add(row["root_cluster"].strip())
            if claim not in claims:
                print(f"ADJUDICATION_INVALID unknown_claim={claim}")
                return 1
            if disposition not in DISPOSITIONS:
                print(f"ADJUDICATION_INVALID unsupported_disposition={disposition}")
                return 1
            if not row["root_cluster"].strip() or not row["evidence"].strip():
                print("ADJUDICATION_INVALID missing_root_or_evidence")
                return 1
            if disposition == "BLOCKED" and not row.get("boundary", "").strip():
                print(f"ADJUDICATION_INVALID unbounded_blocked={row['finding_id']}")
                return 1
            if disposition == "CONFIRMED" and not row.get("test", "").strip():
                print(f"ADJUDICATION_INVALID unsupported_confirmed={row['finding_id']}")
                return 1
        print(
            "ADJUDICATION_VALID "
            f"claims={len(claims)} findings={len(rows)} roots={len(roots)} "
            "unassigned=0 unsupported_confirmed=0 unbounded_blocked=0"
        )
        return 0
    except FileNotFoundError as exc:
        print(f"ADJUDICATION_INVALID missing={exc.filename}")
        return 2
    except (OSError, UnicodeError, csv.Error) as exc:
        print(f"ADJUDICATION_INVALID parse_error={exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
