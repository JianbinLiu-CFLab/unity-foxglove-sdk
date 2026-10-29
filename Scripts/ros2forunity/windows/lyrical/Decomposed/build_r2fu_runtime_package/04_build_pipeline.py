def write_package_files(paths: BuildPaths, inventory: dict[str, object], artifact: RuntimeArtifact) -> None:
    """Write package metadata, docs, notices, and support manifests."""
    write_json(paths.package / "package.json", package_json())
    write_text(paths.package / "README.md", readme_text(artifact))
    shutil.copyfile(UPSTREAM_LICENSE, paths.package / "LICENSE")
    write_text(paths.package / "THIRD_PARTY_NOTICES.md", notices_text(inventory, artifact))
    write_json(paths.package / "RuntimeSupport" / "runtime-manifest.json", runtime_manifest(artifact))
    shutil.copyfile(paths.inventory, paths.package / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json")
    update_zenoh_config_inventory_hashes(paths.package)
    write_json(
        paths.package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.LyricalWin64.asmdef",
        runtime_asmdef(),
    )


def validate_ros2cs_metadata_descriptions(package: Path) -> None:
    """Require each ros2cs metadata document to declare the Lyrical runtime."""
    metadata_files = (
        package / "Runtime" / "Ros2ForUnity" / "metadata_ros2cs.xml",
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "metadata_ros2cs.xml",
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml",
    )
    for path in metadata_files:
        text = path.read_text(encoding="utf-8", errors="replace")
        try:
            root = ElementTree.fromstring(text)
        except ElementTree.ParseError as error:
            raise ValueError(f"Invalid ros2cs metadata XML: {path}") from error
        distro = (root.findtext("ros2") or "").strip() if root.tag == "ros2cs" else ""
        if distro != "lyrical":
            raise ValueError(f"Unexpected ros2cs distro in {path}: expected lyrical")


def build_package(paths: BuildPaths) -> None:
    """Build the runtime package from the runtime artifact."""
    inventory, artifact = require_inputs(paths)
    validate_package_target(paths.package)
    snapshot = snapshot_package_dir(paths.package)
    overlays = collect_local_patch_overlays(paths.package)
    meta_overlays = collect_meta_overlays(paths.package)
    supplemental_runtime_files = collect_supplemental_runtime_files(paths.package)
    snapshot_safe_to_remove = False
    try:
        reset_package_dir(paths.package)
        extract_runtime(paths)
        apply_supplemental_runtime_files(paths.package, supplemental_runtime_files)
        normalize_ros2cs_plugin_roots(paths.package)
        prune_non_contract_examples(paths.package)
        apply_local_patch_overlays(paths.package, overlays)
        patch_ros2_for_unity(paths.package)
        patch_component_main_thread_prewarm(paths.package)
        patch_ros_time_source_contract(paths.package)
        patch_zenoh_session_config_safety(paths.package)
        patch_zenoh_router_config_notes(paths.package)
        validate_ros2cs_metadata_descriptions(paths.package)
        write_package_files(paths, inventory, artifact)
        patch_deps_json_sha512(paths.package)
        apply_meta_overlays(paths.package, meta_overlays)
        write_generated_metas(paths.package)
        snapshot_safe_to_remove = True
    except Exception as generation_error:
        try:
            restore_package_dir(paths.package, snapshot)
        except Exception as rollback_error:
            snapshot_path = str(snapshot) if snapshot is not None else "<not available>"
            raise RuntimeError(
                "Runtime package generation failed "
                f"({type(generation_error).__name__}: {generation_error}); rollback also failed "
                f"({type(rollback_error).__name__}: {rollback_error}). "
                f"Rollback snapshot preserved for manual recovery: {snapshot_path}"
            ) from rollback_error
        snapshot_safe_to_remove = True
        raise
    finally:
        if snapshot_safe_to_remove:
            remove_package_snapshot(snapshot)
