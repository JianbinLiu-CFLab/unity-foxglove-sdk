from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase138s_imu_native_dds_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def require_regex(label: str, output: str, pattern: str, message: str) -> re.Match[str]:
    """Find a regex match and raise if no match is found."""


    match = re.search(pattern, output, re.MULTILINE)
    if not match:
        raise RuntimeError(f"{label}: {message}\n{output}")
    return match


def parse_timestamp(echo_output: str) -> tuple[int, int]:
    """Parse message timestamp fields from IMU echo output."""


    sec = int(require_regex("imu echo", echo_output, r"^\s*sec:\s*([0-9]+)\s*$", "missing stamp.sec").group(1))
    nanosec = int(
        require_regex("imu echo", echo_output, r"^\s*nanosec:\s*([0-9]+)\s*$", "missing stamp.nanosec").group(1)
    )
    if sec <= 0:
        raise RuntimeError(f"imu echo: stamp.sec must be positive, got {sec}\n{echo_output}")
    if not 0 <= nanosec < 1_000_000_000:
        raise RuntimeError(f"imu echo: stamp.nanosec out of range, got {nanosec}\n{echo_output}")
    return sec, nanosec


def parse_frame_id(echo_output: str) -> str:
    """Extract frame_id value from IMU echo output."""


    return require_regex("imu echo", echo_output, r"^\s*frame_id:\s*['\"]?([^'\"\r\n]+)['\"]?\s*$", "missing frame_id").group(1)


def section_after(output: str, field: str) -> str:
    """Return the text section that follows a top-level field."""


    match = re.search(rf"(?m)^{re.escape(field)}:\s*$", output)
    if not match:
        return ""

    section = output[match.end() :]
    next_top_level = re.search(r"(?m)^[a-zA-Z_][a-zA-Z0-9_]*:", section)
    if next_top_level:
        section = section[: next_top_level.start()]
    return section


def parse_vector(output: str, field: str) -> dict[str, float]:
    """Parse x/y/z vector values from IMU echo output section."""


    section = section_after(output, field)
    if not section:
        raise RuntimeError(f"imu echo: missing {field} section\n{output}")

    values: dict[str, float] = {}
    for axis in ("x", "y", "z"):
        match = require_regex(field, section, rf"^\s*{axis}:\s*({NUMBER_RE})\s*$", f"missing {axis}")
        values[axis] = float(match.group(1))
    return values


def parse_covariance(output: str, field: str) -> list[float]:
    """Parse 9 covariance values from IMU echo output."""


    section = section_after(output, field)
    if not section:
        raise RuntimeError(f"imu echo: missing {field} section\n{output}")

    values = [float(match.group(1)) for match in re.finditer(rf"(?m)^\s*-\s*({NUMBER_RE})\s*$", section)]
    if len(values) != 9:
        raise RuntimeError(f"imu echo: {field} must contain 9 values, got {len(values)}\n{output}")
    return values


def topic_list_has_type(topic_list: str, topic: str, msg_type: str) -> bool:
    """Check whether topic list shows expected type for topic."""


    return f"{topic} [{msg_type}]" in topic_list


def probe_topic_list(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float = 5.0,
) -> str:
    """Invoke ros2-script helper to list topic/type information."""


    try:
        return ros2env.run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "list", "-t", "--no-daemon"],
            check=False,
            timeout_seconds=timeout_seconds,
        ).stdout
    except subprocess.TimeoutExpired:
        return f"<topic list timed out after {timeout_seconds:.1f}s>"


__all__ = [name for name in globals() if not name.startswith("__")]
