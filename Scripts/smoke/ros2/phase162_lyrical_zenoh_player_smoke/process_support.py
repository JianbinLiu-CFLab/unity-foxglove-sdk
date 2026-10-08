from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase162_lyrical_zenoh_player_smoke.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def default_zenoh_router(ros2_root: pathlib.Path) -> pathlib.Path | None:
    """Return the rmw_zenohd executable bundled with a Windows ROS2 root, if present."""

    candidate = ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"
    if candidate.is_file():
        return candidate
    return None


def kill_process_tree(pid: int) -> None:
    """Kill a Windows process tree, falling back to process.kill elsewhere."""

    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        return

    try:
        os.kill(pid, 9)
    except OSError:
        pass


def wait_for_marker(path: pathlib.Path, marker: str, timeout_seconds: float) -> bool:
    """Wait for a marker in a growing log file."""

    if not marker:
        return True

    deadline = time.monotonic() + timeout_seconds
    last_position = 0
    tail = ""
    while time.monotonic() < deadline:
        if path.exists():
            size = path.stat().st_size
            if size < last_position:
                last_position = 0
                tail = ""
            with path.open("r", encoding="utf-8", errors="replace") as stream:
                stream.seek(last_position)
                chunk = stream.read()
                last_position = stream.tell()
            if chunk:
                combined = tail + chunk
                if marker in combined:
                    return True
                tail = combined[-max(len(marker) - 1, 0):]
        time.sleep(0.25)
    return False


def launch_to_log(command: list[str], cwd: pathlib.Path, env: dict[str, str], log_path: pathlib.Path) -> subprocess.Popen:
    """Launch a subprocess with stdout/stderr redirected to a UTF-8 log."""

    log_path.parent.mkdir(parents=True, exist_ok=True)
    log = log_path.open("w", encoding="utf-8", errors="replace")
    log.write("cmd: " + " ".join(command) + "\n")
    log.write(f"cwd: {cwd}\n\n")
    log.flush()
    return subprocess.Popen(
        command,
        cwd=str(cwd),
        env=env,
        text=True,
        stdout=log,
        stderr=subprocess.STDOUT,
    )


def run_bounded_to_file(
    command: list[str],
    cwd: pathlib.Path,
    env: dict[str, str],
    log_path: pathlib.Path,
    timeout_seconds: float,
) -> int:
    """Run a subprocess to a file and kill its process tree on timeout."""

    process = launch_to_log(command, cwd, env, log_path)
    try:
        process.wait(timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        kill_process_tree(process.pid)
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.returncode = 124

    if process.poll() is None:
        process.returncode = 124

    with log_path.open("a", encoding="utf-8", errors="replace") as log:
        log.write(f"\nexitCode: {process.returncode}\n")
    return int(process.returncode or 0)


def build_phase162_env(args: argparse.Namespace) -> tuple[dict[str, str], pathlib.Path, pathlib.Path]:
    """Build a Lyrical ROS2 CLI environment with explicit RMW selection."""

    ros2_root = args.ros2_root.resolve()
    pixi_python, ros2_script = validate_ros2_root(ros2_root)
    env = build_ros_env(
        ros2_root,
        rmw_implementation=args.rmw_implementation,
        domain_id=args.domain_id,
        ros_distro="lyrical",
    )
    if args.rmw_implementation == "rmw_zenoh_cpp":
        env.setdefault("ZENOH_ROUTER_CHECK_ATTEMPTS", "10")
    return env, pixi_python, ros2_script


__all__ = [name for name in globals() if not name.startswith("__")]
