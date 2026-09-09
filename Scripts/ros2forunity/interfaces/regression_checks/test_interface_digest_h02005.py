import unittest
from Scripts.ros2forunity.interfaces.regression_checks.test_validate_foxrun_custom_typesupport_addon import _Fixture, _sha256
from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import AddonValidationError, validate_addon

class H02005ManagedPayloadTests(unittest.TestCase):
    def test_managed_payload_is_a_clr_pe_with_expected_type(self):
        with _Fixture("humble") as fixture:
            assembly = fixture.addon / "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll"
            data = assembly.read_bytes()
            self.assertEqual(b"MZ", data[:2]); self.assertIn(b"BSJB", data); self.assertIn(b"Phase181State48D288ED82F1Envelope", data)
    def test_matching_hash_does_not_rescue_non_pe_payload(self):
        with _Fixture("humble") as fixture:
            assembly = fixture.addon / "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll"
            assembly.write_bytes(b"not-a-pe"); fixture.manifest_at(("managed", "assembly", "sha256"), _sha256(assembly)); fixture.refresh_inventory()
            with self.assertRaisesRegex(AddonValidationError, "repair-managed-typesupport-payload"): validate_addon(fixture.request)
    def test_matching_hash_does_not_rescue_wrong_managed_type(self):
        with _Fixture("humble") as fixture:
            assembly = fixture.addon / "Runtime/Ros2ForUnity/Plugins/unity2foxglove_foxrun_interfaces_v1_assembly.dll"
            data = bytearray(assembly.read_bytes()); idx = data.find(b"Phase181State48D288ED82F1Envelope"); data[idx:idx + len(b"Phase181State48D288ED82F1Envelope")] = b"WrongEnvelopeName________________"
            assembly.write_bytes(bytes(data)); fixture.manifest_at(("managed", "assembly", "sha256"), _sha256(assembly)); fixture.refresh_inventory()
            with self.assertRaisesRegex(AddonValidationError, "repair-managed-type-map"): validate_addon(fixture.request)
if __name__ == '__main__': unittest.main()

