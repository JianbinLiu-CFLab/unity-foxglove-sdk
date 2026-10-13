from __future__ import annotations
from .ros_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase138s_imu_native_dds_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def subscribe_once_imu(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    topic: str,
    spin_seconds: float,
) -> dict[str, object]:
    """Receive one IMU sample with rclpy, avoiding Windows ros2 CLI echo hangs."""
    # Receive one IMU sample with rclpy, avoiding Windows ros2 CLI echo hangs.

    subscriber_code = r'''
import json
import sys
import time

import rclpy
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import Imu

topic = sys.argv[1]
spin_seconds = float(sys.argv[2])
received = []

def vector_dict(value):
    """Build a dict from Vector3-like values."""
    return {"x": float(value.x), "y": float(value.y), "z": float(value.z)}

def capture(msg):
    """Capture first sample and keep it for downstream validation."""
    if received:
        return
    received.append(
        {
            "stamp": {
                "sec": int(msg.header.stamp.sec),
                "nanosec": int(msg.header.stamp.nanosec),
            },
            "frame_id": str(msg.header.frame_id),
            "orientation": {
                "x": float(msg.orientation.x),
                "y": float(msg.orientation.y),
                "z": float(msg.orientation.z),
                "w": float(msg.orientation.w),
            },
            "angular_velocity": vector_dict(msg.angular_velocity),
            "linear_acceleration": vector_dict(msg.linear_acceleration),
            "orientation_covariance": [float(value) for value in msg.orientation_covariance],
            "angular_velocity_covariance": [float(value) for value in msg.angular_velocity_covariance],
            "linear_acceleration_covariance": [float(value) for value in msg.linear_acceleration_covariance],
        }
    )

rclpy.init(args=None)
node = rclpy.create_node("u2f_phase138s_imu_native_acceptance")
qos_reliable = QoSProfile(depth=10)
qos_best_effort = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=10,
    reliability=ReliabilityPolicy.BEST_EFFORT,
)
subscriptions = [
    node.create_subscription(Imu, topic, capture, qos_reliable),
    node.create_subscription(Imu, topic, capture, qos_best_effort),
]
deadline = time.monotonic() + spin_seconds
try:
    while time.monotonic() < deadline and not received:
        rclpy.spin_once(node, timeout_sec=0.2)
finally:
    for subscription in subscriptions:
        node.destroy_subscription(subscription)
    node.destroy_node()
    rclpy.shutdown()

if not received:
    print("No sensor_msgs/msg/Imu sample received on " + topic, flush=True)
    sys.exit(2)

print("U2F_IMU_JSON=" + json.dumps(received[0], sort_keys=True), flush=True)
'''

    result = subprocess.run(
        [str(pixi_python), "-c", subscriber_code, topic, str(spin_seconds)],
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=spin_seconds + 10.0,
    )
    if result.returncode != 0:
        raise RuntimeError(
            "IMU direct rclpy subscriber failed:\n"
            + f"exit={result.returncode}\n"
            + result.stdout
        )

    for line in reversed(result.stdout.splitlines()):
        if line.startswith("U2F_IMU_JSON="):
            return json.loads(line[len("U2F_IMU_JSON=") :])

    raise RuntimeError("IMU direct rclpy subscriber did not print structured payload.\n" + result.stdout)


def main(argv: list[str]) -> int:
    """Run IMU DDS acceptance flow and return result code."""


    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    use_windows_ros2 = os.name == "nt" or args.ros2_root is not None
    if use_windows_ros2:
        ros2_root_text = args.ros2_root or str(ros2env.DEFAULT_ROS2_ROOT)
        ros2_root = ros2env.resolve_existing_path(ros2_root_text, "ROS2 root", workspace_root)
        pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
        env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)
        print(f"[phase138s-imu-dds] ROS2 root: {ros2_root}")
        print(f"[phase138s-imu-dds] ros2-script.py: {ros2_script}")
    else:
        ros2_script = None
        pixi_python = pathlib.Path(sys.executable)
        env = build_native_ros_env(args)
        print("[phase138s-imu-dds] ROS2 root: <native WSL/Linux environment>")
        print(f"[phase138s-imu-dds] python: {pixi_python}")
    print(f"[phase138s-imu-dds] ROS_DISTRO: {env.get('ROS_DISTRO', '<unset>')}")
    print(f"[phase138s-imu-dds] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION', '<unset>')}")
    print(f"[phase138s-imu-dds] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID', '<unset>')}")
    print(f"[phase138s-imu-dds] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase138s-imu-dds] topic={args.topic} wait={args.wait_seconds}s spin={args.echo_spin_seconds}s")

    if ros2_script is None:
        topic_list = run_native_ros2_cli(["topic", "list", "-t", "--no-daemon"], env, timeout_seconds=5.0)
    else:
        topic_list = probe_topic_list(pixi_python, ros2_script, env)
    print(f"--- ros2 topic list -t --no-daemon ---\n{topic_list}")
    if not topic_list_has_type(topic_list, args.topic, IMU_MSG_TYPE):
        ros2env.log_event(
            "phase138s-imu-dds",
            f"{args.topic} was not confirmed as {IMU_MSG_TYPE} in diagnostic topic list; direct subscriber remains the hard gate.",
        )

    print(f"--- ros2 topic info {args.topic} --verbose --no-daemon ---")
    if args.require_graph_info:
        if ros2_script is None:
            topic_info = wait_for_native_publisher(env, args.topic, args.wait_seconds, IMU_MSG_TYPE, args.node_name)
        else:
            topic_info = ros2env.wait_for_publisher(
                pixi_python,
                ros2_script,
                env,
                args.topic,
                args.wait_seconds,
                expected_type=IMU_MSG_TYPE,
                node_name=args.node_name,
            )
    else:
        if ros2_script is None:
            topic_info = run_native_ros2_cli(["topic", "info", "-v", args.topic, "--no-daemon"], env, timeout_seconds=5.0)
        else:
            topic_info = ros2env.probe_topic_info(
                pixi_python,
                ros2_script,
                env,
                args.topic,
                timeout_seconds=5.0,
            )
    print(topic_info)

    print(f"--- direct rclpy subscribe {args.topic} {IMU_MSG_TYPE} --once ---")
    sample = subscribe_once_imu(pixi_python, env, args.topic, args.echo_spin_seconds)
    print(json.dumps(sample, indent=2, sort_keys=True))

    stamp = sample["stamp"]
    sec = int(stamp["sec"])
    nanosec = int(stamp["nanosec"])
    if sec <= 0:
        raise RuntimeError(f"imu sample: stamp.sec must be positive, got {sec}")
    if not 0 <= nanosec < 1_000_000_000:
        raise RuntimeError(f"imu sample: stamp.nanosec out of range, got {nanosec}")

    frame_id = str(sample["frame_id"])
    if args.expected_frame_id is not None and frame_id != args.expected_frame_id:
        raise RuntimeError(
            f"imu sample: frame_id mismatch, expected {args.expected_frame_id!r}, got {frame_id!r}"
        )

    angular_velocity = sample["angular_velocity"]
    linear_acceleration = sample["linear_acceleration"]
    orientation_covariance = sample["orientation_covariance"]
    angular_velocity_covariance = sample["angular_velocity_covariance"]
    linear_acceleration_covariance = sample["linear_acceleration_covariance"]
    for label, covariance in (
        ("orientation_covariance", orientation_covariance),
        ("angular_velocity_covariance", angular_velocity_covariance),
        ("linear_acceleration_covariance", linear_acceleration_covariance),
    ):
        if len(covariance) != 9:
            raise RuntimeError(f"imu sample: {label} must contain 9 values, got {len(covariance)}")

    if args.require_nonzero_vector:
        vector_values = [*angular_velocity.values(), *linear_acceleration.values()]
        if not any(abs(value) > 1e-9 for value in vector_values):
            raise RuntimeError("imu echo: angular_velocity and linear_acceleration are all zero.")

    evidence = {
        "topic": args.topic,
        "msg_type": IMU_MSG_TYPE,
        "stamp": {"sec": sec, "nanosec": nanosec},
        "frame_id": frame_id,
        "angular_velocity": angular_velocity,
        "linear_acceleration": linear_acceleration,
        "orientation_covariance_0": orientation_covariance[0],
        "covariance_lengths": {
            "orientation": len(orientation_covariance),
            "angular_velocity": len(angular_velocity_covariance),
            "linear_acceleration": len(linear_acceleration_covariance),
        },
    }

    if args.print_json:
        print("--- structured evidence ---")
        print(json.dumps(evidence, indent=2, sort_keys=True))

    print("[phase138s-imu-dds] PASS")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (RuntimeError, TimeoutError, FileNotFoundError, subprocess.TimeoutExpired) as exc:
        print(f"[phase138s-imu-dds] FAIL: {exc}", file=sys.stderr)


__all__ = [name for name in globals() if not name.startswith("__")]
