from __future__ import annotations

from .foundation import *

def build_ros_env(
    ros2_root: pathlib.Path,
    rmw_implementation: str | None = None,
    discovery_range: str | None = None,
    domain_id: str | None = None,
    ros_distro: str | None = None,
    fastdds_builtin_transports: str | None = None,
) -> dict[str, str]:
    """Build a deterministic Windows ROS2 environment."""

    pixi = ros2_root / ".pixi" / "envs" / "default"
    env = sanitized_subprocess_env(os.environ)
    env["PATH"] = os.pathsep.join(
        [
            str(ros2_root / "bin"),
            *(str(path) for path in ros2_opt_bin_paths(ros2_root)),
            str(ros2_root / "Scripts"),
            str(pixi),
            str(pixi / "Library" / "bin"),
            str(pixi / "Scripts"),
            r"C:\Windows\system32",
            r"C:\Windows",
            r"C:\Windows\System32\Wbem",
            r"C:\Windows\System32\WindowsPowerShell\v1.0",
        ]
    )
    env["PYTHONPATH"] = str(ros2_root / "Lib" / "site-packages")
    env["AMENT_PREFIX_PATH"] = str(ros2_root)
    env["CMAKE_PREFIX_PATH"] = str(ros2_root)
    env["COLCON_PREFIX_PATH"] = str(ros2_root)
    env["COLCON_PYTHON_EXECUTABLE"] = str(pixi / "python.exe")
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    inferred_distro = infer_ros_distro(ros2_root)
    if ros_distro is None and _workspace_distro_marker(ros2_root) is None:
        raise ValueError(
            "ROS2 distro must be explicit when the root has no authenticated pixi workspace marker"
        )
    if ros_distro is not None and ros_distro.lower().strip() != inferred_distro:
        raise ValueError(
            f"ROS2 distro argument does not match authenticated root: "
            f"argument={ros_distro} root={inferred_distro}"
        )
    env["ROS_DISTRO"] = ros_distro or inferred_distro
    env["ROS_DOMAIN_ID"] = str(domain_id) if domain_id is not None else "0"
    inherited_rmw = env.get("RMW_IMPLEMENTATION")
    if rmw_implementation is None and inherited_rmw and inherited_rmw != "rmw_fastrtps_cpp":
        log_event(
            "ros2-env",
            "Inheriting non-default RMW_IMPLEMENTATION="
            + inherited_rmw
            + "; pass --rmw to make smoke acceptance explicit.",
        )
    env["RMW_IMPLEMENTATION"] = rmw_implementation or inherited_rmw or "rmw_fastrtps_cpp"
    if os.name == "nt" and env["RMW_IMPLEMENTATION"] == "rmw_fastrtps_cpp":
        env["FASTDDS_BUILTIN_TRANSPORTS"] = (
            fastdds_builtin_transports
            or env.get("FASTDDS_BUILTIN_TRANSPORTS")
            or "UDPv4"
        )
    if discovery_range:
        env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = discovery_range
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)
    return env


def sanitized_subprocess_env(source: os._Environ[str] | dict[str, str]) -> dict[str, str]:
    """Copy an environment for ROS tools without forwarding unrelated secrets."""

    env = dict(source)
    for key in list(env):
        upper = key.upper()
        if any(fragment in upper for fragment in _SENSITIVE_ENV_NAME_FRAGMENTS):
            env.pop(key, None)
    return env


def validate_ros2_root(ros2_root: pathlib.Path) -> tuple[pathlib.Path, pathlib.Path]:
    """Validate ROS2 root and return python.exe plus ros2-script.py paths."""

    pixi_python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    ros2_script = ros2_root / "Scripts" / "ros2-script.py"
    missing = [path for path in (pixi_python, ros2_script) if not path.exists()]
    if missing:
        details = "\n".join(f"  missing: {path}" for path in missing)
        raise FileNotFoundError(f"Invalid ROS2 root: {ros2_root}\n{details}")
    return pixi_python, ros2_script


def run_ros2(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    args: list[str],
    check: bool = True,
    timeout_seconds: float = 30.0,
) -> subprocess.CompletedProcess[str]:
    """Run ros2-script.py with captured output."""

    result = subprocess.run(
        [str(pixi_python), str(ros2_script), *args],
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=timeout_seconds,
    )
    if check and result.returncode != 0:
        raise RuntimeError(
            "ROS2 command failed:\n"
            + " ".join(["ros2", *args])
            + f"\nexit={result.returncode}\n{result.stdout}"
        )
    return result


def probe_node_list(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float = 10.0,
) -> str:
    """Return one node-list snapshot without making it a hard acceptance gate."""

    try:
        result = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["node", "list", "--no-daemon"],
            check=False,
            timeout_seconds=timeout_seconds,
        )
    except subprocess.TimeoutExpired:
        return f"<node list timed out after {timeout_seconds:.1f}s>"

    return result.stdout


def probe_topic_info(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float = 10.0,
) -> str:
    """Return one verbose topic-info snapshot without making it a hard gate."""

    try:
        result = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "info", "-v", topic, "--no-daemon"],
            check=False,
            timeout_seconds=timeout_seconds,
        )
    except subprocess.TimeoutExpired:
        return f"<topic info {topic} timed out after {timeout_seconds:.1f}s>"

    return result.stdout


def topic_info_has_publisher(topic_info: str, node_name: str | None = None) -> bool:
    """Return whether verbose topic info contains a publisher, optionally from a node."""

    publisher_match = re.search(r"Publisher count:\s*([1-9][0-9]*)", topic_info)
    if not publisher_match:
        return False
    if node_name is None:
        return True
    return f"Node name: {node_name}" in topic_info




__all__ = ['build_ros_env', 'sanitized_subprocess_env', 'validate_ros2_root', 'run_ros2', 'probe_node_list', 'probe_topic_info', 'topic_info_has_publisher']
