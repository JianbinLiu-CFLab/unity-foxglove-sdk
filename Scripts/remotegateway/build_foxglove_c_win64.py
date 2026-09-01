#!/usr/bin/env python3
"""Build the Phase171 foxglove_c remote-access DLL for Windows x64.

The script keeps native build outputs outside Packages by default. Use
    --copy-to-package only after reviewing the produced DLL. The committed package
    manifest remains the trust anchor unless --update-package-manifest
    is explicitly supplied.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CRATE = ROOT / "third-party" / "foxglove-sdk" / "c"
STAGING_RELATIVE = "build/remotegateway/foxglove-c-win64"
STAGING = ROOT / STAGING_RELATIVE
PACKAGE_PLUGIN_RELATIVE = (
    "Packages/dev.unity2foxglove.remotegateway.win64/Runtime/Plugins/Windows/x86_64"
)
PACKAGE_PLUGIN_DIR = (
    ROOT / PACKAGE_PLUGIN_RELATIVE
)
PACKAGE_MANIFEST_NAME = "foxglove-gateway-native-artifact.json"
DEVICE_TOKEN_ENVIRONMENT_VARIABLE = "FOXGLOVE_DEVICE_TOKEN"
APPROVED_ARTIFACTS = ("foxglove.dll", "foxglove.dll.lib")
PDB_ARTIFACT = "foxglove.pdb"
ALLOWED_ARTIFACTS = frozenset((*APPROVED_ARTIFACTS, PDB_ARTIFACT))


def parse_args() -> argparse.Namespace:
    """Parse CLI options for the native gateway build."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--target-dir",
        default=os.environ.get("UNITY2FOXGLOVE_REMOTE_GATEWAY_TARGET_DIR", r"C:\u2fg171target"),
        help="Short Cargo target directory. Keep this short to avoid MSVC include path failures.",
    )
    parser.add_argument(
        "--libclang-path",
        default=os.environ.get("LIBCLANG_PATH"),
        help="Directory containing libclang.dll. Defaults to the repo-local LLVM extraction if present.",
    )
    parser.add_argument(
        "--copy-to-package",
        action="store_true",
        help="Copy approved native artifacts into the optional package plugin folder without replacing its manifest.",
    )
    parser.add_argument(
        "--update-package-manifest",
        action="store_true",
        help="Also replace the package manifest with the generated one (requires --copy-to-package; review and commit it before skip-build acceptance).",
    )
    parser.add_argument(
        "--include-pdb",
        action="store_true",
        help="Also copy foxglove.pdb for local symbol debugging. Off by default because PDBs can expose build-machine paths.",
    )
    return parser.parse_args()


def find_repo_local_libclang() -> str | None:
    """Return the repo-local libclang directory when the extraction exists."""
    candidate = ROOT / "build" / "remotegateway" / "tools" / "llvm-22.1.8-extract" / "bin"
    if (candidate / "libclang.dll").exists():
        return str(candidate)
    return None


def run(command: list[str], *, cwd: Path, env: dict[str, str]) -> None:
    """Run one subprocess command with visible command logging."""
    print("+", " ".join(command), flush=True)
    subprocess.run(command, cwd=str(cwd), env=env, check=True)


def sha256(path: Path) -> str:
    """Compute an uppercase SHA-256 digest for a file."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def build_environment(args: argparse.Namespace) -> dict[str, str]:
    """Build the Cargo/MSVC environment used for a static CRT gateway DLL."""
    env = os.environ.copy()
    env.pop(DEVICE_TOKEN_ENVIRONMENT_VARIABLE, None)
    libclang_path = args.libclang_path or find_repo_local_libclang()
    if libclang_path:
        env["LIBCLANG_PATH"] = libclang_path
        env["PATH"] = libclang_path + os.pathsep + env.get("PATH", "")

    cargo_home = Path.home() / ".cargo" / "bin"
    env["PATH"] = str(cargo_home) + os.pathsep + env.get("PATH", "")
    env["CARGO_TARGET_DIR"] = str(Path(args.target_dir))
    env["AWS_LC_SYS_PREBUILT_NASM"] = "1"
    env["RUSTFLAGS"] = "-C target-feature=+crt-static"
    env["CXXFLAGS_x86_64_pc_windows_msvc"] = "/MT"
    env["CFLAGS_x86_64_pc_windows_msvc"] = "/MT"
    return env


def selected_artifacts(include_pdb: bool) -> tuple[str, ...]:
    """Return the reviewed artifact list for this invocation."""
    return APPROVED_ARTIFACTS + ((PDB_ARTIFACT,) if include_pdb else ())


def write_manifest(target_dir: Path, env: dict[str, str], artifact_names: tuple[str, ...]) -> Path:
    """Write reviewed native artifact metadata into the staging directory."""
    dll = target_dir / "release" / "foxglove.dll"
    if not dll.is_file():
        raise FileNotFoundError(dll)
    artifacts = {}
    for name in artifact_names:
        artifact = target_dir / "release" / name
        if not artifact.is_file():
            continue
        artifacts[name] = {
            "sha256": sha256(artifact),
            "sizeBytes": artifact.stat().st_size,
        }

    manifest = {
        "artifact": "foxglove.dll",
        "platform": "windows-x64",
        "source": "third-party/foxglove-sdk/c",
        "features": "remote-access",
        "rustflags": env["RUSTFLAGS"],
        "cflags": env["CFLAGS_x86_64_pc_windows_msvc"],
        "cxxflags": env["CXXFLAGS_x86_64_pc_windows_msvc"],
        "environment": {
            "AWS_LC_SYS_PREBUILT_NASM": env["AWS_LC_SYS_PREBUILT_NASM"],
            "CARGO_TARGET_DIR": env["CARGO_TARGET_DIR"],
        },
        "sha256": sha256(dll),
        "sizeBytes": dll.stat().st_size,
        "artifacts": artifacts,
    }

    STAGING.mkdir(parents=True, exist_ok=True)
    manifest_path = STAGING / "foxglove-gateway-native-artifact.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest_path


def copy_approved_artifacts(
    target_dir: Path,
    manifest_path: Path,
    artifact_names: tuple[str, ...],
    *,
    copy_manifest: bool = False,
) -> None:
    """Copy approved DLL-side artifacts without silently replacing trust metadata."""
    unapproved = sorted(set(artifact_names) - ALLOWED_ARTIFACTS)
    if unapproved:
        raise ValueError(f"unapproved artifact name(s): {', '.join(unapproved)}")
    if len(set(artifact_names)) != len(artifact_names):
        raise ValueError("artifact selection contains duplicate names")
    PACKAGE_PLUGIN_DIR.mkdir(parents=True, exist_ok=True)
    for stale_name in sorted(ALLOWED_ARTIFACTS - set(artifact_names)):
        stale = PACKAGE_PLUGIN_DIR / stale_name
        if stale.is_file():
            stale.unlink()
    for name in artifact_names:
        source = target_dir / "release" / name
        if source.is_file():
            shutil.copy2(source, PACKAGE_PLUGIN_DIR / name)
    if copy_manifest:
        if manifest_path.name != PACKAGE_MANIFEST_NAME:
            raise ValueError(
                f"manifest must be named {PACKAGE_MANIFEST_NAME!r} before it can be copied"
            )
        if not manifest_path.is_file():
            raise FileNotFoundError(manifest_path)
        shutil.copy2(manifest_path, PACKAGE_PLUGIN_DIR / PACKAGE_MANIFEST_NAME)


def main() -> int:
    """Build the native gateway artifact and optionally copy it into the package."""
    args = parse_args()
    if args.update_package_manifest and not args.copy_to_package:
        raise SystemExit("--update-package-manifest requires --copy-to-package")
    target_dir = Path(args.target_dir)
    env = build_environment(args)
    artifact_names = selected_artifacts(args.include_pdb)

    run(["cargo", "build", "--release", "--features", "remote-access"], cwd=CRATE, env=env)
    manifest_path = write_manifest(target_dir, env, artifact_names)
    print(f"Wrote {manifest_path.relative_to(ROOT)}")

    if args.copy_to_package:
        copy_approved_artifacts(
            target_dir,
            manifest_path,
            artifact_names,
            copy_manifest=args.update_package_manifest,
        )
        print(f"Copied approved artifacts to {PACKAGE_PLUGIN_DIR.relative_to(ROOT)}")
        if args.update_package_manifest:
            print("Updated package manifest explicitly; review and commit it before using --skip-native-build.")
    else:
        print("Package copy skipped; pass --copy-to-package after reviewing artifacts.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
