from __future__ import annotations
from .class_phase186_bridge_acceptance_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186BridgeAcceptanceTests_generated:
    """Generated-binding regression component."""
    def test_preflight_checks_current_git_head_not_only_requested_text(self) -> None:
        """Verify that preflight checks current git head not only requested text."""
        with mock.patch.object(acceptance, 'git_head', return_value='b' * 40):
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.require_exact_head(pathlib.Path('D:/repo'), HEAD)
    def test_generated_unity_binding_is_token_scoped_and_uses_real_cdr_shapes(self) -> None:
        """Verify that generated unity binding is token scoped and uses real cdr shapes."""
        token = 'p186h_0123456789abcdef01234567'
        run_id = 'phase186h-manual-0123456789ab'
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            project.mkdir()
            output = repository / 'build' / 'phase186' / 'acceptance' / run_id
            output.mkdir(parents=True)
            config = protocol.make_run_config(repository=repository, project=project, output_root=output, run_id=run_id, token=token, case_id='manual-jazzy-fastrtps-duplex', head=HEAD, bridge_port=18767, domain_id=161)
            source = acceptance.render_unity_run_binding(config)
            for topic in config['topics']:
                self.assertIn(topic, source)
            self.assertIn('Foxglove.Log', source)
            self.assertIn('Phase181State', source)
            self.assertIn(protocol.INTERFACE_DIGEST, source)
            self.assertIn('Mode = FoxRunFlow.PublishAndSubscribe', source)
            self.assertIn('SubscribeTransportId = Ros2BridgeTransportProvider.ProviderId', source)
            self.assertNotIn('/foxrun/phase181/', source)
            self.assertNotIn('/foxrun/phase184/', source)
    def test_generated_unity_binding_install_and_cleanup_are_content_owned(self) -> None:
        """Verify that generated unity binding install and cleanup are content owned."""
        token = 'p186h_0123456789abcdef01234567'
        run_id = 'phase186h-source-0123456789ab'
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            project.mkdir()
            output = repository / 'build' / 'phase186' / 'acceptance' / run_id
            output.mkdir(parents=True)
            config = protocol.make_run_config(repository=repository, project=project, output_root=output, run_id=run_id, token=token, case_id='manual-jazzy-fastrtps-duplex', head=HEAD, bridge_port=18767, domain_id=161)
            installed = acceptance.install_unity_run_binding(project, config)
            self.assertEqual(project / 'Assets' / 'Scripts' / 'Generated' / 'Phase186AcceptanceRun.cs', installed.path)
            self.assertEqual(installed.sha256, acceptance.sha256_file(installed.path))
            installed.path.write_text('foreign', encoding='utf-8')
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.cleanup_unity_run_binding(installed)


class Phase186BridgeAcceptanceTests(_Phase186BridgeAcceptanceTests_support, _Phase186BridgeAcceptanceTests_fixtures, _Phase186BridgeAcceptanceTests_generated, unittest.TestCase):
    """Prove orchestration boundaries without launching live prerequisites."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
