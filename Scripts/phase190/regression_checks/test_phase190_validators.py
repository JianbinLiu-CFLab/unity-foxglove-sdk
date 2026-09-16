from __future__ import annotations

import csv
import tempfile
import unittest
from pathlib import Path

from Scripts.phase190 import validate_adjudication, validate_claim_inventory


class Phase190ValidatorTests(unittest.TestCase):
    def test_claim_inventory_accepts_eleven_claims_and_maps_sections(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            inventory = root / "inventory.tsv"
            architecture = root / "architecture.md"
            fields = [
                "claim_id", "area", "invariant", "source_section", "applicability",
                "environment", "test_entrypoint", "positive_control", "negative_control",
            ]
            with inventory.open("w", encoding="utf-8", newline="") as handle:
                writer = csv.DictWriter(handle, fieldnames=fields, delimiter="\t")
                writer.writeheader()
                for index in range(1, 12):
                    writer.writerow({
                        "claim_id": f"C{index:03}", "area": "area", "invariant": "invariant",
                        "source_section": str((index - 1) % 8 + 1), "applicability": "required",
                        "environment": "python", "test_entrypoint": "test",
                        "positive_control": "positive", "negative_control": "negative",
                    })
            architecture.write_text("\n".join(f"## {i}. section" for i in range(1, 9)), encoding="utf-8")
            self.assertEqual(validate_claim_inventory.main(["--input", str(inventory), "--architecture", str(architecture)]), 0)

    def test_adjudication_requires_boundary_for_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            inventory = root / "inventory.tsv"
            adjudication = root / "adjudication.tsv"
            inventory.write_text("claim_id\nC001\n", encoding="utf-8")
            adjudication.write_text(
                "finding_id\tclaim_id\troot_cluster\tdisposition\tevidence\ttest\tboundary\n"
                "F001\tC001\troot\tBLOCKED\tevidence\tprobe\tboundary\n",
                encoding="utf-8",
            )
            self.assertEqual(validate_adjudication.main(["--inventory", str(inventory), "--adjudication", str(adjudication)]), 0)

    def test_adjudication_rejects_unknown_claim(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            inventory = root / "inventory.tsv"
            adjudication = root / "adjudication.tsv"
            inventory.write_text("claim_id\nC001\n", encoding="utf-8")
            adjudication.write_text(
                "finding_id\tclaim_id\troot_cluster\tdisposition\tevidence\ttest\n"
                "F001\tC999\troot\tREFUTED\tevidence\tprobe\n",
                encoding="utf-8",
            )
            self.assertEqual(validate_adjudication.main(["--inventory", str(inventory), "--adjudication", str(adjudication)]), 1)


if __name__ == "__main__":
    unittest.main()
