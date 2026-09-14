#!/usr/bin/env python3
"""Fail-closed Phase188 Unity Editor and Windows IL2CPP acceptance runner."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path


REQUIRED_COUNTERS = (
    "returnedMessages", "eligibleChunks", "skippedChunks", "decompressedChunks",
    "headersScanned", "payloadCopies", "payloadBytesCopied",
)


def repo_root() -> Path:
    """Resolve and validate the repository root for acceptance runs."""
    root = Path(__file__).resolve().parents[3]
    if not (root / "Packages").exists():
        raise RuntimeError(f"repository root not found: {root}")
    return root


def build_editor_command(unity: Path, fixture: Path, output: Path, log: Path, project: Path) -> list[str]:
    """Build the explicit Unity batch command for the Phase188 editor probe."""
    return [
        str(unity), "-batchmode", "-nographics", "-quit",
        "-projectPath", str(project),
        "-executeMethod", "Unity2Foxglove.Phase188ReplayPerformanceBuilder.Run",
        "-phase188Fixture", str(fixture), "-phase188Output", str(output),
        "-logFile", str(log),
    ]


def validate_editor_probe(output: Path, log: Path) -> dict:
    """Validate probe counters and the terminal pass marker in Unity output."""
    if not output.is_file():
        raise RuntimeError(f"Unity probe output missing: {output}")
    data = json.loads(output.read_text(encoding="utf-8"))
    missing = [key for key in REQUIRED_COUNTERS if key not in data]
    if missing:
        raise RuntimeError("Unity probe missing counters: " + ", ".join(missing))
    for key in REQUIRED_COUNTERS:
        if not isinstance(data[key], int) or data[key] < 0:
            raise RuntimeError(f"Unity probe counter is invalid: {key}={data[key]!r}")
    if data["payloadBytesCopied"] < data["payloadCopies"]:
        raise RuntimeError("payload byte counter is smaller than copy count")
    log_text = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
    if "PHASE188_EDITOR_PASS" not in log_text:
        raise RuntimeError("Unity log does not contain PHASE188_EDITOR_PASS")
    data["status"] = "pass"
    data["terminalMarker"] = "PHASE188_EDITOR_PASS"
    return data


def run_editor(unity: Path, fixture: Path, output_dir: Path, project: Path) -> dict:
    """Run Unity Editor and persist stdout, stderr, log, and validated evidence."""
    output_dir.mkdir(parents=True, exist_ok=True)
    probe = output_dir / "phase188-editor-probe.json"
    log = output_dir / "Editor.log"
    command = build_editor_command(unity, fixture, probe, log, project)
    completed = subprocess.run(command, cwd=project.parent, text=True, capture_output=True)
    (output_dir / "Editor.stdout.log").write_text(completed.stdout or "", encoding="utf-8")
    (output_dir / "Editor.stderr.log").write_text(completed.stderr or "", encoding="utf-8")
    if completed.returncode != 0:
        raise RuntimeError(f"Unity Editor exited {completed.returncode}; see {log}")
    result = validate_editor_probe(probe, log)
    result["command"] = command
    result["exitStatus"] = completed.returncode
    return result


def run_il2cpp(unity: Path, output_dir: Path, project: Path, timeout_minutes: int) -> dict:
    """Run the explicit Windows IL2CPP build and require a produced player."""
    output_dir.mkdir(parents=True, exist_ok=True)
    player = output_dir / "FoxgloveDemo.exe"
    log = output_dir / "build.log"
    command = [sys.executable, str(project.parent / "Scripts" / "unity_build" / "unity_il2cpp.py"),
               "--target", "win64", "--unity", str(unity), "--project", str(project),
               "--build-dir", str(output_dir), "--output", str(player), "--log", str(log),
               "--timeout-minutes", str(timeout_minutes)]
    completed = subprocess.run(command, cwd=project.parent, text=True, capture_output=True)
    (output_dir / "build.stdout.log").write_text(completed.stdout or "", encoding="utf-8")
    (output_dir / "build.stderr.log").write_text(completed.stderr or "", encoding="utf-8")
    if completed.returncode != 0:
        raise RuntimeError(f"IL2CPP build exited {completed.returncode}; see {log}")
    if not player.exists():
        raise RuntimeError(f"IL2CPP player missing: {player}")
    return {"status": "pass", "player": str(player), "command": command, "exitStatus": completed.returncode}


def run_acceptance(mode: str, output_dir: Path, fixture: Path, unity: Path | None = None,
                   project: Path | None = None, timeout_minutes: int = 45) -> dict:
    """Execute one acceptance mode and write fail-closed machine-readable evidence."""
    evidence = {"phase": "188", "mode": mode, "generatedAtUnix": time.time(), "status": "pass"}
    try:
        if mode == "windows-editor":
            if unity is None or project is None:
                raise RuntimeError("windows-editor requires an explicit Unity executable and project")
            evidence["probe"] = run_editor(unity, fixture, output_dir, project)
        elif mode == "windows-il2cpp-player":
            if unity is None or project is None:
                raise RuntimeError("windows-il2cpp-player requires an explicit Unity executable and project")
            evidence["player"] = run_il2cpp(unity, output_dir, project, timeout_minutes)
        else:
            raise RuntimeError(f"unsupported mode: {mode}")
    except Exception as exc:
        evidence["status"] = "fail"
        evidence["error"] = str(exc)
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir / "phase188-acceptance.json").write_text(json.dumps(evidence, indent=2, sort_keys=True), encoding="utf-8")
    return evidence


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line mode, environment, fixture, and output settings."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--windows-editor", action="store_true")
    parser.add_argument("--windows-il2cpp-player", action="store_true")
    parser.add_argument("--unity", type=Path, default=Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"))
    parser.add_argument("--project", type=Path, default=Path("Unity2Foxglove"))
    parser.add_argument("--fixture", type=Path, default=Path("build/phase188/baseline-quick/replay-quick.mcap"))
    parser.add_argument("--output", type=Path, default=Path("build/phase188/windows-editor"))
    parser.add_argument("--timeout-minutes", type=int, default=45)
    args = parser.parse_args(argv)
    if args.windows_editor == args.windows_il2cpp_player:
        parser.error("select exactly one of --windows-editor or --windows-il2cpp-player")
    return args


def main(argv: list[str]) -> int:
    """Run the selected acceptance mode and return its observed status code."""
    args = parse_args(argv)
    root = repo_root()
    mode = "windows-editor" if args.windows_editor else "windows-il2cpp-player"
    evidence = run_acceptance(mode, (root / args.output).resolve(), (root / args.fixture).resolve(),
                              (root / args.unity).resolve(), (root / args.project).resolve(), args.timeout_minutes)
    print(json.dumps(evidence, indent=2, sort_keys=True))
    return 0 if evidence["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
