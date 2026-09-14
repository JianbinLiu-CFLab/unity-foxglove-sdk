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


def terminate_process_tree(pid: int) -> None:
    """Terminate a Windows child process tree after a bounded smoke timeout."""
    if sys.platform.startswith("win"):
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"],
                       capture_output=True, text=True, check=False)


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


def run_il2cpp(unity: Path, output_dir: Path, project: Path, fixture: Path, timeout_minutes: int) -> dict:
    """Build and execute the opt-in Phase188 player replay smoke."""
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
    run_command = [str(player), "-batchmode", "-nographics", "-phase188PlayerAcceptance",
                   "-phase188Fixture", str(fixture)]
    run_log = output_dir / "player.stdout.log"
    run_err = output_dir / "player.stderr.log"
    player_log = output_dir / "Player.log"
    run_command += ["-logFile", str(player_log)]
    try:
        player_run = subprocess.run(run_command, cwd=output_dir, text=True,
                                    capture_output=True, timeout=120)
    except subprocess.TimeoutExpired as exc:
        # subprocess.run has no handle available in the exception; the
        # executable is launched from this output directory, so terminate only
        # matching player processes and leave all unrelated Unity processes.
        for process in subprocess.check_output(
                ["tasklist", "/FI", "IMAGENAME eq FoxgloveDemo.exe", "/FO", "CSV", "/NH"],
                text=True, errors="replace").splitlines():
            if "FoxgloveDemo.exe" in process:
                fields = [part.strip('"') for part in process.split('","')]
                if len(fields) > 1:
                    terminate_process_tree(int(fields[1]))
        run_log.write_text(exc.stdout or "", encoding="utf-8")
        run_err.write_text(exc.stderr or "", encoding="utf-8")
        raise RuntimeError("IL2CPP player replay smoke timed out") from exc
    run_log.write_text(player_run.stdout or "", encoding="utf-8")
    run_err.write_text(player_run.stderr or "", encoding="utf-8")
    combined = (player_run.stdout or "") + (player_run.stderr or "")
    player_log_text = player_log.read_text(encoding="utf-8", errors="replace") if player_log.is_file() else ""
    if player_run.returncode != 0 or "PHASE188_PLAYER_PASS" not in (combined + player_log_text):
        raise RuntimeError(
            f"IL2CPP player replay smoke failed exit={player_run.returncode}; see {run_log}")
    return {"status": "pass", "player": str(player), "command": command,
            "exitStatus": completed.returncode, "runtimeCommand": run_command,
            "runtimeExitStatus": player_run.returncode, "runtimeMarker": "PHASE188_PLAYER_PASS"}


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
            evidence["player"] = run_il2cpp(unity, output_dir, project, fixture, timeout_minutes)
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
