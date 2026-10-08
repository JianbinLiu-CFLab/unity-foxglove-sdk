#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138m_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def image_republisher_main(args: argparse.Namespace) -> int:
    """Decode CompressedImage and publish raw Image for RViz2."""

    print(f"[phase138m-rviz-image] starting source={args.image_topic} raw={args.raw_image_topic}", flush=True)
    import cv2
    print("[phase138m-rviz-image] imported cv2", flush=True)
    import numpy as np
    print("[phase138m-rviz-image] imported numpy", flush=True)
    import rclpy
    print("[phase138m-rviz-image] imported rclpy", flush=True)
    from rclpy.node import Node
    from sensor_msgs.msg import CompressedImage, Image
    print("[phase138m-rviz-image] imported sensor_msgs", flush=True)

    class ImageRepublisher(Node):
        """CompressedImage to raw Image bridge for RViz2 display."""

        def __init__(self) -> None:
            """Create publisher/subscriber state for one RViz helper node."""
            super().__init__("phase138m_rviz_image_republisher")
            self.publisher = self.create_publisher(Image, args.raw_image_topic, 10)
            self.subscription = self.create_subscription(CompressedImage, args.image_topic, self.on_image, 10)
            self.count = 0

        def on_image(self, msg: CompressedImage) -> None:
            """Decode one CompressedImage and republish it as raw Image."""
            encoded = np.frombuffer(bytes(msg.data), dtype=np.uint8)
            bgr = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
            if bgr is None:
                return
            rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)

            out = Image()
            out.header = msg.header
            out.height = int(rgb.shape[0])
            out.width = int(rgb.shape[1])
            out.encoding = "rgb8"
            out.is_bigendian = False
            out.step = int(rgb.shape[1] * 3)
            out.data = rgb.tobytes()
            self.publisher.publish(out)
            self.count += 1
            if self.count == 1 or self.count % 60 == 0:
                self.get_logger().info(
                    f"republished {self.count} frame(s) {args.image_topic} -> {args.raw_image_topic} "
                    f"({out.width}x{out.height})"
                )

    rclpy.init()
    print("[phase138m-rviz-image] rclpy initialized", flush=True)
    node = ImageRepublisher()
    print(f"[phase138m-rviz-image] ready source={args.image_topic} raw={args.raw_image_topic}", flush=True)
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()
    return 0

__all__ = [name for name in globals() if not name.startswith("__")]
