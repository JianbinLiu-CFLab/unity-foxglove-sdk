"""Regression tests for the Phase189 maintained manual acceptance surface."""
from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
MANUAL = ROOT / "Scripts" / "smoke" / "websocket" / "phase189_component_messagepack_manual.py"
SCENE = ROOT / "Unity2Foxglove" / "Assets" / "Scenes" / "ManualAcceptance" / "Phase189ComponentMessagePackAcceptance.unity"
CONTROLLER = ROOT / "Unity2Foxglove" / "Assets" / "Scripts" / "ManualAcceptance" / "Phase189ComponentMessagePackAcceptance.cs"
BUILDER = ROOT / "Unity2Foxglove" / "Assets" / "Editor" / "ManualAcceptance" / "Phase189ComponentMessagePackAcceptanceBuilder.cs"
BATCH = ROOT / "Unity2Foxglove" / "Assets" / "Editor" / "ManualAcceptance" / "Phase189ComponentMessagePackBatchProbe.cs"
INSPECTOR = ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "ComponentMessagePackMcapInspector.cs"
VALIDATION = ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "ComponentMessagePackAcceptanceValidation.cs"

class Phase189ManualAcceptanceTests(unittest.TestCase):
    """Validate the maintained Phase189 manual acceptance assets and protocol."""
    def test_maintained_acceptance_assets_are_present(self):
        """Require the maintained scene, controller, builder, probe, and inspector assets."""
        for path in (SCENE, CONTROLLER, BUILDER, BATCH, INSPECTOR, VALIDATION):
            self.assertTrue(path.is_file(), path)
            self.assertTrue(path.with_suffix(path.suffix + ".meta").is_file(), path)

    def test_scene_and_controller_name_the_required_topics_and_actions(self):
        """Require the four component topics and the five-stage controller markers."""
        scene = SCENE.read_text(encoding="utf-8")
        source = CONTROLLER.read_text(encoding="utf-8")
        for topic in ("/phase189/component/scalar", "/phase189/component/nested", "/phase189/component/jpeg", "/phase189/component/pointcloud"):
            self.assertIn(topic, scene + source)
        for marker in ("Stage pending topic + JSON", "Restart Manager", "Restore MessagePack", "PHASE189_COMPONENT_MESSAGEPACK_PROBE_PASS"):
            self.assertIn(marker, source)
        self.assertIn("PHASE189_BATCH_DIAGNOSTIC_PASS", BATCH.read_text(encoding="utf-8"))

    def test_manual_coordinator_has_bounded_five_stage_protocol(self):
        """Require the non-interactive coordinator to complete all five bounded stages."""
        completed = subprocess.run([sys.executable, str(MANUAL), "--run-id", "red", "--non-interactive"], text=True, capture_output=True)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        output = completed.stdout
        self.assertIn("stage=1/5", output)
        self.assertIn("stage=5/5", output)
        self.assertIn("PHASE189_MANUAL_STATUS", output)
        self.assertNotIn("placeholder", output.lower())

    def test_noninteractive_batch_binds_component_fixture_evidence(self):
        """Require non-interactive mode to emit and validate concrete four-topic evidence."""
        run_id = "red-evidence"
        completed = subprocess.run(
            [sys.executable, str(MANUAL), "--run-id", run_id, "--non-interactive"],
            text=True,
            capture_output=True,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        report_path = ROOT / "build" / "phase189" / "manual" / run_id / "batch-report.json"
        self.assertTrue(report_path.is_file(), report_path)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        self.assertEqual(report.get("verdict"), "PASS")
        final = report.get("finalMessagePack", {})
        self.assertEqual(final.get("runId"), run_id)
        self.assertEqual(final.get("recordingClosed"), True)
        self.assertEqual(set(final.get("topics", {})), {
            "/phase189/component/scalar",
            "/phase189/component/nested",
            "/phase189/component/jpeg",
            "/phase189/component/pointcloud",
        })
        self.assertEqual(report.get("recordingClose", {}).get("path"), final.get("recordingPath"))

    def test_inspector_declares_identity_and_binary_guards(self):
        """Require strict run identity, close, and binary-member inspector guards."""
        source = INSPECTOR.read_text(encoding="utf-8")
        for token in ("expectedRun", "expectedHead", "expectedGeneration", "messageEncoding", "SchemaId", "payload", "stale", "/phase189/component/jpeg", "/phase189/component/pointcloud", "binaryMember", "recordingClose", "EDIT_MODE"):
            self.assertIn(token, source)

    def test_controller_requires_probe_and_inspector_evidence_before_complete(self):
        """Prevent the UI from declaring PASS without the two independent evidence gates."""
        source = CONTROLLER.read_text(encoding="utf-8")
        self.assertIn("_probeEvidence", source)
        self.assertIn("_inspectorEvidence", source)
        self.assertIn("RecordProbeEvidence", source)
        self.assertIn("RecordInspectorEvidence", source)
        self.assertIn("Probe and MCAP inspector evidence are required", source)

    def test_batch_diagnostic_writes_to_repository_evidence_root(self):
        """Keep Unity batch evidence in the repository build boundary, not inside the project."""
        source = BATCH.read_text(encoding="utf-8")
        self.assertIn("Directory.GetParent(projectRoot)", source)
        self.assertIn('"build", "phase189", "manual", "batch-diagnostic.json"', source)
        self.assertIn("FoxgloveMsgPackWriter", source)
        self.assertIn("WriteBinary", source)
        self.assertIn('\\"wireVectors\\":true', source)

if __name__ == "__main__":
    unittest.main()
