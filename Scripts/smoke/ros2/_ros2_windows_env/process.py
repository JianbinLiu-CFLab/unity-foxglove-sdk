from __future__ import annotations

from .foundation import *

def wait_for_publisher(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
    expected_type: str | None = None,
    node_name: str | None = None,
    poll_interval_seconds: float = 1.0,
) -> str:
    """Wait until a topic has a publisher endpoint, optionally from a node."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0.0:
            break

        probe_timeout_seconds = max(0.5, min(5.0, remaining))
        try:
            result = run_ros2(
                pixi_python,
                ros2_script,
                env,
                ["topic", "info", "-v", topic, "--no-daemon"],
                check=False,
                timeout_seconds=probe_timeout_seconds,
            )
            last_output = result.stdout
        except subprocess.TimeoutExpired:
            last_output = f"<topic info {topic} timed out after {probe_timeout_seconds:.1f}s>"

        type_ok = expected_type is None or expected_type in last_output
        if type_ok and topic_info_has_publisher(last_output, node_name):
            return last_output

        remaining = deadline - time.monotonic()
        if remaining > 0.0:
            time.sleep(min(poll_interval_seconds, remaining))

    try:
        topic_list = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "list", "-t", "--no-daemon"],
            check=False,
            timeout_seconds=5.0,
        ).stdout
    except subprocess.TimeoutExpired:
        topic_list = "<topic list timed out after 5.0s>"
    node_text = "" if node_name is None else f" from {node_name}"
    raise TimeoutError(
        f"Timed out waiting for publisher{node_text} on {topic}.\n"
        f"Current topic list:\n{topic_list}\nLast topic info:\n{last_output}"
    )


def echo_once(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    msg_type: str,
    spin_seconds: float,
) -> str:
    """Echo one ROS2 message with bounded spin time."""

    return run_ros2(
        pixi_python,
        ros2_script,
        env,
        [
            "topic",
            "echo",
            "--once",
            topic,
            msg_type,
            "--spin-time",
            str(spin_seconds),
            "--no-daemon",
        ],
        timeout_seconds=spin_seconds + 10.0,
    ).stdout




__all__ = ['wait_for_publisher', 'echo_once']
