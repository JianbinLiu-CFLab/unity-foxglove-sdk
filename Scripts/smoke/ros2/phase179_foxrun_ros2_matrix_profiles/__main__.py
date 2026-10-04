"""Select and run one named Phase179 ROS2 matrix profile."""

from __future__ import annotations

import argparse
import sys

from . import PROFILES, profile_wrapper_argv, run_profile


def main(argv: list[str] | None = None) -> int:
    """Run a named profile while preserving the wrapper defaults."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile_id", choices=sorted(PROFILES))
    args, remainder = parser.parse_known_args(sys.argv[1:] if argv is None else argv)
    return run_profile(args.profile_id, profile_wrapper_argv(args.profile_id, remainder))


if __name__ == "__main__":
    raise SystemExit(main())
