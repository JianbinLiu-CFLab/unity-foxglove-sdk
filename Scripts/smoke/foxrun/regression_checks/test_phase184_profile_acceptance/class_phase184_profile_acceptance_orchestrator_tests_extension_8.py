from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_additional import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_extension_8:
    """Decomposed Phase192 implementation component."""
    def test_foxglove_connect_waits_for_optional_desktop_barrier_after_context(self):
        """Verify foxglove connect waits for optional desktop barrier after context."""

        module = load_module()
        config = {
            "case": "foxglove-profile",
            "token": "p184g_A1b2C3d4E5f6",
            "topics": ["/profile/default", "/profile/json"],
            "observationWindows": {"positiveSeconds": 3},
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        barrier = pathlib.Path(
            r"D:\owned\phase184g-20260727-desktop01\desktop-client-barrier.json"
        )

        class StopAfterConnect(RuntimeError):
            """Represent the stop after connect contract."""

            pass

        def run_client(environment, *, barrier_failure=None):
            """Run client."""

            events = []

            def context_ready(_config):
                """Handle the context ready step."""

                events.append("context")

            def wait_for_barrier(actual_config, actual_path):
                """Wait for barrier."""

                self.assertIs(config, actual_config)
                self.assertEqual(str(barrier), actual_path)
                events.append("barrier")
                if barrier_failure is not None:
                    raise barrier_failure

            async def connect(*_args, **_kwargs):
                """Handle the connect step."""

                events.append("connect")
                raise StopAfterConnect()

            websockets = mock.Mock(connect=mock.AsyncMock(side_effect=connect))
            with mock.patch.dict(sys.modules, {"websockets": websockets}), mock.patch.dict(
                module.os.environ,
                environment,
                clear=True,
            ), mock.patch.object(
                module,
                "write_actor_ready",
            ), mock.patch.object(
                module,
                "_wait_for_unity_context",
                side_effect=context_ready,
            ), mock.patch.object(
                module.desktop_live_protocol,
                "wait_for_desktop_barrier",
                side_effect=wait_for_barrier,
            ) as wait:
                expected_type = (
                    type(barrier_failure)
                    if barrier_failure is not None
                    else StopAfterConnect
                )
                with self.assertRaises(expected_type):
                    module.asyncio.run(module._run_foxglove_client_async(config))
            return events, wait, websockets.connect

        events, wait, connect = run_client({})
        self.assertEqual(["context", "connect"], events)
        wait.assert_not_called()
        connect.assert_awaited_once()

        events, wait, connect = run_client(
            {"PHASE184H_DESKTOP_CLIENT_BARRIER": str(barrier)}
        )
        self.assertEqual(["context", "barrier", "connect"], events)
        wait.assert_called_once_with(config, str(barrier))
        connect.assert_awaited_once()

        invalid = ValueError("invalid desktop barrier")
        events, wait, connect = run_client(
            {"PHASE184H_DESKTOP_CLIENT_BARRIER": str(barrier)},
            barrier_failure=invalid,
        )
        self.assertEqual(["context", "barrier"], events)
        wait.assert_called_once_with(config, str(barrier))
        connect.assert_not_awaited()
    def test_foxglove_profile_requests_bootstrap_only_after_subscribing(self):
        """Verify foxglove profile requests bootstrap only after subscribing."""

        module = load_module()
        topics = ("/profile/default", "/profile/json")
        config = {
            "case": "foxglove-profile",
            "token": "p184g_A1b2C3d4E5f6",
            "topics": list(topics),
            "observationWindows": {
                "positiveSeconds": 3,
                "negativeSeconds": 3,
            },
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        events: list[str] = []
        websocket = mock.Mock()
        websocket.close = mock.AsyncMock()
        channels = {
            topics[0]: mock.Mock(encoding="protobuf"),
            topics[1]: mock.Mock(encoding="json"),
        }

        async def subscribe(_websocket, _channels):
            """Handle the subscribe step."""

            events.append("subscribe")
            return {184001: topics[0], 184002: topics[1]}

        async def send_json(
            _websocket,
            _topic,
            _field_name,
            _token,
            stage,
            _count,
            _channel_id,
            *,
            advertise,
        ):
            """Handle the send JSON step."""

            events.append(f"send:{stage}:advertise={advertise}")

        receive_count = 0

        async def receive(*_args, **_kwargs):
            """Receive one correlated acceptance sample."""

            nonlocal receive_count
            receive_count += 1
            events.append(f"receive:{receive_count}")
            return {}, [], float(receive_count)

        def wait_marker(_config, marker, _timeout):
            """Handle the wait marker step."""

            events.append(f"marker:{marker}")

        websockets = mock.Mock(
            connect=mock.AsyncMock(return_value=websocket)
        )
        with mock.patch.dict(
            sys.modules,
            {"websockets": websockets},
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ), mock.patch.object(
            module,
            "_wait_for_unity_context",
        ), mock.patch.object(
            module,
            "_wait_for_foxglove_channels",
            new=mock.AsyncMock(return_value=channels),
        ), mock.patch.object(
            module,
            "_foxglove_subscribe",
            side_effect=subscribe,
        ), mock.patch.object(
            module,
            "_foxglove_advertise_and_send_json",
            side_effect=send_json,
        ) as send, mock.patch.object(
            module,
            "_receive_foxglove_stages",
            side_effect=receive,
        ), mock.patch.object(
            module,
            "wait_for_log_marker",
            side_effect=wait_marker,
        ), mock.patch.object(
            module,
            "wait_for_terminal_marker",
        ):
            result = module.asyncio.run(
                module._run_foxglove_client_async(config)
            )

        self.assertTrue(result["deliveryObserved"])
        self.assertLess(
            events.index("subscribe"),
            events.index(
                "send:profile-client-ready:advertise=True"
            ),
        )
        self.assertLess(
            events.index(
                "send:profile-client-ready:advertise=True"
            ),
            events.index(
                "marker:PHASE184G_PROFILE_CLIENT_READY"
            ),
        )
        self.assertLess(
            events.index(
                "marker:PHASE184G_PROFILE_CLIENT_READY"
            ),
            events.index("receive:1"),
        )
        self.assertEqual(
            [
                call.args[4]
                for call in send.await_args_list
            ],
            [
                "profile-client-ready",
                "profile-a",
                "profile-b",
                "profile-b",
            ],
        )
        self.assertTrue(send.await_args_list[0].kwargs["advertise"])
        self.assertEqual(
            (
                websocket,
                topics[1],
                "explicitJson",
                config["token"],
                "profile-client-ready",
                18400,
                184901,
            ),
            send.await_args_list[0].args,
        )
        self.assertTrue(
            all(
                call.args[1] == topics[1]
                and call.args[2] == "explicitJson"
                and call.args[3] == config["token"]
                and call.args[6] == 184901
                for call in send.await_args_list
            )
        )
        self.assertTrue(
            all(
                not call.kwargs["advertise"]
                for call in send.await_args_list[1:]
            )
        )
        websocket.close.assert_awaited_once()
    def test_foxglove_profile_rejects_client_ready_echo_before_profile_input(self):
        """Verify foxglove profile rejects client ready echo before profile input."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        topics = ("/profile/default", "/profile/json")
        config = {
            "case": "foxglove-profile",
            "token": token,
            "topics": list(topics),
            "observationWindows": {
                "positiveSeconds": 3,
                "negativeSeconds": 3,
            },
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        websocket = mock.Mock()
        websocket.close = mock.AsyncMock()
        channels = {
            topics[0]: mock.Mock(encoding="protobuf"),
            topics[1]: mock.Mock(encoding="json"),
        }
        send = mock.AsyncMock()
        websockets = mock.Mock(
            connect=mock.AsyncMock(return_value=websocket)
        )

        with mock.patch.dict(
            sys.modules,
            {"websockets": websockets},
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ), mock.patch.object(
            module,
            "_wait_for_unity_context",
        ), mock.patch.object(
            module,
            "_wait_for_foxglove_channels",
            new=mock.AsyncMock(return_value=channels),
        ), mock.patch.object(
            module,
            "_foxglove_subscribe",
            new=mock.AsyncMock(
                return_value={
                    184001: topics[0],
                    184002: topics[1],
                }
            ),
        ), mock.patch.object(
            module,
            "_foxglove_advertise_and_send_json",
            send,
        ), mock.patch.object(
            module,
            "_receive_foxglove_stages",
            new=mock.AsyncMock(
                return_value=(
                    {},
                    [token + "-profile-client-ready"],
                    1.0,
                )
            ),
        ), mock.patch.object(
            module,
            "wait_for_log_marker",
        ):
            with self.assertRaisesRegex(
                module.AcceptanceFailure,
                "FAIL_ORIGIN",
            ):
                module.asyncio.run(
                    module._run_foxglove_client_async(config)
                )

        self.assertEqual(1, send.await_count)
        self.assertEqual(
            "profile-client-ready",
            send.await_args.args[4],
        )
        websocket.close.assert_awaited_once()


__all__ = [name for name in globals() if not name.startswith("__")]
