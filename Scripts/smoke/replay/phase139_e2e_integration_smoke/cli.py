"""Phase139 command-line orchestration."""

from __future__ import annotations

import asyncio
import sys
from typing import Any

from .argument_contract import *
from .evidence_reporting import *
from .protocol_surface import *
from .summary_models import *
from .websocket_collection import *


def main(argv: list[str]) -> int:
    """Entry point."""

    parser = build_parser()
    args = parser.parse_args(argv)

    if args.self_test:
        summary = self_test_summary(args)
        write_json(args.json_out, summary)
        print_summary(summary)
        return 0

    mcap = inspect_mcap(args.mcap)
    ros2_native = ros2_native_summary(args)

    try:
        if args.mode == "ros2-native":
            summary = make_summary(
                args=args,
                status="limited",
                topics={},
                limitations=["ROS2 Native graph evidence is delegated to the Phase138S/T/U direct subscriber helpers."],
                mcap=mcap,
                ros2_native=ros2_native,
            )
        else:
            status, topics, limitations = asyncio.run(run_websocket_core(args))
            if args.mode == "both":
                limitations.append("ROS2 Native profile was summarized but not launched by this WebSocket pass.")
            summary = make_summary(
                args=args,
                status=status,
                topics=topics,
                limitations=limitations,
                mcap=mcap,
                ros2_native=ros2_native if args.mode == "both" else None,
            )
    except Exception as exc:
        summary = make_summary(
            args=args,
            status="fail",
            topics={},
            limitations=[str(exc)],
            mcap=mcap,
            ros2_native=ros2_native if args.mode in ("ros2-native", "both") else None,
        )

    write_json(args.json_out, summary)
    print_summary(summary)
    return determine_exit_code(summary, args.allow_ros2_skipped)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))


__all__ = [name for name in globals() if not name.startswith("__")]
