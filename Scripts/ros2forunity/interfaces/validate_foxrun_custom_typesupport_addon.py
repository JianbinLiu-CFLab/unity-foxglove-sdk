"""Validate one tracked Phase181 Win64 custom ROS2 typesupport add-on."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys
from typing import Sequence

try:
    from foxrun_custom_typesupport_common import (
        AddonValidationError,
        AddonValidationRequest,
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        validate_addon,
    )
except ModuleNotFoundError:  # pragma: no cover - package import test path
    from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
        AddonValidationError,
        AddonValidationRequest,
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        validate_addon,
    )


def repository_root() -> Path:
    """Run repository root."""
    return Path(__file__).resolve().parents[3]


def parse_args(argv: Sequence[str] | None = None) -> AddonValidationRequest:
    """Parse command-line arguments."""
    root = repository_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", required=True, choices=SUPPORTED_DISTROS)
    parser.add_argument("--addon-package", type=Path)
    parser.add_argument("--static-interface-package", type=Path)
    parser.add_argument("--base-runtime-package", type=Path)
    parser.add_argument("--require-rmw", action="append", default=[])
    args = parser.parse_args(argv)
    return AddonValidationRequest(
        distro=args.distro,
        addon_package=args.addon_package or root / "Packages" / addon_package_id(args.distro),
        static_interface_package=args.static_interface_package
        or root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces",
        base_runtime_package=args.base_runtime_package or root / "Packages" / base_runtime_package_id(args.distro),
        require_rmws=tuple(args.require_rmw),
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    try:
        result = validate_addon(parse_args(argv))
    except AddonValidationError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    print(
        "PASS: "
        + result.distro
        + " "
        + result.package_id
        + " "
        + result.interface_digest
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
