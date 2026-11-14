#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Executable entrypoint for the decomposed Phase179 Player host helper."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase179-player-host] FAIL: {exc}", file=sys.stderr)
        print("[phase179-player-host] Check the Player log, stale ROS2/Zenoh processes, and the supplied runtime selection.", file=sys.stderr)
        raise SystemExit(1)
