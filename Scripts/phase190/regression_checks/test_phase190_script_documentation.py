"""Keep Phase190 entrypoints compatible with the repository's Phase16 gate."""
from __future__ import annotations

import ast
import unittest
from pathlib import Path


class Phase190ScriptDocumentationTests(unittest.TestCase):
    """Exercise the exact missing-documentation failure without a full CI run."""

    def test_all_phase190_definitions_have_docstrings(self) -> None:
        """Every script function and class documents its concrete responsibility."""
        root = Path(__file__).resolve().parents[1]
        missing = []
        for path in sorted(root.rglob("*.py")):
            tree = ast.parse(path.read_text(encoding="utf-8"))
            for node in ast.walk(tree):
                if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                    if not ast.get_docstring(node):
                        missing.append(f"{path.relative_to(root)}:{node.lineno}:{node.name}")
        self.assertEqual([], missing)


if __name__ == "__main__":
    unittest.main()
