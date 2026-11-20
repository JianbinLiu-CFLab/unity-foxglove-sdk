"""Restore the pinned third-party repositories required by repository scripts."""

from __future__ import annotations

import argparse
from collections.abc import Mapping, Sequence
from pathlib import Path
import sys


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from Scripts.setup.third_party_restore.configuration import VENDOR_ROOTS
from Scripts.setup.third_party_restore.operations import restore_third_party as _restore_third_party


def restore_third_party(
    repository_root: Path,
    *,
    restore: bool = False,
    source_urls: Mapping[str, str] | None = None,
) -> int:
    """Report or safely restore the pinned third-party repositories."""
    return _restore_third_party(
        repository_root,
        restore=restore,
        source_urls=source_urls,
        vendor_roots=VENDOR_ROOTS,
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Parse command-line options and run the repository-local restore."""
    parser = argparse.ArgumentParser(
        description="Report or restore the pinned third-party repositories."
    )
    parser.add_argument(
        "--restore",
        action="store_true",
        help="initialize missing repositories and fetch their pinned commits",
    )
    arguments = parser.parse_args(argv)
    try:
        return restore_third_party(REPOSITORY_ROOT, restore=arguments.restore)
    except (OSError, RuntimeError, ValueError) as exc:
        print(f"restore_third_party: ERROR {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
