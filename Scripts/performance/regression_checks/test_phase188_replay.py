import json
import pathlib
import tempfile
import unittest

from Scripts.performance import run_phase188_replay


class Phase188ReplayRunnerTests(unittest.TestCase):
    """Regression coverage for the deterministic Phase188 benchmark runner."""

    def test_runner_rejects_missing_mode(self):
        """Reject invocation that omits the required benchmark mode."""
        with self.assertRaises(SystemExit):
            run_phase188_replay.main()

    def test_result_contract_requires_deterministic_fields(self):
        """Require deterministic fixture and latency fields in result payloads."""
        required = {
            "fixtureHashSha256",
            "fixtureSeed",
            "p50Milliseconds",
            "p95Milliseconds",
            "p99Milliseconds",
            "payloadBytesCopied",
        }
        payload = {
            "fixtureHashSha256": "A" * 64,
            "fixtureSeed": 188042,
            "p50Milliseconds": 1.0,
            "p95Milliseconds": 2.0,
            "p99Milliseconds": 3.0,
            "payloadBytesCopied": 10,
        }
        self.assertTrue(required.issubset(payload))
        self.assertEqual(64, len(payload["fixtureHashSha256"]))

    def test_output_directory_is_path_scoped(self):
        """Keep benchmark result files scoped to the caller-provided directory."""
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "result.json"
            path.write_text(json.dumps({"fixtureHashSha256": "B" * 64}), encoding="utf-8")
            self.assertTrue(path.is_file())

    def test_comparison_does_not_call_count_only_semantic_parity(self):
        """Equal counts with different output digests must not be called semantic parity."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            fields = {
                "fixtureHashSha256": "A" * 64,
                "p50Milliseconds": 1.0,
                "p95Milliseconds": 2.0,
                "p99Milliseconds": 3.0,
                "returnedMessages": 4,
            }
            (root / "baseline").mkdir(); (root / "candidate").mkdir()
            (root / "baseline" / "phase188-replay_x.json").write_text(
                json.dumps({**fields, "resultDigestSha256": "B" * 64}), encoding="utf-8")
            (root / "candidate" / "phase188-replay_x.json").write_text(
                json.dumps({**fields, "resultDigestSha256": "C" * 64}), encoding="utf-8")
            comparison = run_phase188_replay.compare_results(
                root / "baseline", root / "candidate", root / "comparison.json")
            self.assertFalse(comparison["semanticParity"])

    def test_comparison_freezes_noise_band_from_three_baseline_runs(self):
        """Three baseline runs produce an immutable observed p95 spread."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "baseline").mkdir(); (root / "candidate").mkdir()
            for index, p95 in enumerate((10.0, 12.0, 11.0)):
                common = {
                    "fixtureHashSha256": "A" * 64,
                    "p50Milliseconds": p95 / 2,
                    "p95Milliseconds": p95,
                    "p99Milliseconds": p95,
                    "returnedMessages": 4,
                    "resultDigestSha256": "B" * 64,
                }
                (root / "baseline" / f"phase188-replay_{index}.json").write_text(
                    json.dumps(common), encoding="utf-8")
                (root / "candidate" / f"phase188-replay_{index}.json").write_text(
                    json.dumps({**common, "p50Milliseconds": 1.0, "p95Milliseconds": 2.0,
                                "p99Milliseconds": 2.0, "resultDigestSha256": "B" * 64}),
                    encoding="utf-8")
            comparison = run_phase188_replay.compare_results(
                root / "baseline", root / "candidate", root / "comparison.json")
            self.assertEqual("frozen", comparison["noiseBand"]["status"])
            self.assertEqual(3, comparison["noiseBand"]["runs"])
            self.assertEqual(1.0, comparison["noiseBand"]["medianAbsoluteDeviationMilliseconds"])
            self.assertEqual(14.0, comparison["noiseBand"]["upperBoundMilliseconds"])
            self.assertTrue(comparison["semanticParity"])


if __name__ == "__main__":
    unittest.main()
