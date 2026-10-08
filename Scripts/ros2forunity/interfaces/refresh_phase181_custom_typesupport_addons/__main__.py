#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Executable entrypoint for the decomposed Phase181 add-on refresh helper."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
