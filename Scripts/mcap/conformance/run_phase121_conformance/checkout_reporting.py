#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import configuration as _configuration
from .configuration import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "run_phase121_conformance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def prepare_official_checkout(explicit_root: str | None) -> str | None:
    """Select or bootstrap the exact public foxglove/mcap implementation under test."""

    if shutil.which("git") is None:
        return "git is required to obtain the pinned official foxglove/mcap implementation."

    if explicit_root:
        _configuration.set_official_root(Path(explicit_root))
        commit = _configuration.get_official_commit()
        if commit is None:
            return f"The explicit official checkout is not a Git worktree: {_configuration.OFFICIAL_ROOT}"
        if commit != _configuration.EXPECTED_OBSERVED_COMMIT:
            return (
                "The explicit official checkout is at "
                f"{commit}, expected {_configuration.EXPECTED_OBSERVED_COMMIT}."
            )
        return None

    _configuration.set_official_root(_configuration.BOOTSTRAPPED_OFFICIAL_ROOT)
    if _configuration.get_official_commit() == _configuration.EXPECTED_OBSERVED_COMMIT and _configuration.OFFICIAL_CONFORMANCE.exists():
        return None

    if _configuration.OFFICIAL_ROOT.exists() and not (_configuration.OFFICIAL_ROOT / ".git").exists():
        shutil.rmtree(_configuration.OFFICIAL_ROOT)
    _configuration.OFFICIAL_ROOT.mkdir(parents=True, exist_ok=True)

    if not (_configuration.OFFICIAL_ROOT / ".git").exists():
        initialize = _configuration.invoke_command_capture(["git", "init", str(_configuration.OFFICIAL_ROOT)])
        if initialize.exit_code != 0:
            return "Unable to initialize the official MCAP checkout: " + first_lines(
                initialize.stdout + initialize.stderr
            )
        remote = _configuration.invoke_command_capture(
            ["git", "-C", str(_configuration.OFFICIAL_ROOT), "remote", "add", "origin", _configuration.OFFICIAL_REPOSITORY_URL]
        )
        if remote.exit_code != 0:
            return "Unable to configure the official MCAP remote: " + first_lines(
                remote.stdout + remote.stderr
            )

    fetch = _configuration.invoke_command_capture(
        [
            "git",
            "-C",
            str(_configuration.OFFICIAL_ROOT),
            "fetch",
            "--depth",
            "1",
            "origin",
            _configuration.EXPECTED_OBSERVED_COMMIT,
        ],
    )
    if fetch.exit_code != 0:
        return "Unable to fetch the pinned official MCAP commit: " + first_lines(
            fetch.stdout + fetch.stderr
        )

    checkout = _configuration.invoke_command_capture(
        ["git", "-C", str(_configuration.OFFICIAL_ROOT), "checkout", "--detach", "--force", "FETCH_HEAD"]
    )
    if checkout.exit_code != 0:
        return "Unable to check out the pinned official MCAP commit: " + first_lines(
            checkout.stdout + checkout.stderr
        )

    commit = _configuration.get_official_commit()
    if commit != _configuration.EXPECTED_OBSERVED_COMMIT:
        return f"Bootstrapped official MCAP commit {commit!r}, expected {_configuration.EXPECTED_OBSERVED_COMMIT}."
    if not _configuration.OFFICIAL_CONFORMANCE.exists():
        return f"Pinned official conformance harness is missing: {_configuration.OFFICIAL_CONFORMANCE}"
    return None


def new_runner_report(
    *,
    name: str,
    kind: str,
    passed: int = 0,
    failed: int = 0,
    skipped: int = 0,
    failures: list[dict[str, object]] | None = None,
    skips: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    """Create a normalized runner report entry."""

    return {
        "name": name,
        "kind": kind,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
        "failures": failures or [],
        "skips": skips or [],
    }


def write_phase121_report(
    *,
    external_tooling_status: str,
    verdict: str,
    generated_variant_count: int,
    runners: list[dict[str, object]],
    tooling: list[dict[str, object]],
    node_version: str | None = None,
    package_manager_version: str | None = None,
) -> None:
    """Write the Phase 121 conformance baseline report."""

    _configuration.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
    _configuration.REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    report = {
        "officialMcapCommit": _configuration.get_official_commit(),
        "officialMcapPath": str(_configuration.OFFICIAL_ROOT),
        "expectedObservedOfficialMcapCommit": _configuration.EXPECTED_OBSERVED_COMMIT,
        "externalToolingStatus": external_tooling_status,
        "nodeVersion": node_version,
        "packageManagerVersion": package_manager_version,
        "generatedVariantCount": generated_variant_count,
        "runners": runners,
        "tooling": tooling,
        "verdict": verdict,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    _configuration.REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def write_skipped_report(reason: str) -> None:
    """Write a skipped report when external conformance tooling is unavailable."""

    write_phase121_report(
        external_tooling_status="skipped",
        verdict="SKIPPED EXTERNAL TOOLING",
        generated_variant_count=0,
        runners=[
            new_runner_report(
                name="csharp-streamed-reader",
                kind="streamed-reader",
                skips=[{"reason": reason}],
            ),
            new_runner_report(
                name="csharp-indexed-reader",
                kind="indexed-reader",
                skips=[{"reason": reason}],
            ),
            new_runner_report(
                name="csharp-writer",
                kind="writer",
                skips=[{"reason": "Writer option parity requires the external conformance harness."}],
            ),
        ],
        tooling=[{"name": "external-tooling", "status": "skipped", "details": reason}],
    )
    print(f"Phase 121 conformance skipped: {reason}")


def write_ci_smoke_report() -> None:
    """Write a bounded CI report without invoking the official TypeScript harness."""

    project_exists = _configuration.PROJECT_PATH.exists()
    official_status = "present" if _configuration.OFFICIAL_CONFORMANCE.exists() else "not-present"
    write_phase121_report(
        external_tooling_status="ci-smoke",
        verdict="CI SMOKE ONLY",
        generated_variant_count=0,
        runners=[
            new_runner_report(
                name="csharp-streamed-reader",
                kind="streamed-reader",
                skips=[{"reason": "CI smoke validates wrapper/report wiring only; run without --ci-smoke for official conformance."}],
            ),
            new_runner_report(
                name="csharp-indexed-reader",
                kind="indexed-reader",
                skips=[{"reason": "CI smoke validates wrapper/report wiring only; run without --ci-smoke for official conformance."}],
            ),
            new_runner_report(
                name="csharp-writer",
                kind="writer",
                skips=[{"reason": "CI smoke validates wrapper/report wiring only; run without --ci-smoke for official conformance."}],
            ),
        ],
        tooling=[
            {"name": "csharp-conformance-project", "status": "present" if project_exists else "missing", "details": str(_configuration.PROJECT_PATH)},
            {"name": "official-conformance-harness", "status": official_status, "details": str(_configuration.OFFICIAL_CONFORMANCE)},
        ],
    )
    print(f"Phase 121 conformance CI smoke report: {_configuration.REPORT_PATH}")
def first_lines(text: str, count: int = 5) -> str:
    """Return the first non-empty lines from command output."""

    return " ".join(line for line in text.strip().splitlines()[:count])

__all__ = [name for name in globals() if not name.startswith("__")]
