from __future__ import annotations
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
del _Phase192Path

from .package_metadata import *

def check_required_files(results: list[CheckResult]) -> None:
    """Validate files required for the public optional package boundary."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "THIRD_PARTY_NOTICES.md",
        PACKAGE / "Upstream" / "LICENSE.AL2",
        MANIFEST,
        RUNTIME_INVENTORY,
        RUNTIME_NOTICES,
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_manifest(results: list[CheckResult]) -> None:
    """Validate the R2FU adoption manifest."""
    if not MANIFEST.exists():
        add(results, "manifest exists", False, rel(MANIFEST))
        return

    data = load_json(MANIFEST, results, "manifest parses")
    if data is None:
        return

    expected = {
        "schemaVersion": 2,
        "upstreamName": "RobotecAI ROS2 For Unity",
        "upstreamRepository": "https://github.com/RobotecAI/ros2-for-unity",
        "upstreamLicense": "Apache-2.0",
        "upstreamLicenseFile": "Upstream/LICENSE.AL2",
        "bundleStatus": "not_bundled",
        "adapterStatus": "external_assets_sample",
        "distributionModel": "one_repo_multi_package_release_artifacts",
        "distributionPolicy": "runtime_artifacts_live_in_separate_runtime_packages",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "knownRuntimeRmw": "rmw_fastrtps_cpp",
    }
    for key, value in expected.items():
        add(results, f"manifest {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")
    known_distros = str(data.get("knownRuntimeRosDistro", ""))
    add(
        results,
        "manifest known runtime distros include supported candidates",
        {"humble", "jazzy", "lyrical"}.issubset({part.strip() for part in known_distros.split(",")}),
        f"knownRuntimeRosDistro={known_distros!r}",
    )

    current = data.get("currentRecommendedRuntime", {})
    add(
        results,
        "manifest current runtime object",
        isinstance(current, dict),
        f"currentRecommendedRuntime={current!r}",
    )
    if isinstance(current, dict):
        current_expected = {
            "packageName": "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
            "runtimeId": "r2fu-jazzy-win64",
            "rosDistro": "jazzy",
            "platform": "win64",
            "supportLevel": "Recommended",
            "distributionLevel": "BundleCandidate",
            "artifact": "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip",
            "inventoryFile": "Compliance/r2fu-jazzy-win64-runtime-inventory.json",
            "runtimeNoticesFile": "Compliance/r2fu-jazzy-win64-runtime-notices.md",
        }
        for key, value in current_expected.items():
            add(
                results,
                f"manifest current runtime {key}",
                current.get(key) == value,
                f"expected {value!r}, got {current.get(key)!r}",
            )
        artifact_sha = current.get("artifactSha256")
        artifact_size = current.get("artifactSize")
        inventory_file_count = current.get("inventoryFileCount")
        add(
            results,
            "manifest current runtime artifactSha256",
            isinstance(artifact_sha, str) and len(artifact_sha) == 64 and all(ch in "0123456789abcdef" for ch in artifact_sha),
            f"artifactSha256={artifact_sha!r}",
        )
        add(
            results,
            "manifest current runtime artifactSize",
            isinstance(artifact_size, int) and artifact_size > 0,
            f"artifactSize={artifact_size!r}",
        )
        add(
            results,
            "manifest current runtime inventoryFileCount",
            isinstance(inventory_file_count, int) and inventory_file_count >= 1000,
            f"inventoryFileCount={inventory_file_count!r}",
        )
        caveats = " ".join(str(item) for item in current.get("knownCaveats", []))
        add(
            results,
            "manifest current runtime caveats",
            "Windows Firewall" in caveats and "DDS" in caveats,
            caveats,
        )
        critical_files = current.get("criticalRuntimeFiles", [])
        add(
            results,
            "manifest current runtime critical files",
            isinstance(critical_files, list)
            and {"rcl.dll", "yaml.dll", "spdlog.dll", "fmt.dll"}.issubset(set(critical_files)),
            f"criticalRuntimeFiles={critical_files!r}",
        )

    legacy = data.get("legacyRuntime", {})
    add(results, "manifest legacy runtime object", isinstance(legacy, dict), f"legacyRuntime={legacy!r}")
    release_url = str(legacy.get("releaseAssetUrl", "")) if isinstance(legacy, dict) else ""
    add(results, "manifest legacy release asset URL", "Ros2ForUnity_humble_standalone_windows11.zip" in release_url, release_url)

    evidence = str(legacy.get("evidence", "")) if isinstance(legacy, dict) else ""
    add(
        results,
        "manifest legacy evidence",
        "WINDOWS_ROS2_GREEN" in evidence and "WSL_ROS2_DISCOVERY_BLOCKED" in evidence,
        evidence,
    )

    support = str(data.get("upstreamSupportStatus", ""))
    add(
        results,
        "manifest upstream support caveat",
        "AWSIM/Autoware" in support and "general community" in support,
        support,
    )

    packages = data.get("plannedRuntimePackages", [])
    add(
        results,
        "manifest planned runtime packages contain only future candidates",
        isinstance(packages, list)
        and "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64" not in packages
        and "dev.unity2foxglove.ros2forunity.runtime.humble.win64" not in packages
        and "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64" not in packages
        and "dev.unity2foxglove.ros2forunity.runtime.lyrical.ubuntu2604.x64" in packages,
        f"plannedRuntimePackages={packages!r}",
    )
    composition = data.get("packageComposition", {})
    add(
        results,
        "manifest package composition",
        isinstance(composition, dict)
        and "adapterAlone" in composition
        and "runtimeAlone" in composition
        and "adapterPlusRuntime" in composition,
        f"packageComposition={composition!r}",
    )
    add(results, "manifest modifications empty", data.get("modifications") == [], f"modifications={data.get('modifications')!r}")


__all__ = [name for name in globals() if not name.startswith("__")]
