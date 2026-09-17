#!/usr/bin/env python3
"""Execute managed Phase190 controls and retain command-owned test evidence.

Passing these controls does not certify the separate Unity/ROS2/IL2CPP matrix.
Baseline and modified are evidence labels, never fabricated RED/GREEN results.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PROJECT = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj"
VECTORS = {
    "FR-DECL-001": "DeclarationDefaultsVector",
    "FR-HOST-002": "DualHostVector",
    "FR-EMIT-003": "SharedEmitterVector",
    "FR-OUT-004": "OutputFanoutVector",
    "FR-IN-005": "InputAdmissionVector",
    "FR-OWN-006": "OwnershipVector",
    "FR-LIFE-007": "LifecycleVector",
    "FR-SCHEMA-008": "SchemaReplayVector",
    "FR-ROS-009": "Ros2MappingSourceVector",
    "FR-AOT-010": "AotGeneratedSourceVector",
    "FR-EVID-011": "EvidenceToolingVector",
}


def results_pass(path: Path, expected: set[str]) -> bool:
    """Require an exact, non-skipped result for every selected test method."""
    try:
        root = ET.parse(path).getroot()
        results = root.findall(".//{*}UnitTestResult")
        names = [row.attrib.get("testName", "").rsplit(".", 1)[-1] for row in results]
        return bool(expected) and len(names) == len(expected) and set(names) == expected and all(
            row.attrib.get("outcome") == "Passed" for row in results
        )
    except (OSError, ET.ParseError):
        return False


def allocate_evidence_dir(root: Path, mode: str, evidence_id: str) -> Path:
    """Create a non-destructive evidence directory, suffixing repeat attempts."""
    base = root / (mode + "-" + evidence_id)
    for attempt in range(100):
        candidate = base if attempt == 0 else root / f"{base.name}-r{attempt + 1}"
        try:
            candidate.mkdir(parents=True)
            return candidate
        except FileExistsError:
            continue
    raise OSError(f"could not allocate evidence directory under {root}")


def main(argv: list[str] | None = None) -> int:
    """Run selected managed controls and reject absent, skipped or failed tests."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("baseline", "modified"), default="baseline")
    parser.add_argument(
        "--inventory",
        type=Path,
        default=Path("build/phase190/review/claim-inventory.tsv"),
    )
    parser.add_argument("--evidence-root", type=Path, default=REPO_ROOT / "build/phase190/conformance")
    args = parser.parse_args(argv)
    try:
        with args.inventory.open("r", encoding="utf-8-sig", newline="") as handle:
            rows = list(csv.DictReader(handle, delimiter="\t"))
        ids = [row.get("claim_id", "") for row in rows]
        if not ids or len(set(ids)) != len(ids) or any(value not in VECTORS for value in ids):
            print("CONFORMANCE_INVALID empty_duplicate_or_unknown_claim")
            return 1
        inventory_sha = hashlib.sha256(args.inventory.read_bytes()).hexdigest()
        test_path = PROJECT.parent / "FoxRun/Phase190ConformanceVectorTests.cs"
        test_sha = hashlib.sha256(test_path.read_bytes()).hexdigest()
        evidence_id = hashlib.sha256((args.mode + inventory_sha + test_sha).encode("ascii")).hexdigest()[:12]
        evidence = allocate_evidence_dir(args.evidence_root.resolve(), args.mode, evidence_id)
        filter_value = "|".join("FullyQualifiedName~Phase190ConformanceVectorTests." + VECTORS[key] for key in ids)
        props = [
            "-p:BaseOutputPath=" + (evidence / "bin").as_posix() + "/",
            "-p:BaseIntermediateOutputPath=" + (evidence / "obj").as_posix() + "/",
            "-p:MSBuildProjectExtensionsPath=" + (evidence / "obj").as_posix() + "/",
            "-p:RestoreOutputPath=" + (evidence / "obj").as_posix() + "/",
        ]
        command = ["dotnet", "test", str(PROJECT), *props, "--filter", filter_value,
                   "--logger", "trx;LogFileName=conformance.trx", "--results-directory", str(evidence),
                   "--verbosity", "minimal"]
        with (evidence / "runner.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(command, cwd=REPO_ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=900)
        manifest = {"mode": args.mode, "scope": "managed-controls-only", "claim_ids": ids,
                    "command": command, "exit_status": result.returncode,
                    "inventory_sha256": inventory_sha,
                    "runner_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                    "test_sha256": test_sha,
                    "output_sha256": hashlib.sha256((evidence / "runner.log").read_bytes()).hexdigest()}
        (evidence / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print(f"CONFORMANCE_EVIDENCE={evidence}")
        print(f"RUNNER_EXIT={result.returncode}")
        if result.returncode != 0:
            return result.returncode
        if not results_pass(evidence / "conformance.trx", {VECTORS[key] for key in ids}):
            print("CONFORMANCE_INVALID missing_failed_skipped_or_unexpected_tests")
            return 1
    except (OSError, UnicodeError, csv.Error, subprocess.TimeoutExpired) as exc:
        print(f"CONFORMANCE_INVALID {type(exc).__name__}: {exc}")
        return 2
    print(f"CONFORMANCE_VECTOR_COUNT={len(ids)}")
    print(f"CONFORMANCE_MODE={args.mode}")
    print("CONFORMANCE_CONTROL_PASS scope=managed-controls-only")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
