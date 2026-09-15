import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
MODULE = ROOT / "Scripts" / "smoke" / "websocket" / "phase189_component_messagepack_probe.py"

spec = importlib.util.spec_from_file_location("phase189_probe", MODULE)
probe = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = probe
spec.loader.exec_module(probe)

class Phase189ProbeTests(unittest.TestCase):
    def test_contract_service_and_binary_vector(self):
        self.assertEqual(probe.CATALOG_SERVICE, "/foxglove/component-publish-contracts")
        payload = probe.encode_probe_payload(189001, 42)
        self.assertEqual(probe.decode_complete_msgpack(payload), {"messagePackSequence": 189001, "messagePackValue": 42})


    def test_sensor_msgpack_success_gates_state_and_diagnostics(self):
        jpeg = (ROOT / "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs").read_text(encoding="utf-8")
        video = (ROOT / "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Video.cs").read_text(encoding="utf-8")
        pointcloud = (ROOT / "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Raw.cs").read_text(encoding="utf-8")
        self.assertGreaterEqual(jpeg.count("if (TryPublishComponentMessagePackImage("), 2)
        self.assertIn("if (TryPublishComponentMessagePackVideo(", video)
        self.assertIn("if (!msgPackPublished && !publishProvider)", pointcloud)
        # These state/diagnostic updates must only occur after a true helper result.
        self.assertNotIn("TryPublishComponentMessagePackImage(result.JpegBytes, captureUnixNs, ResolveFrameId(), \"jpeg\");\n                _lastPublishedCaptureUnixNs", jpeg)
        self.assertNotIn("TryPublishComponentMessagePackVideo(accessUnit, unixNs, ResolveFrameId(), videoFormat);\n                _diagnostics.RecordVideoAccessUnitPublished", video)

    def test_malformed_vector_is_rejected(self):
        with self.assertRaises(probe.ProbeFailure):
            probe.decode_complete_msgpack(probe.encode_probe_payload(1, 2)[:-1])

if __name__ == "__main__":
    unittest.main()
