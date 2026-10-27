def reset_package_dir(package: Path) -> None:
    """Delete and recreate only the expected generated runtime package directory."""
    package = validate_package_target(package)
    if package.exists():
        last_error: Exception | None = None
        for _ in range(5):
            try:
                rmtree_with_writable_retry(package)
                break
            except OSError as exc:
                last_error = exc
                time.sleep(0.25)
        else:
            remove_tree_manually(package)
            if package.exists():
                raise last_error if last_error is not None else OSError(f"Could not remove {package}")
    package.mkdir(parents=True)


def snapshot_package_dir(package: Path) -> Path | None:
    """Copy the existing package to an ignored rollback root before regeneration."""
    if not package.exists():
        return None

    rollback_root = ROOT / "build" / "r2fu-runtime-package-rollback"
    rollback_root.mkdir(parents=True, exist_ok=True)
    snapshot = rollback_root / f"{package.name}-{os.getpid()}-{time.time_ns()}"
    shutil.copytree(windows_long_path(package), windows_long_path(snapshot), copy_function=shutil.copy2)
    return snapshot


def restore_package_dir(package: Path, snapshot: Path | None) -> None:
    """Restore or remove the generated package after a failed regeneration."""
    if package.exists():
        rmtree_with_writable_retry(package)

    if snapshot is None:
        return

    package.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(windows_long_path(snapshot), windows_long_path(package), copy_function=shutil.copy2)


def remove_package_snapshot(snapshot: Path | None) -> None:
    """Delete a temporary package rollback snapshot."""
    if snapshot is None or not snapshot.exists():
        return
    rmtree_with_writable_retry(snapshot)


def rmtree_with_writable_retry(path: Path) -> None:
    """Remove a tree, retrying read-only paths across Python shutil APIs."""
    raw_path = windows_long_path(path)
    if "onexc" in inspect.signature(shutil.rmtree).parameters:
        shutil.rmtree(raw_path, onexc=make_writable_onexc)
    else:
        shutil.rmtree(raw_path, onerror=make_writable_onerror)


def make_writable_onerror(function, path: str, exc_info) -> None:
    """Clear a read-only bit and retry a failed removal operation."""

    os.chmod(path, os.stat(path).st_mode | 0o200)
    function(path)


def make_writable_onexc(function, path: str, exc: BaseException) -> None:
    """Clear a read-only bit and retry a failed removal operation."""

    os.chmod(path, os.stat(path).st_mode | 0o200)
    function(path)


def windows_long_path(path: Path) -> str:
    """Return a Windows extended-length path for deletion-heavy filesystem work."""

    resolved = str(path.resolve())
    if os.name != "nt" or resolved.startswith("\\\\?\\"):
        return resolved
    return "\\\\?\\" + resolved


def path_exists(path: Path) -> bool:
    """Return whether a path exists, including long Windows paths."""
    return os.path.exists(windows_long_path(path))


def remove_tree_manually(root: Path) -> None:
    """Fallback removal for sync folders where rmtree can leave late-arriving files."""

    if not root.exists():
        return
    for _ in range(5):
        for path in sorted(root.rglob("*"), key=lambda item: len(item.parts), reverse=True):
            try:
                raw_path = windows_long_path(path)
                os.chmod(raw_path, os.stat(raw_path).st_mode | 0o200)
                if path.is_dir():
                    os.rmdir(raw_path)
                else:
                    os.unlink(raw_path)
            except (FileNotFoundError, OSError):
                continue
        try:
            os.rmdir(windows_long_path(root))
            return
        except FileNotFoundError:
            return
        except OSError:
            time.sleep(0.25)


def write_text(path: Path, content: str) -> None:
    """Write UTF-8 text with a trailing newline."""
    Path(windows_long_path(path.parent)).mkdir(parents=True, exist_ok=True)
    with open(windows_long_path(path), "w", encoding="utf-8", newline="\n") as stream:
        stream.write(content.rstrip() + "\n")


def write_json(path: Path, data: dict[str, object]) -> None:
    """Write JSON with stable formatting."""
    write_text(path, json.dumps(data, indent=2, ensure_ascii=False))


def sha512_file(path: Path) -> str:
    """Return the SHA-512 hex digest for a file."""
    digest = hashlib.sha512()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def patch_deps_json_sha512(package: Path) -> None:
    """Populate informational deps.json sha512 fields for packaged DLLs."""
    plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins"
    inventory_path = package / "RuntimeSupport" / "r2fu-humble-win64-runtime-inventory.json"
    inventory = json.loads(inventory_path.read_text(encoding="utf-8")) if inventory_path.exists() else None
    inventory_by_path = {
        str(item.get("path", "")): item
        for item in (inventory or {}).get("files", [])
        if isinstance(item, dict)
    }

    inventory_changed = False
    for deps_path in sorted(plugin_root.glob("*.deps.json")):
        data = json.loads(deps_path.read_text(encoding="utf-8"))
        changed = False
        for library_name, metadata in data.get("libraries", {}).items():
            if not isinstance(metadata, dict):
                continue

            dll_path = plugin_root / (library_name.split("/", 1)[0] + ".dll")
            if not dll_path.exists():
                continue

            digest = sha512_file(dll_path)
            if metadata.get("sha512") != digest:
                metadata["sha512"] = digest
                changed = True

        if not changed:
            continue

        write_json(deps_path, data)
        inventory_item = inventory_by_path.get("Ros2ForUnity/Plugins/" + deps_path.name)
        if inventory_item is not None:
            inventory_item["sha256"] = sha256_file(deps_path)
            inventory_item["size"] = deps_path.stat().st_size
            inventory_changed = True

    if inventory_changed and inventory_path.exists():
        write_json(inventory_path, inventory)


def runtime_asmdef() -> dict[str, object]:
    """Return the runtime assembly definition used by the packaged R2FU copy."""
    return {
        "name": "Unity2Foxglove.Ros2ForUnity.Runtime",
        "rootNamespace": "",
        "references": [],
        "includePlatforms": ["Editor", "WindowsStandalone64"],
        "excludePlatforms": [],
        "allowUnsafeCode": False,
        "overrideReferences": False,
        "precompiledReferences": [],
        "autoReferenced": True,
        "versionDefines": [],
        "defineConstraints": ["UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN"],
        "noEngineReferences": False,
    }


def collect_local_patch_overlays(package: Path) -> dict[str, str]:
    """Capture committed local patches before regenerating from the upstream artifact."""
    scripts = package / "Runtime" / "Ros2ForUnity" / "Scripts"
    if not scripts.exists():
        return {}

    overlays: dict[str, str] = {}
    for path in scripts.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        relative = path.relative_to(package).as_posix()
        if LOCAL_PATCH_MARKER in text or relative in LOCAL_PATCH_OVERLAY_FILES:
            overlays[relative] = text
    return overlays


def collect_meta_overlays(package: Path) -> dict[str, bytes]:
    """Capture existing Unity metadata so regeneration does not churn GUIDs."""
    if not package.exists():
        return {}

    overlays: dict[str, bytes] = {}
    for path in package.rglob("*.meta"):
        with open(windows_long_path(path), "rb") as stream:
            overlays[path.relative_to(package).as_posix()] = stream.read()
    return overlays


def apply_local_patch_overlays(package: Path, overlays: dict[str, str]) -> None:
    """Replay local lifecycle/time/package-path patches onto the regenerated runtime."""
    for relative, text in overlays.items():
        target = package / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        write_text(target, text)


def collect_supplemental_runtime_files(package: Path) -> dict[str, bytes]:
    """Capture legacy ament-index files that may be absent from a refreshed archive."""
    root = package.joinpath(*SUPPLEMENTAL_RUNTIME_RELATIVE.split("/"))
    if not path_exists(root):
        return {}

    overlays: dict[str, bytes] = {}
    for path in root.rglob("*"):
        if path.is_file():
            with open(windows_long_path(path), "rb") as stream:
                overlays[path.relative_to(package).as_posix()] = stream.read()
    return overlays


def apply_supplemental_runtime_files(package: Path, overlays: dict[str, bytes]) -> None:
    """Restore only missing legacy ament-index files after archive extraction."""
    for relative, data in overlays.items():
        target = package / Path(*relative.split("/"))
        if path_exists(target):
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        with open(windows_long_path(target), "wb") as stream:
            stream.write(data)


def apply_meta_overlays(package: Path, overlays: dict[str, bytes]) -> None:
    """Replay metadata only when the corresponding generated asset still exists."""
    for relative, data in overlays.items():
        asset_relative = relative.removesuffix(".meta")
        if not path_exists(package / asset_relative):
            continue
        data = normalize_meta_overlay(relative, data)
        target = package / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with open(windows_long_path(target), "wb") as stream:
            stream.write(data)


def normalize_meta_overlay(relative: str, data: bytes) -> bytes:
    """Preserve legacy GUIDs while upgrading generated DLL metas to PluginImporter."""
    if not relative.lower().endswith(".dll.meta") or b"PluginImporter:" in data:
        return data

    guid = extract_unity_meta_guid(data.decode("utf-8", errors="replace"))
    if not guid:
        return data

    asset_relative = relative.removesuffix(".meta")
    text = generated_meta_text(Path(asset_relative), asset_relative, is_dir=False, guid=guid)
    return text.encode("utf-8")


def extract_unity_meta_guid(text: str) -> str:
    """Extract a Unity meta GUID from a small generated metadata file."""
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("guid:"):
            value = stripped.split(":", 1)[1].strip()
            if len(value) == 32 and all(c in "0123456789abcdefABCDEF" for c in value):
                return value.lower()
    return ""


def deterministic_guid(relative_path: str) -> str:
    """Return a deterministic Unity GUID for generated metadata."""
    seed = f"{PACKAGE_NAME}:{relative_path.replace(chr(92), '/')}"
    return hashlib.md5(seed.encode("utf-8"), usedforsecurity=False).hexdigest()


def meta_importer_for(path: Path) -> str:
    """Choose the Unity importer block for generated metadata."""
    if path.name == "package.json":
        return "PackageManifestImporter"
    if path.suffix == ".asmdef":
        return "AssemblyDefinitionImporter"
    if path.suffix.lower() == ".dll":
        return "PluginImporter"
    return "TextScriptImporter"


def generated_meta_text(path: Path, relative_path: str, is_dir: bool, guid: str | None = None) -> str:
    """Return deterministic Unity .meta text for a generated path."""
    guid = guid or deterministic_guid(relative_path)
    if is_dir:
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )

    importer = meta_importer_for(path)
    if importer == "PluginImporter":
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "PluginImporter:\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 2\n"
            "  iconMap: {}\n"
            "  executionOrder: {}\n"
            "  defineConstraints: []\n"
            "  isPreloaded: 0\n"
            "  isOverridable: 0\n"
            "  isExplicitlyReferenced: 0\n"
            "  validateReferences: 1\n"
            "  platformData:\n"
            "  - first:\n"
            "      Any:\n"
            "    second:\n"
            "      enabled: 0\n"
            "      settings: {}\n"
            "  - first:\n"
            "      Editor: Editor\n"
            "    second:\n"
            "      enabled: 1\n"
            "      settings:\n"
            "        CPU: x86_64\n"
            "        OS: Windows\n"
            "  - first:\n"
            "      Standalone: Windows\n"
            "    second:\n"
            "      enabled: 1\n"
            "      settings:\n"
            "        CPU: x86_64\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )

    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        f"{importer}:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    )


def ensure_generated_meta(package: Path, target: Path, is_dir: bool, existing_paths: set[str]) -> None:
    """Create a deterministic .meta file when the artifact did not provide one."""
    meta = target.with_name(target.name + ".meta")
    meta_key = meta.as_posix()
    if meta_key in existing_paths:
        return
    relative = target.relative_to(package).as_posix()
    write_text(meta, generated_meta_text(target, relative, is_dir))
    existing_paths.add(meta_key)


def write_generated_metas(package: Path) -> None:
    """Generate metadata for package-owned files and directories lacking upstream metadata."""
    paths = list(package.rglob("*"))
    existing_paths = {path.as_posix() for path in paths}
    directories = sorted((path for path in paths if path.is_dir()), key=lambda item: item.as_posix())
    files = sorted((path for path in paths if path.is_file()), key=lambda item: item.as_posix())
    for directory in directories:
        ensure_generated_meta(package, directory, is_dir=True, existing_paths=existing_paths)
    for path in files:
        if path.name.lower().endswith(".dll.meta"):
            asset = path.with_suffix("")
            text = path.read_text(encoding="utf-8", errors="replace")
            if asset.exists() and "PluginImporter:" not in text:
                guid = extract_unity_meta_guid(text)
                if guid:
                    write_text(path, generated_meta_text(asset, asset.relative_to(package).as_posix(), is_dir=False, guid=guid))
            continue
        if path.name.endswith(".meta") or path.name == ".gitkeep":
            continue
        ensure_generated_meta(package, path, is_dir=False, existing_paths=existing_paths)


def package_json() -> dict[str, object]:
    """Return the Unity package manifest."""
    return {
        "name": PACKAGE_NAME,
        "version": PACKAGE_VERSION,
        "unity2foxgloveRuntimeId": RUNTIME_ID,
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Humble Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional Humble Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",
        "keywords": [
            "unity2foxglove",
            "ros2",
            "ros2-for-unity",
            "humble",
            "win64",
        ],
        "unity2foxgloveConflicts": [
            "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
            "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
        ],
        "author": {"name": "Unity2Foxglove"},
    }


def runtime_manifest(artifact: RuntimeArtifact) -> dict[str, object]:
    """Return the runtime package manifest."""
    return {
        "schemaVersion": 1,
        "runtimeId": RUNTIME_ID,
        "packageName": PACKAGE_NAME,
        "packageVersion": PACKAGE_VERSION,
        "rosDistro": "humble",
        "unityVersion": "6000.3.14f1",
        "platform": "win64",
        "unityPlatform": "Windows",
        "architecture": "x86_64",
        "buildType": "standalone",
        "rmwImplementation": "rmw_fastrtps_cpp",
        "artifactName": artifact.name,
        "artifactSha256": artifact.sha256,
        "artifactSize": artifact.size,
        "inventoryFile": "RuntimeSupport/r2fu-humble-win64-runtime-inventory.json",
        "inventoryFileCount": artifact.inventory_file_count,
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "sourceBasis": "Local Humble rebuild from RobotecAI ROS2 For Unity and ros2cs sources with Windows ROS2 Humble dependency closure",
        "supportLevel": "Recommended",
        "distributionLevel": "Prototype",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "criticalRuntimeFiles": [
            "rcl.dll",
            "yaml.dll",
            "spdlog.dll",
            "rosgraph_msgs_assembly.dll",
        ],
        "packagePathPatch": {
            "modifiedFile": "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs",
            "reason": "Resolve the Editor runtime root from this Unity package and stage Player metadata/share under StreamingAssets.",
            "keepsAssetFolderFallback": True,
        },
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }


def readme_text(artifact: RuntimeArtifact) -> str:
    """Return the runtime package README."""
    return f"""# Unity2Foxglove ROS2 For Unity Runtime - Humble Win64

This package is an optional Windows x64 runtime for the Unity2Foxglove ROS2 For Unity integration. It carries the ROS2 For Unity runtime files, generated message assemblies, native ROS2 Humble DLLs, Fast DDS/RMW files, ros2cs files, metadata, inventory, and notices.

## Package Role

Install this package when a Unity project needs to run as a ROS2 node through ROS2 For Unity on Windows x64.

This package is independent from `dev.unity2foxglove.sdk` and can import by itself. It does not provide the high-level Unity2Foxglove facade or samples by itself; those live in `dev.unity2foxglove.ros2forunity`.

Recommended combinations:

- `dev.unity2foxglove.ros2forunity.runtime.humble.win64` alone: imports runtime files, manifest, notices, and diagnostics.
- `dev.unity2foxglove.ros2forunity` plus this runtime package: enables adapter-backed ROS2 publish/subscribe.
- `dev.unity2foxglove.sdk` plus adapter plus this runtime package: enables the combined Unity2Foxglove workflow.

## One Runtime Policy

Install only one `dev.unity2foxglove.ros2forunity.runtime.*` package in a Unity project. Multiple ROS2 runtime packages can load conflicting native DLLs or generated message assemblies.

Do not import the old `Assets/Ros2ForUnity` asset folder and this package in the same project. Use either an external asset-folder runtime or this package runtime.

The script assembly is intentionally named `Unity2Foxglove.Ros2ForUnity.Runtime` across all distro runtime packages. The adapter package references that stable assembly name, while the one-runtime policy and package conflict metadata prevent multiple distro runtimes from being active in the same Unity project.

## Runtime Identity

- ROS distro: Humble
- Platform: Windows x64
- Build type: standalone
- RMW implementation: `rmw_fastrtps_cpp`
- Runtime id: `r2fu-humble-win64`
- Artifact source: `{artifact.name}`
- SHA-256: `{artifact.sha256}`

The runtime manifest is `RuntimeSupport/runtime-manifest.json`. The file inventory is `RuntimeSupport/r2fu-humble-win64-runtime-inventory.json`.

## Process Environment Contract

The first active ROS2 For Unity context acquires a process-wide ROS environment lease
for the variables it changes. It snapshots each prior value, including whether a
value was unset, applies the packaged Humble runtime settings, and on the last
safe shutdown conditionally restores only values still equal to the value it
applied. If application code changes a value while the context is active, that
caller change is preserved. A failed startup attempts the same rollback; an
incomplete restore keeps the lease pending and blocks a new context until cleanup
succeeds.

The runtime may update `ROS_DISTRO`, `AMENT_PREFIX_PATH`,
`RMW_IMPLEMENTATION`, runtime-specific `RCUTILS_*`/`ROS2CS_*`, and on Windows
the native plugin `PATH`. `ROS_DOMAIN_ID`, DDS discovery/firewall settings, and
other caller-owned values are not rewritten. Native DLLs cannot be unloaded or
safely mixed after initialization, so restart Unity after changing the runtime package, distro, or communication mode.

## Known Artifact Debt

The current Humble artifact still carries OpenSSL 1.1.x runtime DLLs through its transitive ROS2/DDS closure. Those DLLs are not used by the default FastRTPS visualization path unless DDS security/TLS features are enabled, but OpenSSL 1.1.x is end-of-life. Treat this as an artifact refresh requirement: a future Humble runtime rebuild must move the transitive OpenSSL dependency to OpenSSL 3.x before this package is considered release-hardened.

## Package Path Patch

The bundled `ROS2ForUnity.cs` keeps the upstream `Assets/Ros2ForUnity` lookup and adds a package-path fallback so Unity Editor can load this runtime from:

```text
Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity
```

This patch is limited to locating runtime files from a Unity package. It does not change ROS2 For Unity node, publisher, subscriber, or DDS behavior.

## Network Acceptance Notes

WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Humble or a real remote Linux topology for final external-graph acceptance.

## Support Boundary

This is a prototype runtime package. Fresh-project install acceptance and public release readiness are separate gates. Linux, macOS, Jazzy, and Lyrical runtime packages are not included here.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove-specific packaging and support belong to Unity2Foxglove, not RobotecAI.
"""


def notices_text(inventory: dict[str, object], artifact: RuntimeArtifact) -> str:
    """Return third-party notices for the runtime package."""
    file_count = inventory.get("fileCount", artifact.inventory_file_count)
    return f"""# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Humble Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Humble native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `{artifact.name}` |
| Runtime id | `r2fu-humble-win64` |
| ROS distro | `humble` |
| Platform | Windows x64 |
| Build type | standalone |
| RMW | `rmw_fastrtps_cpp` |
| SHA-256 | `{artifact.sha256}` |
| Inventory file count | `{file_count}` |

## Known Upstream Components

| Component | Relationship |
|---|---|
| RobotecAI ROS2 For Unity | Unity integration surface for ROS2 node behavior |
| ros2cs | ROS2 C# binding stack used by ROS2 For Unity |
| ROS2 Humble native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` runtime path used by the current Windows artifact |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

## Critical Runtime Closure

The package includes the transitive runtime DLLs required for Unity to load `rcl.dll`, including:

```text
rcl.dll
yaml.dll
spdlog.dll
```

If these closure DLLs are removed, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This package is a prototype until fresh-project acceptance passes.
- The inventory is an engineering inventory generated from the local runtime artifact, not a complete legal audit.
- The current artifact still carries OpenSSL 1.1.x runtime DLLs through transitive ROS2/DDS closure. They are not used by the default FastRTPS visualization path unless DDS security/TLS features are enabled, but OpenSSL 1.1.x is end-of-life and must be replaced with OpenSSL 3.x in a future Humble artifact rebuild before release-hardening.
- Public release should refresh transitive license attribution before registry or binary distribution.
- WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Humble or a real remote Linux topology for final external-graph acceptance.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove must preserve that caveat and must not imply upstream community support for Unity2Foxglove-specific packaging.
"""


