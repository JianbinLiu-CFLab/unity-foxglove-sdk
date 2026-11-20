"""Run Phase202 Unity package-resolution and compile probes on the runner."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from typing import Iterable

from Scripts.package.compose_unity_manifest import (
    DISTROS,
    PACKAGE_ROOT,
    compose_manifest,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_UNITY = Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe")
ERROR_PATTERN = re.compile(
    r"Failed to resolve packages|Package Manager Error|error CS\d+|Compilation failed|Unable to resolve",
    re.IGNORECASE,
)
PACKAGE_RESOLUTION_PATTERN = re.compile(
    r"Failed to resolve packages|Package Manager Error|Unable to resolve|Package\s+\[[^\]]+\]\s+cannot be found",
    re.IGNORECASE,
)


def _reference(project: Path, package_id: str, package_root: Path = PACKAGE_ROOT) -> str:
    """Return a repository package reference relative to one temporary project."""
    return "file:" + os.path.relpath(package_root / package_id, project / "Packages").replace(os.sep, "/")


def _new_project(root: Path, name: str) -> Path:
    """Create one empty Unity project below the owned evidence directory."""
    project = root / "projects" / name
    (project / "Packages").mkdir(parents=True, exist_ok=True)
    (project / "Assets").mkdir(parents=True, exist_ok=True)
    (project / "ProjectSettings").mkdir(parents=True, exist_ok=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 6000.3.14f1\n", encoding="utf-8"
    )
    (project / "Packages" / "manifest.json").write_text(
        json.dumps({"dependencies": _builtin_dependencies()}, indent=2) + "\n",
        encoding="utf-8",
    )
    return project


def _builtin_dependencies() -> dict[str, str]:
    """Return the Unity built-in modules used by the checked-in demo project."""
    manifest = REPOSITORY_ROOT / "Unity2Foxglove" / "Packages" / "manifest.json"
    document = json.loads(manifest.read_text(encoding="utf-8"))
    dependencies = document.get("dependencies", {})
    return {
        name: version
        for name, version in dependencies.items()
        if isinstance(name, str) and name.startswith("com.unity.modules.")
    }


def _run_unity(unity: Path, project: Path, log_path: Path, timeout_seconds: int) -> dict[str, object]:
    """Run Unity in batch mode and classify package or compiler diagnostics."""
    log_path.parent.mkdir(parents=True, exist_ok=True)
    repository_root = Path(__file__).resolve().parents[2]
    project_argument = os.path.relpath(project, repository_root).replace(os.sep, "/")
    log_argument = os.path.relpath(log_path, repository_root).replace(os.sep, "/")
    command = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        project_argument,
        "-logFile",
        log_argument,
    ]
    timed_out = False
    try:
        completed = subprocess.run(command, cwd=Path(__file__).resolve().parents[2], timeout=timeout_seconds, check=False)
        exit_code = completed.returncode
    except subprocess.TimeoutExpired:
        exit_code = 124
        timed_out = True
    text = log_path.read_text(encoding="utf-8", errors="replace") if log_path.is_file() else ""
    errors = [line for line in text.splitlines() if ERROR_PATTERN.search(line)]
    verdict = "PASS" if exit_code == 0 and not errors and not timed_out else "FAIL"
    package_resolution_failure = bool(PACKAGE_RESOLUTION_PATTERN.search(text)) and not timed_out
    if verdict == "PASS":
        failure_kind = "PASS"
    elif package_resolution_failure:
        failure_kind = "EXPECTED_PACKAGE_RESOLUTION_FAILURE"
    elif timed_out:
        failure_kind = "INFRASTRUCTURE_FAILURE"
    elif any(re.search(r"error CS\d+|Compilation failed", line, re.IGNORECASE) for line in errors):
        failure_kind = "UNEXPECTED_COMPILE_FAILURE"
    else:
        failure_kind = "INFRASTRUCTURE_FAILURE"
    return {
        "verdict": verdict,
        "failureKind": failure_kind,
        "exitCode": exit_code,
        "timedOut": timed_out,
        "errors": errors,
        "log": str(log_path),
    }


def _write_manifest(project: Path, dependencies: dict[str, str]) -> None:
    """Write a strict manifest for a direct package-manager probe."""
    combined = _builtin_dependencies()
    combined.update(dependencies)
    (project / "Packages" / "manifest.json").write_text(
        json.dumps({"dependencies": combined}, indent=2) + "\n", encoding="utf-8"
    )


def _resolution_cases(root: Path) -> Iterable[tuple[str, Path, bool]]:
    """Yield the two fail-closed package-manager probes."""
    missing_interface = _new_project(root, "missing-interface")
    distro = "jazzy"
    _write_manifest(
        missing_interface,
        {
            "com.unity.modules.jsonserialize": "1.0.0",
            "dev.unity2foxglove.sdk": _reference(missing_interface, "dev.unity2foxglove.sdk"),
            "dev.unity2foxglove.ros2forunity": _reference(missing_interface, "dev.unity2foxglove.ros2forunity"),
            f"dev.unity2foxglove.ros2forunity.runtime.{distro}.win64": _reference(
                missing_interface, f"dev.unity2foxglove.ros2forunity.runtime.{distro}.win64"
            ),
            f"dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.{distro}.win64": _reference(
                missing_interface, f"dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.{distro}.win64"
            ),
        },
    )
    yield "missing-interface", missing_interface, True

    missing_file = _new_project(root, "missing-file-package")
    _write_manifest(
        missing_file,
        {
            "com.unity.modules.jsonserialize": "1.0.0",
            "dev.unity2foxglove.phase202.missing": "file:../../missing-package",
        },
    )
    yield "missing-file-package", missing_file, True


def _matrix_cases(root: Path) -> Iterable[tuple[str, Path, bool]]:
    """Yield all README profiles and distro combinations."""
    for profile in ("core", "bridge", "gateway"):
        project = _new_project(root, profile)
        compose_manifest(project, profile, package_root=PACKAGE_ROOT)
        yield profile, project, False
    for profile in ("native", "native-custom"):
        for distro in DISTROS:
            name = f"{profile}-{distro}"
            project = _new_project(root, name)
            compose_manifest(project, profile, distro, package_root=PACKAGE_ROOT)
            yield name, project, False


def run(
    *,
    mode: str,
    unity: Path,
    output: Path,
    timeout_seconds: int,
) -> int:
    """Run selected probes and write a machine-readable summary."""
    output = output.resolve()
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    cases: list[tuple[str, Path, bool]] = []
    if mode in {"resolution", "all"}:
        cases.extend(_resolution_cases(output))
    if mode in {"matrix", "all"}:
        cases.extend(_matrix_cases(output))
    results = []
    for name, project, expected_failure in cases:
        result = _run_unity(unity, project, output / "logs" / f"{name}.log", timeout_seconds)
        result.update({"case": name, "project": str(project), "expectedFailure": expected_failure})
        result["expectationMet"] = (
            result["failureKind"] == "EXPECTED_PACKAGE_RESOLUTION_FAILURE"
            if expected_failure
            else result["verdict"] == "PASS"
        )
        results.append(result)
    summary = {"mode": mode, "unity": str(unity), "cases": results}
    (output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0 if all(result["expectationMet"] for result in results) else 1


def main(argv: list[str] | None = None) -> int:
    """Parse runner probe arguments and execute the requested matrix."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("resolution", "matrix", "all"), default="all")
    parser.add_argument("--unity", type=Path, default=DEFAULT_UNITY)
    parser.add_argument("--output", type=Path, default=Path("build/phase202/unity"))
    parser.add_argument("--timeout-seconds", type=int, default=3600)
    args = parser.parse_args(argv)
    return run(mode=args.mode, unity=args.unity, output=args.output, timeout_seconds=args.timeout_seconds)


if __name__ == "__main__":
    raise SystemExit(main())
