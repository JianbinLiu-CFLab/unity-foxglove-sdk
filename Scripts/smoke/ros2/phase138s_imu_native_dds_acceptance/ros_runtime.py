from __future__ import annotations
from .parsing_and_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase138s_imu_native_dds_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def run_native_ros2_cli(args: list[str], env: dict[str, str], timeout_seconds: float) -> str:
    """Run a ros2 command in native environment and capture output."""


    ros2_exe = shutil.which("ros2", path=env.get("PATH"))
    if not ros2_exe:
        return "<ros2 command not found on PATH; source ROS2 before relying on graph diagnostics>"

    try:
        return subprocess.run(
            [ros2_exe, *args],
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
            timeout=timeout_seconds,
        ).stdout
    except subprocess.TimeoutExpired:
        return f"<ros2 {' '.join(args)} timed out after {timeout_seconds:.1f}s>"


def build_native_ros_env(args: argparse.Namespace) -> dict[str, str]:
    """Build ROS environment for native (non-Windows-helper) runs."""


    env = os.environ.copy()
    env["ROS_VERSION"] = env.get("ROS_VERSION") or "2"
    env["ROS_PYTHON_VERSION"] = env.get("ROS_PYTHON_VERSION") or "3"
    env["ROS_DOMAIN_ID"] = str(args.domain_id) if args.domain_id is not None else env.get("ROS_DOMAIN_ID", "0")
    env["RMW_IMPLEMENTATION"] = args.rmw or env.get("RMW_IMPLEMENTATION") or "rmw_fastrtps_cpp"
    if args.discovery_range:
        env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = args.discovery_range
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)
    return env


def topic_info_has_publisher(topic_info: str, node_name: str | None = None) -> bool:
    """Return true if topic info includes an active publisher."""


    publisher_match = re.search(r"Publisher count:\s*([1-9][0-9]*)", topic_info)
    if not publisher_match:
        return False
    return node_name is None or f"Node name: {node_name}" in topic_info


def wait_for_native_publisher(
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
    expected_type: str,
    node_name: str | None,
) -> str:
    """Wait until the publisher appears in native topic info output."""


    import time

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while time.monotonic() < deadline:
        last_output = run_native_ros2_cli(["topic", "info", "-v", topic, "--no-daemon"], env, timeout_seconds=5.0)
        if expected_type in last_output and topic_info_has_publisher(last_output, node_name):
            return last_output
        time.sleep(1.0)

    raise TimeoutError(f"Timed out waiting for publisher on {topic}.\nLast topic info:\n{last_output}")


__all__ = [name for name in globals() if not name.startswith("__")]
