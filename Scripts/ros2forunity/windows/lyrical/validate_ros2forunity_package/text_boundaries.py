from __future__ import annotations

from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
del _Phase192Path

from .core import *

def check_text_boundaries(results: list[CheckResult]) -> None:
    """Check README and notices wording for attribution and non-bundling boundaries."""
    readme = (PACKAGE / "README.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "README.md").exists() else ""
    notices = (
        (PACKAGE / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8", errors="replace")
        if (PACKAGE / "THIRD_PARTY_NOTICES.md").exists()
        else ""
    )
    sample_readme = (
        (ADAPTER_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (ADAPTER_SAMPLE / "README.md").exists()
        else ""
    )
    rviz_sample_readme = (
        (RVIZ_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_SAMPLE / "README.md").exists()
        else ""
    )
    pointcloud_sample_readme = (
        (RVIZ_POINTCLOUD2_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_POINTCLOUD2_SAMPLE / "README.md").exists()
        else ""
    )
    markerarray_sample_readme = (
        (RVIZ_MARKERARRAY_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_MARKERARRAY_SAMPLE / "README.md").exists()
        else ""
    )
    v1_sample_readme = (
        (RVIZ_V1_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_V1_SAMPLE / "README.md").exists()
        else ""
    )
    runtime_notices = RUNTIME_NOTICES.read_text(encoding="utf-8", errors="replace") if RUNTIME_NOTICES.exists() else ""
    runtime_inventory = RUNTIME_INVENTORY.read_text(encoding="utf-8", errors="replace") if RUNTIME_INVENTORY.exists() else ""
    combined = readme + "\n" + notices + "\n" + sample_readme + "\n" + rviz_sample_readme + "\n" + pointcloud_sample_readme + "\n" + markerarray_sample_readme + "\n" + v1_sample_readme + "\n" + runtime_notices + "\n" + runtime_inventory

    add(results, "README says runtime not bundled", "runtime binaries are not bundled" in readme.lower(), rel(PACKAGE / "README.md"))
    add(results, "README says external adapter sample", "ros2 for unity external adapter" in readme.lower(), rel(PACKAGE / "README.md"))
    add(results, "README explains Windows Firewall is the root cause, not WSL2 NAT", "windows firewall" in readme.lower() and "fast dds" in readme.lower() and "wsl2" in readme.lower(), rel(PACKAGE / "README.md"))
    add(
        results,
        "README defers standard visualization",
        "standard ros2 visualization" in readme.lower(),
        rel(PACKAGE / "README.md"),
    )
    add(results, "notices attribute R2FU", "RobotecAI ROS2 For Unity" in notices and "Apache-2.0" in notices, rel(PACKAGE / "THIRD_PARTY_NOTICES.md"))
    add(results, "notices attribute ros2cs", "ros2cs" in notices, rel(PACKAGE / "THIRD_PARTY_NOTICES.md"))
    add(results, "notices preserve support caveat", "AWSIM/Autoware" in combined and "general community" in combined, "support caveat")
    add(results, "notices require future inventory", "complete transitive inventory" in combined, "future binary bundling boundary")
    add(
        results,
        "runtime notices name artifact candidate",
        "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip" in runtime_notices
        and "Lyrical" in runtime_notices,
        rel(RUNTIME_NOTICES),
    )
    add(
        results,
        "runtime notices document critical DLL closure",
        "rcl.dll" in runtime_notices
        and "yaml.dll" in runtime_notices
        and "spdlog.dll" in runtime_notices
        and "fmt.dll" in runtime_notices
        and "critical runtime closure" in runtime_notices.lower(),
        rel(RUNTIME_NOTICES),
    )
    general_public_docs = readme + "\n" + notices + "\n" + sample_readme + "\n" + runtime_notices + "\n" + runtime_inventory
    forbidden_public_tokens = ("Phase 137B", "Phase106B", "Phase110", "phase110", "Phase 108")
    hits = [token for token in forbidden_public_tokens if token in general_public_docs]
    hits.extend(match.group(0) for match in FORBIDDEN_PUBLIC_PHASE_PATTERN.finditer(general_public_docs))
    manifest_text = MANIFEST.read_text(encoding="utf-8", errors="replace") if MANIFEST.exists() else ""
    hits.extend(token for token in forbidden_public_tokens if token in manifest_text)
    hits.extend(match.group(0) for match in FORBIDDEN_PUBLIC_PHASE_PATTERN.finditer(manifest_text))
    add(results, "public R2FU docs avoid internal phase names", not hits, ", ".join(hits) if hits else "no phase tokens")

__all__ = [name for name in globals() if not name.startswith("__")]
