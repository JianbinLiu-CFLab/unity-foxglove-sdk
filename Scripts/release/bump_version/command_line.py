from __future__ import annotations
from .version_bump import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "bump_version.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for the version-bump workflow."""
    parser = argparse.ArgumentParser(description="Synchronize Unity2Foxglove package version references.")
    parser.add_argument("version", help="Target semantic version, for example 1.2.0.")
    parser.add_argument(
        "--date",
        type=parse_release_date,
        default=date.today().isoformat(),
        help="Release date for new changelog/release notes.",
    )
    parser.add_argument("--dry-run", action="store_true", help="Print planned changes without writing files.")
    return parser.parse_args()


def main() -> int:
    """Validate CLI input and run the package-version synchronization."""
    args = parse_args()
    if not VERSION_RE.match(args.version):
        raise SystemExit(f"Invalid version '{args.version}'. Expected MAJOR.MINOR.PATCH.")

    root = resolve_repo_root()
    return VersionBump(root, args.version, args.date, args.dry_run).run()


__all__ = [name for name in globals() if not name.startswith("__")]
