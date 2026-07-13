#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Independent binary Protobuf control for the FoxRun Publish panel acceptance.
# Usage: python Scripts/smoke/websocket/phase176_foxrun_publish_panel_probe.py --port 8765 --value 10

"""Run the Phase175 binary Protobuf control used to validate FoxRun Publish.

The custom panel must produce the same explicit client ``protobuf`` advertisement
and MessageData bytes. This wrapper intentionally reuses the Phase175 control
implementation instead of maintaining a second protocol encoder.
"""

from __future__ import annotations

from phase175_protobuf_inbound_publish import main as phase175_main


def main() -> int:
    """Run the independently maintained direct Protobuf acceptance client."""
    return phase175_main()


if __name__ == "__main__":
    raise SystemExit(main())
