from __future__ import annotations

import unittest
import tempfile
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces


_BASE = "def helper():\n    return 1\n__all__ = ['helper']\n"
_RISK = ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"]


class IdentitySemanticEdgeTests(unittest.TestCase):
    """Regression coverage for deferred execution semantic edges."""

    def _errors(self, head: str) -> list[str]:
        """Compare one semantic edge against the stable identity fixture."""
        with tempfile.TemporaryDirectory() as temporary:
            roots = (Path(temporary) / "base", Path(temporary) / "head")
            for root, section in zip(roots, (_BASE, head)):
                package = root / "Scripts/smoke/foxrun/fixture"
                package.mkdir(parents=True)
                (root / "Scripts/smoke/foxrun/fixture.py").write_text("PUBLIC = 1\n", encoding="utf-8")
                (package / "__init__.py").write_text("from . import live\n", encoding="utf-8")
                (package / "live.py").write_text(section, encoding="utf-8")
            return compare_identity_surfaces._compare_surfaces(
                compare_identity_surfaces._surfaces(roots[0], strict=False),
                compare_identity_surfaces._surfaces(roots[1], strict=False),
                strict=False,
            )

    def test_local_callback_through_lazy_producer_is_eager_when_drained(self) -> None:
        """A local callback passed to map executes when list drains the producer."""
        head = "import os\ndef apply(callback):\n    return list(map(callback, [1]))\napply(lambda _: os.environ.setdefault('X', '1'))\n" + _BASE
        self.assertEqual(_RISK, self._errors(head))
        assigned = "import os\ndef apply(callback):\n    producer = map(callback, [1])\n    return list(producer)\napply(lambda _: os.environ.setdefault('X', '1'))\n" + _BASE
        self.assertEqual(_RISK, self._errors(assigned))

    def test_eager_itertools_materializers_consume_generators(self) -> None:
        """Combinations, permutations, and product materialize their input iterable."""
        variants = (
            "import itertools\nitertools.{consumer}(values, 1)",
            "import itertools as it\nit.{consumer}(values, 1)",
            "from itertools import {consumer}\n{consumer}(values, 1)",
        )
        for consumer in ("combinations", "permutations", "product"):
            for variant in variants:
                with self.subTest(consumer=consumer, variant=variant):
                    head = "import os\nvalues = (os.environ.setdefault('X', '1') for _ in [1])\n" + variant.format(consumer=consumer) + "\n" + _BASE
                    self.assertEqual(_RISK, self._errors(head))

    def test_break_does_not_overwrite_a_deferred_binding(self) -> None:
        """An assignment after break is unreachable and cannot replace the generator."""
        head = "import os\nvalues = (os.environ.setdefault('X', '1') for _ in [1])\nfor _ in [1]:\n    break\n    values = []\nlist(values)\n" + _BASE
        self.assertEqual(_RISK, self._errors(head))

    def test_nested_class_export_preserves_deferred_attribute_path(self) -> None:
        """Nested class attributes remain reachable through the complete class path."""
        head = "import os\nclass Box:\n    class Inner:\n        values = (os.environ.setdefault('X', '1') for _ in [1])\nlist(Box.Inner.values)\n" + _BASE
        self.assertEqual(_RISK, self._errors(head))

    def test_object_alias_preserves_attribute_identity(self) -> None:
        """A simple object alias resolves later attribute reads to the same object."""
        head = "import os\nclass Box: pass\nholder = Box()\nalias = holder\nholder.values = (os.environ.setdefault('X', '1') for _ in [1])\nlist(alias.values)\n" + _BASE
        self.assertEqual(_RISK, self._errors(head))

    def test_empty_comprehensions_do_not_execute_their_element(self) -> None:
        """Empty generator and list comprehensions execute no element expression."""
        for expression in (
            "list(os.environ.setdefault('X', '1') for _ in [])",
            "[os.environ.setdefault('X', '1') for _ in []]",
        ):
            with self.subTest(expression=expression):
                self.assertEqual([], self._errors("import os\n" + expression + "\n" + _BASE))

    def test_dead_local_callback_branch_is_not_eager(self) -> None:
        """A callback call under if False does not make the callback eager."""
        head = "import os\ndef apply(callback):\n    if False:\n        callback()\n    return None\napply(lambda: os.environ.setdefault('X', '1'))\n" + _BASE
        self.assertEqual([], self._errors(head))

    def test_loop_else_after_break_is_unreachable(self) -> None:
        """A while else suite is skipped when every execution path breaks."""
        head = "while True:\n    break\nelse:\n    import os\n    os.environ['X'] = '1'\n" + _BASE
        self.assertEqual([], self._errors(head))

    def test_empty_zip_propagates_zero_cardinality_to_map(self) -> None:
        """An empty zip source prevents a mapped callback from executing."""
        head = "import os\nlist(map(lambda _: os.environ.setdefault('X', '1'), zip([], [])))\n" + _BASE
        self.assertEqual([], self._errors(head))
