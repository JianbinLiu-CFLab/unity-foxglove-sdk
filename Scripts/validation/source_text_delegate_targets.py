"""Generate or check the source-text delegate-target manifest enforced by analyzer FOXDT001 (Plan 209)."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Callable, Sequence

MANIFEST = Path("Scripts/validation/source_text_delegate_targets.tsv")
SARIF_DIRECTORY = Path("build/review/delegate-targets")
RUNTIME = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj"
UNIT = "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj"
BRIDGE = "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj"
VARIANTS: tuple[tuple[str, str, tuple[str, ...]], ...] = (
    ("runtime", RUNTIME, ()),
    ("runtime-adapter", RUNTIME, ("-p:IncludeRos2ForUnityAdapter=true",)),
    ("unit", UNIT, ()),
    ("unit-adapter", UNIT, ("-p:IncludeRos2ForUnityAdapter=true",)),
    ("unit-native", UNIT, ("-p:IncludeRos2ForUnityNative=true",)),
    ("unit-manager", UNIT, ("-p:IncludeManagerProductionBoundary=true",)),
    ("bridge-unit", BRIDGE, ()),
)
ENTRY_RULE = "FOXDT900"

Runner = Callable[[Sequence[str], Path], "subprocess.CompletedProcess[str]"]


class DelegateTargetsError(RuntimeError):
    """A variant build or its SARIF log did not yield a trustworthy entry set."""


def _run(command: Sequence[str], cwd: Path) -> "subprocess.CompletedProcess[str]":
    """Run one build command and capture its output."""
    return subprocess.run(
        list(command),
        cwd=cwd,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )


def entries_from_sarif(path: Path, variant: str, returncode: int, output: str) -> list[str]:
    """Return the FOXDT900 manifest lines of one emit build, failing on any other error."""
    if not path.is_file():
        tail = "\n".join(output.splitlines()[-20:])
        raise DelegateTargetsError(f"{variant}: no SARIF log at {path} (exit {returncode})\n{tail}")
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    lines: list[str] = []
    errors: list[str] = []
    for run in data.get("runs", []):
        for result in run.get("results", []):
            rule = result.get("ruleId", "")
            message = result.get("message", {}).get("text", "")
            if rule == ENTRY_RULE:
                lines.append(message)
            elif result.get("level") == "error":
                errors.append(f"{rule}: {message}")
    if errors:
        raise DelegateTargetsError(f"{variant}: build reported errors:\n" + "\n".join(errors[:20]))
    if returncode != 0:
        tail = "\n".join(output.splitlines()[-20:])
        raise DelegateTargetsError(f"{variant}: build failed (exit {returncode}) without compiler errors in SARIF\n{tail}")
    bad = [line for line in lines if not line.startswith(variant + "\t")]
    if bad:
        raise DelegateTargetsError(f"{variant}: FOXDT900 lines for another variant: {bad[:3]}")
    return lines


def collect(root: Path, variants: Sequence[str], runner: Runner = _run) -> list[str]:
    """Build each selected variant in emit mode and return the sorted union of its entries."""
    directory = root / SARIF_DIRECTORY
    directory.mkdir(parents=True, exist_ok=True)
    union: set[str] = set()
    for name, project, switches in VARIANTS:
        if name not in variants:
            continue
        sarif = directory / f"{name}.sarif"
        command = [
            "dotnet", "build", project, "-nologo",
            "-p:FoxDelegateTargetsEmit=true",
            "-p:FoxDelegateTargetsEmitAuthorized=true",
            f"-p:FoxDelegateTargetsErrorLog={sarif}",
            *switches,
        ]
        completed = runner(command, root)
        union.update(entries_from_sarif(sarif, name, completed.returncode, completed.stdout + completed.stderr))
    return sorted(union)


def render(lines: Sequence[str]) -> str:
    """Render manifest lines in the canonical sorted, LF-terminated form."""
    return "".join(line + "\n" for line in sorted(lines))


def selected_lines(text: str, variants: Sequence[str]) -> list[str]:
    """Return the committed manifest lines that belong to the selected variants."""
    return [line for line in text.splitlines() if line.split("\t", 1)[0] in variants]


def main(argv: Sequence[str] | None = None, runner: Runner = _run) -> int:
    """Write or check the manifest for all or selected variants."""
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--variants", default=",".join(name for name, _, _ in VARIANTS))
    args = parser.parse_args(argv)
    known = {name for name, _, _ in VARIANTS}
    variants = [name.strip() for name in args.variants.split(",") if name.strip()]
    unknown = sorted(set(variants) - known)
    if unknown or not variants:
        print(f"DELEGATE_TARGETS_ERROR unknown variants: {unknown or '(none selected)'}")
        return 2
    root = args.root.resolve()
    try:
        actual = collect(root, variants, runner)
    except DelegateTargetsError as error:
        print(f"DELEGATE_TARGETS_ERROR {error}")
        return 1
    manifest = root / MANIFEST
    if args.write:
        kept = [] if set(variants) == known or not manifest.is_file() else [
            line for line in manifest.read_text(encoding="utf-8").splitlines()
            if line.split("\t", 1)[0] not in variants
        ]
        manifest.parent.mkdir(parents=True, exist_ok=True)
        manifest.write_text(render([*kept, *actual]), encoding="utf-8", newline="\n")
        print(f"DELEGATE_TARGETS_WRITTEN lines={len(kept) + len(actual)} variants={','.join(variants)}")
        return 0
    committed = selected_lines(manifest.read_text(encoding="utf-8"), variants) if manifest.is_file() else None
    if committed is None:
        print(f"DELEGATE_TARGETS_MISMATCH manifest missing: {MANIFEST.as_posix()}")
        return 1
    missing = sorted(set(actual) - set(committed))
    stale = sorted(set(committed) - set(actual))
    for line in missing[:20]:
        print(f"DELEGATE_TARGETS_MISSING {line}")
    for line in stale[:20]:
        print(f"DELEGATE_TARGETS_STALE {line}")
    if missing or stale:
        print(f"DELEGATE_TARGETS_MISMATCH missing={len(missing)} stale={len(stale)}")
        return 1
    print(f"DELEGATE_TARGETS_OK lines={len(actual)} variants={','.join(variants)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
