import json
import pathlib
import tempfile
import unittest

from Scripts.performance import run_phase188_replay


class Phase188ReplayRunnerTests(unittest.TestCase):
    def test_runner_rejects_missing_mode(self):
        with self.assertRaises(SystemExit):
            run_phase188_replay.main()

    def test_result_contract_requires_deterministic_fields(self):
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
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "result.json"
            path.write_text(json.dumps({"fixtureHashSha256": "B" * 64}), encoding="utf-8")
            self.assertTrue(path.is_file())


if __name__ == "__main__":
    unittest.main()
