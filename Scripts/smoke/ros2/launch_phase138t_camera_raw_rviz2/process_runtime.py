#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138t_camera_raw_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def cleanup_stale_processes(script_path: pathlib.Path, rviz_config: pathlib.Path, camera_parent_frame: str) -> None:
    """Stop stale Phase138T RViz/helper processes from previous script runs."""

    if sys.platform != "win32":
        return

    escaped_script = powershell_single_quote(str(script_path))
    escaped_config = powershell_single_quote(str(rviz_config))
    escaped_camera = powershell_single_quote(camera_parent_frame)
    command = f"""
$script = [System.Management.Automation.WildcardPattern]::Escape('{escaped_script}')
$config = [System.Management.Automation.WildcardPattern]::Escape('{escaped_config}')
$camera = [System.Management.Automation.WildcardPattern]::Escape('{escaped_camera}')
$matches = Get-CimInstance Win32_Process | Where-Object {{
    $cmd = $_.CommandLine
    if ([string]::IsNullOrWhiteSpace($cmd)) {{ return $false }}
    if ($cmd -like "*$script*" -and $cmd -like "*--camera-tf-fallback-child*") {{ return $true }}
    if ($_.Name -eq "rviz2.exe" -and $cmd -like "*$config*") {{ return $true }}
    if ($cmd -like "*static_transform_publisher*" -and $cmd -like "*--child-frame-id $camera*") {{ return $true }}
    if ($cmd -like "*topic pub /tf*" -and $cmd -like "*child_frame_id: '$camera'*") {{ return $true }}
    return $false
}}
foreach ($p in $matches) {{
    try {{
        Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop
        Write-Output "$($p.ProcessId) $($p.Name)"
    }} catch {{}}
}}
"""
    result = subprocess.run(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=10.0,
    )
    output = result.stdout.strip()
    if output:
        print("[phase138t-rviz] Stopped stale helper processes:")
        print(output)


def powershell_single_quote(value: str) -> str:
    """Escape a string for a PowerShell single-quoted literal."""

    return value.replace("'", "''")


def probe_topic(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
    strict: bool,
) -> bool:
    """Run diagnostic topic probes without making Windows discovery quirks a default hard gate."""

    print(f"[phase138t-rviz] Probing ROS2 topic: {topic}")
    try:
        info = ros2env.run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "info", topic, "--verbose", "--no-daemon"],
            check=False,
            timeout_seconds=max(0.1, timeout_seconds),
        )
    except subprocess.TimeoutExpired as exc:
        message = (
            f"Topic info timed out after {timeout_seconds:.1f}s. "
            "RViz2 can still launch; direct subscribers may succeed on this Windows/FastDDS setup."
        )
        print(f"[phase138t-rviz] {message}")
        if strict:
            raise RuntimeError(message) from exc
        return False

    print(info.stdout.strip() or "<no topic info output>")
    if info.returncode == 0 and "Publisher count:" in info.stdout:
        return True

    message = (
        "[phase138t-rviz] Topic probe did not confirm a publisher. "
        "RViz2 will still launch unless --strict-topic-probe was used."
    )
    print(message)
    if strict:
        raise RuntimeError(message)
    return False


def camera_tf_fallback_child(args: argparse.Namespace) -> int:
    """Publish a visual map -> camera-parent TF fallback on /tf."""

    import rclpy
    from geometry_msgs.msg import TransformStamped
    from tf2_msgs.msg import TFMessage

    fixed_frame = normalize_frame(args.fixed_frame)
    camera_parent_frame = normalize_frame(args.camera_parent_frame)
    rclpy.init()
    node = rclpy.create_node("phase138t_camera_tf_fallback")
    publisher = node.create_publisher(TFMessage, "/tf", 10)
    message = TFMessage()
    transform = TransformStamped()
    transform.header.frame_id = fixed_frame
    transform.child_frame_id = camera_parent_frame
    transform.transform.rotation.w = 1.0
    message.transforms.append(transform)
    print(f"[phase138t-rviz-tf] publishing /tf {fixed_frame}->{camera_parent_frame}", flush=True)
    try:
        while rclpy.ok():
            stamp = node.get_clock().now().to_msg()
            message.transforms[0].header.stamp = stamp
            publisher.publish(message)
            rclpy.spin_once(node, timeout_sec=0.1)
            time.sleep(0.1)
    finally:
        node.destroy_node()
        rclpy.shutdown()
    return 0


def launch_camera_tf_fallback(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    script_path: pathlib.Path,
    fixed_frame: str,
    camera_parent_frame: str,
    workspace_root: pathlib.Path,
) -> subprocess.Popen[str] | None:
    """Launch a visual TF fallback from fixed frame to camera parent frame."""

    if fixed_frame == camera_parent_frame:
        return None

    log_path = workspace_root / "build" / "rviz2" / "phase138t_camera_tf_fallback.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    command = [
        str(pixi_python),
        str(script_path),
        "--camera-tf-fallback-child",
        "--fixed-frame",
        fixed_frame,
        "--camera-parent-frame",
        camera_parent_frame,
    ]
    log_file = log_path.open("w", encoding="utf-8")
    try:
        process = subprocess.Popen(command, env=env, text=True, stdout=log_file, stderr=subprocess.STDOUT)
    finally:
        log_file.close()
    time.sleep(0.5)
    exit_code = process.poll()
    if exit_code is not None:
        print(f"[phase138t-rviz] Camera TF fallback helper exited immediately with code {exit_code}. log={log_path}")
        return None

    print(f"[phase138t-rviz] Camera TF fallback running pid={process.pid}: {fixed_frame} -> {camera_parent_frame}")
    print(f"[phase138t-rviz] Camera TF fallback log: {log_path}")
    return process

__all__ = [name for name in globals() if not name.startswith("__")]
