import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from Scripts.smoke.replay import phase188_deterministic_replay_acceptance as acceptance


class Phase188DeterministicReplayAcceptanceTests(unittest.TestCase):
    def test_validate_editor_probe_requires_marker_and_structural_counters(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            output = root / "probe.json"
            log = root / "Editor.log"
            output.write_text(json.dumps({
                "returnedMessages": 4,
                "eligibleChunks": 2,
                "skippedChunks": 0,
                "decompressedChunks": 1,
                "headersScanned": 30,
                "payloadCopies": 30,
                "payloadBytesCopied": 960,
            }), encoding="utf-8")
            log.write_text("PHASE188_EDITOR_PASS\n", encoding="utf-8")
            result = acceptance.validate_editor_probe(output, log)
            self.assertEqual(result["status"], "pass")
            self.assertEqual(result["returnedMessages"], 4)

    def test_validate_editor_probe_fails_closed_without_marker(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            output = root / "probe.json"
            log = root / "Editor.log"
            output.write_text(json.dumps({
                "returnedMessages": 4, "eligibleChunks": 2, "skippedChunks": 0,
                "decompressedChunks": 1, "headersScanned": 30,
                "payloadCopies": 30, "payloadBytesCopied": 960,
            }), encoding="utf-8")
            log.write_text("PHASE188_EDITOR_FAIL\n", encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "PHASE188_EDITOR_PASS"):
                acceptance.validate_editor_probe(output, log)

    def test_build_command_uses_explicit_unity_and_output(self):
        command = acceptance.build_editor_command(
            Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"),
            Path("fixture.mcap"), Path("probe.json"), Path("Editor.log"), Path("Unity2Foxglove")
        )
        self.assertIn("-executeMethod", command)
        self.assertIn("Unity2Foxglove.Phase188ReplayPerformanceBuilder.Run", command)
        self.assertIn("-phase188Fixture", command)

    def test_acceptance_writes_failure_evidence_and_nonzero_status(self):
        with tempfile.TemporaryDirectory() as td:
            out = Path(td) / "evidence.json"
            with mock.patch.object(acceptance, "run_editor", side_effect=RuntimeError("missing fixture")):
                result = acceptance.run_acceptance(
                    mode="windows-editor", output_dir=Path(td), fixture=Path("missing.mcap"),
                    unity=Path("Unity.exe"), project=Path("Unity2Foxglove"))
            self.assertEqual(result["status"], "fail")
            self.assertIn("missing fixture", result["error"])
            self.assertTrue((Path(td) / "phase188-acceptance.json").exists())


if __name__ == "__main__":
    unittest.main()
