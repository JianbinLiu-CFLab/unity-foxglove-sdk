from __future__ import annotations
from .foundation import *

def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: ros2-windows\\ros2_jazzy",
    )
    parser.add_argument(
        "--rviz-config",
        default=str(DEFAULT_RVIZ_CONFIG),
        help="RViz2 config path to launch or print in evidence.",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=120.0,
        help="Compatibility timeout for publisher preflight. Echo remains the hard acceptance gate.",
    )
    parser.add_argument(
        "--topic-info-timeout-seconds",
        type=float,
        default=2.0,
        help="Per-topic timeout for optional verbose topic-info diagnostics before echo validation.",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=20.0,
        help="ROS2 spin time for each bounded echo.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation to use. Omit to preserve RMW_IMPLEMENTATION or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID to use for the helper. Omit to use domain 0.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="LOCALHOST",
        help="ROS_AUTOMATIC_DISCOVERY_RANGE for same-machine Unity acceptance. Default: LOCALHOST.",
    )
    launch_group = parser.add_mutually_exclusive_group()
    launch_group.add_argument(
        "--launch-rviz",
        dest="launch_rviz",
        action="store_true",
        help="Launch RViz2 with --rviz-config after the rclpy message checks. This is the default.",
    )
    launch_group.add_argument(
        "--no-launch-rviz",
        dest="launch_rviz",
        action="store_false",
        help="Run ROS2 graph and echo checks without launching RViz2.",
    )
    parser.set_defaults(launch_rviz=True)
    parser.add_argument(
        "--rviz-startup-check-seconds",
        type=float,
        default=1.5,
        help="Seconds to wait for an immediate RViz2 process exit after launch.",
    )
    parser.add_argument(
        "--rviz-window-wait-seconds",
        type=float,
        default=45.0,
        help="Seconds to wait for a visible RViz2 window after launch.",
    )
    parser.add_argument(
        "--graph-diagnostics",
        action="store_true",
        help="Run optional ros2 node/topic graph diagnostics before the rclpy collection gate.",
    )
    return parser.parse_args(argv)


def require_tokens(label: str, output: str, tokens: list[str]) -> None:
    """Raise when any required token is absent from output."""

    missing = [token for token in tokens if token not in output]
    if missing:
        raise RuntimeError(f"{label} missing required token(s): {', '.join(missing)}\n{output}")


def require_regex(label: str, output: str, pattern: str, message: str) -> re.Match[str]:
    """Return the regex match or raise with context."""

    match = re.search(pattern, output, re.MULTILINE)
    if not match:
        raise RuntimeError(f"{label}: {message}\n{output}")
    return match


def parse_positive_int(label: str, output: str, field: str) -> int:
    """Parse a positive integer field from ROS2 YAML echo output."""

    match = require_regex(label, output, rf"^\s*{re.escape(field)}:\s*([1-9][0-9]*)\s*$", f"missing positive {field}")
    return int(match.group(1))


def parse_float(label: str, output: str, field: str) -> float:
    """Parse a floating-point field from ROS2 YAML echo output."""

    match = require_regex(
        label,
        output,
        rf"^\s*{re.escape(field)}:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*$",
        f"missing numeric {field}",
    )
    return float(match.group(1))


def count_yaml_array_numbers_after(output: str, field: str) -> int:
    """Count YAML list scalar numbers following a top-level array field."""

    start = output.find(field + ":")
    if start < 0:
        return 0
    section = output[start + len(field) + 1 :]
    next_top_level = re.search(r"(?m)^[a-zA-Z_][a-zA-Z0-9_]*:", section)
    if next_top_level:
        section = section[: next_top_level.start()]
    return len(re.findall(rf"(?m)^-\s*{YAML_NUMBER_RE}\s*$", section))


def collect_phase132_messages(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float,
) -> dict[str, dict[str, object]]:
    """Collect one message from every Phase132 topic using rclpy in the ROS2 runtime."""

    if not PHASE132_RCLPY_PROBE_PATH.exists():
        raise FileNotFoundError(f"Phase132 rclpy probe script does not exist: {PHASE132_RCLPY_PROBE_PATH}")

    try:
        completed = subprocess.run(
            [str(pixi_python), str(PHASE132_RCLPY_PROBE_PATH), str(timeout_seconds)],
            env=env,
            capture_output=True,
            text=True,
            timeout=timeout_seconds + 10.0,
            check=False,
        )
        stdout = completed.stdout
        stderr = completed.stderr
        returncode = completed.returncode
    except subprocess.TimeoutExpired as exc:
        stdout = exc.stdout or ""
        stderr = exc.stderr or ""
        if isinstance(stdout, bytes):
            stdout = stdout.decode(errors="replace")
        if isinstance(stderr, bytes):
            stderr = stderr.decode(errors="replace")
        returncode = 124

    prefix = "PHASE132_PROBE_JSON="
    payload_line = next((line[len(prefix) :] for line in stdout.splitlines() if line.startswith(prefix)), None)
    if payload_line is None:
        raise RuntimeError(
            "Phase132 rclpy probe did not produce JSON output.\n"
            f"exit={returncode}\nstdout:\n{stdout}\nstderr:\n{stderr}"
        )

    payload = json.loads(payload_line)
    missing = payload.get("missing") or []
    messages = payload.get("messages") or {}
    if missing:
        raise RuntimeError(
            "Phase132 rclpy probe timed out before receiving all topics: "
            + ", ".join(str(topic) for topic in missing)
            + f"\nstdout:\n{stdout}\nstderr:\n{stderr}"
        )
    if returncode != 0:
        raise RuntimeError(
            f"Phase132 rclpy probe failed with exit code {returncode}.\n"
            f"stdout:\n{stdout}\nstderr:\n{stderr}"
        )
    return messages


__all__ = [name for name in globals() if not name.startswith("__")]
