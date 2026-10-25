#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Decomposed ROS 2 CDR generator section."""

from __future__ import annotations

from .shared import *

def main() -> int:
    """Run the generator and print a concise summary."""

    args = parse_args()
    print(generate(args.input, args.output_dir))
    return 0



__all__ = [name for name in globals() if not name.startswith("__")]
