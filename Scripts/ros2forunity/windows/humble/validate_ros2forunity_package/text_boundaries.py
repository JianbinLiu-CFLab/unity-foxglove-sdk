from __future__ import annotations
from .manifest import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def check_text_boundaries(results: list[CheckResult]) -> None:
    """Check README and notices wording for attribution and non-bundling boundaries."""
    readme = read_text_cached(PACKAGE / "README.md")
    notices = read_text_cached(PACKAGE / "THIRD_PARTY_NOTICES.md")
    sample_readme = read_text_cached(ADAPTER_SAMPLE / "README.md")
    rviz_sample_readme = read_text_cached(RVIZ_SAMPLE / "README.md")
    pointcloud_sample_readme = read_text_cached(RVIZ_POINTCLOUD2_SAMPLE / "README.md")
    markerarray_sample_readme = read_text_cached(RVIZ_MARKERARRAY_SAMPLE / "README.md")
    v1_sample_readme = read_text_cached(RVIZ_V1_SAMPLE / "README.md")
    standard_messages_sample_readme = read_text_cached(
        STANDARD_MESSAGES_SAMPLE / "README.md"
    )
    runtime_notices = read_text_cached(RUNTIME_NOTICES)
    boundary_texts = (
        readme,
        notices,
        sample_readme,
        rviz_sample_readme,
        pointcloud_sample_readme,
        markerarray_sample_readme,
        v1_sample_readme,
        standard_messages_sample_readme,
        runtime_notices,
    )

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
    add(
        results,
        "notices preserve support caveat",
        any_text_contains(boundary_texts, "AWSIM/Autoware")
        and any_text_contains(boundary_texts, "general community"),
        "support caveat",
    )
    add(
        results,
        "notices require future inventory",
        any_text_contains(boundary_texts, "complete transitive inventory"),
        "future binary bundling boundary",
    )
    add(
        results,
        "runtime notices bind artifact and checksum",
        RUNTIME_ARTIFACT in runtime_notices
        and RUNTIME_SHA256 in runtime_notices,
        rel(RUNTIME_NOTICES),
    )
    add(
        results,
        "runtime notices document critical DLL closure",
        "rcl.dll" in runtime_notices
        and "yaml.dll" in runtime_notices
        and "spdlog.dll" in runtime_notices
        and "UnsatisfiedLinkError" in runtime_notices,
        rel(RUNTIME_NOTICES),
    )
    general_public_docs = "\n".join(boundary_texts)
    forbidden_public_tokens = ("Phase 137B", "Phase106B", "Phase110", "phase110", "Phase 108")
    hits = [token for token in forbidden_public_tokens if token in general_public_docs]
    hits.extend(match.group(0) for match in FORBIDDEN_PUBLIC_PHASE_PATTERN.finditer(general_public_docs))
    manifest_text = read_text_cached(MANIFEST)
    hits.extend(token for token in forbidden_public_tokens if token in manifest_text)
    hits.extend(match.group(0) for match in FORBIDDEN_PUBLIC_PHASE_PATTERN.finditer(manifest_text))
    hits = list(dict.fromkeys(hits))
    add(results, "public R2FU docs avoid internal phase names", not hits, ", ".join(hits) if hits else "no phase tokens")



__all__ = [name for name in globals() if not name.startswith("__")]
