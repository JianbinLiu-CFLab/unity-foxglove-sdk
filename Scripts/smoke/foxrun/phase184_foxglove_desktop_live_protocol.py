#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure provenance protocol for Phase184-H Foxglove tooling acceptance."""

from __future__ import annotations

import contextlib
import dataclasses
import datetime as dt
import hashlib
import json
import math
import ntpath
import os
import pathlib
import re
import stat
import time
import uuid
from typing import Any, Callable, Iterable, Mapping
from urllib.parse import urlsplit


CLI_RECEIPT_SCHEMA_VERSION = 1
CLI_ARCHITECTURE = "windows-amd64"
CLI_ASSET_NAME = "foxglove-windows-amd64.exe"
CLI_ASSET_NAMES = frozenset(
    {
        "foxglove-windows-amd64",
        CLI_ASSET_NAME,
    }
)
CLI_RECEIPT_KEYS = frozenset(
    {
        "schemaVersion",
        "releaseTag",
        "releaseVersion",
        "architecture",
        "assetName",
        "assetUrl",
        "downloadSha256",
        "downloadVersion",
        "installedPath",
        "installedSha256",
        "installedVersion",
        "previousSha256",
        "backupPath",
        "installedUtc",
    }
)

MAX_DIAGNOSTIC_CHARACTERS = 512
MAX_RECEIPT_BYTES = 32 * 1024

DESKTOP_CLIENT_BARRIER_FILENAME = "desktop-client-barrier.json"
DESKTOP_CLIENT_BARRIER_SCHEMA_VERSION = 1
DESKTOP_CLIENT_BARRIER_STATE = "desktop-client-proved"
DESKTOP_CLIENT_BARRIER_KEYS = frozenset(
    {
        "schemaVersion",
        "runId",
        "tokenDigest",
        "state",
        "acceptedClients",
    }
)
MAX_DESKTOP_CLIENT_BARRIER_BYTES = 4 * 1024
DESKTOP_CLIENT_BARRIER_STARTUP_ALLOWANCE_SECONDS = 120.0
DESKTOP_CLIENT_BARRIER_POLL_SECONDS = 0.25

TRANSPORT_CLIENTS_MARKER = "PHASE184H_TRANSPORT_CLIENTS"
TRANSPORT_CLIENTS_OVERFLOW_MARKER = "PHASE184H_TRANSPORT_CLIENTS_OVERFLOW"
MAX_TRANSPORT_CLIENT_MARKER_BYTES = 512
MAX_TRANSPORT_CLIENT_MARKERS = 8
MAX_TRANSPORT_CLIENT_COUNT = (2**31) - 1

FAIL_CLI_PROVENANCE = "FAIL_CLI_PROVENANCE"
FAIL_DESKTOP_PREFLIGHT = "FAIL_DESKTOP_PREFLIGHT"
FAIL_DESKTOP_START = "FAIL_DESKTOP_START"
FAIL_DESKTOP_IDENTITY = "FAIL_DESKTOP_IDENTITY"
FAIL_DESKTOP_CONNECTION = "FAIL_DESKTOP_CONNECTION"
FAIL_CLIENT = "FAIL_CLIENT"
FAIL_FOXRUN_CHILD = "FAIL_FOXRUN_CHILD"
FAIL_EVIDENCE = "FAIL_EVIDENCE"
FAIL_CLEANUP = "FAIL_CLEANUP"

TERMINAL_FAILURE_CODES = frozenset(
    {
        FAIL_CLI_PROVENANCE,
        FAIL_DESKTOP_PREFLIGHT,
        FAIL_DESKTOP_START,
        FAIL_DESKTOP_IDENTITY,
        FAIL_DESKTOP_CONNECTION,
        FAIL_CLIENT,
        FAIL_FOXRUN_CHILD,
        FAIL_EVIDENCE,
        FAIL_CLEANUP,
    }
)

_SEMANTIC_VERSION = re.compile(
    r"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z"
)
_UPPER_SHA256 = re.compile(r"\A[0-9A-F]{64}\Z")
_UTC_TIMESTAMP = re.compile(
    r"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T"
    r"[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?Z\Z"
)
_MAX_VERSION_CHARACTERS = 64
_MAX_URL_CHARACTERS = 2048
_MAX_WINDOWS_PATH_CHARACTERS = 32767
_HASH_CHUNK_BYTES = 1024 * 1024
_MAX_OBSERVATION_SECONDS = 3600
_SAFE_RUN_ID = re.compile(r"\Aphase184g-[A-Za-z0-9][A-Za-z0-9._-]{7,79}\Z")
_SAFE_TOKEN = re.compile(r"\Ap184g_[A-Za-z0-9]{12,64}\Z")
_SAFE_CASE = re.compile(r"\A[A-Za-z0-9][A-Za-z0-9._-]{0,79}\Z")
_TRANSPORT_CLIENT_MARKER = re.compile(
    rf"\A(?P<marker>{re.escape(TRANSPORT_CLIENTS_MARKER)}|"
    rf"{re.escape(TRANSPORT_CLIENTS_OVERFLOW_MARKER)}) "
    r"case=(?P<case>[^\s=]+) "
    r"token=(?P<token>[^\s=]+) "
    r"active=(?P<active>0|[1-9][0-9]*) "
    r"accepted=(?P<accepted>0|[1-9][0-9]*)\Z"
)


def _bounded_message(message: object) -> str:
    if isinstance(message, str):
        text = message
    else:
        text = type(message).__name__
    text = text.replace("\r", " ").replace("\n", " ")
    text = re.sub(r"[ \t]+", " ", text).strip()
    if not text:
        text = "Unspecified acceptance failure."
    if len(text) > MAX_DIAGNOSTIC_CHARACTERS:
        text = text[: MAX_DIAGNOSTIC_CHARACTERS - 1] + "\N{HORIZONTAL ELLIPSIS}"
    return text


class AcceptanceFailure(RuntimeError):
    """Machine-classifiable Phase184-H failure with a bounded diagnostic."""

    def __init__(self, code: str, bounded_message: object):
        if code not in TERMINAL_FAILURE_CODES:
            raise ValueError("Unknown Phase184-H terminal failure code.")
        self.code = code
        self.message = _bounded_message(bounded_message)
        super().__init__(f"{self.code}: {self.message}")


def _fail(message: object) -> AcceptanceFailure:
    return AcceptanceFailure(FAIL_CLI_PROVENANCE, message)


def normalize_semantic_version(value: object) -> str:
    """Return one canonical stable major.minor.patch version."""

    if not isinstance(value, str):
        raise _fail("CLI version must be one stable semantic version.")
    candidate = value.strip()
    if not candidate or len(candidate) > _MAX_VERSION_CHARACTERS:
        raise _fail("CLI version must be one stable semantic version.")
    match = _SEMANTIC_VERSION.fullmatch(candidate)
    if match is None:
        raise _fail("CLI version must be one stable semantic version.")
    return ".".join(match.groups())


def validate_sha256(value: object) -> str:
    """Validate and return one canonical uppercase SHA-256 digest."""

    if not isinstance(value, str) or _UPPER_SHA256.fullmatch(value) is None:
        raise _fail("SHA-256 must be exactly 64 uppercase hexadecimal characters.")
    return value


def sha256_file(path: os.PathLike[str] | str) -> str:
    """Stream a file into one canonical uppercase SHA-256 digest."""

    digest = hashlib.sha256()
    try:
        with pathlib.Path(path).open("rb") as stream:
            while chunk := stream.read(_HASH_CHUNK_BYTES):
                digest.update(chunk)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("CLI file could not be read for SHA-256.") from exc
    return digest.hexdigest().upper()


def validate_official_asset_url(
    asset_url: object,
    *,
    expected_release_version: object | None = None,
) -> str:
    """Validate the exact official GitHub Windows release-asset route."""

    if (
        not isinstance(asset_url, str)
        or not asset_url
        or len(asset_url) > _MAX_URL_CHARACTERS
        or asset_url != asset_url.strip()
    ):
        raise _fail("Foxglove CLI asset URL is invalid.")
    try:
        parsed = urlsplit(asset_url)
    except ValueError as exc:
        raise _fail("Foxglove CLI asset URL is invalid.") from exc
    if (
        parsed.scheme != "https"
        or parsed.netloc != "github.com"
        or parsed.query
        or parsed.fragment
    ):
        raise _fail("Foxglove CLI asset URL is not an official GitHub release asset.")

    segments = parsed.path.split("/")
    if (
        len(segments) != 7
        or segments[:5]
        != ["", "foxglove", "foxglove-cli", "releases", "download"]
        or segments[6] not in CLI_ASSET_NAMES
    ):
        raise _fail("Foxglove CLI asset URL is not the exact Windows asset.")
    canonical_url = (
        "https://github.com/foxglove/foxglove-cli/releases/download/"
        f"{segments[5]}/{segments[6]}"
    )
    if asset_url != canonical_url:
        raise _fail("Foxglove CLI asset URL is not canonical.")

    asset_release_version = normalize_semantic_version(segments[5])
    if expected_release_version is not None:
        expected = normalize_semantic_version(expected_release_version)
        if asset_release_version != expected:
            raise _fail("Foxglove CLI asset URL release does not match the receipt.")
    return asset_release_version


def _require_receipt_string(
    receipt: Mapping[str, Any],
    key: str,
    *,
    maximum: int,
) -> str:
    value = receipt[key]
    if (
        not isinstance(value, str)
        or not value
        or len(value) > maximum
        or "\x00" in value
    ):
        raise _fail(f"CLI receipt field {key} is invalid.")
    return value


def _windows_path_key(
    value: object,
    label: str,
    *,
    allow_pathlike: bool,
) -> str:
    if allow_pathlike:
        try:
            value = os.fspath(value)
        except TypeError as exc:
            raise _fail(f"{label} must be an absolute Windows path.") from exc
    if (
        not isinstance(value, str)
        or not value
        or len(value) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in value
        or "\r" in value
        or "\n" in value
    ):
        raise _fail(f"{label} must be an absolute Windows path.")

    ordinary = value.replace("/", "\\")
    folded = ordinary.casefold()
    if folded.startswith("\\\\?\\unc\\"):
        ordinary = "\\\\" + ordinary[8:]
    elif folded.startswith("\\\\?\\"):
        ordinary = ordinary[4:]

    drive, tail = ntpath.splitdrive(ordinary)
    if not drive or not tail.startswith("\\") or not ntpath.isabs(ordinary):
        raise _fail(f"{label} must be an absolute Windows drive or UNC path.")
    if drive.startswith("\\\\"):
        unc_parts = drive[2:].split("\\")
        if (
            len(unc_parts) != 2
            or not all(unc_parts)
            or unc_parts[0] in {".", "?"}
        ):
            raise _fail(f"{label} must be an absolute Windows drive or UNC path.")
    elif re.fullmatch(r"[A-Za-z]:", drive) is None:
        raise _fail(f"{label} must be an absolute Windows drive or UNC path.")
    return ntpath.normcase(ntpath.normpath(ordinary))


def windows_path_key(
    value: os.PathLike[str] | str,
    *,
    label: str = "Windows path",
) -> str:
    """Return one identity key for ordinary or extended drive/UNC paths."""

    return _windows_path_key(value, label, allow_pathlike=True)


def windows_paths_equal(
    left: os.PathLike[str] | str,
    right: os.PathLike[str] | str,
) -> bool:
    """Compare two strictly absolute Windows paths by canonical identity."""

    return windows_path_key(left) == windows_path_key(right)


def _normalize_windows_path(
    value: object,
    label: str,
    *,
    allow_pathlike: bool,
) -> str:
    """Compatibility wrapper around the canonical public path identity."""

    return _windows_path_key(value, label, allow_pathlike=allow_pathlike)


def _validate_installed_utc(value: object) -> str:
    if not isinstance(value, str) or _UTC_TIMESTAMP.fullmatch(value) is None:
        raise _fail("CLI receipt installedUtc must be a canonical UTC timestamp.")
    parse_value = value[:-1] + "+00:00"
    try:
        parsed = dt.datetime.fromisoformat(parse_value)
    except ValueError as exc:
        raise _fail("CLI receipt installedUtc must be a valid UTC timestamp.") from exc
    if parsed.utcoffset() != dt.timedelta(0):
        raise _fail("CLI receipt installedUtc must be a UTC timestamp.")
    return value


def _validate_receipt(receipt: object) -> tuple[dict[str, object], str, str, str]:
    if not isinstance(receipt, Mapping):
        raise _fail("CLI receipt root must be an object.")
    try:
        actual_keys = frozenset(receipt)
    except (TypeError, ValueError) as exc:
        raise _fail("CLI receipt keys are invalid.") from exc
    if actual_keys != CLI_RECEIPT_KEYS:
        raise _fail("CLI receipt keys do not match schemaVersion 1.")

    if (
        isinstance(receipt["schemaVersion"], bool)
        or not isinstance(receipt["schemaVersion"], int)
        or receipt["schemaVersion"] != CLI_RECEIPT_SCHEMA_VERSION
    ):
        raise _fail("CLI receipt schemaVersion is unsupported.")
    if receipt["architecture"] != CLI_ARCHITECTURE:
        raise _fail("CLI receipt architecture is not windows-amd64.")
    asset_name = receipt["assetName"]
    if not isinstance(asset_name, str) or asset_name not in CLI_ASSET_NAMES:
        raise _fail("CLI receipt assetName is not the exact Windows asset.")

    release_tag = _require_receipt_string(
        receipt,
        "releaseTag",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    release_version_text = _require_receipt_string(
        receipt,
        "releaseVersion",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    download_version_text = _require_receipt_string(
        receipt,
        "downloadVersion",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    installed_version_text = _require_receipt_string(
        receipt,
        "installedVersion",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    release_version = normalize_semantic_version(release_tag)
    versions = {
        release_version,
        normalize_semantic_version(release_version_text),
        normalize_semantic_version(download_version_text),
        normalize_semantic_version(installed_version_text),
    }
    if len(versions) != 1:
        raise _fail("CLI receipt release and executable versions do not match.")

    asset_url = _require_receipt_string(
        receipt,
        "assetUrl",
        maximum=_MAX_URL_CHARACTERS,
    )
    validate_official_asset_url(
        asset_url,
        expected_release_version=release_version,
    )
    if urlsplit(asset_url).path.rsplit("/", 1)[-1] != asset_name:
        raise _fail("CLI receipt assetName does not match its official asset URL.")

    download_sha256 = validate_sha256(receipt["downloadSha256"])
    installed_sha256 = validate_sha256(receipt["installedSha256"])
    validate_sha256(receipt["previousSha256"])
    if download_sha256 != installed_sha256:
        raise _fail("Downloaded and installed Foxglove CLI hashes do not match.")

    installed_path = _normalize_windows_path(
        _require_receipt_string(
            receipt,
            "installedPath",
            maximum=_MAX_WINDOWS_PATH_CHARACTERS,
        ),
        "CLI receipt installedPath",
        allow_pathlike=False,
    )
    backup_path = _normalize_windows_path(
        _require_receipt_string(
            receipt,
            "backupPath",
            maximum=_MAX_WINDOWS_PATH_CHARACTERS,
        ),
        "CLI receipt backupPath",
        allow_pathlike=False,
    )
    if installed_path == backup_path:
        raise _fail("CLI receipt backupPath must differ from installedPath.")
    _validate_installed_utc(receipt["installedUtc"])

    return dict(receipt), release_version, installed_path, installed_sha256


def validate_cli_receipt(
    receipt: object,
    installed_path: os.PathLike[str] | str,
    installed_version: object,
    installed_sha256: object,
) -> dict[str, object]:
    """Validate a receipt against the exact currently installed CLI."""

    validated, receipt_version, receipt_path, receipt_sha256 = _validate_receipt(
        receipt
    )
    live_path = _normalize_windows_path(
        installed_path,
        "Installed Foxglove CLI path",
        allow_pathlike=True,
    )
    live_version = normalize_semantic_version(installed_version)
    live_sha256 = validate_sha256(installed_sha256)
    if live_path != receipt_path:
        raise _fail("Installed Foxglove CLI path does not match the receipt.")
    if live_version != receipt_version:
        raise _fail("Installed Foxglove CLI version does not match the receipt.")
    if live_sha256 != receipt_sha256:
        raise _fail("Installed Foxglove CLI hash does not match the receipt.")
    return validated


class _DuplicateJsonKey(ValueError):
    pass


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateJsonKey("Duplicate JSON key.")
        result[key] = value
    return result


def load_json_bounded(
    path: os.PathLike[str] | str,
    *,
    max_bytes: int = MAX_RECEIPT_BYTES,
) -> object:
    """Load one UTF-8 JSON document without reading beyond its fixed bound."""

    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RECEIPT_BYTES
    ):
        raise _fail("JSON size bound is invalid.")
    try:
        with pathlib.Path(path).open("rb") as stream:
            raw = stream.read(max_bytes + 1)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("CLI receipt is unavailable.") from exc
    if not raw or len(raw) > max_bytes:
        raise _fail("CLI receipt size is invalid.")
    try:
        text = raw.decode("utf-8")
        return json.loads(text, object_pairs_hook=_unique_object)
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise _fail("CLI receipt JSON is malformed.") from exc


def load_cli_receipt(path: os.PathLike[str] | str) -> dict[str, object]:
    """Load and intrinsically validate one bounded CLI installation receipt."""

    receipt = load_json_bounded(path)
    validated, _, _, _ = _validate_receipt(receipt)
    return validated


def write_json_atomic(
    destination: os.PathLike[str] | str,
    payload: Mapping[str, Any],
    *,
    max_bytes: int = MAX_RECEIPT_BYTES,
) -> None:
    """Write deterministic bounded JSON through an atomic sibling replace."""

    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RECEIPT_BYTES
    ):
        raise _fail("JSON size bound is invalid.")
    if not isinstance(payload, Mapping):
        raise _fail("JSON payload must be an object.")
    try:
        serialized = (
            json.dumps(
                dict(payload),
                allow_nan=False,
                ensure_ascii=True,
                separators=(",", ":"),
                sort_keys=True,
            )
            + "\n"
        ).encode("utf-8")
    except (TypeError, ValueError) as exc:
        raise _fail("JSON payload is not serializable.") from exc
    if len(serialized) > max_bytes:
        raise _fail("JSON payload exceeds the fixed size bound.")

    try:
        target = pathlib.Path(destination)
        target.parent.mkdir(parents=True, exist_ok=True)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("JSON destination is invalid.") from exc
    temporary = target.with_name(f"{target.name}.{uuid.uuid4().hex}.tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(serialized)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, target)
    except OSError as exc:
        raise _fail("JSON document could not be written atomically.") from exc
    finally:
        with contextlib.suppress(OSError):
            temporary.unlink()


def _client_failure(message: object) -> AcceptanceFailure:
    return AcceptanceFailure(FAIL_CLIENT, message)


def _evidence_failure(message: object) -> AcceptanceFailure:
    return AcceptanceFailure(FAIL_EVIDENCE, message)


def _is_reparse_point(info: os.stat_result) -> bool:
    attributes = getattr(info, "st_file_attributes", 0)
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return bool(attributes & reparse_flag)


def _plain_resolved_output_root(value: object) -> pathlib.Path:
    try:
        raw = os.fspath(value)
    except TypeError as exc:
        raise _client_failure("Desktop barrier output root is invalid.") from exc
    if (
        not isinstance(raw, str)
        or not raw
        or len(raw) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in raw
        or "\r" in raw
        or "\n" in raw
    ):
        raise _client_failure("Desktop barrier output root is invalid.")

    candidate = pathlib.Path(raw)
    if not candidate.is_absolute() or ".." in candidate.parts:
        raise _client_failure("Desktop barrier output root is invalid.")
    try:
        info = candidate.lstat()
        resolved = candidate.resolve(strict=True)
        absolute = candidate.absolute()
    except OSError as exc:
        raise _client_failure("Desktop barrier output root is unavailable.") from exc
    if (
        not stat.S_ISDIR(info.st_mode)
        or stat.S_ISLNK(info.st_mode)
        or _is_reparse_point(info)
        or absolute != resolved
    ):
        raise _client_failure(
            "Desktop barrier output root must be one plain owned directory."
        )
    return resolved


def resolve_desktop_client_barrier_path(
    output_root: os.PathLike[str] | str,
) -> pathlib.Path:
    """Return the one fixed barrier path below a plain owned output root."""

    return _plain_resolved_output_root(output_root) / DESKTOP_CLIENT_BARRIER_FILENAME


@dataclasses.dataclass(frozen=True, slots=True)
class _DesktopBarrierContract:
    output_root: pathlib.Path
    run_id: str
    token_digest: str
    positive_seconds: int


def _desktop_barrier_contract(config: object) -> _DesktopBarrierContract:
    if not isinstance(config, Mapping):
        raise _client_failure("Desktop barrier configuration is invalid.")

    run_id = config.get("runId")
    token = config.get("token")
    windows = config.get("observationWindows")
    if not isinstance(run_id, str) or _SAFE_RUN_ID.fullmatch(run_id) is None:
        raise _client_failure("Desktop barrier run identity is invalid.")
    if not isinstance(token, str) or _SAFE_TOKEN.fullmatch(token) is None:
        raise _client_failure("Desktop barrier token identity is invalid.")
    if not isinstance(windows, Mapping):
        raise _client_failure("Desktop barrier observation window is invalid.")
    positive_seconds = windows.get("positiveSeconds")
    if (
        isinstance(positive_seconds, bool)
        or not isinstance(positive_seconds, int)
        or positive_seconds < 1
        or positive_seconds > _MAX_OBSERVATION_SECONDS
    ):
        raise _client_failure("Desktop barrier observation window is invalid.")

    output_root = _plain_resolved_output_root(config.get("outputRoot"))
    token_digest = hashlib.sha256(token.encode("utf-8")).hexdigest().upper()
    return _DesktopBarrierContract(
        output_root=output_root,
        run_id=run_id,
        token_digest=token_digest,
        positive_seconds=positive_seconds,
    )


def _exact_desktop_barrier_path(
    value: os.PathLike[str] | str,
    output_root: pathlib.Path,
) -> pathlib.Path:
    try:
        raw = os.fspath(value)
    except TypeError as exc:
        raise _client_failure("Desktop barrier path is invalid.") from exc
    if (
        not isinstance(raw, str)
        or not raw
        or len(raw) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in raw
        or "\r" in raw
        or "\n" in raw
    ):
        raise _client_failure("Desktop barrier path is invalid.")

    candidate = pathlib.Path(raw)
    expected = output_root / DESKTOP_CLIENT_BARRIER_FILENAME
    if (
        not candidate.is_absolute()
        or ".." in candidate.parts
        or candidate.name != DESKTOP_CLIENT_BARRIER_FILENAME
        or candidate.parent != output_root
        or candidate != expected
    ):
        raise _client_failure(
            "Desktop barrier path must be the fixed owned output path."
        )
    try:
        if candidate.resolve(strict=False) != expected:
            raise _client_failure(
                "Desktop barrier path must not use a filesystem alias."
            )
    except OSError as exc:
        raise _client_failure("Desktop barrier path could not be resolved.") from exc
    return expected


def _same_file_snapshot(left: os.stat_result, right: os.stat_result) -> bool:
    return (
        left.st_dev,
        left.st_ino,
        left.st_size,
        left.st_mtime_ns,
    ) == (
        right.st_dev,
        right.st_ino,
        right.st_size,
        right.st_mtime_ns,
    )


def _load_visible_desktop_barrier(
    path: pathlib.Path,
    contract: _DesktopBarrierContract,
) -> dict[str, object] | None:
    try:
        before = path.lstat()
    except FileNotFoundError:
        return None
    except OSError as exc:
        raise _client_failure("Desktop barrier could not be inspected.") from exc

    if (
        not stat.S_ISREG(before.st_mode)
        or stat.S_ISLNK(before.st_mode)
        or _is_reparse_point(before)
    ):
        raise _client_failure("Desktop barrier must be one plain regular file.")

    try:
        with path.open("rb") as stream:
            opened = os.fstat(stream.fileno())
            raw = stream.read(MAX_DESKTOP_CLIENT_BARRIER_BYTES + 1)
        after = path.lstat()
    except OSError as exc:
        raise _client_failure("Desktop barrier could not be read.") from exc
    if (
        not _same_file_snapshot(before, opened)
        or not _same_file_snapshot(opened, after)
        or stat.S_ISLNK(after.st_mode)
        or _is_reparse_point(after)
    ):
        raise _client_failure("Desktop barrier changed while it was read.")
    if not raw or len(raw) > MAX_DESKTOP_CLIENT_BARRIER_BYTES:
        raise _client_failure("Desktop barrier size is invalid.")

    try:
        text = raw.decode("utf-8")
        document = json.loads(text, object_pairs_hook=_unique_object)
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise _client_failure("Desktop barrier JSON is malformed.") from exc
    if not isinstance(document, dict):
        raise _client_failure("Desktop barrier root must be an object.")
    if frozenset(document) != DESKTOP_CLIENT_BARRIER_KEYS:
        raise _client_failure("Desktop barrier keys do not match schemaVersion 1.")

    schema_version = document["schemaVersion"]
    accepted_clients = document["acceptedClients"]
    if (
        isinstance(schema_version, bool)
        or not isinstance(schema_version, int)
        or schema_version != DESKTOP_CLIENT_BARRIER_SCHEMA_VERSION
    ):
        raise _client_failure("Desktop barrier schemaVersion is unsupported.")
    if (
        not isinstance(document["runId"], str)
        or document["runId"] != contract.run_id
    ):
        raise _client_failure("Desktop barrier run identity is stale or mismatched.")
    token_digest = document["tokenDigest"]
    if (
        not isinstance(token_digest, str)
        or _UPPER_SHA256.fullmatch(token_digest) is None
        or token_digest != contract.token_digest
    ):
        raise _client_failure("Desktop barrier token identity is stale or mismatched.")
    if (
        not isinstance(document["state"], str)
        or document["state"] != DESKTOP_CLIENT_BARRIER_STATE
    ):
        raise _client_failure("Desktop barrier state is invalid.")
    if (
        isinstance(accepted_clients, bool)
        or not isinstance(accepted_clients, int)
        or accepted_clients != 1
    ):
        raise _client_failure("Desktop barrier acceptedClients must equal one.")
    return document


def _monotonic_value(clock: Callable[[], float]) -> float:
    try:
        value = clock()
    except Exception as exc:
        raise _client_failure("Desktop barrier clock failed.") from exc
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
    ):
        raise _client_failure("Desktop barrier clock is invalid.")
    return float(value)


def wait_for_desktop_barrier(
    config: Mapping[str, Any],
    path: os.PathLike[str] | str,
    *,
    clock: Callable[[], float] | None = None,
    sleep: Callable[[float], None] | None = None,
    deadline: float | None = None,
) -> dict[str, object]:
    """Wait for one exact token-bound Desktop barrier without doing I/O elsewhere."""

    contract = _desktop_barrier_contract(config)
    barrier = _exact_desktop_barrier_path(path, contract.output_root)
    selected_clock = time.monotonic if clock is None else clock
    selected_sleep = time.sleep if sleep is None else sleep
    if not callable(selected_clock) or not callable(selected_sleep):
        raise _client_failure("Desktop barrier wait dependencies are invalid.")

    started = _monotonic_value(selected_clock)
    bounded_deadline = (
        started
        + contract.positive_seconds
        + DESKTOP_CLIENT_BARRIER_STARTUP_ALLOWANCE_SECONDS
    )
    if deadline is not None:
        if (
            isinstance(deadline, bool)
            or not isinstance(deadline, (int, float))
            or not math.isfinite(float(deadline))
        ):
            raise _client_failure("Desktop barrier deadline is invalid.")
        bounded_deadline = min(bounded_deadline, float(deadline))

    while True:
        now = _monotonic_value(selected_clock)
        if now >= bounded_deadline:
            raise _client_failure(
                "Desktop client barrier did not appear before the bounded deadline."
            )

        document = _load_visible_desktop_barrier(barrier, contract)
        if document is not None:
            return document

        remaining = bounded_deadline - now
        delay = min(DESKTOP_CLIENT_BARRIER_POLL_SECONDS, remaining)
        try:
            selected_sleep(delay)
        except Exception as exc:
            raise _client_failure("Desktop barrier polling failed.") from exc


@dataclasses.dataclass(frozen=True, slots=True)
class TransportClientMarker:
    """One validated transport-count marker with token identity discarded."""

    overflow: bool
    active: int
    accepted: int

    def to_document(self) -> dict[str, int]:
        return {"active": self.active, "accepted": self.accepted}


def _validate_transport_identity(case: object, token: object) -> tuple[str, str]:
    if not isinstance(case, str) or _SAFE_CASE.fullmatch(case) is None:
        raise _evidence_failure("Transport marker case identity is invalid.")
    if not isinstance(token, str) or _SAFE_TOKEN.fullmatch(token) is None:
        raise _evidence_failure("Transport marker token identity is invalid.")
    return case, token


def parse_transport_client_marker(
    line: object,
    *,
    case: str,
    token: str,
) -> TransportClientMarker:
    """Parse one exact bounded token-correlated transport-count marker."""

    expected_case, expected_token = _validate_transport_identity(case, token)
    if not isinstance(line, str) or "\r" in line or "\n" in line:
        raise _evidence_failure("Transport client marker line is invalid.")
    try:
        encoded_size = len(line.encode("utf-8"))
    except UnicodeError as exc:
        raise _evidence_failure("Transport client marker line is invalid.") from exc
    if not line or encoded_size > MAX_TRANSPORT_CLIENT_MARKER_BYTES:
        raise _evidence_failure("Transport client marker line exceeds its fixed bound.")

    match = _TRANSPORT_CLIENT_MARKER.fullmatch(line)
    if match is None:
        raise _evidence_failure("Transport client marker envelope is malformed.")
    if (
        match.group("case") != expected_case
        or match.group("token") != expected_token
    ):
        raise _evidence_failure("Transport client marker identity is mismatched.")

    active = int(match.group("active"))
    accepted = int(match.group("accepted"))
    if active > MAX_TRANSPORT_CLIENT_COUNT or accepted > MAX_TRANSPORT_CLIENT_COUNT:
        raise _evidence_failure("Transport client marker count exceeds its fixed bound.")
    return TransportClientMarker(
        overflow=match.group("marker") == TRANSPORT_CLIENTS_OVERFLOW_MARKER,
        active=active,
        accepted=accepted,
    )


def validate_transport_client_transition_order(
    lines: Iterable[str],
    *,
    case: str,
    token: str,
) -> tuple[TransportClientMarker, TransportClientMarker, TransportClientMarker]:
    """Require the exact chronological 0/0 -> 1/1 -> 2/2+ live transition."""

    _validate_transport_identity(case, token)
    if isinstance(lines, (str, bytes)):
        raise _evidence_failure("Transport marker evidence must be a line sequence.")
    try:
        iterator = iter(lines)
    except TypeError as exc:
        raise _evidence_failure(
            "Transport marker evidence must be a line sequence."
        ) from exc

    required: list[TransportClientMarker] = []
    previous_line: str | None = None
    marker_line_count = 0
    collapsed_count = 0
    for line in iterator:
        if not isinstance(line, str):
            raise _evidence_failure("Transport marker evidence line is invalid.")
        if not line.startswith("PHASE184H_TRANSPORT_CLIENT"):
            continue
        marker = parse_transport_client_marker(line, case=case, token=token)
        if marker.overflow:
            raise _evidence_failure("Transport client marker overflow was observed.")

        marker_line_count += 1
        if marker_line_count > MAX_TRANSPORT_CLIENT_MARKERS:
            raise _evidence_failure(
                "Transport client marker evidence exceeds its fixed bound."
            )
        if line == previous_line:
            continue
        previous_line = line
        collapsed_count += 1
        if collapsed_count > MAX_TRANSPORT_CLIENT_MARKERS:
            raise _evidence_failure(
                "Transport client marker evidence exceeds its fixed bound."
            )

        pair = (marker.active, marker.accepted)
        stage = len(required)
        if stage == 0:
            matches = pair == (0, 0)
        elif stage == 1:
            matches = pair == (1, 1)
        elif stage == 2:
            matches = marker.active == 2 and marker.accepted >= 2
        else:
            matches = False
        if not matches:
            raise _evidence_failure(
                "Transport client markers are missing the required strict order."
            )
        required.append(marker)

    if len(required) != 3:
        raise _evidence_failure(
            "Transport client markers are missing the required strict order."
        )
    return required[0], required[1], required[2]
