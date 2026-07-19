"""Deterministic Phase181 static-interface digest framing.

This module intentionally has no ROS, Unity, local-path, or build-tool
dependency. It mirrors ``FoxRunRos2InterfaceDigest`` byte-for-byte so an
operator can verify a source package before a Linux colcon build.
"""

from __future__ import annotations

import hashlib
import argparse
import json
from dataclasses import dataclass
from pathlib import Path
import sys
from typing import Iterable, Union


INTERFACE_SCHEMA_VERSION = 1
_DOMAIN = b"unity2foxglove:foxrun-ros2-interface-digest:v1"
_LOCK_RELATIVE_PATH = "RuntimeSupport/foxrun-ros2-interface-lock.json"
_FIXED_GENERATED_PATHS = (
    "package.json",
    "README.md",
    "RuntimeSupport/foxrun-ros2-interface-settings.json",
    "Ros2Package~/package.xml",
    "Ros2Package~/CMakeLists.txt",
)


@dataclass(frozen=True)
class DigestInput:
    """Represent DigestInput."""
    relative_path: str
    content: Union[str, bytes]


def normalize_relative_path(relative_path: str) -> str:
    """Run normalize relative path."""
    normalized = (relative_path or "").replace("\\", "/")
    if (
        not normalized
        or normalized.startswith("/")
        or normalized.endswith("/")
        or "//" in normalized
    ):
        raise ValueError("digest paths must be normalized relative package paths")
    if any(segment in {"", ".", ".."} for segment in normalized.split("/")):
        raise ValueError("digest paths cannot escape the package root")
    return normalized


def encode_text(value: str) -> bytes:
    """Run encode text."""
    return (value or "").replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")


def compute(schema_version: int, inputs: Iterable[DigestInput]) -> str:
    """Run compute."""
    if schema_version != INTERFACE_SCHEMA_VERSION:
        raise ValueError("the digest framing accepts only the current interface schema version")

    normalized = []
    seen_casefolded = set()
    for item in inputs or ():
        if item is None:
            raise ValueError("digest inputs cannot contain null")
        path = normalize_relative_path(item.relative_path)
        collision_key = path.casefold()
        if collision_key in seen_casefolded:
            raise ValueError("digest inputs contain duplicate or case-colliding paths: " + path)
        seen_casefolded.add(collision_key)
        content = item.content
        normalized.append((path, encode_text(content) if isinstance(content, str) else bytes(content)))

    stream = bytearray()
    _append_frame(stream, _DOMAIN)
    _append_frame(stream, str(schema_version).encode("ascii"))
    for path, content in sorted(normalized, key=lambda value: value[0]):
        _append_frame(stream, path.encode("utf-8"))
        _append_frame(stream, content)
    return hashlib.sha256(stream).hexdigest()


def _append_frame(stream: bytearray, value: bytes) -> None:
    """Implement the internal append frame step."""
    stream.extend(len(value).to_bytes(8, byteorder="big", signed=False))
    stream.extend(value)


def verify_package(package_root: Union[str, Path]) -> str:
    """Verify the source-only package bytes recorded by its checked-in lock.

    Unity-generated ``.meta`` files and unrelated workspace files are not
    digest inputs. The lock defines the payload/envelope message set, while
    this function checks every generated source file that the C# renderer
    includes in its interface digest.
    """

    root = Path(package_root)
    lock_path = root / _LOCK_RELATIVE_PATH
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError("could not read the static interface lock") from error

    if not isinstance(lock, dict):
        raise ValueError("the static interface lock must be an object")
    schema_version = lock.get("interfaceSchemaVersion")
    expected_digest = lock.get("interfaceDigest")
    contracts = lock.get("contracts")
    if schema_version != INTERFACE_SCHEMA_VERSION:
        raise ValueError("the source package uses an unsupported interface schema version")
    if not isinstance(expected_digest, str) or len(expected_digest) != 64:
        raise ValueError("the source package lock has no valid interface digest")
    if not isinstance(contracts, list):
        raise ValueError("the source package lock has no contracts array")

    relative_paths = set(_FIXED_GENERATED_PATHS)
    for contract in contracts:
        if not isinstance(contract, dict):
            raise ValueError("the source package lock contains an invalid contract")
        payload = contract.get("payloadMessageName")
        envelope = contract.get("envelopeMessageName")
        if not isinstance(payload, str) or not isinstance(envelope, str) or not payload or not envelope:
            raise ValueError("the source package lock has an incomplete message mapping")
        if envelope != payload + "Envelope":
            raise ValueError("the source package lock has an invalid envelope mapping")
        relative_paths.add("Ros2Package~/msg/" + payload + ".msg")
        relative_paths.add("Ros2Package~/msg/" + envelope + ".msg")

    # Contract roots are recorded in the lock, but a payload graph can also
    # contain deterministic nested DTO messages. The renderer includes every
    # generated ``.msg`` source in its digest, so the verifier must do the
    # same rather than silently excluding a nested wire type.
    message_root = root / "Ros2Package~" / "msg"
    if message_root.is_dir():
        for message_path in message_root.rglob("*.msg"):
            if message_path.is_file():
                relative_paths.add(message_path.relative_to(root).as_posix())

    inputs = []
    for relative_path in sorted(relative_paths):
        path = root / Path(relative_path)
        try:
            inputs.append(DigestInput(relative_path, path.read_bytes()))
        except OSError as error:
            raise ValueError("missing generated source file: " + relative_path) from error

    actual_digest = compute(schema_version, inputs)
    if actual_digest != expected_digest:
        raise ValueError("static interface source bytes do not match the lock digest")
    return actual_digest


def _default_package_root() -> Path:
    """Implement the internal default package root step."""
    return Path(__file__).resolve().parents[3] / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces"


def main(argv: list[str] | None = None) -> int:
    """Run the command-line entry point."""
    parser = argparse.ArgumentParser(description="Verify a locked FoxRun ROS2 interface source package.")
    parser.add_argument(
        "--package-root",
        type=Path,
        default=_default_package_root(),
        help="source package root containing RuntimeSupport/foxrun-ros2-interface-lock.json",
    )
    args = parser.parse_args(argv)
    try:
        digest = verify_package(args.package_root)
    except ValueError as error:
        print("FAIL: " + str(error), file=sys.stderr)
        return 1
    print("PASS: " + digest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
