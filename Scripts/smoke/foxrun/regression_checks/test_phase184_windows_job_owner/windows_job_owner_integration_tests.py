from __future__ import annotations
from .class_windows_job_owner_pure_tests_validation_and_windows_job_owner_pure_tests_public import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
@unittest.skipUnless(
    os.name == "nt" and job_owner is not None,
    "Windows-only disposable Job Object integration.",
)
class WindowsJobOwnerIntegrationTests(unittest.TestCase):
    """Exercise the windows job owner integration tests behavior."""

    def test_disposable_python_root_can_exit_normally_before_poll_and_wait(self):
        """Verify disposable python root can exit normally before poll and wait."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="normal-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            with job_owner.WindowsJobOwner(
                sys.executable,
                handoff_policy=job_owner.RootHandoffPolicy.OWNED_PROCESS,
            ) as owner:
                identity = owner.launch_suspended_owned(
                    sys.executable,
                    ("-c", "raise SystemExit(17)"),
                    cwd=str(root),
                    environment=dict(os.environ),
                    stdout_log=str(root / "normal.stdout.log"),
                    stderr_log=str(root / "normal.stderr.log"),
                )
                self.assertTrue(owner.wait(identity, timeout_seconds=10.0))
                self.assertEqual(17, owner.poll(identity))

    def test_helper_death_closes_job_and_terminates_its_disposable_python_child(self):
        """Verify helper death closes job and terminates its disposable python child."""

        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="real-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            identity_path = root / "child-identity.json"
            helper_code = "\n".join(
                (
                    "import json, os, pathlib, sys, time",
                    "from Scripts.smoke.foxrun import phase184_windows_job_owner as owner",
                    "root = pathlib.Path(sys.argv[1])",
                    "identity_path = pathlib.Path(sys.argv[2])",
                    "job = owner.WindowsJobOwner(",
                    "    sys.executable,",
                    "    handoff_policy=owner.RootHandoffPolicy.OWNED_PROCESS,",
                    ")",
                    "identity = job.launch_suspended_owned(",
                    "    sys.executable,",
                    "    ('-c', 'import time; time.sleep(120)'),",
                    "    cwd=str(root),",
                    "    environment=dict(os.environ),",
                    "    stdout_log=str(root / 'child.stdout.log'),",
                    "    stderr_log=str(root / 'child.stderr.log'),",
                    ")",
                    "if identity not in job.members():",
                    "    raise RuntimeError('Disposable child is not an exact Job member.')",
                    "identity_temporary = identity_path.with_suffix('.tmp')",
                    "identity_temporary.write_text(json.dumps({",
                    "    'pid': identity.pid,",
                    "    'creation': identity.creation_time_100ns,",
                    "    'path': identity.executable,",
                    "}), encoding='utf-8')",
                    "os.replace(identity_temporary, identity_path)",
                    "time.sleep(120)",
                )
            )
            helper = subprocess.Popen(
                (
                    sys.executable,
                    "-c",
                    helper_code,
                    str(root),
                    str(identity_path),
                ),
                cwd=ROOT,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                text=True,
            )
            child_handle = None
            try:
                deadline = time.monotonic() + 15.0
                while time.monotonic() < deadline and not identity_path.exists():
                    if helper.poll() is not None:
                        break
                    time.sleep(0.05)
                if not identity_path.exists():
                    if helper.poll() is None:
                        helper.kill()
                        helper.wait(timeout=10)
                    stderr = (helper.stderr.read(512) if helper.stderr else "")
                    self.fail(f"Disposable helper did not create child identity: {stderr}")

                identity = json.loads(identity_path.read_text(encoding="utf-8"))
                import ctypes

                kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel32.OpenProcess.argtypes = [
                    ctypes.c_uint32,
                    ctypes.c_int,
                    ctypes.c_uint32,
                ]
                kernel32.OpenProcess.restype = ctypes.c_void_p
                kernel32.WaitForSingleObject.argtypes = [
                    ctypes.c_void_p,
                    ctypes.c_uint32,
                ]
                kernel32.WaitForSingleObject.restype = ctypes.c_uint32
                kernel32.TerminateProcess.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
                kernel32.TerminateProcess.restype = ctypes.c_int
                kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
                kernel32.CloseHandle.restype = ctypes.c_int

                child_handle = kernel32.OpenProcess(
                    0x00100000 | 0x0001,
                    False,
                    int(identity["pid"]),
                )
                self.assertTrue(child_handle, "Disposable child process is not live.")
                helper.kill()
                helper.wait(timeout=10)
                wait_result = kernel32.WaitForSingleObject(child_handle, 10_000)
                if wait_result != 0:
                    kernel32.TerminateProcess(child_handle, 184)
                self.assertEqual(
                    0,
                    wait_result,
                    "Kill-on-close Job did not terminate the disposable Python child.",
                )
            finally:
                if helper.poll() is None:
                    helper.kill()
                    helper.wait(timeout=10)
                if helper.stderr is not None:
                    helper.stderr.close()
                if child_handle:
                    kernel32.CloseHandle(child_handle)


__all__ = [name for name in globals() if not name.startswith("__")]
