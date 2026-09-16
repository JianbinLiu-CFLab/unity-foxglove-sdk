#!/usr/bin/env python3
"""Deterministic Phase190 contract-vector smoke harness.

The harness is deliberately source-level: it checks the frozen claim inventory
and emits a stable vector manifest. Product/runtime vectors are added only when
their claim has a current source binding and a root-specific RED.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("baseline", "modified"), default="baseline")
    parser.add_argument(
        "--inventory",
        type=Path,
        default=Path("build/phase190/review/claim-inventory.tsv"),
    )
    args = parser.parse_args(argv)
    if not args.inventory.exists():
        print(f"CONFORMANCE_INVALID missing_inventory={args.inventory}")
        return 2
    with args.inventory.open("r", encoding="utf-8", newline="") as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))
    vectors = [
        {"claim_id": row["claim_id"], "mode": args.mode, "invariant": row["invariant"]}
        for row in rows
    ]
    digest = hashlib.sha256(
        json.dumps(vectors, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    print(f"CONFORMANCE_VECTOR_COUNT={len(vectors)}")
    print(f"CONFORMANCE_MODE={args.mode}")
    print(f"CONFORMANCE_MANIFEST_SHA256={digest}")
    print("CONFORMANCE_SMOKE_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
