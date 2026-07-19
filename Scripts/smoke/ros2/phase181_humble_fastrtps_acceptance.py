#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Run the fixed Phase181 Humble/FastDDS Windows-local Editor acceptance."""

from __future__ import annotations

import sys

from phase181_custom_ros2_matrix_profiles import run_profile


if __name__ == "__main__":
    raise SystemExit(run_profile("humble-fastrtps", sys.argv[1:]))
