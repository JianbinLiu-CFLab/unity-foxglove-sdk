from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_runtime:
    """Decomposed Phase192 implementation component."""
    def test_sample_attribution_uses_duplicate_sequences_and_exact_graph_gids(self):
        """Verify sample attribution uses duplicate sequences and exact graph gids."""

        module = load_module()
        publishers = [
            {"node": "/unity_native", "gid": "native-gid"},
            {
                "node": "/unity2foxglove_ros2_bridge",
                "gid": "bridge-gid",
            },
        ]

        gids, source = module._attribute_sample_publishers(
            direct_gids=[],
            publication_sequences=[41, 41, 42, 42],
            graph_publishers=publishers,
            minimum_publishers=2,
        )

        self.assertEqual(["bridge-gid", "native-gid"], gids)
        self.assertEqual(
            "publication-sequence-plus-graph-gid",
            source,
        )
        self.assertEqual(
            ([], ""),
            module._attribute_sample_publishers(
                direct_gids=[],
                publication_sequences=[41, 42, 43],
                graph_publishers=publishers,
                minimum_publishers=2,
            ),
        )
        self.assertEqual(
            ([], ""),
            module._attribute_sample_publishers(
                direct_gids=[],
                publication_sequences=[41, 41],
                graph_publishers=publishers + [
                    {"node": "/unexpected", "gid": "third-gid"}
                ],
                minimum_publishers=2,
            ),
        )
    def test_multi_graph_allows_only_unrepresented_history_and_depth(self):
        """Verify multi graph allows only unrepresented history and depth."""

        module = load_module()
        topic = "/foxrun/phase184/multi/state"
        topic_type = "demo/msg/State"
        publishers = [
            {
                "node": "/unity_native",
                "gid": "native-gid",
                "topicType": topic_type,
                "qos": {
                    "reliability": "reliable",
                    "durability": "volatile",
                    "history": "unknown",
                    "depth": 0,
                },
            },
            {
                "node": "/unity2foxglove_ros2_bridge",
                "gid": "bridge-gid",
                "topicType": topic_type,
                "qos": {
                    "reliability": "reliable",
                    "durability": "volatile",
                    "history": "unknown",
                    "depth": 0,
                },
            },
        ]
        config = {
            "case": "multi-target",
            "interfaceType": topic_type,
            "topics": [topic],
        }
        graphs = {
            topic: {
                "publishers": publishers,
                "subscriptions": [],
            }
        }

        self.assertTrue(module._graph_ready(config, graphs))
        contradicted = copy.deepcopy(graphs)
        contradicted[topic]["publishers"][0]["qos"][
            "reliability"
        ] = "best_effort"
        self.assertFalse(module._graph_ready(config, contradicted))
    def test_qos_graph_accepts_matching_system_default_resolution_only(self):
        """Verify QoS graph accepts matching system default resolution only."""

        module = load_module()
        topic_type = "demo/msg/State"
        topics = list(module.protocol.CASE_CONTRACTS["qos-contract"].topics)
        config = {
            "case": "qos-contract",
            "interfaceType": topic_type,
            "topics": topics,
        }
        actual_qos = (
            {
                "reliability": "reliable",
                "durability": "transient_local",
                "history": "unknown",
                "depth": 0,
                "representedAxes": ["reliability", "durability"],
            },
            {
                "reliability": "reliable",
                "durability": "volatile",
                "history": "unknown",
                "depth": 0,
                "representedAxes": ["reliability", "durability"],
            },
            {
                "reliability": "best_effort",
                "durability": "transient_local",
                "history": "unknown",
                "depth": 0,
                "representedAxes": ["reliability", "durability"],
            },
        )
        graphs = {}
        for topic, qos in zip(topics, actual_qos):
            graphs[topic] = {
                "publishers": [
                    {
                        "node": "/unity_native",
                        "gid": f"native-{topic}",
                        "topicType": topic_type,
                        "qos": copy.deepcopy(qos),
                    },
                    {
                        "node": "/unity2foxglove_ros2_bridge",
                        "gid": f"bridge-{topic}",
                        "topicType": topic_type,
                        "qos": copy.deepcopy(qos),
                    },
                ],
                "subscriptions": [],
            }

        self.assertTrue(module._graph_ready(config, graphs))

        divergent = copy.deepcopy(graphs)
        divergent[topics[0]]["publishers"][1]["qos"]["durability"] = "volatile"
        self.assertFalse(module._graph_ready(config, divergent))
    def test_stream_peer_waits_for_transport_graph_before_production(self):
        """Verify stream peer waits for transport graph before production."""

        module = load_module()
        source = inspect.getsource(module._run_stream_peer)

        self.assertLess(
            source.index("_wait_for_stream_subscription"),
            source.index("offered = 1280"),
        )
    def test_stream_production_window_reports_observed_elapsed(self):
        """A timing failure must retain its measured value for diagnosis."""

        module = load_module()

        self.assertEqual(
            2.0,
            module._validated_stream_production_elapsed(2.0),
        )
        with self.assertRaisesRegex(
            module.AcceptanceFailure,
            r"observed=4\.250000s",
        ):
            module._validated_stream_production_elapsed(4.25)
    def test_prepared_stream_publisher_stamps_and_paces_without_executor_spin(self):
        """The timed producer owns only stamping, publishing, and pacing."""

        module = load_module()

        class FakeTimeline:
            """Deterministic monotonic clock used by the paced publisher test."""

            def __init__(self):
                """Start the synthetic clock at zero seconds."""
                self.now = 0.0

            def perf_counter(self):
                """Return the current synthetic monotonic time."""
                return self.now

            def sleep(self, seconds):
                """Advance synthetic time without blocking the test process."""
                self.now += seconds

        class FakeStamp:
            """Minimal ROS clock stamp wrapper."""

            def __init__(self, value):
                """Capture one deterministic clock value."""
                self.value = value

            def to_msg(self):
                """Return the captured value as the fake wire stamp."""
                return self.value

        class FakeClock:
            """Deterministic ROS clock facade."""

            def __init__(self):
                """Start before the first generated stamp."""
                self.value = 0

            def now(self):
                """Return the next deterministic stamp."""
                self.value += 1
                return FakeStamp(self.value)

        class FakeNode:
            """Minimal node facade exposing the deterministic clock."""

            def __init__(self):
                """Own one fake ROS clock."""
                self.clock = FakeClock()

            def get_clock(self):
                """Return the node-owned fake ROS clock."""
                return self.clock

        class FakeMessage:
            """Mutable message surface populated by the prepared publisher."""

            foxrun_stamp = None

        timeline = FakeTimeline()
        messages = [FakeMessage() for _ in range(4)]
        published = []
        elapsed = module._publish_prepared_stream_samples(
            messages,
            publisher=mock.Mock(publish=published.append),
            node=FakeNode(),
            nominal_hz=2.0,
            perf_counter=timeline.perf_counter,
            sleep=timeline.sleep,
        )

        self.assertEqual(2.0, elapsed)
        self.assertEqual([1, 2, 3, 4], [message.foxrun_stamp for message in messages])
        self.assertEqual(messages, published)
        source = inspect.getsource(module._run_stream_peer)
        self.assertLess(
            source.index("stream_samples = ["),
            source.index("_publish_prepared_stream_samples("),
        )
    def test_stream_production_gate_requires_exact_external_subscription(self):
        """Verify stream production gate requires exact external subscription."""

        module = load_module()
        expected_type = "example_interfaces/msg/Envelope"
        config = {
            "case": "stream-640hz",
            "interfaceType": expected_type,
            "topics": ["/stream", "/origin"],
        }

        helper = {
            "node": "/phase184g_peer_deadbeef",
            "gid": "01",
            "topicType": expected_type,
            "qos": {},
        }
        external = {
            "node": "/unity2foxglove_foxrun_input",
            "gid": "",
            "topicType": expected_type,
            "qos": {},
        }
        wrong_type = dict(external, topicType="std_msgs/msg/String")

        self.assertFalse(
            module._stream_subscription_ready(
                config,
                {"/stream": {"publishers": [], "subscriptions": [helper]}},
            )
        )
        self.assertFalse(
            module._stream_subscription_ready(
                config,
                {"/stream": {"publishers": [], "subscriptions": [wrong_type]}},
            )
        )
        self.assertTrue(
            module._stream_subscription_ready(
                config,
                {"/stream": {"publishers": [], "subscriptions": [external]}},
            )
        )
    def test_graph_timeout_snapshot_is_persisted_below_owned_run_root(self):
        """Verify graph timeout snapshot is persisted below owned run root."""

        module = load_module()
        with tempfile.TemporaryDirectory(dir=TEST_ROOT) as temp:
            output = pathlib.Path(temp)
            config = {
                "case": "stream-640hz",
                "outputRoot": str(output),
            }
            graphs = {
                "/stream": {
                    "publishers": [],
                    "subscriptions": [
                        {
                            "node": "/unity2foxglove_foxrun_input",
                            "gid": "01",
                            "topicType": "example_interfaces/msg/Envelope",
                            "qos": {
                                "reliability": "best_effort",
                                "durability": "volatile",
                                "history": "keep_last",
                                "depth": 5,
                            },
                        }
                    ],
                }
            }

            path = module._write_graph_timeout_snapshot(config, graphs)

            self.assertEqual(
                output / "diagnostics" / "ros2-peer-graph-timeout.json",
                path,
            )
            payload = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("stream-640hz", payload["case"])
            self.assertEqual(graphs, payload["topics"])
    def test_windows_job_assignment_is_required_and_close_is_idempotent(self):
        """Verify windows job assignment is required and close is idempotent."""

        module = load_module()
        api = FakeJobApi()
        job = module.WindowsKillOnCloseJob(api=api, platform_name="nt")
        process = FakeProcess(pid=18401)
        job.assign(process)
        job.close()
        job.close()

        self.assertEqual(1, api.created)
        self.assertEqual([(731, 18401)], api.assigned)
        self.assertEqual([731], api.closed)

        failed = module.WindowsKillOnCloseJob(
            api=FakeJobApi(assign_ok=False),
            platform_name="nt",
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            failed.assign(process)
        failed.close()
    def test_process_owner_terminates_only_registered_children(self):
        """Verify process owner terminates only registered children."""

        module = load_module()
        first = FakeProcess(1)
        second = FakeProcess(2)
        owner = module.OwnedProcessSet(job=None)
        owner.register("foxglove-client", first)
        owner.register("ros2-peer", second)
        owner.close()

        self.assertEqual(1, first.terminated)
        self.assertEqual(1, second.terminated)
        self.assertEqual(
            {"foxglove-client": 0, "ros2-peer": 0},
            owner.exit_codes(),
        )
        self.assertTrue(owner.all_stopped())
    def test_process_owner_can_stop_one_preflight_child_without_closing_others(self):
        """Verify process owner can stop one preflight child without closing others."""

        module = load_module()
        preflight = FakeProcess(1)
        actual = FakeProcess(2)
        owner = module.OwnedProcessSet(job=None)
        owner.register("bridge-health", preflight)
        owner.register("bridge", actual)

        self.assertEqual(0, owner.stop("bridge-health"))
        self.assertEqual(1, preflight.terminated)
        self.assertIsNone(actual.poll())
        self.assertEqual({"bridge-health": 0}, owner.exit_codes())
        self.assertEqual({"bridge-health"}, owner.owner_stopped_roles())

        owner.close()
        self.assertEqual(1, actual.terminated)
        self.assertTrue(owner.all_stopped())


__all__ = [name for name in globals() if not name.startswith("__")]
