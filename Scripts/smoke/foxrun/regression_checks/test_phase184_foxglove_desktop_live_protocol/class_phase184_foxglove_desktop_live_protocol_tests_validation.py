from __future__ import annotations
from .class_phase184_foxglove_desktop_live_protocol_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveDesktopLiveProtocolTests_validation:
    """Decomposed Phase192 implementation component."""
    def test_desktop_client_barrier_rejects_every_malformed_schema_shape_immediately(self):
        """Verify desktop client barrier rejects every malformed schema shape immediately."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-json-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            token = str(config["token"])
            valid = valid_barrier_document(config)
            duplicate = (
                b'{"schemaVersion":1,"schemaVersion":1,'
                + json.dumps(
                    {
                        "runId": valid["runId"],
                        "tokenDigest": valid["tokenDigest"],
                        "state": valid["state"],
                        "acceptedClients": valid["acceptedClients"],
                    }
                )[1:].encode("utf-8")
            )
            mutations: dict[str, bytes] = {
                "empty": b"",
                "malformed": b'{"schemaVersion":',
                "invalid-utf8": b"\xff",
                "root-array": b"[]",
                "duplicate": duplicate,
                "extra": json.dumps(dict(valid, extra=True)).encode("utf-8"),
                "schema-bool": json.dumps(dict(valid, schemaVersion=True)).encode(
                    "utf-8"
                ),
                "schema-string": json.dumps(dict(valid, schemaVersion="1")).encode(
                    "utf-8"
                ),
                "schema-zero": json.dumps(dict(valid, schemaVersion=0)).encode(
                    "utf-8"
                ),
                "run-type": json.dumps(dict(valid, runId=1)).encode("utf-8"),
                "run-stale": json.dumps(
                    dict(valid, runId=str(config["runId"]) + "-stale")
                ).encode("utf-8"),
                "digest-type": json.dumps(dict(valid, tokenDigest=1)).encode("utf-8"),
                "digest-lower": json.dumps(
                    dict(valid, tokenDigest=str(valid["tokenDigest"]).lower())
                ).encode("utf-8"),
                "digest-short": json.dumps(dict(valid, tokenDigest="A" * 63)).encode(
                    "utf-8"
                ),
                "digest-mismatch": json.dumps(
                    dict(valid, tokenDigest="A" * 64)
                ).encode("utf-8"),
                "state-type": json.dumps(dict(valid, state=1)).encode("utf-8"),
                "state-wrong": json.dumps(
                    dict(valid, state="desktop-started")
                ).encode("utf-8"),
                "accepted-bool": json.dumps(
                    dict(valid, acceptedClients=True)
                ).encode("utf-8"),
                "accepted-string": json.dumps(
                    dict(valid, acceptedClients="1")
                ).encode("utf-8"),
                "accepted-zero": json.dumps(
                    dict(valid, acceptedClients=0)
                ).encode("utf-8"),
                "accepted-two": json.dumps(
                    dict(valid, acceptedClients=2)
                ).encode("utf-8"),
                "oversize": b"x"
                * (protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES + 1),
            }
            for key in valid:
                incomplete = dict(valid)
                del incomplete[key]
                mutations[f"missing-{key}"] = json.dumps(incomplete).encode("utf-8")

            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            for label, payload in mutations.items():
                with self.subTest(label=label):
                    barrier.write_bytes(payload)
                    sleep = mock.Mock()
                    self.assert_client_failure(
                        lambda: protocol.wait_for_desktop_barrier(
                            config,
                            barrier,
                            clock=lambda: 0.0,
                            sleep=sleep,
                        ),
                        token,
                    )
                    sleep.assert_not_called()
    def test_desktop_client_barrier_missing_wait_uses_injected_bounded_deadline(self):
        """Verify desktop client barrier missing wait uses injected bounded deadline."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-wait-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [10.0]
            sleeps: list[float] = []

            def clock() -> float:
                """Handle the clock step."""

                return now[0]

            def sleep(seconds: float) -> None:
                """Handle the sleep step."""

                sleeps.append(seconds)
                now[0] += seconds

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=clock,
                    sleep=sleep,
                    deadline=10.75,
                ),
                str(config["token"]),
            )
            self.assertGreater(len(sleeps), 1)
            self.assertAlmostEqual(10.75, now[0])
            self.assertTrue(
                all(
                    0 < value <= protocol.DESKTOP_CLIENT_BARRIER_POLL_SECONDS
                    for value in sleeps
                )
            )
    def test_desktop_client_barrier_default_deadline_uses_window_plus_allowance(self):
        """Verify desktop client barrier default deadline uses window plus allowance."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-window-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output, positive_seconds=2)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [100.0]

            def clock() -> float:
                """Handle the clock step."""

                return now[0]

            def sleep(seconds: float) -> None:
                """Handle the sleep step."""

                now[0] += seconds

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=clock,
                    sleep=sleep,
                ),
                str(config["token"]),
            )
            self.assertAlmostEqual(
                100.0
                + 2
                + protocol.DESKTOP_CLIENT_BARRIER_STARTUP_ALLOWANCE_SECONDS,
                now[0],
            )
    def test_desktop_client_barrier_polls_then_accepts_atomic_document(self):
        """Verify desktop client barrier polls then accepts atomic document."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-appears-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            document = valid_barrier_document(config)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [0.0]
            sleeps: list[float] = []

            def sleep(seconds: float) -> None:
                """Handle the sleep step."""

                sleeps.append(seconds)
                now[0] += seconds
                protocol.write_json_atomic(
                    barrier,
                    document,
                    max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
                )

            self.assertEqual(
                document,
                protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: now[0],
                    sleep=sleep,
                    deadline=1.0,
                ),
            )
            self.assertEqual(1, len(sleeps))
    def test_desktop_client_barrier_rejects_valid_document_created_after_deadline(self):
        """Verify desktop client barrier rejects valid document created after deadline."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-late-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            document = valid_barrier_document(config)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            now = [0.0]
            sleeps: list[float] = []

            def sleep(seconds: float) -> None:
                """Handle the sleep step."""

                sleeps.append(seconds)
                now[0] = 1.25
                protocol.write_json_atomic(
                    barrier,
                    document,
                    max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
                )

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: now[0],
                    sleep=sleep,
                    deadline=1.0,
                ),
                str(config["token"]),
            )
            self.assertEqual(1, len(sleeps))
            self.assertGreater(sleeps[0], 0)
            self.assertLessEqual(
                sleeps[0],
                protocol.DESKTOP_CLIENT_BARRIER_POLL_SECONDS,
            )
            self.assertTrue(barrier.is_file())
    def test_desktop_client_barrier_at_exact_deadline_times_out_before_read(self):
        """Verify desktop client barrier at exact deadline times out before read."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-equal-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            protocol.write_json_atomic(
                barrier,
                valid_barrier_document(config),
                max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
            )

            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: 5.0,
                    sleep=lambda _: self.fail("expired barrier must not sleep"),
                    deadline=5.0,
                ),
                str(config["token"]),
            )
    def test_desktop_client_barrier_clock_failure_precedes_visible_document(self):
        """Verify desktop client barrier clock failure precedes visible document."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_ClockFailureRawToken77"
        with tempfile.TemporaryDirectory(prefix="barrier-clock-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output, token=token)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            protocol.write_json_atomic(
                barrier,
                valid_barrier_document(config),
                max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
            )
            clocks = (
                mock.Mock(side_effect=(0.0, float("nan"))),
                mock.Mock(side_effect=(0.0, RuntimeError(token))),
            )
            for clock in clocks:
                with self.subTest(clock=clock):
                    self.assert_client_failure(
                        lambda clock=clock: protocol.wait_for_desktop_barrier(
                            config,
                            barrier,
                            clock=clock,
                            sleep=lambda _: self.fail("clock failure must not sleep"),
                            deadline=1.0,
                        ),
                        token,
                    )
    def test_desktop_client_barrier_rejects_symlink_or_reparse_alias(self):
        """Verify desktop client barrier rejects symlink or reparse alias."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="barrier-link-", dir=TEST_ROOT) as raw:
            output = pathlib.Path(raw).resolve()
            config = valid_barrier_config(output)
            barrier = output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
            target = output / "atomic-target.json"
            target.write_text(
                json.dumps(valid_barrier_document(config)),
                encoding="utf-8",
            )
            try:
                barrier.symlink_to(target)
            except OSError:
                self.skipTest("This Windows identity cannot create a test symlink.")
            self.assert_client_failure(
                lambda: protocol.wait_for_desktop_barrier(
                    config,
                    barrier,
                    clock=lambda: 0.0,
                    sleep=lambda _: None,
                ),
                str(config["token"]),
            )
    def test_transport_client_marker_parser_accepts_only_exact_bounded_envelopes(self):
        """Verify transport client marker parser accepts only exact bounded envelopes."""

        token = "p184g_A1b2C3d4E5f6"
        case = "foxglove-profile"
        normal = protocol.parse_transport_client_marker(
            f"{protocol.TRANSPORT_CLIENTS_MARKER} case={case} token={token} "
            "active=0 accepted=0",
            case=case,
            token=token,
        )
        overflow = protocol.parse_transport_client_marker(
            f"{protocol.TRANSPORT_CLIENTS_OVERFLOW_MARKER} case={case} "
            f"token={token} active=8 accepted=13",
            case=case,
            token=token,
        )
        self.assertFalse(normal.overflow)
        self.assertEqual((0, 0), (normal.active, normal.accepted))
        self.assertTrue(overflow.overflow)
        self.assertEqual((8, 13), (overflow.active, overflow.accepted))
        self.assertEqual(
            {"active": 0, "accepted": 0},
            normal.to_document(),
        )
        with self.assertRaises(dataclasses.FrozenInstanceError):
            normal.active = 9
    def test_transport_client_marker_parser_rejects_every_malformed_envelope(self):
        """Verify transport client marker parser rejects every malformed envelope."""

        token = "p184g_UniqueRawToken77"
        case = "foxglove-profile"
        marker = protocol.TRANSPORT_CLIENTS_MARKER
        valid = f"{marker} case={case} token={token} active=1 accepted=1"
        invalid = (
            "",
            f"PHASE184H_TRANSPORT_CLIENT case={case} token={token} active=1 accepted=1",
            f"{marker} token={token} active=1 accepted=1",
            f"{marker} case={case} case={case} token={token} active=1 accepted=1",
            f"{marker} case={case} token={token} active=1 accepted=1 extra=1",
            f"{marker} token={token} case={case} active=1 accepted=1",
            f"{marker} case=other token={token} active=1 accepted=1",
            f"{marker} case={case} token=stale active=1 accepted=1",
            f"{marker} case={case} token={token} active=True accepted=1",
            f"{marker} case={case} token={token} active=-1 accepted=1",
            f"{marker} case={case} token={token} active=01 accepted=1",
            f"{marker} case={case} token={token} active=1 accepted=False",
            f"{marker} case={case} token={token} active=1 accepted=-1",
            f"{marker} case={case} token={token} active=1 accepted=01",
            f"{marker} case={case} token={token} "
            f"active={protocol.MAX_TRANSPORT_CLIENT_COUNT + 1} accepted=1",
            f"{marker} case={case} token={token} active=1 "
            f"accepted={protocol.MAX_TRANSPORT_CLIENT_COUNT + 1}",
            valid + "\n",
            valid + ("x" * protocol.MAX_TRANSPORT_CLIENT_MARKER_BYTES),
        )
        for line in invalid:
            with self.subTest(line=line[:80]):
                self.assert_evidence_failure(
                    lambda line=line: protocol.parse_transport_client_marker(
                        line,
                        case=case,
                        token=token,
                    ),
                    token,
                )


__all__ = [name for name in globals() if not name.startswith("__")]
