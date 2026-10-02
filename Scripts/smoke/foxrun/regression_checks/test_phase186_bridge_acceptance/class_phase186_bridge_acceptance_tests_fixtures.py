from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186BridgeAcceptanceTests_fixtures:
    """Decomposed Phase192 implementation component."""
    def test_every_case_has_an_exact_generated_unity_contract_layout(self) -> None:
        """Verify that every case has an exact generated unity contract layout."""
        token = 'p186h_0123456789abcdef01234567'
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            project.mkdir()
            for case_id in protocol.CASES:
                run_id = 'phase186h-' + case_id + '-012345'
                output = repository / 'build' / 'phase186' / run_id
                output.mkdir(parents=True)
                config = protocol.make_run_config(repository=repository, project=project, output_root=output, run_id=run_id, token=token, case_id=case_id, head=HEAD, bridge_port=18767, domain_id=161)
                source = acceptance.render_unity_run_binding(config)
                with self.subTest(case_id=case_id):
                    self.assertEqual(len(config['topics']), source.count('public const string Phase186GeneratedTopic'))
                    for topic in config['topics']:
                        self.assertEqual(1, source.count(f'= "{topic}";'))
                    for index, kind in enumerate(protocol.CASE_CONTRACT_KINDS[case_id]):
                        if kind.endswith('subscribe'):
                            self.assertIn(f'_incomingPhase186Generated{index}', source)
                            self.assertNotIn(f'_phase186GeneratedValue{index}', source)
                            self.assertNotIn(f'_phase186GeneratedIncoming{index}', source)
                    self.assertIn('partial void Phase186Generated_Tick', source)
                    self.assertIn('Phase186GeneratedInterfaceDigest', source)
            bridge_output = repository / 'build' / 'phase186' / 'phase186h-bridge-source-check'
            bridge_output.mkdir(parents=True)
            bridge_config = protocol.make_run_config(repository=repository, project=project, output_root=bridge_output, run_id='phase186h-bridge-source-check', token=token, case_id='bridge-source', head=HEAD, bridge_port=18767, domain_id=161)
            bridge_source = acceptance.render_unity_run_binding(bridge_config)
            mutation_method = bridge_source.split('partial void Phase186Generated_PublishLocalMutation', 1)[1].split('private static Foxglove.Log', 1)[0]
            self.assertIn('published = false;', mutation_method)
            self.assertNotIn('evidence.LocalMutations++', mutation_method)
            fanout_output = repository / 'build' / 'phase186' / 'phase186h-fanout-check-012345'
            fanout_output.mkdir(parents=True)
            fanout_config = protocol.make_run_config(repository=repository, project=project, output_root=fanout_output, run_id='phase186h-fanout-check-012345', token=token, case_id='fanout-fairness-health', head=HEAD, bridge_port=18767, domain_id=161)
            fanout_source = acceptance.render_unity_run_binding(fanout_config)
            self.assertEqual(3, fanout_source.count('"unity2foxglove.r2fu"'))
            self.assertEqual(3, fanout_source.count('unity-local-b-'))
            self.assertEqual(('custom_publish', 'custom_publish', 'custom_publish'), protocol.CASE_CONTRACT_KINDS['fanout-fairness-health'])
            self.assertEqual(3, fanout_source.count('[SerializeField] private Phase181State'))
            self.assertNotIn('[SerializeField] private Foxglove.Log', fanout_source)
    def test_duplex_binding_warms_publisher_without_claiming_local_mutation(self) -> None:
        """Verify that duplex binding warms publisher without claiming local mutation."""
        token = 'p186h_0123456789abcdef01234567'
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            project.mkdir()
            output = repository / 'build' / 'phase186' / 'phase186h-warmup-check'
            output.mkdir(parents=True)
            config = protocol.make_run_config(repository=repository, project=project, output_root=output, run_id='phase186h-warmup-check', token=token, case_id='slow-main-thread-640hz', head=HEAD, bridge_port=18767, domain_id=161)
            source = acceptance.render_unity_run_binding(config)
            warmup_method = source.split('partial void Phase186Generated_WarmPublishers', 1)[1].split('partial void Phase186Generated_PublishLocalMutation', 1)[0]
            self.assertIn('unity-publisher-warmup', warmup_method)
            self.assertIn('published = true;', warmup_method)
            self.assertNotIn('evidence.LocalMutations++', warmup_method)
    def test_manual_binding_mutates_every_duplex_contract(self) -> None:
        """Verify that manual binding mutates every duplex contract."""
        token = 'p186h_0123456789abcdef01234567'
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            project.mkdir()
            output = repository / 'build' / 'phase186' / 'phase186h-manual-duplex-check'
            output.mkdir(parents=True)
            config = protocol.make_run_config(repository=repository, project=project, output_root=output, run_id='phase186h-manual-duplex-check', token=token, case_id='manual-jazzy-fastrtps-duplex', head=HEAD, bridge_port=18767, domain_id=161)
            source = acceptance.render_unity_run_binding(config)
            mutation_method = source.split('partial void Phase186Generated_PublishLocalMutation', 1)[1].split('private static Foxglove.Log', 1)[0]
            self.assertEqual(2, mutation_method.count('unity-local-b-'))
            self.assertIn('_phase186GeneratedValue0 = CreatePhase186State', mutation_method)
            self.assertIn('_phase186GeneratedValue1 = CreatePhase186Log', mutation_method)


__all__ = [name for name in globals() if not name.startswith("__")]
