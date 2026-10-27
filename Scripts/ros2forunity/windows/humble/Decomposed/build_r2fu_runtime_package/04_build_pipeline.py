def write_package_files(paths: BuildPaths, inventory: dict[str, object], artifact: RuntimeArtifact) -> None:
    """Write package metadata, docs, notices, and support manifests."""
    write_json(paths.package / "package.json", package_json())
    write_text(paths.package / "README.md", readme_text(artifact))
    shutil.copyfile(UPSTREAM_LICENSE, paths.package / "LICENSE")
    write_text(paths.package / "THIRD_PARTY_NOTICES.md", notices_text(inventory, artifact))
    write_json(paths.package / "RuntimeSupport" / "runtime-manifest.json", runtime_manifest(artifact))
    shutil.copyfile(paths.inventory, paths.package / "RuntimeSupport" / "r2fu-humble-win64-runtime-inventory.json")
    write_json(
        paths.package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.HumbleWin64.asmdef",
        runtime_asmdef(),
    )


def build_package(paths: BuildPaths) -> RuntimeArtifact:
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
        patch_ros2_for_unity(paths.package)
        apply_local_patch_overlays(paths.package, overlays)
        patch_ros_time_source_contract(paths.package)
        write_package_files(paths, inventory, artifact)
        patch_deps_json_sha512(paths.package)
        apply_meta_overlays(paths.package, meta_overlays)
        write_generated_metas(paths.package)
        snapshot_safe_to_remove = True
        return artifact
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

