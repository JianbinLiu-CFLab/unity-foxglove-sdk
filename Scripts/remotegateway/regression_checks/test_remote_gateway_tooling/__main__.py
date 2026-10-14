#!/usr/bin/env python3
"""Executable entrypoint for decomposed remote-gateway tests."""

from __future__ import annotations

import unittest

if __name__ == "__main__":
    raise SystemExit(unittest.main(module="Scripts.remotegateway.regression_checks.test_remote_gateway_tooling"))
