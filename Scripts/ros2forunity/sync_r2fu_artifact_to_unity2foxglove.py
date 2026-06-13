#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Sync a verified ROS2 For Unity artifact into the Unity2Foxglove
# Jazzy Win64 runtime package and record local evidence.

"""Sync a verified R2FU Windows artifact into Unity2Foxglove's runtime package."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORKSPACE_ROOT = Path(r"D:\ros2unity")
PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
DEFAULT_ARTIFACT = (
    WORKSPACE_ROOT
    / "artifacts"
    / "ros2-for-unity"
    / "jazzy"
    / "windows_x86_64"
    / "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
)
DEFAULT_EVIDENCE_DIR = WORKSPACE_ROOT / "logs" / "unity2foxglove-r2fu"
DEFAULT_UNITY_EXE = Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def manifest_for_artifact(path: Path) -> Path:
    return path.with_name(path.stem + ".manifest.json")


def run(command: list[str], *, cwd: Path = ROOT, log: Path | None = None, env: dict[str, str] | None = None) -> None:
    print(f"==> {' '.join(command)}")
    if log is None:
        subprocess.run(command, cwd=cwd, check=True, env=env)
        return

    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8", errors="replace") as stream:
        completed = subprocess.run(
            command,
            cwd=cwd,
            stdout=stream,
            stderr=subprocess.STDOUT,
            text=True,
            env=env,
        )
    if completed.returncode != 0:
        raise subprocess.CalledProcessError(completed.returncode, command)


def run_text(command: list[str], *, cwd: Path = ROOT) -> str:
    completed = subprocess.run(
        command,
        cwd=cwd,
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return completed.stdout.strip()


def git_info(repo: Path) -> dict[str, object]:
    def git(*args: str) -> str:
        return run_text(["git", *args], cwd=repo)

    status = git("status", "--short")
    return {
        "branch": git("branch", "--show-current"),
        "commit": git("rev-parse", "HEAD"),
        "shortCommit": git("rev-parse", "--short", "HEAD"),
        "statusShort": status,
        "dirty": bool(status),
    }


def read_json(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, data: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def assert_artifact_matches_manifest(artifact: Path, manifest: Path | None) -> dict[str, object]:
    if not artifact.exists():
        raise FileNotFoundError(f"Missing artifact zip: {artifact}")
    digest = sha256_file(artifact)
    if manifest is None:
        manifest = manifest_for_artifact(artifact)
    if not manifest.exists():
        return {"path": str(artifact), "sha256": digest, "manifest": None}

    data = read_json(manifest)
    expected = data.get("sha256")
    if expected and expected != digest:
        raise ValueError(f"Artifact sha256 does not match manifest: {digest} != {expected}")
    return {"path": str(artifact), "sha256": digest, "manifest": str(manifest), "manifestData": data}


def ensure_project_uses_runtime_package(project_root: Path, *, update: bool) -> dict[str, object]:
    manifest_path = project_root / "Unity2Foxglove" / "Packages" / "manifest.json"
    lock_path = project_root / "Unity2Foxglove" / "Packages" / "packages-lock.json"
    direct_asset = project_root / "Unity2Foxglove" / "Assets" / "Ros2ForUnity"
    manifest = read_json(manifest_path)
    dependencies = manifest.setdefault("dependencies", {})
    runtime_ref = "file:../../Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"

    changed = False
    if dependencies.get(PACKAGE_NAME) != runtime_ref:
        if not update:
            raise RuntimeError(f"{manifest_path} does not reference {PACKAGE_NAME}; rerun with --update-project-manifest")
        dependencies[PACKAGE_NAME] = runtime_ref
        write_json(manifest_path, manifest)
        changed = True

    if direct_asset.exists():
        raise RuntimeError(
            "Direct Unity2Foxglove/Assets/Ros2ForUnity is importable. "
            "Remove or quarantine it before package-mode acceptance."
        )

    lock_has_runtime = False
    if lock_path.exists():
        lock_data = read_json(lock_path)
        lock_has_runtime = PACKAGE_NAME in lock_data.get("dependencies", {})

    return {
        "manifestPath": str(manifest_path),
        "manifestUpdated": changed,
        "lockPath": str(lock_path),
        "lockHasRuntimePackage": lock_has_runtime,
        "directAssetsRos2ForUnityExists": direct_asset.exists(),
    }


def run_unity_import(unity_exe: Path, project_path: Path, log_path: Path) -> None:
    if not unity_exe.exists():
        raise FileNotFoundError(f"Unity editor not found: {unity_exe}")
    clean_env = os.environ.copy()
    for key in ("ROS_DISTRO", "ROS_VERSION", "ROS_PYTHON_VERSION", "AMENT_PREFIX_PATH", "COLCON_PREFIX_PATH", "RMW_IMPLEMENTATION"):
        clean_env.pop(key, None)
    clean_env["PATH"] = ";".join(
        item
        for item in clean_env.get("PATH", "").split(";")
        if "ros2_jazzy" not in item.lower()
        and "ros2-windows" not in item.lower()
        and ".pixi\\envs\\default" not in item.lower()
    )
    run(
        [
            str(unity_exe),
            "-projectPath",
            str(project_path),
            "-batchmode",
            "-nographics",
            "-quit",
            "-logFile",
            str(log_path),
        ],
        cwd=ROOT,
        env=clean_env,
    )


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-zip", type=Path, default=DEFAULT_ARTIFACT)
    parser.add_argument("--artifact-manifest", type=Path, default=None)
    parser.add_argument("--project-root", type=Path, default=ROOT)
    parser.add_argument("--evidence-dir", type=Path, default=DEFAULT_EVIDENCE_DIR)
    parser.add_argument("--update-project-manifest", action="store_true", help="Add the runtime package dependency if it is missing.")
    parser.add_argument("--skip-validate", action="store_true")
    parser.add_argument("--run-unity-import", action="store_true")
    parser.add_argument("--unity-editor", type=Path, default=Path(os.environ.get("R2FU_UNITY_EXE", str(DEFAULT_UNITY_EXE))))
    parser.add_argument("--dry-run", action="store_true", help="Validate inputs and print planned paths without modifying the package.")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    project_root = args.project_root.resolve()
    artifact = args.artifact_zip.resolve()
    evidence_dir = args.evidence_dir.resolve()
    evidence_dir.mkdir(parents=True, exist_ok=True)
    timestamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")

    artifact_info = assert_artifact_matches_manifest(artifact, args.artifact_manifest)
    inventory_path = project_root / "Packages" / "dev.unity2foxglove.ros2forunity" / "Compliance" / "r2fu-jazzy-win64-runtime-inventory.json"
    package_path = project_root / "Packages" / PACKAGE_NAME
    inspect_script = project_root / "Scripts" / "release" / "inspect_r2fu_runtime_artifact.py"
    build_script = project_root / "Scripts" / "release" / "build_r2fu_runtime_package.py"
    validate_script = project_root / "Scripts" / "release" / "validate_r2fu_runtime_package.py"

    if args.dry_run:
        project_shape = ensure_project_uses_runtime_package(project_root, update=False)
        print("[DRY-RUN] artifact:", artifact)
        print("[DRY-RUN] sha256:", artifact_info["sha256"])
        print("[DRY-RUN] inventory:", inventory_path)
        print("[DRY-RUN] package:", package_path)
        print("[DRY-RUN] direct Assets/Ros2ForUnity exists:", project_shape["directAssetsRos2ForUnityExists"])
        print("[DRY-RUN] lock has runtime package:", project_shape["lockHasRuntimePackage"])
        return 0

    run([sys.executable, str(inspect_script), "--zip", str(artifact), "--out", str(inventory_path)], cwd=project_root)
    run(
        [
            sys.executable,
            str(build_script),
            "--zip",
            str(artifact),
            "--inventory",
            str(inventory_path),
            "--package",
            str(package_path),
        ],
        cwd=project_root,
    )
    project_shape = ensure_project_uses_runtime_package(project_root, update=args.update_project_manifest)

    validation_log = evidence_dir / f"sync-r2fu-runtime-validate-{timestamp}.log"
    if not args.skip_validate:
        run([sys.executable, str(validate_script)], cwd=project_root, log=validation_log)

    unity_log = None
    if args.run_unity_import:
        unity_log = evidence_dir / f"sync-r2fu-runtime-unity-import-{timestamp}.log"
        run_unity_import(args.unity_editor, project_root / "Unity2Foxglove", unity_log)

    package_manifest = read_json(package_path / "RuntimeSupport" / "runtime-manifest.json")
    summary = {
        "schemaVersion": 1,
        "generatedAtLocal": dt.datetime.now().astimezone().isoformat(),
        "operation": "sync_r2fu_artifact_to_unity2foxglove",
        "unity2foxglove": git_info(project_root),
        "artifact": {
            "path": artifact_info["path"],
            "sha256": artifact_info["sha256"],
            "manifest": artifact_info.get("manifest"),
            "source": (artifact_info.get("manifestData") or {}).get("source"),
        },
        "runtimePackage": {
            "path": str(package_path),
            "name": PACKAGE_NAME,
            "manifest": package_manifest,
            "inventoryPath": str(inventory_path),
        },
        "projectShape": project_shape,
        "validation": {
            "runtimePackageValidator": "SKIPPED" if args.skip_validate else "PASS",
            "runtimePackageValidatorLog": None if args.skip_validate else str(validation_log),
            "unityImport": "PASS" if args.run_unity_import else "NOT_RUN",
            "unityImportLog": None if unity_log is None else str(unity_log),
            "runtimeSmoke": "NOT_RUN",
        },
        "boundaries": [
            "Core SDK remains ROS-free; this sync touches the optional R2FU runtime package.",
            "Unity2Foxglove/Assets/Ros2ForUnity must remain absent for package-mode acceptance.",
            "Unity runtime smoke and ROS graph evidence are separate gates unless explicitly run after this sync.",
        ],
    }
    summary_path = evidence_dir / f"sync-r2fu-runtime-summary-{timestamp}.json"
    write_json(summary_path, summary)
    print(f"[PASS] synced artifact sha256={artifact_info['sha256']}")
    print(f"[PASS] runtime package={package_path}")
    print(f"[PASS] evidence={summary_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
