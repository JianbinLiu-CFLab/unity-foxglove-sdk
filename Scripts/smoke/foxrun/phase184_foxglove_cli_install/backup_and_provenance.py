from __future__ import annotations
from .process_execution_and_dependencies import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _prepare_backup(
    install_path: str,
    dependencies: InstallerDependencies,
    previous_revision: object | None,
    reserved_paths: Sequence[tuple[str, object]],
) -> tuple[str, str, bool, bool]:
    """Preserve an existing installation in a verified immutable backup."""

    filesystem = dependencies.filesystem
    if not filesystem.exists(install_path):
        backup_path = build_backup_path(
            install_path,
            "none",
            NO_PREVIOUS_SHA256,
        )
        _require_distinct_windows_paths(
            (*reserved_paths, ("Foxglove CLI backup path", backup_path))
        )
        return NO_PREVIOUS_SHA256, backup_path, False, False

    previous_sha256 = protocol.validate_sha256(filesystem.sha256(install_path))
    revision = previous_revision
    if revision is None:
        try:
            revision = dependencies.command_runner(
                install_path,
                ("version",),
                build_minimal_process_environment(
                    dependencies.process_environment
                ),
            )
        except Exception:
            revision = "unknown"
    backup_path = build_backup_path(
        install_path,
        revision,
        previous_sha256,
    )
    _require_distinct_windows_paths(
        (*reserved_paths, ("Foxglove CLI backup path", backup_path))
    )
    if filesystem.exists(backup_path):
        if filesystem.sha256(backup_path) != previous_sha256:
            raise _fail(
                "Foxglove CLI backup path already contains a different binary."
            )
        return previous_sha256, backup_path, True, False

    backup_temp = filesystem.new_sibling_temp(backup_path, "backup")
    _require_distinct_windows_paths(
        (
            *reserved_paths,
            ("Foxglove CLI backup path", backup_path),
            ("Foxglove CLI backup temporary path", backup_temp),
        )
    )
    if ntpath.dirname(
        protocol.windows_path_key(
            backup_temp,
            label="Foxglove CLI backup temporary path",
        )
    ) != ntpath.dirname(
        protocol.windows_path_key(
            backup_path,
            label="Foxglove CLI backup path",
        )
    ):
        raise _fail("Foxglove CLI backup temporary must be a sibling.")

    temp_owned = False
    backup_published = False
    try:
        temp_owned = True
        try:
            filesystem.copy_exclusive(install_path, backup_temp)
        except FileExistsError as exc:
            temp_owned = False
            raise _fail(
                "Foxglove CLI backup temporary path already exists."
            ) from exc
        except Exception as exc:
            raise _fail(
                "Existing Foxglove CLI could not be preserved."
            ) from exc
        if filesystem.sha256(backup_temp) != previous_sha256:
            raise _fail("Preserved Foxglove CLI backup hash does not match.")
        try:
            filesystem.publish_exclusive(backup_temp, backup_path)
        except FileExistsError as exc:
            raise _fail(
                "Foxglove CLI backup path was claimed before publication."
            ) from exc
        except Exception as exc:
            raise _fail(
                "Foxglove CLI backup could not be published."
            ) from exc
        backup_published = True
        temp_owned = False
    except BaseException:
        if temp_owned:
            try:
                filesystem.remove(backup_temp)
                if filesystem.exists(backup_temp):
                    raise _fail(
                        "Owned Foxglove CLI backup temporary was not removed."
                    )
            except Exception as cleanup_exc:
                raise _fail(
                    "Owned Foxglove CLI backup temporary cleanup failed."
                ) from cleanup_exc
        raise
    return previous_sha256, backup_path, True, backup_published


def _restore_previous_binary(
    install_path: str,
    backup_path: str,
    previous_sha256: str,
    had_previous: bool,
    dependencies: InstallerDependencies,
) -> None:
    """Restore the prior binary or remove a newly introduced installation."""

    filesystem = dependencies.filesystem
    if not had_previous:
        filesystem.remove(install_path)
        if filesystem.exists(install_path):
            raise _fail("New Foxglove CLI could not be removed during rollback.")
        return

    restore_temp = filesystem.new_sibling_temp(install_path, "rollback")
    try:
        filesystem.copy_exclusive(backup_path, restore_temp)
        if filesystem.sha256(restore_temp) != previous_sha256:
            raise _fail("Foxglove CLI rollback copy hash does not match.")
        dependencies.atomic_replacer(restore_temp, install_path)
        if (
            not filesystem.exists(install_path)
            or filesystem.sha256(install_path) != previous_sha256
        ):
            raise _fail("Foxglove CLI rollback verification failed.")
    finally:
        _remove_quietly(filesystem, restore_temp)


def _verify_installed_identity(
    install_path: str,
    release_version: str,
    download_version: str,
    download_sha256: str,
    dependencies: InstallerDependencies,
) -> tuple[str, str]:
    """Cross-check installed and freshly resolved CLI version and digest."""

    filesystem = dependencies.filesystem
    installed_version = _run_version(install_path, dependencies)
    installed_sha256 = protocol.validate_sha256(
        filesystem.sha256(install_path)
    )
    if (
        installed_version != release_version
        or installed_version != download_version
        or installed_sha256 != download_sha256
    ):
        raise _fail(
            "Installed Foxglove CLI version or hash does not match the download."
        )

    try:
        resolved_path = dependencies.command_resolver(
            build_minimal_process_environment(
                dependencies.process_environment
            )
        )
    except protocol.AcceptanceFailure:
        raise
    except Exception as exc:
        raise _fail("Fresh PowerShell Foxglove CLI resolution failed.") from exc
    if not protocol.windows_paths_equal(resolved_path, install_path):
        raise _fail(
            "Fresh PowerShell Foxglove CLI path does not match the install target."
        )
    resolved_version = _run_version(resolved_path, dependencies)
    resolved_sha256 = protocol.validate_sha256(
        filesystem.sha256(resolved_path)
    )
    if (
        resolved_version != release_version
        or resolved_version != download_version
        or resolved_version != installed_version
        or resolved_sha256 != download_sha256
        or resolved_sha256 != installed_sha256
    ):
        raise _fail(
            "Freshly resolved Foxglove CLI version or hash does not match."
        )
    return installed_version, installed_sha256


def _capture_leased_executable(
    lease: Any,
    *,
    expected: ExecutableSnapshot | None = None,
) -> ExecutableSnapshot:
    """Capture a stable leased snapshot and reject path replacement."""

    try:
        snapshot = lease.snapshot()
        path_identity = lease.path_identity()
    except protocol.AcceptanceFailure:
        raise
    except Exception as exc:
        raise _fail("Executable lease identity could not be verified.") from exc
    if (
        not isinstance(snapshot, ExecutableSnapshot)
        or not isinstance(path_identity, ExecutableFileIdentity)
        or snapshot.identity != path_identity
        or (expected is not None and snapshot != expected)
    ):
        raise _fail("Executable changed while its identity was leased.")
    return snapshot


def verify_installed_cli_provenance(
    install_path: os.PathLike[str] | str,
    receipt_path: os.PathLike[str] | str = DEFAULT_RECEIPT_PATH,
    *,
    dependencies: InstallerDependencies | None = None,
) -> VerifiedCliIdentity:
    """Read and cross-check one installed CLI without mutating local state."""

    active_dependencies = (
        dependencies
        if dependencies is not None
        else _production_dependencies()
    )
    target = _validated_windows_path(
        install_path,
        "Foxglove CLI install path",
    )
    receipt_destination = _resolve_receipt_path(receipt_path)
    _require_distinct_windows_paths(
        (
            ("Foxglove CLI install path", target),
            ("Foxglove CLI receipt path", receipt_destination),
        )
    )

    filesystem = active_dependencies.filesystem
    try:
        receipt = filesystem.load_receipt(receipt_destination)
        if not protocol.windows_paths_equal(
            str(receipt["installedPath"]),
            target,
        ):
            raise _fail(
                "Installed Foxglove CLI path does not match the receipt."
            )
        expected_version = protocol.normalize_semantic_version(
            receipt["installedVersion"]
        )
        expected_sha256 = protocol.validate_sha256(
            receipt["installedSha256"]
        )
        minimal_environment = build_minimal_process_environment(
            active_dependencies.process_environment
        )
        with active_dependencies.executable_lease_factory(target) as lease:
            initial = _capture_leased_executable(lease)
            if initial.sha256 != expected_sha256:
                raise _fail(
                    "Installed Foxglove CLI hash does not match the receipt."
                )
            installed_version = _run_version(
                target,
                active_dependencies,
            )
            if installed_version != expected_version:
                raise _fail(
                    "Installed Foxglove CLI version does not match the receipt."
                )
            _capture_leased_executable(lease, expected=initial)
            validated_receipt = protocol.validate_cli_receipt(
                receipt,
                target,
                installed_version,
                initial.sha256,
            )

            try:
                resolved_path = active_dependencies.command_resolver(
                    minimal_environment
                )
            except protocol.AcceptanceFailure:
                raise
            except Exception as exc:
                raise _fail(
                    "Fresh PowerShell Foxglove CLI resolution failed."
                ) from exc
            if not protocol.windows_paths_equal(resolved_path, target):
                raise _fail(
                    "Fresh PowerShell Foxglove CLI path does not match "
                    "the install target."
                )
            _capture_leased_executable(lease, expected=initial)
            resolved_version = _run_version(
                resolved_path,
                active_dependencies,
            )
            _capture_leased_executable(lease, expected=initial)
            if resolved_version != installed_version:
                raise _fail(
                    "Freshly resolved Foxglove CLI version does not match "
                    "the installed executable."
                )
            protocol.validate_cli_receipt(
                validated_receipt,
                resolved_path,
                resolved_version,
                initial.sha256,
            )
        return VerifiedCliIdentity(
            installed_path=target,
            installed_version=installed_version,
            installed_sha256=initial.sha256,
            release_tag=str(validated_receipt["releaseTag"]),
            asset_url=str(validated_receipt["assetUrl"]),
            architecture=str(validated_receipt["architecture"]),
            receipt_path=receipt_destination,
        )
    except protocol.AcceptanceFailure:
        raise
    except Exception as exc:
        raise _fail("Foxglove CLI provenance verification failed.") from exc


__all__ = [name for name in globals() if not name.startswith("__")]
