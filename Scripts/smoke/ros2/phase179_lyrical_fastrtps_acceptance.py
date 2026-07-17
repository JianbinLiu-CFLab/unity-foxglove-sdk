#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Run Lyrical/FastDDS local Unity acceptance by default, with explicit advanced interop roles."""

from __future__ import annotations

import sys

from phase179_foxrun_ros2_matrix_profiles import profile_wrapper_argv, run_profile


PROFILE_ID = "lyrical-fastrtps"


if __name__ == "__main__":
    raise SystemExit(run_profile(PROFILE_ID, profile_wrapper_argv(PROFILE_ID, sys.argv[1:])))
