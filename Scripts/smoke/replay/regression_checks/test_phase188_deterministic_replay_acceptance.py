import json
import hashlib
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from Scripts.smoke.replay import phase188_deterministic_replay_acceptance as acceptance


class Phase188DeterministicReplayAcceptanceTests(unittest.TestCase):
    """Regression tests for fail-closed Phase188 Unity acceptance orchestration."""

    def test_player_timeout_never_selects_unrelated_processes_by_name(self):
        """A timed-out player must only terminate its owned tree, not a same-name peer."""
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / "FoxgloveDemo.exe").touch()
            tree = mock.Mock()
            tree.process.pid = 1234
            tree.process.poll.return_value = None
            tree.process.wait.side_effect = subprocess.TimeoutExpired("owned-player", 120)
            tree.active_pids.return_value = [1234]
            tree.terminate.return_value = []
            observed_terminations = []

            def simulate_run(command, **kwargs):
                """Model the old launcher without ever terminating a real process."""
                if "taskkill" in command[0]:
                    observed_terminations.append(int(command[command.index("/PID") + 1]))
                    return subprocess.CompletedProcess(command, 0, "", "")
                if command[0].endswith("FoxgloveDemo.exe"):
                    raise subprocess.TimeoutExpired(command, 120, output="timeout", stderr="")
                return subprocess.CompletedProcess(command, 0, "", "")

            with mock.patch.object(acceptance.subprocess, "run", side_effect=simulate_run), \
                 mock.patch.object(acceptance.subprocess, "check_output", return_value='"FoxgloveDemo.exe","9876"'), \
                 mock.patch.object(acceptance, "start_owned_process", return_value=tree, create=True):
                with self.assertRaisesRegex(RuntimeError, "timed out"):
                    acceptance.run_il2cpp(Path("Unity.exe"), root, root / "project", root / "fixture.mcap", 1)
            self.assertNotIn(9876, observed_terminations, "unrelated same-name player was targeted")
            tree.terminate.assert_called_once()
            tree.close.assert_called_once()

    def test_player_rejects_stale_marker_from_previous_invocation(self):
        """An exit-zero player with no new marker must not inherit old PASS evidence."""
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / "FoxgloveDemo.exe").touch()
            (root / "Player.log").write_text("PHASE188_PLAYER_PASS\n", encoding="utf-8")
            completed = subprocess.CompletedProcess([], 0, "", "")
            with mock.patch.object(acceptance.subprocess, "run", return_value=completed), \
                 mock.patch.object(acceptance, "run_owned_process", return_value=0, create=True):
                with self.assertRaisesRegex(RuntimeError, "replay smoke failed"):
                    acceptance.run_il2cpp(Path("Unity.exe"), root, root / "project", root / "fixture.mcap", 1)
            self.assertEqual("PHASE188_PLAYER_PASS\n", (root / "Player.log").read_text(encoding="utf-8"))

    def test_editor_rejects_stale_probe_from_previous_invocation(self):
        """Editor output must be newly generated, while older evidence remains intact."""
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            old_probe = root / "phase188-editor-probe.json"
            old_probe.write_text(json.dumps({name: 1 for name in acceptance.REQUIRED_COUNTERS}), encoding="utf-8")
            (root / "Editor.log").write_text("PHASE188_EDITOR_PASS\n", encoding="utf-8")
            with mock.patch.object(acceptance, "run_owned_process", side_effect=subprocess.TimeoutExpired("unity", 1800)):
                with self.assertRaisesRegex(RuntimeError, "timed out"):
                    acceptance.run_editor(Path("Unity.exe"), root / "fixture", root, root / "project")
            self.assertTrue(old_probe.is_file())

    def test_player_requires_fresh_marker_and_zero_exit(self):
        """Fresh logs and process exit must independently satisfy the acceptance gate."""
        for exit_status, marker, passes in ((0, "PHASE188_PLAYER_PASS", True),
                                             (1, "PHASE188_PLAYER_PASS", False),
                                             (0, "PHASE188_PLAYER_FAIL", False)):
            with self.subTest(exit_status=exit_status, marker=marker), tempfile.TemporaryDirectory() as td:
                root = Path(td)
                (root / "FoxgloveDemo.exe").touch()

                def emit_observed_log(command, cwd, timeout_seconds):
                    """Write only the logfile supplied to this invocation's player."""
                    Path(command[command.index("-logFile") + 1]).write_text(marker, encoding="utf-8")
                    return exit_status

                with mock.patch.object(acceptance.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "", "")), \
                     mock.patch.object(acceptance, "run_owned_process", side_effect=emit_observed_log):
                    if passes:
                        result = acceptance.run_il2cpp(Path("Unity.exe"), root, root / "project", root / "fixture", 1)
                        self.assertEqual(0, result["runtimeExitStatus"])
                        self.assertTrue(Path(result["runtimeLogPath"]).is_file())
                    else:
                        with self.assertRaisesRegex(RuntimeError, "replay smoke failed"):
                            acceptance.run_il2cpp(Path("Unity.exe"), root, root / "project", root / "fixture", 1)

    def test_owned_runner_closes_tree_on_error_and_rejects_survivors(self):
        """Exceptions and surviving descendants never skip releasing owned resources."""
        for wait_error, survivors in ((None, [1234]), (OSError("wait failed"), [])):
            tree = mock.Mock()
            tree.process.wait.return_value = 0
            tree.process.wait.side_effect = wait_error
            with self.subTest(wait_error=wait_error), \
                 mock.patch.object(acceptance, "start_owned_process", return_value=tree), \
                 mock.patch.object(acceptance, "await_tree_quiescence", return_value=survivors):
                with self.assertRaises((RuntimeError, OSError)):
                    acceptance.run_owned_process(["owned"], Path.cwd(), 1)
            tree.close.assert_called_once()

    def test_validate_editor_probe_requires_marker_and_structural_counters(self):
        """Accept a probe only when all counters and the pass marker are present."""
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
        """Reject probe evidence that lacks the terminal editor pass marker."""
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
        """Build commands must carry explicit Unity, fixture, and output paths."""
        command = acceptance.build_editor_command(
            Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"),
            Path("fixture.mcap"), Path("probe.json"), Path("Editor.log"), Path("Unity2Foxglove")
        )
        self.assertIn("-executeMethod", command)
        self.assertIn("Unity2Foxglove.Phase188ReplayPerformanceBuilder.Run", command)
        self.assertIn("-phase188Fixture", command)

    def test_acceptance_writes_failure_evidence_and_nonzero_status(self):
        """Persist structured failure evidence when an acceptance run raises."""
        with tempfile.TemporaryDirectory() as td:
            out = Path(td) / "evidence.json"
            with mock.patch.object(acceptance, "run_editor", side_effect=RuntimeError("missing fixture")):
                result = acceptance.run_acceptance(
                    mode="windows-editor", output_dir=Path(td), fixture=Path("missing.mcap"),
                    unity=Path("Unity.exe"), project=Path("Unity2Foxglove"))
            self.assertEqual(result["status"], "fail")
            self.assertIn("fixture missing", result["error"])
            self.assertTrue((Path(td) / "phase188-acceptance.json").exists())

    def test_fixture_manifest_is_verified_before_launch(self):
        """Acceptance must bind the run to a complete, byte-matching fixture manifest."""
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            fixture = root / "fixture.mcap"
            fixture.write_bytes(b"deterministic-fixture")
            digest = hashlib.sha256(fixture.read_bytes()).hexdigest().upper()
            manifest = {
                "Path": str(fixture), "HashSha256": digest, "Bytes": fixture.stat().st_size,
                "Seed": 188042, "MessageCount": 4, "ChannelCount": 2,
                "GeneratorVersion": "phase188-fixture-v2", "ChunkSizeBytes": 4096,
                "Compression": "none", "Density": "dense", "EncodingMix": ["json"],
                "TimeStartNs": 0, "TimeEndNs": 3000, "MessagesPerChannel": [2, 2],
            }
            (root / "fixture.mcap.manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            self.assertEqual(manifest, acceptance.validate_fixture_manifest(fixture))

    def test_fixture_manifest_rejects_hash_mismatch(self):
        """A stale manifest cannot be used to certify an acceptance run."""
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            fixture = root / "fixture.mcap"
            fixture.write_bytes(b"actual")
            (root / "fixture.mcap.manifest.json").write_text(json.dumps({
                "Path": str(fixture), "HashSha256": "0" * 64, "Bytes": fixture.stat().st_size,
                "Seed": 188042, "MessageCount": 1, "ChannelCount": 1,
                "GeneratorVersion": "phase188-fixture-v2", "ChunkSizeBytes": 4096,
                "Compression": "none", "Density": "dense", "EncodingMix": ["json"],
                "TimeStartNs": 0, "TimeEndNs": 0, "MessagesPerChannel": [1],
            }), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "SHA-256 mismatch"):
                acceptance.validate_fixture_manifest(fixture)


if __name__ == "__main__":
    unittest.main()
