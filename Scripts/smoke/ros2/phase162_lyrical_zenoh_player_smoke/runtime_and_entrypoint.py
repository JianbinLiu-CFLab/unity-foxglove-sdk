from __future__ import annotations
from .command_builders import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase162_lyrical_zenoh_player_smoke.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def wait_for_rviz_shutdown_before_router_cleanup(
    args: argparse.Namespace,
    router: subprocess.Popen | None,
    player: subprocess.Popen | None,
    captured_rviz_processes: list[subprocess.Popen],
    summary: dict[str, object],
) -> str | None:
    """Keep an owned Zenoh router alive until helper-launched RViz2 exits."""

    if router is None or router.poll() is not None:
        return None
    if args.echo_only or not args.launch_rviz or args.release_router_on_exit:
        return None

    running_rviz = [process for process in captured_rviz_processes if process.poll() is None]
    if not running_rviz:
        return None

    pids = [process.pid for process in running_rviz]
    summary["zenohRouterHeldForRvizPids"] = pids
    print(
        "Zenoh router is staying alive for RViz2 pid(s) "
        + ", ".join(str(pid) for pid in pids)
        + ". Close RViz2 to let this helper stop the router, or press Ctrl+C to stop the router now.",
        flush=True,
    )

    deadline = time.monotonic() + max(0.0, args.rviz_hold_timeout_seconds)
    try:
        while any(process.poll() is None for process in running_rviz):
            if player is not None:
                player_exit_code = player.poll()
                if player_exit_code is not None:
                    summary["zenohRouterRvizWaitPlayerExitCode"] = player_exit_code
                    print(
                        f"Unity player exited with code {player_exit_code} while RViz2 remained open; "
                        "stopping the owned Zenoh router.",
                        file=sys.stderr,
                    )
                    return "player-exited"
            remaining = deadline - time.monotonic()
            if remaining <= 0.0:
                summary["zenohRouterRvizWaitTimedOut"] = True
                print(
                    f"RViz2 remained open past the bounded {args.rviz_hold_timeout_seconds:.1f}s router hold; "
                    "stopping the owned Zenoh router.",
                    file=sys.stderr,
                )
                return "timed-out"
            time.sleep(min(0.5, remaining))
    except KeyboardInterrupt:
        summary["zenohRouterRvizWaitInterrupted"] = True
        print("Stopping the Zenoh router after Ctrl+C; RViz2 may remain open without a router.", file=sys.stderr)
        return "interrupted"
    else:
        summary["zenohRouterHeldUntilRvizExit"] = True
        return "rviz-exited"


def write_summary(args: argparse.Namespace, summary: dict[str, object]) -> None:
    """Write the JSON summary and print the operator-facing result."""

    args.summary_output.parent.mkdir(parents=True, exist_ok=True)
    args.summary_output.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"Summary: {args.summary_output}")
    print(f"Verdict: {summary['verdict']}")
    if "error" in summary:
        print(f"Error: {summary['error']}", file=sys.stderr)


def main(argv: list[str] | None = None) -> int:
    """Run the Phase162 smoke helper."""

    args = parse_args(argv)
    root = workspace_root()
    env, pixi_python, ros2_script = build_phase162_env(args)
    args.echo_output.parent.mkdir(parents=True, exist_ok=True)
    args.summary_output.parent.mkdir(parents=True, exist_ok=True)

    run_id = datetime.now().astimezone().isoformat(timespec="seconds")
    router_log = args.echo_output.with_name("zenoh-router.log")
    player_log = args.player_log or args.echo_output.with_name("unity-player.log")
    router = None
    player = None
    captured_rviz_processes: list[subprocess.Popen] = []
    summary: dict[str, object] = {
        "runId": run_id,
        "rosDistro": "lyrical",
        "rmwImplementation": args.rmw_implementation,
        "scriptingBackend": args.scripting_backend,
        "topic": args.topic,
        "messageType": args.message_type,
        "ros2Root": str(args.ros2_root),
        "echoOutput": str(args.echo_output),
        "summaryOutput": str(args.summary_output),
        "zenohRouterLog": str(router_log),
        "playerLog": str(player_log),
        "mode": "echo-only" if args.echo_only else "rviz2-pointcloud2",
        "releaseRouterOnExit": bool(args.release_router_on_exit),
        "exitCodes": {},
    }

    try:
        router_exe = args.zenoh_router
        if args.rmw_implementation == "rmw_zenoh_cpp" and router_exe is None and not args.no_zenoh_router:
            router_exe = default_zenoh_router(args.ros2_root.resolve())
            if router_exe is not None:
                summary["autoZenohRouter"] = str(router_exe)

        if args.rmw_implementation == "rmw_zenoh_cpp" and router_exe is not None:
            router_exe = router_exe.resolve()
            if not router_exe.is_file():
                raise FileNotFoundError(f"Zenoh router executable not found: {router_exe}")
            router = launch_to_log([str(router_exe)], root, env, router_log)
            if not wait_for_marker(router_log, args.router_ready_marker, args.timeout_seconds):
                raise TimeoutError(
                    f"Zenoh router did not emit marker {args.router_ready_marker!r}. See {router_log}"
                )

        if args.unity_player is not None:
            player_exe = args.unity_player.resolve()
            if not player_exe.is_file():
                raise FileNotFoundError(f"Unity player executable not found: {player_exe}")
            player = launch_to_log(
                [str(player_exe), "-batchmode", "-nographics", "-logFile", str(player_log)],
                player_exe.parent,
                env,
                player_log,
            )
            if not wait_for_marker(player_log, args.player_ready_marker, args.timeout_seconds):
                raise TimeoutError(
                    f"Unity player did not emit marker {args.player_ready_marker!r}. See {player_log}"
                )

        if args.echo_only:
            echo_command = build_echo_command(pixi_python, ros2_script, args)
            echo_code = run_bounded_to_file(echo_command, root, env, args.echo_output, args.timeout_seconds)
            summary["exitCodes"]["ros2TopicEcho"] = echo_code
            echo_text = args.echo_output.read_text(encoding="utf-8", errors="replace")
            if echo_code != 0:
                raise RuntimeError(f"ros2 topic echo failed with exit code {echo_code}. See {args.echo_output}")
            if args.expected_text and args.expected_text not in echo_text:
                raise RuntimeError(f"Expected text {args.expected_text!r} was not found in {args.echo_output}")

            summary["verdict"] = "PHASE162_LYRICAL_ZENOH_EXTERNAL_ECHO_PASS"
            return_code = 0
        else:
            rviz_args = build_rviz_acceptance_args(args)
            summary["rvizAcceptanceArgs"] = rviz_args
            try:
                rviz_code = run_phase138u_with_rviz_capture(rviz_args, captured_rviz_processes)
            except phase138u.InconclusiveError as exc:
                summary["exitCodes"]["phase138uRviz2Acceptance"] = 2
                summary["verdict"] = "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_INCONCLUSIVE"
                summary["error"] = str(exc)
                return_code = 2
            else:
                summary["exitCodes"]["phase138uRviz2Acceptance"] = rviz_code
                if rviz_code == 2:
                    summary["verdict"] = "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_INCONCLUSIVE"
                    return_code = 2
                elif rviz_code != 0:
                    raise RuntimeError(f"Phase162 Lyrical Zenoh RViz2 acceptance failed with exit code {rviz_code}.")
                else:
                    summary["verdict"] = "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_PASS"
                    return_code = 0
    except Exception as exc:
        summary["verdict"] = (
            "PHASE162_LYRICAL_ZENOH_EXTERNAL_ECHO_FAIL"
            if args.echo_only
            else "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_FAIL"
        )
        summary["error"] = str(exc)
        return_code = 1
    finally:
        hold_outcome = wait_for_rviz_shutdown_before_router_cleanup(
            args,
            router,
            player,
            captured_rviz_processes,
            summary,
        )
        if hold_outcome == "player-exited":
            summary["verdict"] = "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_FAIL"
            summary["error"] = "Unity player exited while RViz2 was still using the owned Zenoh router."
            return_code = 1
        for label, process in (("unityPlayer", player), ("zenohRouter", router)):
            if process is None:
                continue
            if process.poll() is None:
                kill_process_tree(process.pid)
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    summary.setdefault("cleanupTimeouts", []).append(label)
                    return_code = 1
            if process.poll() is None:
                summary.setdefault("cleanupTimeouts", []).append(label)
                return_code = 1
            summary["exitCodes"][label] = process.returncode
        write_summary(args, summary)
    return return_code


__all__ = [name for name in globals() if not name.startswith("__")]
