"""End-to-end regression tests for source-text inventory gate semantics."""

import contextlib
import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from Scripts.validation.source_text_inventory import inventory, main


class SourceTextInventoryGateTests(unittest.TestCase):
    """Verify gate semantics, parser binding, and cross-platform source scanning."""

    def test_ratchet_requires_exact_ack_for_weakening(self):
        """Require an exact acknowledgement for a target becoming unresolved."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs")',
                    "ReadRequiredRepoText(ResolvePath())",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            changes = [
                json.loads(item.split(" ", 1)[1])
                for item in output.splitlines()
                if item.startswith("SOURCE_TEXT_INVENTORY_UNACKNOWLEDGED ")
            ]
            for change in changes:
                change["reason"] = "The behavior test keeps the product path assertion executable."
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": changes}, indent=2) + "\n",
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(0, code)
            changes[0]["head_sha256"] = "0" * 64
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": changes}, indent=2) + "\n",
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(1, code)

    def test_checker_and_product_changes_do_not_reuse_head_rows(self):
        """Scan the base tree when checker and product inputs change together."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            product = root / "Packages/fixture/Runtime/Product.cs"
            product.write_text(
                "public sealed class Renamed {\n" "    // Product\n" "}\n",
                encoding="utf-8",
            )
            checker = root / "Scripts/validation/source_text_inventory_text.py"
            checker.write_text(
                checker.read_text(encoding="utf-8") + "\n# checker change\n",
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn("unacknowledged=1", output)

    def test_discarded_receiver_expression_requires_ack(self):
        """Reject a source assertion changed from Check to a discarded expression."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        source.Contains("Product");\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "assertion_context"', output)

    def test_expected_assertion_literal_requires_ack(self):
        """Reject changing the expected literal while retaining the same source target."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8")
                .replace(
                    '        Check(source.Contains("Product"));\n',
                    '        Assert.Contains("Product", source);\n',
                ),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "assertion-base"], check=True)
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Assert.Contains("Product", source);\n',
                    '        Assert.Contains("Other", source);\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "assertion_literal"', output)

    def test_checker_row_loss_requires_ack_without_package_changes(self):
        """Reject a checker-only change that hides every trusted inventory row."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            checker.write_text(
                checker.read_text(encoding="utf-8").replace(
                    "\nif __name__ == \"__main__\":",
                    "\n\ndef inventory(root):\n    return []\n\nif __name__ == \"__main__\":",
                ),
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    str(checker),
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('field": "parser_row_removed"', result.stdout)

    def test_arbitrary_helper_exit_condition_is_bound_to_inventory_row(self):
        """Detect changes to an arbitrary helper's exit condition."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Product.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Product {}\n", encoding="utf-8")
            validation = runtime / "ArbitraryHelperValidation.cs"
            original = (
                "public static class ArbitraryHelperValidation {\n"
                "    private static void Run(string source, bool skip) {\n"
                "        if (skip) return;\n"
                '        Check(source.Contains("needle"));\n'
                "    }\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                "        Run(source, skip: false);\n"
                "    }\n"
                "}\n"
            )
            validation.write_text(original, encoding="utf-8")
            registry = (
                root
                / "Packages"
                / "dev.unity2foxglove.sdk"
                / "Tests"
                / "Runtime"
                / "PhaseValidationRegistry.cs"
            )
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-arbitrary-helper", "fixture", ArbitraryHelperValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            base_row = next(row for row in inventory(root) if "needle" in row["token"])
            validation.write_text(original.replace("if (skip)", "if (true)"), encoding="utf-8")
            head_row = next(row for row in inventory(root) if "needle" in row["token"])
            self.assertNotEqual(base_row["harness_signature"], head_row["harness_signature"])
            self.assertNotEqual(base_row["reachability"], head_row["reachability"])

    def test_crlf_and_lf_inventory_rows_match(self):
        """Keep inventory rows identical for LF and CRLF validation sources."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            lf_rows = inventory(root)
            payload = validation.read_bytes().replace(b"\r\n", b"\n")
            validation.write_bytes(payload.replace(b"\n", b"\r\n"))
            crlf_rows = inventory(root)
            self.assertEqual(lf_rows, crlf_rows)

    def _git_fixture(self, directory: str) -> Path:
        """Create a small Git fixture containing the current checker modules."""
        root = Path(directory)
        for source in (
            Path(__file__).resolve().parents[3].glob("Scripts/validation/source_text_inventory*.py")
        ):
            destination = root / source.relative_to(Path(__file__).resolve().parents[3])
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
        acknowledgement = root / "Scripts/validation/source_text_inventory_acknowledgements.json"
        acknowledgement.write_text('{"acknowledgements": []}\n', encoding="utf-8")
        runtime = root / "Packages/fixture/Tests/Runtime"
        runtime.mkdir(parents=True)
        product = root / "Packages/fixture/Runtime/Product.cs"
        product.parent.mkdir(parents=True)
        product.write_text("public sealed class Product {}\n", encoding="utf-8")
        validation = runtime / "FixtureValidation.cs"
        validation.write_text(
            "public static class FixtureValidation {\n"
            "    public static void Validate() {\n"
            '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
            '        Check(source.Contains("Product"));\n'
            "    }\n"
            "}\n",
            encoding="utf-8",
        )
        registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
        registry.parent.mkdir(parents=True, exist_ok=True)
        registry.write_text(
            'Ci("--phase-fixture", "fixture", FixtureValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
            encoding="utf-8",
        )
        subprocess.run(["git", "init", "-q", str(root)], check=True)
        subprocess.run(["git", "-C", str(root), "config", "user.name", "fixture"], check=True)
        subprocess.run(
            ["git", "-C", str(root), "config", "user.email", "fixture@example.invalid"], check=True
        )
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "base"], check=True)
        return root

    def test_ratchet_allows_removed_row_only_with_ack(self):
        """Allow a removed row only when a non-empty exact acknowledgement exists."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n', ""
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            line = next(
                item
                for item in output.splitlines()
                if item.startswith("SOURCE_TEXT_INVENTORY_UNACKNOWLEDGED ")
            )
            change = json.loads(line.split(" ", 1)[1])
            change["reason"] = "A behavior test replaces the removed source assertion."
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": [change]}, indent=2) + "\n",
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(0, code)
            change["reason"] = ""
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": [change]}, indent=2) + "\n",
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(1, code)

    def _run_fixture_gate(self, root: Path) -> tuple[int, str]:
        """Run the real CLI entrypoint against a fixture Git repository."""
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            code = main(
                [
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ]
            )
        return code, output.getvalue()

    def test_two_argument_read_text_resolves_file_constants(self):
        """Resolve a multiline const path passed as ReadText's second argument."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Product.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Product {}\n", encoding="utf-8")
            validation = runtime / "ConstReadTextValidation.cs"
            validation.write_text(
                "public static class ConstReadTextValidation {\n"
                "    private const string ProductPath =\n"
                '        "Packages/fixture/Runtime/Product.cs";\n'
                "    public static void Validate() {\n"
                "        var source = ReadText(\n"
                "            repoRoot, ProductPath);\n"
                '        Check(source.Contains("Product"));\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = (
                root
                / "Packages"
                / "dev.unity2foxglove.sdk"
                / "Tests"
                / "Runtime"
                / "PhaseValidationRegistry.cs"
            )
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-const-read-text", "fixture", ConstReadTextValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(1, len(rows))
            self.assertEqual("Packages/fixture/Runtime/Product.cs", rows[0]["target"])
