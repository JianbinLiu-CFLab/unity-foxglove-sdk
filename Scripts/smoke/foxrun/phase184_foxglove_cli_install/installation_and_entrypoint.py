from __future__ import annotations
from .backup_and_provenance import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _restore_previous_receipt(
    receipt_path: str,
    receipt_preexisted: bool,
    receipt_rollback_temp: str | None,
    receipt_previous_sha256: str | None,
    dependencies: InstallerDependencies,
) -> None:
    """Restore the prior receipt or remove a newly written receipt."""

    filesystem = dependencies.filesystem
    if not receipt_preexisted:
        filesystem.remove(receipt_path)
        if filesystem.exists(receipt_path):
            raise _fail("Invalid Foxglove CLI receipt could not be removed.")
        return

    if (
        receipt_rollback_temp is None
        or receipt_previous_sha256 is None
        or not filesystem.exists(receipt_rollback_temp)
    ):
        raise _fail("Previous Foxglove CLI receipt backup is unavailable.")
    try:
        dependencies.atomic_replacer(receipt_rollback_temp, receipt_path)
    except Exception as exc:
        try:
            restored = (
                filesystem.exists(receipt_path)
                and filesystem.sha256(receipt_path)
                == receipt_previous_sha256
            )
        except Exception:
            restored = False
        if not restored:
            raise _fail(
                "Previous Foxglove CLI receipt could not be restored."
            ) from exc
    if (
        not filesystem.exists(receipt_path)
        or filesystem.sha256(receipt_path) != receipt_previous_sha256
    ):
        raise _fail("Previous Foxglove CLI receipt restoration did not verify.")


def _coerce_failure(exc: Exception) -> protocol.AcceptanceFailure:
    """Preserve stable failures and wrap unexpected installer errors."""

    if isinstance(exc, protocol.AcceptanceFailure):
        return exc
    return _fail("Foxglove CLI installation failed.")


def install_cli(
    install_path: os.PathLike[str] | str,
    receipt_path: os.PathLike[str] | str,
    dependencies: InstallerDependencies,
    *,
    previous_revision: object | None = None,
) -> dict[str, object]:
    """Install, verify, receipt, and revalidate one official Foxglove CLI."""

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

    filesystem = dependencies.filesystem
    download_temp: str | None = None
    receipt_rollback_temp: str | None = None
    receipt_previous_sha256: str | None = None
    retain_receipt_rollback = False
    previous_sha256 = NO_PREVIOUS_SHA256
    backup_path = build_backup_path(target, "none", NO_PREVIOUS_SHA256)
    had_previous = False
    created_backup = False
    replaced = False
    receipt_write_attempted = False
    receipt_preexisted = False
    mutation_transaction_started = False

    try:
        try:
            release = select_release_asset(
                dependencies.release_fetcher(RELEASE_ENDPOINT)
            )
            filesystem.ensure_parent(target)
            download_temp = filesystem.new_sibling_temp(target, "download")
            transaction_paths = (
                ("Foxglove CLI install path", target),
                ("Foxglove CLI receipt path", receipt_destination),
                ("Foxglove CLI download path", download_temp),
            )
            _require_distinct_windows_paths(transaction_paths)
            if ntpath.dirname(
                protocol.windows_path_key(
                    download_temp,
                    label="Foxglove CLI download path",
                )
            ) != ntpath.dirname(
                protocol.windows_path_key(
                    target,
                    label="Foxglove CLI install path",
                )
            ):
                raise _fail(
                    "Foxglove CLI download path must be a sibling temporary."
                )
            dependencies.downloader(release.asset_url, download_temp)
            download_size = (
                filesystem.size(download_temp)
                if filesystem.exists(download_temp)
                else 0
            )
            if (
                not filesystem.exists(download_temp)
                or download_size < 1
                or download_size > MAX_DOWNLOAD_BYTES
            ):
                raise _fail("Downloaded Foxglove CLI size is invalid.")
            if download_size != release.asset_size:
                raise _fail(
                    "Downloaded Foxglove CLI size does not match release metadata."
                )

            download_sha256 = protocol.validate_sha256(
                filesystem.sha256(download_temp)
            )
            if download_sha256 != release.asset_sha256:
                raise _fail(
                    "Downloaded Foxglove CLI digest does not match release metadata."
                )
            download_version = _run_version(download_temp, dependencies)
            if download_version != release.release_version:
                raise _fail(
                    "Downloaded Foxglove CLI version does not match the release."
                )

            (
                previous_sha256,
                backup_path,
                had_previous,
                created_backup,
            ) = _prepare_backup(
                target,
                dependencies,
                previous_revision,
                transaction_paths,
            )
            transaction_paths = (
                *transaction_paths,
                ("Foxglove CLI backup path", backup_path),
            )
        except Exception as exc:
            if created_backup:
                _remove_quietly(filesystem, backup_path)
            if isinstance(exc, protocol.AcceptanceFailure):
                raise
            raise _coerce_failure(exc) from exc

        try:
            mutation_transaction_started = True
            replaced = True
            dependencies.atomic_replacer(download_temp, target)

            installed_version, installed_sha256 = _verify_installed_identity(
                target,
                release.release_version,
                download_version,
                download_sha256,
                dependencies,
            )

            receipt = {
                "schemaVersion": protocol.CLI_RECEIPT_SCHEMA_VERSION,
                "releaseTag": release.release_tag,
                "releaseVersion": release.release_version,
                "architecture": protocol.CLI_ARCHITECTURE,
                "assetName": release.asset_name,
                "assetUrl": release.asset_url,
                "downloadSha256": download_sha256,
                "downloadVersion": download_version,
                "installedPath": target,
                "installedSha256": installed_sha256,
                "installedVersion": installed_version,
                "previousSha256": previous_sha256,
                "backupPath": backup_path,
                "installedUtc": _installed_utc(dependencies.clock),
            }
            protocol.validate_cli_receipt(
                receipt,
                target,
                installed_version,
                installed_sha256,
            )
            receipt_preexisted = filesystem.exists(receipt_destination)
            if receipt_preexisted:
                receipt_previous_sha256 = protocol.validate_sha256(
                    filesystem.sha256(receipt_destination)
                )
                receipt_rollback_temp = filesystem.new_sibling_temp(
                    receipt_destination,
                    "receipt-rollback",
                )
                _require_distinct_windows_paths(
                    (
                        *transaction_paths,
                        (
                            "Foxglove CLI receipt rollback path",
                            receipt_rollback_temp,
                        ),
                    )
                )
                try:
                    filesystem.copy_exclusive(
                        receipt_destination,
                        receipt_rollback_temp,
                    )
                except Exception as exc:
                    raise _fail(
                        "Previous Foxglove CLI receipt could not be preserved."
                    ) from exc
                if (
                    filesystem.sha256(receipt_rollback_temp)
                    != receipt_previous_sha256
                ):
                    raise _fail(
                        "Previous Foxglove CLI receipt backup hash does not match."
                    )
            receipt_write_attempted = True
            filesystem.write_receipt(receipt_destination, receipt)
            (
                post_write_version,
                post_write_sha256,
            ) = _verify_installed_identity(
                target,
                release.release_version,
                download_version,
                download_sha256,
                dependencies,
            )
            reloaded = filesystem.load_receipt(receipt_destination)
            validated = protocol.validate_cli_receipt(
                reloaded,
                target,
                post_write_version,
                post_write_sha256,
            )
            if validated != receipt:
                raise _fail("Reloaded Foxglove CLI receipt is not exact.")
            _remove_quietly(filesystem, receipt_rollback_temp)
            receipt_rollback_temp = None
            return validated
        except BaseException as exc:
            binary_rollback_error: BaseException | None = None
            receipt_rollback_error: BaseException | None = None
            if replaced:
                try:
                    _restore_previous_binary(
                        target,
                        backup_path,
                        previous_sha256,
                        had_previous,
                        dependencies,
                    )
                except BaseException as rollback_exc:
                    binary_rollback_error = rollback_exc
            if receipt_write_attempted:
                try:
                    _restore_previous_receipt(
                        receipt_destination,
                        receipt_preexisted,
                        receipt_rollback_temp,
                        receipt_previous_sha256,
                        dependencies,
                    )
                    _remove_quietly(filesystem, receipt_rollback_temp)
                    receipt_rollback_temp = None
                except BaseException as rollback_exc:
                    receipt_rollback_error = rollback_exc
                    retain_receipt_rollback = True
            if created_backup and binary_rollback_error is None:
                _remove_quietly(filesystem, backup_path)
            if (
                binary_rollback_error is not None
                or receipt_rollback_error is not None
            ):
                raise _fail(
                    "Foxglove CLI installation failed and rollback did not complete."
                ) from (binary_rollback_error or receipt_rollback_error)
            if isinstance(exc, protocol.AcceptanceFailure):
                raise
            if isinstance(exc, Exception):
                raise _coerce_failure(exc) from exc
            raise
    finally:
        _remove_quietly(filesystem, download_temp)
        if not retain_receipt_rollback:
            _remove_quietly(filesystem, receipt_rollback_temp)
        if not mutation_transaction_started and created_backup:
            _remove_quietly(filesystem, backup_path)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse the direct installer command line."""

    parser = argparse.ArgumentParser(
        description=(
            "Install and provenance-check the official Windows amd64 "
            "Foxglove CLI."
        )
    )
    parser.add_argument("--install-path", required=True)
    parser.add_argument("--receipt", default=str(DEFAULT_RECEIPT_PATH))
    return parser.parse_args(argv)


def main(
    argv: Sequence[str] | None = None,
    dependencies: InstallerDependencies | None = None,
) -> int:
    """Install the requested official CLI and return a process status."""

    args = parse_args(argv)
    active_dependencies = dependencies or _production_dependencies()
    install_cli(
        args.install_path,
        args.receipt,
        active_dependencies,
    )
    return 0


def _entrypoint() -> int:
    """Translate stable acceptance failures into a nonzero process status."""

    try:
        return main()
    except protocol.AcceptanceFailure as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(_entrypoint())


__all__ = [name for name in globals() if not name.startswith("__")]
