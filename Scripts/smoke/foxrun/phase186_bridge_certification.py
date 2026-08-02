#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Run the complete serial Phase186-H Windows Bridge certification matrix."""

from __future__ import annotations

import argparse
import dataclasses
import datetime as _datetime
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import time
from collections.abc import Mapping, Sequence
from typing import Any

from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol


SCHEMA_VERSION = 1
EXIT_PASS = 0
EXIT_FAIL = 1
EXIT_USAGE = 2
EXIT_NOT_RUN = 3
PRIMARY_ROW = "jazzy-fastrtps"
_CERT_RUN_ID = re.compile(r"\Aphase186h-cert-[A-Za-z0-9][A-Za-z0-9._-]{11,79}\Z")


@dataclasses.dataclass(frozen=True)
class LiveInvocation:
    """Represent live invocation."""
    ordinal: int
    case_id: str
    row_id: str
    run_id: str
    output_parent: pathlib.Path

    @property
    def output_root(self) -> pathlib.Path:
        """Handle output root for Phase186 acceptance."""
        return self.output_parent / self.run_id


class CertificationFailure(RuntimeError):
    """Stable aggregate certification failure."""


def timestamp() -> str:
    """Handle timestamp for Phase186 acceptance."""
    return _datetime.datetime.now().astimezone().isoformat(timespec="milliseconds")


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse args."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--output-root", type=pathlib.Path, required=True)
    parser.add_argument("--unity-editor", type=pathlib.Path)
    parser.add_argument("--run-id")
    return parser.parse_args(argv)


def validate_args(args: argparse.Namespace) -> argparse.Namespace:
    """Validate args."""
    protocol.require_head(args.expected_head)
    if args.run_id is not None and _CERT_RUN_ID.fullmatch(args.run_id) is None:
        raise CertificationFailure("certification run ID is malformed")
    return args


def certification_run_id(head: str, requested: str | None = None) -> str:
    """Handle certification run id for Phase186 acceptance."""
    if requested is not None:
        if _CERT_RUN_ID.fullmatch(requested) is None:
            raise CertificationFailure("certification run ID is malformed")
        return requested
    nonce = f"{os.getpid():x}{time.time_ns():x}"[-6:]
    return f"phase186h-cert-{head[:6]}{nonce}"


def live_invocations(
    certification_root: pathlib.Path,
    head: str,
) -> tuple[LiveInvocation, ...]:
    """Handle live invocations for Phase186 acceptance."""
    rows = [(case_id, PRIMARY_ROW) for case_id in protocol.AUTOMATIC_CASE_IDS]
    rows.extend(
        ("full-duplex", row_id)
        for row_id in protocol.ROWS
        if row_id != PRIMARY_ROW
    )
    result: list[LiveInvocation] = []
    for ordinal, (case_id, row_id) in enumerate(rows, start=1):
        run_id = f"phase186h-c{ordinal:02d}-{head[:8]}"
        result.append(
            LiveInvocation(
                ordinal,
                case_id,
                row_id,
                run_id,
                certification_root / "c" / f"{ordinal:02d}",
            )
        )
    return tuple(result)


def _owned_root(
    repository: pathlib.Path,
    requested: pathlib.Path,
    run_id: str,
) -> pathlib.Path:
    """Handle owned root for Phase186 acceptance."""
    root = pathlib.Path(requested)
    if not root.is_absolute():
        root = repository / root
    root = root.resolve()
    phase_root = (repository / "build" / "phase186").resolve()
    try:
        root.relative_to(phase_root)
    except ValueError as exc:
        raise CertificationFailure(
            "certification output must stay below build/phase186"
        ) from exc
    target = root / run_id
    if target.exists() and any(target.iterdir()):
        raise CertificationFailure("certification output already exists and is not empty")
    target.mkdir(parents=True, exist_ok=True)
    return target


def _write_json_atomic(path: pathlib.Path, value: Mapping[str, Any]) -> None:
    """Write json atomic."""
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary: pathlib.Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\n",
            dir=path.parent,
            prefix=path.name + ".",
            suffix=".tmp",
            delete=False,
        ) as stream:
            json.dump(value, stream, indent=2, sort_keys=True)
            stream.write("\n")
            temporary = pathlib.Path(stream.name)
        os.replace(temporary, path)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def _run_logged(
    command: Sequence[str],
    *,
    repository: pathlib.Path,
    log: pathlib.Path,
    timeout_seconds: float,
) -> int:
    """Run logged."""
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8", newline="\n") as stream:
        process = subprocess.Popen(
            list(command),
            cwd=repository,
            stdin=subprocess.DEVNULL,
            stdout=stream,
            stderr=subprocess.STDOUT,
            shell=False,
        )
        try:
            return process.wait(timeout=timeout_seconds)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=30)
            raise CertificationFailure(
                "owned certification command exceeded its bounded timeout"
            )


def _run_package_matrix(
    repository: pathlib.Path,
    output: pathlib.Path,
) -> Mapping[str, Any]:
    """Run package matrix."""
    exit_code = _run_logged(
        [sys.executable, "Scripts/package/validate_phase186_package_matrix.py"],
        repository=repository,
        log=output / "package-matrix.log",
        timeout_seconds=1800.0,
    )
    report = repository / "build" / "phase186" / "package-matrix" / "report.json"
    if exit_code != 0 or not report.is_file():
        raise CertificationFailure("four-composition package matrix did not pass")
    try:
        value = json.loads(report.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise CertificationFailure("package matrix report is unavailable") from exc
    expected = ["sdk-only", "sdk-r2fu", "sdk-bridge", "all-providers"]
    gates = value.get("compileGates") if isinstance(value, Mapping) else None
    if (
        value.get("verdict") != "PASS"
        or not isinstance(gates, list)
        or [item.get("name") for item in gates if isinstance(item, Mapping)]
        != expected
        or any(item.get("exitCode") != 0 for item in gates)
    ):
        raise CertificationFailure("package matrix evidence differs from authority")
    copied = output / "package-matrix-report.json"
    shutil.copy2(report, copied)
    return {
        "verdict": "PASS",
        "combinations": expected,
        "report": str(copied.resolve()),
    }


def _acceptance_command(
    invocation: LiveInvocation,
    head: str,
    unity_editor: pathlib.Path | None,
) -> list[str]:
    """Handle acceptance command for Phase186 acceptance."""
    command = [
        sys.executable,
        "-m",
        "Scripts.smoke.foxrun.phase186_bridge_acceptance",
        "--case",
        invocation.case_id,
        "--runtime-row",
        invocation.row_id,
        "--expected-head",
        head,
        "--output-root",
        str(invocation.output_parent),
        "--run-id",
        invocation.run_id,
    ]
    if unity_editor is not None:
        command.extend(("--unity-editor", str(unity_editor)))
    return command


def _load_case_summary(invocation: LiveInvocation) -> Mapping[str, Any]:
    """Load case summary."""
    path = invocation.output_root / "terminal-summary.json"
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise CertificationFailure(
            f"case {invocation.case_id}/{invocation.row_id} has no terminal summary"
        ) from exc
    if not isinstance(value, Mapping):
        raise CertificationFailure("case terminal summary is not an object")
    protocol.validate_terminal_summary(value)
    if (
        value.get("runId") != invocation.run_id
        or value.get("caseId") != invocation.case_id
    ):
        raise CertificationFailure("case terminal identity differs from invocation")
    return value


def _validate_case_package_evidence(invocation: LiveInvocation) -> Mapping[str, Any]:
    """Validate case package evidence."""
    path = invocation.output_root / "preflight.json"
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise CertificationFailure("case package preflight is unavailable") from exc
    composition = value.get("unityComposition") if isinstance(value, Mapping) else None
    if not isinstance(composition, Mapping):
        raise CertificationFailure("case lacks Unity composition evidence")
    expected = acceptance.unity_composition_for_case(invocation.case_id)
    actual = str(composition.get("composition", ""))
    if expected == "bridge-only":
        if actual != "sdk-bridge" or composition.get("productPackages") != [
            "dev.unity2foxglove.ros2bridge",
            "dev.unity2foxglove.sdk",
        ]:
            raise CertificationFailure("Bridge-only case package evidence differs")
    elif actual != "all-providers":
        raise CertificationFailure("all-Providers case package evidence differs")
    return dict(composition)


def _aggregate(
    *,
    run_id: str,
    head: str,
    root: pathlib.Path,
    package_matrix: Mapping[str, Any] | None,
    cases: Sequence[Mapping[str, Any]],
    verdict: str,
    missing_prerequisite: str = "",
    failure: str = "",
    started_at: str,
) -> dict[str, Any]:
    """Handle aggregate for Phase186 acceptance."""
    return {
        "schemaVersion": SCHEMA_VERSION,
        "runId": run_id,
        "head": head,
        "verdict": verdict,
        "missingPrerequisite": missing_prerequisite[:512],
        "failure": failure[:512],
        "packageMatrix": dict(package_matrix or {}),
        "cases": list(cases),
        "automaticCaseIds": list(protocol.AUTOMATIC_CASE_IDS),
        "exactRows": list(protocol.ROWS),
        "evidenceRoot": str(root.resolve()),
        "startedAt": started_at,
        "finishedAt": timestamp(),
    }


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    try:
        args = validate_args(parse_args(argv))
        repository = acceptance.repository_root()
        acceptance.require_exact_head(repository, args.expected_head)
        acceptance.require_clean_tracked_tree(repository)
        run_id = certification_run_id(args.expected_head, args.run_id)
        root = _owned_root(repository, args.output_root, run_id)
    except (protocol.ProtocolFailure, CertificationFailure) as exc:
        print(f"PHASE186_CERTIFICATION_FAIL failure={str(exc)[:512]}", file=sys.stderr)
        return EXIT_FAIL

    started_at = timestamp()
    package_matrix: Mapping[str, Any] | None = None
    cases: list[Mapping[str, Any]] = []
    try:
        package_matrix = _run_package_matrix(repository, root)
        for invocation in live_invocations(root, args.expected_head):
            exit_code = _run_logged(
                _acceptance_command(invocation, args.expected_head, args.unity_editor),
                repository=repository,
                log=root / "logs" / f"{invocation.ordinal:02d}.log",
                timeout_seconds=3600.0,
            )
            summary = _load_case_summary(invocation)
            entry = {
                "ordinal": invocation.ordinal,
                "caseId": invocation.case_id,
                "rowId": invocation.row_id,
                "runId": invocation.run_id,
                "verdict": summary["verdict"],
                "terminalSummary": str(
                    (invocation.output_root / "terminal-summary.json").resolve()
                ),
                "unityComposition": _validate_case_package_evidence(invocation),
            }
            cases.append(entry)
            if exit_code == acceptance.EXIT_NOT_RUN and summary["verdict"] == "NOT RUN":
                aggregate = _aggregate(
                    run_id=run_id,
                    head=args.expected_head,
                    root=root,
                    package_matrix=package_matrix,
                    cases=cases,
                    verdict="NOT RUN",
                    missing_prerequisite=str(summary.get("missingPrerequisite", "")),
                    started_at=started_at,
                )
                _write_json_atomic(root / "certification-summary.json", aggregate)
                print(
                    "PHASE186_CERTIFICATION_NOT_RUN"
                    + f" run={run_id} head={args.expected_head}"
                    + f" missing={aggregate['missingPrerequisite']}",
                    flush=True,
                )
                return EXIT_NOT_RUN
            if exit_code != 0 or summary["verdict"] != "PASS":
                raise CertificationFailure(
                    f"case {invocation.case_id}/{invocation.row_id} did not pass"
                )
        if {entry["caseId"] for entry in cases[:9]} != set(
            protocol.AUTOMATIC_CASE_IDS
        ) or {entry["rowId"] for entry in cases if entry["caseId"] == "full-duplex"} != set(
            protocol.ROWS
        ):
            raise CertificationFailure("serial case or exact-row coverage differs")
        aggregate = _aggregate(
            run_id=run_id,
            head=args.expected_head,
            root=root,
            package_matrix=package_matrix,
            cases=cases,
            verdict="PASS",
            started_at=started_at,
        )
        _write_json_atomic(root / "certification-summary.json", aggregate)
        print(
            "PHASE186_CERTIFICATION_PASS"
            + f" run={run_id} head={args.expected_head} cases={len(cases)}"
            + f" evidence={root / 'certification-summary.json'}",
            flush=True,
        )
        return EXIT_PASS
    except (OSError, subprocess.SubprocessError, protocol.ProtocolFailure, CertificationFailure) as exc:
        aggregate = _aggregate(
            run_id=run_id,
            head=args.expected_head,
            root=root,
            package_matrix=package_matrix,
            cases=cases,
            verdict="FAIL",
            failure=str(exc),
            started_at=started_at,
        )
        _write_json_atomic(root / "certification-summary.json", aggregate)
        print(
            "PHASE186_CERTIFICATION_FAIL"
            + f" run={run_id} head={args.expected_head} failure={str(exc)[:512]}",
            file=sys.stderr,
            flush=True,
        )
        return EXIT_FAIL


if __name__ == "__main__":
    raise SystemExit(main())
