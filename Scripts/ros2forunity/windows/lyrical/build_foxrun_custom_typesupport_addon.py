#!/usr/bin/env python3
"""Build the Phase181 Lyrical Win64 custom ROS2 typesupport add-on."""

from __future__ import annotations

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[4]
INTERFACES = ROOT / "Scripts" / "ros2forunity" / "interfaces"
if str(INTERFACES) not in sys.path:
    sys.path.insert(0, str(INTERFACES))

from build_foxrun_custom_typesupport_addon import main  # noqa: E402


if __name__ == "__main__":
    raise SystemExit(main(("--distro", "lyrical", *sys.argv[1:])))
