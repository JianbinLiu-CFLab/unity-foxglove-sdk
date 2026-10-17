"""Package manifest, dependency, and boundary checks."""

from __future__ import annotations

from .foundation import *
def _package_matrix_specs() -> tuple[tuple[str, str, Path, str, dict[str, str]], ...]:
    """Resolve package roots from patchable module globals."""
    return (
        ("sdk", "dev.unity2foxglove.sdk", PACKAGE, "1.9.6", {
            "com.unity.nuget.newtonsoft-json": "3.2.1",
            "com.unity.burst": "1.8.18",
            "com.unity.collections": "2.5.5",
            "com.unity.mathematics": "1.3.2",
        }),
        ("r2fu", "dev.unity2foxglove.ros2forunity", ROS2_FOR_UNITY_PACKAGE, "0.1.0-preview.1", {
            "dev.unity2foxglove.sdk": "1.9.6",
        }),
        ("bridge", "dev.unity2foxglove.ros2bridge", ROS2_BRIDGE_PACKAGE, "0.1.0-preview.1", {
            "dev.unity2foxglove.sdk": "1.9.6",
        }),
        ("remote_gateway", "dev.unity2foxglove.remotegateway.win64", REMOTE_GATEWAY_PACKAGE, "0.1.0-preview.1", {
            "dev.unity2foxglove.sdk": "1.9.6",
        }),
    )


def check_package_matrix(results: list[CheckResult]) -> None:
    """Authenticate all four package identities, versions, and exact dependencies."""
    for key, expected_name, package_root, expected_version, expected_dependencies in _package_matrix_specs():
        manifest = package_root / "package.json"
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
        except Exception as exc:
            add(results, f"package matrix {key} manifest", False, f"{rel(manifest)}: {exc}")
            continue
        if not isinstance(data, dict):
            add(
                results,
                f"package matrix {key} manifest",
                False,
                f"{rel(manifest)}: JSON root is {type(data).__name__}, expected object",
            )
            continue
        add(results, f"package matrix {key} manifest", True, rel(manifest))
        actual_name = data.get("name")
        add(
            results,
            f"package matrix {key} name",
            actual_name == expected_name,
            f"expected {expected_name!r}, got {actual_name!r}",
        )
        actual_version = data.get("version")
        add(
            results,
            f"package matrix {key} version",
            actual_version == expected_version,
            f"expected {expected_version!r}, got {actual_version!r}",
        )
        actual_dependencies = data.get("dependencies")
        add(
            results,
            f"package matrix {key} dependencies",
            actual_dependencies == expected_dependencies,
            f"expected {expected_dependencies!r}, got {actual_dependencies!r}",
        )


def check_consumer_local_bindings(results: list[CheckResult]) -> None:
    """Authenticate every local UPM binding in the real Unity consumer manifest."""
    manifest = ROOT / "Unity2Foxglove" / "Packages" / "manifest.json"
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, "consumer local package bindings", False, f"{rel(manifest)}: {exc}")
        return
    dependencies = data.get("dependencies") if isinstance(data, dict) else None
    if not isinstance(dependencies, dict):
        add(results, "consumer local package bindings", False, "dependencies is not an object")
        return
    consumer_packages = manifest.parent
    package_root = ROOT / "Packages"
    # The consumer selects one ROS2 distro at a time. Validate the selected
    # distro bindings rather than assuming the developer's local distro.
    dependencies_to_check = [
        name for name in CONSUMER_LOCAL_DEPENDENCIES
        if not any(name.startswith(prefix) for prefix in CONSUMER_LOCAL_DEPENDENCY_PREFIXES)
    ]
    for prefix in CONSUMER_LOCAL_DEPENDENCY_PREFIXES:
        selected = sorted(name for name in dependencies if name.startswith(prefix))
        if selected:
            dependencies_to_check.append(selected[0])
    failures: list[str] = []
    for package_name in dependencies_to_check:
        value = dependencies.get(package_name)
        if not isinstance(value, str) or not value.startswith("file:"):
            failures.append(f"{package_name}: missing file: binding")
            continue
        target = (consumer_packages / value[5:]).resolve()
        canonical = (package_root / package_name).resolve()
        if target != canonical:
            failures.append(f"{package_name}: target {target} is not canonical {canonical}")
            continue
        package_manifest = target / "package.json"
        try:
            package_data = json.loads(package_manifest.read_text(encoding="utf-8"))
        except Exception as exc:
            failures.append(f"{package_name}: package.json unreadable: {exc}")
            continue
        if not isinstance(package_data, dict) or package_data.get("name") != package_name:
            failures.append(f"{package_name}: package identity mismatch")
    add(
        results,
        "consumer local package bindings",
        not failures,
        "; ".join(failures) if failures else f"{len(dependencies_to_check)} canonical file bindings authenticated",
    )


def check_ros2_bridge_package(results: list[CheckResult]) -> None:
    """Validate the extracted optional Bridge package and its one sample."""
    manifest = ROS2_BRIDGE_PACKAGE / "package.json"
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        add(
            results,
            "ROS2 Bridge package.json parses",
            False,
            f"{rel(manifest)}: {exc}",
        )
        return

    dependencies = data.get("dependencies")
    samples = data.get("samples")
    sample = samples[0] if isinstance(samples, list) and len(samples) == 1 else {}
    add(
        results,
        "ROS2 Bridge package identity",
        data.get("name") == "dev.unity2foxglove.ros2bridge"
        and data.get("version") == "0.1.0-preview.1",
        f"name={data.get('name')}, version={data.get('version')}",
    )
    add(
        results,
        "ROS2 Bridge dependency boundary",
        isinstance(dependencies, dict)
        and set(dependencies) == {"dev.unity2foxglove.sdk"},
        f"dependencies={sorted(dependencies) if isinstance(dependencies, dict) else 'n/a'}",
    )
    add(
        results,
        "ROS2 Bridge sample declaration",
        sample.get("displayName") == "ROS2 Bridge Sample"
        and sample.get("path") == "Samples~/Ros2BridgeSample"
        and (ROS2_BRIDGE_PACKAGE / "Samples~/Ros2BridgeSample").is_dir(),
        f"sample={sample}",
    )

    required = (
        ("Runtime/Unity2Foxglove.Ros2Bridge.asmdef", True),
        ("Editor/Unity2Foxglove.Ros2Bridge.Editor.asmdef", True),
        ("Tests/Unity2Foxglove.Ros2Bridge.Tests.asmdef", True),
        ("Documentation~/en/PHASE186_BREAKING_UPGRADE.md", False),
        ("Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity", True),
        ("Samples~/Ros2BridgeSample/Scripts/Unity2Foxglove.Ros2Bridge.Sample.asmdef", True),
        ("Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleDuplex.cs", True),
        ("Samples~/Ros2BridgeSample/Editor/Unity2Foxglove.Ros2Bridge.Sample.Editor.asmdef", True),
        ("Samples~/Ros2BridgeSample/Editor/Ros2BridgeSampleSceneBuilder.cs", True),
        ("Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll", True),
    )
    missing = [
        path
        for path, requires_meta in required
        if not (ROS2_BRIDGE_PACKAGE / path).is_file()
        or (
            requires_meta
            and not Path(str(ROS2_BRIDGE_PACKAGE / path) + ".meta").is_file()
        )
    ]
    add(
        results,
        "ROS2 Bridge required assets and metas",
        not missing,
        "complete" if not missing else f"missing={missing}",
    )

    sample_sync = ROOT / "Scripts" / "samples" / "sync_ros2_bridge_sample.py"
    add(
        results,
        "ROS2 Bridge dedicated sample sync tool",
        sample_sync.is_file(),
        rel(sample_sync),
    )

    cross_references = []
    for asmdef in ROS2_BRIDGE_PACKAGE.rglob("*.asmdef"):
        if "Unity2Foxglove.Ros2ForUnity" in asmdef.read_text(
            encoding="utf-8"
        ):
            cross_references.append(rel(asmdef))
    add(
        results,
        "ROS2 Bridge has no R2FU assembly reference",
        not cross_references,
        "none" if not cross_references else f"offenders={cross_references}",
    )


def check_package_identity(results: list[CheckResult], data: dict) -> None:
    """Validate package identity fields and sample declarations."""
    expected = {
        "name": "dev.unity2foxglove.sdk",
        "displayName": "Unity2Foxglove SDK",
        "license": "Apache-2.0",
    }
    for key, value in expected.items():
        actual = data.get(key)
        add(results, f"package {key}", actual == value, f"expected {value!r}, got {actual!r}")

    version = data.get("version")
    add(results, "package version", isinstance(version, str) and VERSION_RE.match(version) is not None, f"version={version!r}")

    samples = data.get("samples")
    add(
        results,
        "package samples list",
        isinstance(samples, list) and len(samples) == EXPECTED_SAMPLE_COUNT,
        f"count={len(samples) if isinstance(samples, list) else 'n/a'}",
    )
    if not isinstance(samples, list):
        return

    for display_name, sample_path in EXPECTED_SAMPLES.items():
        match = next((s for s in samples if s.get("displayName") == display_name), None)
        add(
            results,
            f"sample declared: {display_name}",
            match is not None,
            "declared" if match is not None else "missing from package.json samples",
        )
        if match is None:
            continue
        actual_path = match.get("path")
        add(results, f"sample path: {display_name}", actual_path == sample_path, f"expected {sample_path!r}, got {actual_path!r}")
        add(results, f"sample path exists: {display_name}", (PACKAGE / actual_path).exists(), rel(PACKAGE / str(actual_path)))


def check_dependent_package_versions(results: list[CheckResult], sdk_data: dict) -> None:
    """Ensure optional repository packages depend on the current SDK package version."""
    sdk_version = sdk_data.get("version")
    if not isinstance(sdk_version, str) or VERSION_RE.match(sdk_version) is None:
        add(results, "dependent package SDK version pins", False, f"invalid SDK version={sdk_version!r}")
        return

    gateway_manifest = REMOTE_GATEWAY_PACKAGE / "package.json"
    if not gateway_manifest.exists():
        add(results, "dependent package SDK version pins", True, "remote gateway package not present")
        return

    try:
        gateway_data = json.loads(gateway_manifest.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, "dependent package SDK version pins", False, f"{rel(gateway_manifest)}: {exc}")
        return

    dependency = (
        gateway_data.get("dependencies", {})
        .get("dev.unity2foxglove.sdk")
    )
    add(
        results,
        "dependent package SDK version pins",
        dependency == sdk_version,
        f"remote gateway depends on {dependency!r}, SDK version is {sdk_version!r}",
    )


def check_optional_package_boundaries(results: list[CheckResult]) -> None:
    """Validate optional package release boundaries that public package checks can see."""
    notice_path = REMOTE_GATEWAY_PACKAGE / "THIRD_PARTY_NOTICES.md"
    if notice_path.exists():
        notice = notice_path.read_text(encoding="utf-8", errors="replace")
        add(
            results,
            "remote gateway notice publish sentinel",
            "Before publishing this package" not in notice,
            rel(notice_path),
        )

    runtime_manifests = [path / "package.json" for path in ROS2_RUNTIME_PACKAGES if (path / "package.json").exists()]
    runtime_names: list[str] = []
    runtime_data: list[tuple[Path, dict]] = []
    for manifest in runtime_manifests:
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
        except Exception as exc:
            add(results, "ROS2 runtime package conflict metadata", False, f"{rel(manifest)}: {exc}")
            return
        name = data.get("name")
        if isinstance(name, str):
            runtime_names.append(name)
        runtime_data.append((manifest, data))

    offenders: list[str] = []
    for manifest, data in runtime_data:
        name = data.get("name")
        expected = sorted(item for item in runtime_names if item != name)
        actual = data.get("unity2foxgloveConflicts")
        actual_conflicts = sorted(actual) if isinstance(actual, list) else []
        if actual_conflicts != expected:
            offenders.append(f"{rel(manifest)} expected conflicts {expected!r}")

    add(
        results,
        "ROS2 runtime package conflict metadata",
        not offenders,
        "; ".join(offenders) if offenders else "all runtime packages declare sibling conflicts",
    )

    addon_manifests = [path / "package.json" for path in ROS2_TYPESUPPORT_PACKAGES if (path / "package.json").exists()]
    addon_names: list[str] = []
    addon_data: list[tuple[Path, dict]] = []
    for manifest in addon_manifests:
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
        except Exception as exc:
            add(results, "ROS2 typesupport add-on conflict metadata", False, f"{rel(manifest)}: {exc}")
            addon_data = []
            break
        name = data.get("name")
        if isinstance(name, str):
            addon_names.append(name)
        addon_data.append((manifest, data))
    addon_offenders: list[str] = []
    for manifest, data in addon_data:
        name = data.get("name")
        expected = sorted(item for item in addon_names if item != name)
        actual = data.get("unity2foxgloveConflicts")
        actual_conflicts = sorted(actual) if isinstance(actual, list) else []
        if actual_conflicts != expected:
            addon_offenders.append(f"{rel(manifest)} expected conflicts {expected!r}")
    if addon_manifests:
        add(
            results,
            "ROS2 typesupport add-on conflict metadata",
            not addon_offenders and len(addon_data) == len(addon_manifests),
            "; ".join(addon_offenders) if addon_offenders else "all typesupport add-ons declare sibling conflicts",
        )

    demo_link = UNITY_DEMO_ASSETS / "link.xml"
    add(
        results,
        "demo project avoids duplicate package link.xml",
        not demo_link.exists(),
        rel(demo_link) if demo_link.exists() else "package Runtime/link.xml is authoritative",
    )



__all__ = sorted(name for name in globals() if not name.startswith("__"))
