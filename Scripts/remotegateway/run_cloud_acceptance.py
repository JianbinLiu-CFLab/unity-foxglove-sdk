#!/usr/bin/env python3
"""Run the Phase171 real Foxglove Cloud acceptance helper.

This helper intentionally reads FOXGLOVE_DEVICE_TOKEN only from the inherited
process environment. Do not add token command-line arguments; command lines are
easy to leak through process lists and shell history.
"""

from __future__ import annotations

import argparse
import datetime as dt
import os
import shutil
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
BUILD_SCRIPT = ROOT / "Scripts" / "remotegateway" / "build_foxglove_c_win64.py"
DEFAULT_PROJECT = ROOT / "Unity2Foxglove"
PLUGIN_DIR = ROOT / "Packages" / "dev.unity2foxglove.remotegateway.win64" / "Runtime" / "Plugins" / "Windows" / "x86_64"
TOKEN_ENV = "FOXGLOVE_DEVICE_TOKEN"
EXPECTED_START_LOG = "[Foxglove] Remote gateway started. Publishing to Foxglove Cloud."
UNSUPPORTED_V1 = "ClientPublish, Services, Parameters, Assets, ConnectionGraph"


def parse_args() -> argparse.Namespace:
    """Parse command-line options for the Cloud acceptance helper."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--unity-exe",
        default=os.environ.get("UNITY_EXE") or os.environ.get("UNITY_EDITOR_PATH"),
        help="Path to Unity.exe. Defaults to UNITY_EXE/UNITY_EDITOR_PATH or a Unity Hub scan.",
    )
    parser.add_argument(
        "--project-path",
        default=str(DEFAULT_PROJECT),
        help="Unity project path to open. Defaults to Unity2Foxglove.",
    )
    parser.add_argument(
        "--skip-native-build",
        action="store_true",
        help="Reuse an existing package foxglove.dll instead of building and copying one.",
    )
    parser.add_argument(
        "--no-wait",
        action="store_true",
        help="Return after launching Unity instead of collecting Editor.log when Unity exits.",
    )
    parser.add_argument(
        "--unity-arg",
        action="append",
        default=[],
        help="Extra Unity argument. Repeat for multiple arguments.",
    )
    return parser.parse_args()


def main() -> int:
    """Build optional native artifacts, launch Unity, and collect run evidence."""
    args = parse_args()
    ensure_token_env()

    project_path = Path(args.project_path).resolve()
    if not project_path.exists():
        raise SystemExit(f"Unity project path does not exist: {project_path}")

    unity_exe = find_unity_exe(args.unity_exe)
    run_dir = create_run_dir()
    copy_editor_log(run_dir, "before")

    if args.skip_native_build:
        ensure_native_artifact()
    else:
        build_and_copy_native()
        ensure_native_artifact()

    checklist = write_checklist(run_dir, unity_exe, project_path)
    print_summary(run_dir, checklist, unity_exe, project_path)

    command = [str(unity_exe), "-projectPath", str(project_path)] + list(args.unity_arg)
    print("+", " ".join(quote_for_display(part) for part in command), flush=True)
    process = subprocess.Popen(command, cwd=str(ROOT), env=os.environ.copy())

    if args.no_wait:
        print("Unity launched. This process will not collect the post-run Editor.log because --no-wait was used.")
        return 0

    print("Unity is running. Complete the manual checklist, then close Unity to let this helper copy Editor.log.")
    exit_code = process.wait()
    copy_editor_log(run_dir, "after")
    print(f"Unity exited with code {exit_code}. Logs/checklist are under: {run_dir}")
    return exit_code


def ensure_token_env() -> None:
    """Require a non-empty inherited Foxglove device token without printing it."""
    token = os.environ.get(TOKEN_ENV, "").strip()
    if not token:
        raise SystemExit(
            f"{TOKEN_ENV} is not set. In PowerShell, set it in the same shell before launching Unity:\n"
            f'  $env:{TOKEN_ENV}="YOUR_TOKEN"\n'
            "Then run this helper from that PowerShell window."
        )
    print(f"{TOKEN_ENV} is set in the inherited environment. The value will not be printed.")


def find_unity_exe(configured: str | None) -> Path:
    """Resolve Unity.exe from an explicit path, environment, or Unity Hub install."""
    if configured:
        candidate = Path(configured).expanduser().resolve()
        if candidate.exists():
            return candidate
        raise SystemExit(f"Configured Unity.exe was not found: {candidate}")

    candidates: list[Path] = []
    for root in (
        Path(r"C:\Program Files\Unity\Hub\Editor"),
        Path(r"C:\Program Files (x86)\Unity\Hub\Editor"),
    ):
        if root.exists():
            candidates.extend(root.glob(r"*\Editor\Unity.exe"))

    candidates = sorted(candidates, key=lambda path: path.parent.parent.name, reverse=True)
    if candidates:
        return candidates[0]

    raise SystemExit(
        "Could not find Unity.exe. Pass --unity-exe or set UNITY_EXE in this PowerShell session."
    )


def create_run_dir() -> Path:
    """Create a timestamped local evidence directory for this acceptance run."""
    timestamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    run_dir = ROOT / "build" / "remotegateway" / "cloud-acceptance" / timestamp
    run_dir.mkdir(parents=True, exist_ok=True)
    return run_dir


def build_and_copy_native() -> None:
    """Build foxglove_c and copy approved native artifacts into the package."""
    print("Building foxglove_c and copying approved native artifacts into the optional package.")
    subprocess.run(
        [sys.executable, str(BUILD_SCRIPT), "--copy-to-package"],
        cwd=str(ROOT),
        check=True,
        env=os.environ.copy(),
    )


def ensure_native_artifact() -> None:
    """Verify that the optional package contains the reviewed native artifact."""
    dll = PLUGIN_DIR / "foxglove.dll"
    manifest = PLUGIN_DIR / "foxglove-gateway-native-artifact.json"
    if not dll.exists() or not manifest.exists():
        raise SystemExit(
            "Native gateway artifact is missing from the optional package. Run without --skip-native-build first."
        )
    print(f"Native artifact present: {dll.relative_to(ROOT)}")
    print("Do not submit generated foxglove.dll, foxglove.dll.lib, or foxglove.pdb.")


def editor_log_path() -> Path:
    """Return the current user's Unity Editor.log path."""
    local_app_data = os.environ.get("LOCALAPPDATA")
    if not local_app_data:
        return Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"
    return Path(local_app_data) / "Unity" / "Editor" / "Editor.log"


def copy_editor_log(run_dir: Path, suffix: str) -> None:
    """Copy Editor.log into the run directory when Unity has written one."""
    source = editor_log_path()
    if not source.exists():
        return
    shutil.copy2(source, run_dir / f"Editor.{suffix}.log")


def write_checklist(run_dir: Path, unity_exe: Path, project_path: Path) -> Path:
    """Write the per-run manual validation checklist next to captured logs."""
    path = run_dir / "Phase171CloudAcceptanceChecklist.md"
    body = f"""# Phase171 Remote Gateway Cloud Acceptance

Run directory: `{run_dir}`
Unity: `{unity_exe}`
Project: `{project_path}`

## Manual Steps

1. Keep this PowerShell window open. Unity was launched from it so the process inherits `{TOKEN_ENV}`.
2. In Unity, wait until import/compile finishes and confirm there are no remote gateway compile errors.
3. Enter Play Mode.
4. Confirm `FoxgloveManager` is running locally, for example Foxglove Desktop can still connect to `ws://127.0.0.1:8765`.
5. On the GameObject with `FoxgloveRemoteGatewayController`, enable `Enable Remote Gateway`.
6. In Unity Console, expect exactly this success line:
   `{EXPECTED_START_LOG}`
7. In Foxglove Cloud, confirm the device appears online.
8. In Foxglove Cloud, confirm outbound visualization topics appear and live data is visible.
9. Toggle `Enable Remote Gateway` off, then on again at least twice.
10. Exit Play Mode, re-enter Play Mode, and repeat one enable/disable cycle.
11. Close Unity so this helper can copy the final Editor.log.

## Pass Criteria

- Default closed: with the checkbox off, no cloud connection starts and local WebSocket publishing still works.
- Failure closed: missing/invalid token or missing native DLL logs a warning and does not break the local link.
- Cloud outbound path: Cloud receives visualization topics after the success log.
- Lifecycle: repeated enable/disable and Play Mode exit do not hang Unity and do not cause long main-thread stalls.
- Scope: Phase171 v1 is outbound-only. Do not expect {UNSUPPORTED_V1}.

## Evidence To Record

- Unity Console success/warning lines.
- Foxglove Cloud device online/offline observations.
- Topic names visible in Cloud.
- Whether local Foxglove Desktop stayed connected during gateway toggles.
- Any stall or hang timing.

## Cleanup Reminder

Generated native files under `{PLUGIN_DIR.relative_to(ROOT)}` are local validation artifacts.
Do not commit `foxglove.dll`, `foxglove.dll.lib`, or `foxglove.pdb`.
"""
    path.write_text(body, encoding="utf-8")
    return path


def print_summary(run_dir: Path, checklist: Path, unity_exe: Path, project_path: Path) -> None:
    """Print the operator-facing next steps without exposing the token."""
    print()
    print("Phase171 real Foxglove Cloud acceptance is ready.")
    print(f"Unity.exe: {unity_exe}")
    print(f"Project:   {project_path}")
    print(f"Run dir:   {run_dir}")
    print(f"Checklist: {checklist}")
    print()
    print("In Unity: Play Mode -> ensure FoxgloveManager is running -> enable Remote Gateway.")
    print(f"Expected Console line: {EXPECTED_START_LOG}")
    print(f"V1 excludes: {UNSUPPORTED_V1}")
    print()


def quote_for_display(value: str) -> str:
    """Quote a command-line argument for display-only logging."""
    if " " not in value and "\t" not in value:
        return value
    return '"' + value.replace('"', '\\"') + '"'


if __name__ == "__main__":
    raise SystemExit(main())
