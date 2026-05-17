# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, LogInfo
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    host = LaunchConfiguration("host")
    port = LaunchConfiguration("port")
    payload_format = LaunchConfiguration("payload_format")

    return LaunchDescription(
        [
            DeclareLaunchArgument("host", default_value="127.0.0.1"),
            DeclareLaunchArgument("port", default_value="8767"),
            DeclareLaunchArgument("payload_format", default_value="cdr-with-encapsulation"),
            LogInfo(msg=["Starting Unity2Foxglove ROS2 Bridge on ", host, ":", port]),
            Node(
                package="unity2foxglove_ros2_bridge",
                executable="unity2foxglove_ros2_bridge",
                name="unity2foxglove_ros2_bridge",
                output="screen",
                arguments=[
                    "--host",
                    host,
                    "--port",
                    port,
                    "--payload-format",
                    payload_format,
                ],
            ),
        ]
    )
