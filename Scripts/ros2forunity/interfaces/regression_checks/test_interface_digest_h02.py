import json, shutil, tempfile, unittest
from pathlib import Path
from Scripts.ros2forunity.interfaces.interface_digest import verify_package
from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import compute_static_interface_digest

class H02DigestParityTests(unittest.TestCase):
    """Cover interface digest parity and fail-closed drift handling."""
    def _copy(self):
        """Copy the interface package into an isolated fixture."""
        root = Path.cwd(); scratch = root / "build/phase187-round4/scratch/r41-h02-source-digest-20260909"; scratch.mkdir(parents=True, exist_ok=True); source = root / "Packages/dev.unity2foxglove.foxrun.ros2.interfaces"
        tmp = tempfile.TemporaryDirectory(dir=scratch)
        package = Path(tmp.name) / source.name; shutil.copytree(source, package)
        return tmp, package
    def _set_digest(self, package):
        """Write the fixture lock with its current static digest."""
        lock_path = package / "RuntimeSupport/foxrun-ros2-interface-lock.json"
        lock = json.loads(lock_path.read_text(encoding="utf-8")); lock["interfaceDigest"] = compute_static_interface_digest(package)
        lock_path.write_text(json.dumps(lock, indent=2) + "\n", encoding="utf-8", newline="\n")
    def test_positive_current_source_package(self):
        """Accept an unchanged package whose digest matches its lock."""
        tmp, package = self._copy()
        try: self.assertEqual(compute_static_interface_digest(package), verify_package(package))
        finally: tmp.cleanup()
    def test_verify_package_hashes_every_source_file_including_non_msg(self):
        """Include non-message interface files in the digest contract."""
        tmp, package = self._copy()
        try:
            extra = package / "Ros2Package~" / "srv" / "Parity.srv"; extra.parent.mkdir(parents=True)
            extra.write_text("string request\n---\nstring response\n", encoding="utf-8", newline="\n")
            self._set_digest(package)
            self.assertEqual(compute_static_interface_digest(package), verify_package(package))
        finally: tmp.cleanup()
    def test_negative_stale_lock_rejects_non_msg_drift(self):
        """Reject source drift when the lock digest is stale."""
        tmp, package = self._copy()
        try:
            (package / "Ros2Package~" / "srv").mkdir(); (package / "Ros2Package~" / "srv" / "Drift.srv").write_text("---\n", encoding="utf-8")
            with self.assertRaises(ValueError): verify_package(package)
        finally: tmp.cleanup()
    def test_adjacent_line_endings_are_canonicalized(self):
        """Canonicalize line endings before comparing interface digests."""
        tmp, package = self._copy()
        try:
            msg = package / "Ros2Package~" / "msg" / "Phase181State48D288ED82F1Envelope.msg"; msg.write_bytes(msg.read_bytes().replace(b"\n", b"\r\n"))
            self._set_digest(package); self.assertEqual(compute_static_interface_digest(package), verify_package(package))
        finally: tmp.cleanup()
    def test_boundary_missing_contract_source_and_malformed_lock_fail_closed(self):
        """Fail closed for missing contract sources and malformed locks."""
        tmp, package = self._copy()
        try:
            (package / "Ros2Package~" / "msg" / "Phase181State48D288ED82F1Envelope.msg").unlink()
            with self.assertRaises(ValueError): verify_package(package)
        finally: tmp.cleanup()
        tmp, package = self._copy()
        try:
            (package / "RuntimeSupport" / "foxrun-ros2-interface-lock.json").write_text("{", encoding="utf-8")
            with self.assertRaises(ValueError): verify_package(package)
        finally: tmp.cleanup()

if __name__ == "__main__": unittest.main()

