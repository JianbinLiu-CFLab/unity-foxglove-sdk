# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Run the Phase 121 MCAP conformance baseline through a cross-platform Python entry point.
# Usage: python Scripts/mcap/conformance/run_phase121_conformance.py [--release-blocking]
# Inputs: third-party/mcap checkout, C# conformance console project, and csharp runner overlay sources.
# Outputs: phase121 conformance report (default under build/mcap-conformance)
# and overlay/test artifacts under build/mcap-conformance.

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable


EXPECTED_OBSERVED_COMMIT = "c3cab6bd3ce79199e362766daec3a4689f3a0335"
SCRIPT_PATH = Path(__file__).resolve()
REPO_ROOT = SCRIPT_PATH.parents[3]
OFFICIAL_ROOT = REPO_ROOT / "third-party/mcap"
OFFICIAL_CONFORMANCE = OFFICIAL_ROOT / "tests/conformance"
BUILD_ROOT = REPO_ROOT / "build/mcap-conformance"
OVERLAY_ROOT = BUILD_ROOT / "mcap-overlay"
DATA_DIR = BUILD_ROOT / "data"
REPORT_PATH = BUILD_ROOT / "phase121-conformance-report.json"
PROJECT_PATH = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Tests/McapConformance/Unity2Foxglove.McapConformance.csproj"
RUNNER_SOURCE_ROOT = REPO_ROOT / "Scripts/mcap/conformance/csharp-runners"
RUNNER_ARRAY_DECLARATION = "const runners: readonly (IndexedReadTestRunner | StreamedReadTestRunner | WriteTestRunner)[] = ["
CSHARP_RUNNER_INSERTION = (
    "const runners: readonly (IndexedReadTestRunner | StreamedReadTestRunner | WriteTestRunner)[] = [\n"
    "  new CsharpIndexedReaderTestRunner(),\n"
    "  new CsharpStreamedReaderTestRunner(),\n"
    "  new CsharpWriterTestRunner(),"
)
CSHARP_CONFORMANCE_DLL_NAME = "Unity2Foxglove.McapConformance.dll"


@dataclass(frozen=True)
class CommandResult:
    """Captured process result used by the conformance wrapper."""

    exit_code: int
    stdout: str
    stderr: str
    command: str
    timed_out: bool = False


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command line arguments."""

    parser = argparse.ArgumentParser(description="Run Phase 121 MCAP conformance baseline.")
    parser.add_argument(
        "--release-blocking",
        action="store_true",
        help="Return a non-zero exit code when external tooling is skipped or measured failures exist.",
    )
    parser.add_argument(
        "--report-path",
        help="Optional report output path. Defaults to build/mcap-conformance/phase121-conformance-report.json.",
    )
    return parser.parse_args(argv)


def invoke_command_capture(
    command: list[str],
    *,
    cwd: Path = REPO_ROOT,
    env: dict[str, str] | None = None,
    timeout_seconds: int | None = None,
) -> CommandResult:
    """Run a command and capture stdout, stderr, exit code, and timeout state."""

    process_env = os.environ.copy()
    if env:
        process_env.update(env)

    resolved_command = command[:]
    executable = shutil.which(resolved_command[0])
    if executable:
        resolved_command[0] = executable

    command_text = " ".join(command)
    try:
        completed = subprocess.run(
            resolved_command,
            cwd=str(cwd),
            env=process_env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout_seconds,
            check=False,
        )
        return CommandResult(completed.returncode, completed.stdout, completed.stderr, command_text)
    except subprocess.TimeoutExpired as ex:
        stdout = ex.stdout if isinstance(ex.stdout, str) else (ex.stdout or b"").decode(errors="replace")
        stderr = ex.stderr if isinstance(ex.stderr, str) else (ex.stderr or b"").decode(errors="replace")
        timeout_message = f"Timed out after {timeout_seconds} second(s)."
        stderr = timeout_message if not stderr.strip() else stderr + "\n" + timeout_message
        return CommandResult(-1, stdout, stderr, command_text, timed_out=True)
    except OSError as ex:
        return CommandResult(-1, "", str(ex), command_text)


def get_official_commit() -> str | None:
    """Return the checked out official MCAP commit if available."""

    if not OFFICIAL_ROOT.exists() or shutil.which("git") is None:
        return None

    result = invoke_command_capture(["git", "-C", str(OFFICIAL_ROOT), "rev-parse", "HEAD"])
    if result.exit_code != 0:
        return None
    return result.stdout.strip()


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

    BUILD_ROOT.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    report = {
        "officialMcapCommit": get_official_commit(),
        "officialMcapPath": str(OFFICIAL_ROOT),
        "expectedObservedOfficialMcapCommit": EXPECTED_OBSERVED_COMMIT,
        "externalToolingStatus": external_tooling_status,
        "nodeVersion": node_version,
        "packageManagerVersion": package_manager_version,
        "generatedVariantCount": generated_variant_count,
        "runners": runners,
        "tooling": tooling,
        "verdict": verdict,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


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


def copy_directory_without_local_state(source: Path, destination: Path) -> None:
    """Copy a directory while excluding local checkout and package-manager state."""

    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True, exist_ok=True)

    for child in source.iterdir():
        if child.name in {".git", "node_modules"}:
            continue
        target = destination / child.name
        if child.is_dir():
            copy_directory_without_local_state(child, target)
        else:
            shutil.copy2(child, target)


def add_csharp_runner_overlay() -> None:
    """Overlay C# conformance runners into the copied official MCAP harness."""

    runner_dest = OVERLAY_ROOT / "tests/conformance/scripts/run-tests/runners"
    for filename in (
        "CsharpStreamedReaderTestRunner.ts",
        "CsharpIndexedReaderTestRunner.ts",
        "CsharpWriterTestRunner.ts",
    ):
        shutil.copy2(RUNNER_SOURCE_ROOT / filename, runner_dest / filename)

    index_path = runner_dest / "index.ts"
    index = index_path.read_text(encoding="utf-8")
    if RUNNER_ARRAY_DECLARATION not in index:
        raise RuntimeError(
            "Unable to inject C# conformance runners: upstream runner array declaration was not found."
        )
    imports = (
        'import CsharpIndexedReaderTestRunner from "./CsharpIndexedReaderTestRunner.ts";\n'
        'import CsharpStreamedReaderTestRunner from "./CsharpStreamedReaderTestRunner.ts";\n'
        'import CsharpWriterTestRunner from "./CsharpWriterTestRunner.ts";\n'
    )
    index = imports + "\n" + index
    index = index.replace(RUNNER_ARRAY_DECLARATION, CSHARP_RUNNER_INSERTION, 1)
    if "new CsharpIndexedReaderTestRunner()" not in index or "new CsharpWriterTestRunner()" not in index:
        raise RuntimeError("Unable to inject C# conformance runners: overlay output is missing C# runners.")
    index_path.write_text(index, encoding="utf-8")


def measure_runner_output(name: str, kind: str, result: CommandResult) -> dict[str, object]:
    """Summarize official conformance runner console output."""

    text = result.stdout + "\n" + result.stderr
    tested = len(re.findall(r"(?m)^\s*testing\s+", text))
    skipped = len(re.findall(r"(?m)^\s*(not supported|unsupported)\s+", text))
    errors = len(re.findall(r"(?m)^(Error:|FAIL\b|\w+Error:)", text, re.IGNORECASE))
    if result.exit_code != 0 and errors == 0:
        errors = 1
    passed = max(0, tested - errors)
    failures: list[dict[str, object]] = []
    if errors > 0:
        failures.append(
            {
                "exitCode": result.exit_code,
                "details": "\n".join((line for line in text.strip().splitlines() if line.strip()))[:4000],
            }
        )
    return new_runner_report(name=name, kind=kind, passed=passed, failed=errors, skipped=skipped, failures=failures)


def first_lines(text: str, count: int = 5) -> str:
    """Return the first non-empty lines from command output."""

    return " ".join(line for line in text.strip().splitlines()[:count])


def package_manager_command() -> tuple[list[str], str] | None:
    """Return the package-manager command used for the official TypeScript harness."""

    if shutil.which("corepack"):
        return ["corepack", "yarn"], "corepack"
    if shutil.which("yarn"):
        return ["yarn"], "yarn"
    return None


def run_package_manager(base_command: list[str], args: Iterable[str], *, cwd: Path, timeout_seconds: int) -> CommandResult:
    """Run the selected package manager with additional arguments."""

    return invoke_command_capture(base_command + list(args), cwd=cwd, timeout_seconds=timeout_seconds)


def count_generated_variants() -> int:
    """Count generated MCAP variants in the official conformance data directory."""

    if not DATA_DIR.exists():
        return 0
    return sum(1 for path in DATA_DIR.rglob("*.mcap") if path.is_file())


def read_target_framework() -> str:
    """Read the target framework from the C# conformance project."""

    import xml.etree.ElementTree as ET
    tree = ET.parse(str(PROJECT_PATH))
    root = tree.getroot()
    tf = root.find(".//TargetFramework")
    if tf is None:
        tf = root.find(".//{http://schemas.microsoft.com/developer/msbuild/2003}TargetFramework")
    if tf is not None and tf.text:
        return tf.text.strip()
    raise RuntimeError(f"TargetFramework was not found in {PROJECT_PATH}")


def resolve_conformance_dll_path() -> Path:
    """Resolve the built C# conformance DLL from the project target framework."""

    target_framework = read_target_framework()
    expected = REPO_ROOT / "build" / "McapConformance" / "Release" / target_framework / CSHARP_CONFORMANCE_DLL_NAME
    if expected.exists():
        return expected

    candidates = sorted(
        (REPO_ROOT / "build" / "McapConformance" / "Release").glob(f"*/{CSHARP_CONFORMANCE_DLL_NAME}")
    )
    if len(candidates) == 1:
        return candidates[0]
    if candidates:
        raise RuntimeError("Multiple C# conformance DLL candidates found: " + ", ".join(str(path) for path in candidates))
    return expected


def run_conformance(release_blocking: bool) -> int:
    """Run the full Phase 121 measured conformance baseline."""

    BUILD_ROOT.mkdir(parents=True, exist_ok=True)

    if not OFFICIAL_CONFORMANCE.exists():
        write_skipped_report("third-party/mcap/tests/conformance was not found.")
        return 1 if release_blocking else 0

    if shutil.which("dotnet") is None:
        write_skipped_report("dotnet was not found.")
        return 1 if release_blocking else 0

    build = invoke_command_capture(["dotnet", "build", str(PROJECT_PATH), "-c", "Release"])
    if build.exit_code != 0:
        print("C# conformance console build failed.", file=sys.stderr)
        print(build.stdout + build.stderr, file=sys.stderr)
        return build.exit_code

    manager = package_manager_command()
    if shutil.which("node") is None or manager is None:
        write_skipped_report("Node and Yarn are required for the official foxglove/mcap conformance harness.")
        return 1 if release_blocking else 0

    package_command, package_label = manager
    node_version = invoke_command_capture(["node", "--version"]).stdout.strip()
    yarn_version = invoke_command_capture(package_command + ["--version"]).stdout.strip()

    copy_directory_without_local_state(OFFICIAL_ROOT, OVERLAY_ROOT)
    add_csharp_runner_overlay()

    env = {"U2F_MCAP_CONFORMANCE_DLL": str(resolve_conformance_dll_path())}

    install = run_package_manager(package_command, ["install", "--immutable"], cwd=OVERLAY_ROOT, timeout_seconds=600)
    if install.exit_code != 0:
        write_skipped_report("Yarn dependencies are unavailable: " + first_lines(install.stdout + install.stderr))
        return 1 if release_blocking else 0

    generate = run_package_manager(
        package_command,
        ["workspace", "@foxglove/mcap-conformance", "generate-inputs", "--data-dir", str(DATA_DIR)],
        cwd=OVERLAY_ROOT,
        timeout_seconds=300,
    )
    if generate.exit_code != 0:
        write_skipped_report("Official fixture generation failed: " + first_lines(generate.stdout + generate.stderr))
        return 1 if release_blocking else 0

    runner_reports: list[dict[str, object]] = []
    runner_specs = [
        ("csharp-streamed-reader", "streamed-reader"),
        ("csharp-indexed-reader", "indexed-reader"),
        ("csharp-writer", "writer"),
    ]
    for runner_name, kind in runner_specs:
        result = invoke_command_capture(
            package_command
            + [
                "workspace",
                "@foxglove/mcap-conformance",
                "run-tests",
                "--data-dir",
                str(DATA_DIR),
                "--runner",
                runner_name,
            ],
            cwd=OVERLAY_ROOT,
            env=env,
            timeout_seconds=180,
        )
        runner_reports.append(measure_runner_output(runner_name, kind, result))

    failed = sum(int(report["failed"]) for report in runner_reports)
    verdict = "MEASURED BASELINE WITH FAILURES" if failed > 0 else "PASS WITH MEASURED BASELINE"
    write_phase121_report(
        external_tooling_status="available",
        verdict=verdict,
        generated_variant_count=count_generated_variants(),
        runners=runner_reports,
        tooling=[
            {"name": "dotnet-build", "status": "passed", "details": build.stdout.strip()},
            {"name": "fixture-generation", "status": "passed", "details": generate.stdout.strip()},
        ],
        node_version=node_version,
        package_manager_version=f"yarn {yarn_version}" if package_label == "corepack" else f"{package_label} {yarn_version}",
    )

    print(f"Phase 121 conformance report: {REPORT_PATH}")
    if release_blocking and failed > 0:
        return 1
    return 0


def main(argv: list[str]) -> int:
    """Program entry point."""

    global REPORT_PATH
    args = parse_args(argv)
    if args.report_path:
        REPORT_PATH = Path(args.report_path).resolve()
    return run_conformance(bool(args.release_blocking))


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
