from __future__ import annotations
from .foxglove_channel_client import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
async def _run_foxglove_client_async(config: Mapping[str, object]) -> Mapping[str, object]:
    """Exercise the exact Foxglove half of the selected deep case."""

    try:
        import websockets
    except ImportError as exc:
        raise AcceptanceFailure("FAIL_CLIENT", "Python websockets is unavailable.") from exc

    case = str(config["case"])
    token = str(config["token"])
    topics = tuple(str(item) for item in config["topics"])
    windows = config["observationWindows"]
    result_timeout = float(windows["positiveSeconds"]) + 30.0
    write_actor_ready(
        config,
        "foxglove-client",
        {"state": "connect-loop-ready", "host": "loopback", "topicCount": len(topics)},
    )
    await asyncio.to_thread(_wait_for_unity_context, config)
    desktop_barrier = os.environ.get(DESKTOP_CLIENT_BARRIER_ENV)
    if desktop_barrier is not None:
        await asyncio.to_thread(
            desktop_live_protocol.wait_for_desktop_barrier,
            config,
            desktop_barrier,
        )
    url = f"ws://{config['foxgloveHost']}:{config['foxglovePort']}"
    connection_deadline = time.monotonic() + 120.0
    websocket = None
    while websocket is None:
        try:
            websocket = await websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL])
        except OSError:
            if time.monotonic() >= connection_deadline:
                raise AcceptanceFailure("FAIL_CLIENT", "Foxglove loopback connection did not become ready.")
            await asyncio.sleep(0.1)
    try:
        channels = await _wait_for_foxglove_channels(websocket, topics, 30.0)
        subscriptions = await _foxglove_subscribe(websocket, channels)
        encodings = sorted({channel.encoding for channel in channels.values()})
        if case == "foxglove-profile":
            if channels[topics[0]].encoding != "protobuf" or channels[topics[1]].encoding != "json":
                raise AcceptanceFailure("FAIL_CLIENT", "Inherited/explicit Foxglove encodings drifted.")
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-client-ready",
                18400,
                184901,
                advertise=True,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_CLIENT_READY",
                30.0,
            )
            observed, forbidden, timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {
                    topics[0]: {token + "-profile-outbound"},
                    topics[1]: {token + "-json-outbound"},
                },
                {
                    topics[1]: {
                        token + "-profile-client-ready",
                    }
                },
                result_timeout,
            )
            del observed
            if forbidden:
                raise AcceptanceFailure(
                    "FAIL_ORIGIN",
                    "Foxglove client-readiness input was republished.",
                )
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-a",
                18403,
                184901,
                advertise=False,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_GATE_CLOSED",
                30.0,
            )
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-b",
                18404,
                184901,
                advertise=False,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_GATE_REOPENED",
                float(windows["negativeSeconds"]) + 10.0,
            )
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-b",
                18404,
                184901,
                advertise=False,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_LOCAL_MUTATED",
                30.0,
            )
            later, forbidden_origin, later_timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {topics[1]: {token + "-profile-local-after-remote"}},
                {
                    topics[1]: {
                        token + "-profile-client-ready",
                        token + "-profile-a",
                        token + "-profile-b",
                    }
                },
                float(windows["negativeSeconds"]) + 10.0,
                minimum_observation_seconds=float(windows["negativeSeconds"]),
            )
            del later
            if forbidden_origin:
                raise AcceptanceFailure(
                    "FAIL_ORIGIN",
                    "Remote Foxglove input was republished during origin suppression.",
                )
            timestamp = max(timestamp, later_timestamp)
            await asyncio.to_thread(wait_for_terminal_marker, config, 30.0)
            return {
                "deliveryObserved": True,
                "channelEncodings": encodings,
                "sampleToken": protocol.token_sha256(token),
                "sampleStages": [
                    "profile-outbound",
                    "json-outbound",
                    "profile-a",
                    "profile-b",
                    "profile-local-after-remote",
                ],
                "timestamp": timestamp,
                "remoteApplied": True,
                "sameOriginDropped": True,
                "laterLocalPublished": True,
                "noDisabledApply": True,
                "recoveryApplied": True,
            }

        if case == "multi-target":
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_MULTI_LOCAL_ARMED",
                120.0,
            )
            observed, forbidden, timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {topics[0]: {token + "-multi-local-1", token + "-multi-local-3"}},
                {topics[0]: {token + "-multi-remote-2"}},
                60.0,
            )
            del observed
            if forbidden:
                raise AcceptanceFailure("FAIL_ORIGIN", "Remote ROS input was republished to Foxglove.")
            await asyncio.to_thread(wait_for_terminal_marker, config, 30.0)
            return {
                "deliveryObserved": True,
                "channelEncodings": encodings,
                "sampleToken": protocol.token_sha256(token),
                "sampleStages": ["multi-local-1", "multi-local-3"],
                "timestamp": timestamp,
                "remoteRepublishObserved": False,
            }

        if case == "degraded-target":
            await _foxglove_advertise_and_send_json(
                websocket,
                DEGRADED_CLIENT_READY_TOPIC,
                "clientReady",
                token,
                "degraded-client-ready",
                18419,
                184902,
                advertise=True,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_DEGRADED_CLIENT_READY",
                30.0,
            )
            observed, forbidden, timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {topics[0]: {token + "-degraded-local"}},
                {},
                float(windows["negativeSeconds"]) + 30.0,
            )
            del observed, forbidden
            await asyncio.to_thread(wait_for_terminal_marker, config, 30.0)
            return {
                "deliveryObserved": True,
                "channelEncodings": encodings,
                "sampleToken": protocol.token_sha256(token),
                "sampleStages": ["degraded-local"],
                "timestamp": timestamp,
            }
        raise AcceptanceFailure("FAIL_CLIENT", "Selected case does not own a Foxglove worker.")
    finally:
        await websocket.close()


def run_foxglove_client_worker(config: Mapping[str, object]) -> int:
    """Run foxglove client worker."""

    try:
        evidence = asyncio.run(_run_foxglove_client_async(config))
        write_actor_result(config, "foxglove-client", verdict="PASS", evidence=evidence)
        return 0
    except AcceptanceFailure as exc:
        write_actor_result(
            config,
            "foxglove-client",
            verdict=exc.code,
            evidence={"diagnostic": str(exc)[: protocol.MAX_DIAGNOSTIC_CHARACTERS]},
        )
        return 1
    except Exception as exc:
        write_actor_result(
            config,
            "foxglove-client",
            verdict="FAIL_CLIENT",
            evidence={"diagnostic": type(exc).__name__},
        )
        return 1


def _phase181_peer_module():
    """Handle the phase181 peer module step."""

    try:
        import phase181_custom_ros2_peer as peer
    except ImportError as exc:
        raise AcceptanceFailure("FAIL_PEER", "Phase181 peer helpers are unavailable.") from exc
    return peer




__all__ = [name for name in globals() if not name.startswith("__")]
