#!/usr/bin/env python3
"""Regression tests for the positive Phase192 identity contracts."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
import shutil

from Scripts.validation.source_decomposition.compare_identity_surfaces.surface_compare import (
    _approved_entrypoint_contract,
    _approved_initializer_contract,
    _discovery_contract_errors,
)


class SourceDecompositionIdentityContractTests(unittest.TestCase):
    """Exercise the reviewed entrypoint, initializer, and discovery contracts."""

    def test_forwarding_entrypoint_contract_accepts_standard_template(self) -> None:
        """A facade and module entrypoint that only forward to run_entrypoint pass."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            facade = root / "Scripts/example_tool.py"
            package = root / "Scripts/example_tool"
            package.mkdir(parents=True)
            facade.parent.mkdir(parents=True, exist_ok=True)
            facade.write_text(
                'if __name__ == "__main__":\n'
                "    raise SystemExit(run_entrypoint())\n",
                encoding="utf-8",
            )
            (package / "__main__.py").write_text(
                'if __name__ == "__main__":\n'
                "    raise SystemExit(run_entrypoint())\n",
                encoding="utf-8",
            )
            self.assertTrue(_approved_entrypoint_contract(root, "Scripts/example_tool.py"))

    def test_forwarding_entrypoint_contract_rejects_inline_exception_mapping(self) -> None:
        """Inline try/except entrypoint logic remains outside the forwarding template."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            facade = root / "Scripts/example_tool.py"
            package = root / "Scripts/example_tool"
            package.mkdir(parents=True)
            facade.parent.mkdir(parents=True, exist_ok=True)
            inline = (
                'if __name__ == "__main__":\n'
                "    try:\n"
                "        raise SystemExit(main())\n"
                "    except Exception:\n"
                "        raise SystemExit(1)\n"
            )
            facade.write_text(inline, encoding="utf-8")
            (package / "__main__.py").write_text(inline, encoding="utf-8")
            self.assertFalse(_approved_entrypoint_contract(root, "Scripts/example_tool.py"))

    def test_initializer_contract_rejects_global_discovery_sentinel(self) -> None:
        """The one-shot load_tests sentinel is rejected by the positive contract."""
        root = Path(__file__).resolve().parents[3]
        relative = "Scripts/remotegateway/regression_checks/test_remote_gateway_tooling.py"
        self.assertFalse(_approved_initializer_contract(root, relative))

    def test_initializer_contract_accepts_current_generated_template_without_sentinel(self) -> None:
        """A generated package initializer without the sentinel remains valid."""
        root = Path(__file__).resolve().parents[3]
        relative = "Scripts/smoke/ros2/launch_phase138m_rviz2.py"
        self.assertTrue(_approved_initializer_contract(root, relative))

    def test_discovery_contract_accepts_intentional_test_facade_removal(self) -> None:
        """Removing a test facade while retaining its package is an approved migration."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            source_root = Path(__file__).resolve().parents[3]
            relative = Path(
                "Scripts/smoke/ros2/launch_phase138m_rviz2.py"
            )
            source_package = source_root / relative.with_suffix("")
            source_facade = source_root / relative
            shutil.copytree(source_package, base / relative.with_suffix(""))
            shutil.copyfile(source_facade, base / relative)
            shutil.copytree(source_package, head / relative.with_suffix(""))
            shutil.copyfile(source_facade, head / relative)
            (head / relative).unlink()
            self.assertEqual([], _discovery_contract_errors(base, head))


if __name__ == "__main__":
    unittest.main()
