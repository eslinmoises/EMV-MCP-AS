"""Tests for PDF connection specification parser (CONTRACT-013)."""

import os
import unittest
from src.mcp_server.parsers.pdf_connection_parser import default_sample_document, parse_connection_pdf


class TestPdfConnectionParser(unittest.TestCase):
    def setUp(self):
        self.sample_pdf = default_sample_document()

    def test_missing_document_returns_standard_error_envelope(self):
        result = parse_connection_pdf(r"C:\NonExistent\File.pdf")
        self.assertFalse(result["success"])
        self.assertIn("error", result)
        self.assertEqual(result["error"]["code"], "DOCUMENT_NOT_FOUND")

    def test_non_pdf_file_rejected(self):
        result = parse_connection_pdf(os.path.abspath(__file__))
        self.assertFalse(result["success"])
        self.assertEqual(result["error"]["code"], "UNSUPPORTED_FORMAT")

    def test_parse_bfp_sample_document_extracts_members_and_plates(self):
        if not self.sample_pdf or not os.path.isfile(self.sample_pdf):
            self.skipTest("Sample PDF not found on this machine")

        result = parse_connection_pdf(self.sample_pdf)
        self.assertTrue(result["success"], f"Parsing failed: {result.get('error')}")

        spec = result["data"]["specification"]
        self.assertEqual(spec["document"]["connection_type"], "BFP")
        self.assertEqual(spec["members"]["beam"]["profile"], "IPE-360")
        self.assertEqual(spec["members"]["column"]["profile"], "HEB-400")

        # Flange plates verification
        plates = {p["name"]: p for p in spec["plates"]}
        self.assertIn("Top Flange Plate", plates)
        self.assertEqual(plates["Top Flange Plate"]["thickness_mm"], 25.0)
        self.assertEqual(plates["Top Flange Plate"]["width_mm"], 270.0)
        self.assertEqual(plates["Top Flange Plate"]["length_mm"], 450.0)

        # Shear plate verification
        self.assertIn("Shear Tab Plate", plates)
        self.assertEqual(plates["Shear Tab Plate"]["thickness_mm"], 12.0)
        self.assertEqual(plates["Shear Tab Plate"]["width_mm"], 80.0)

        # Bolts verification
        bolts = {b["name"]: b for b in spec["bolt_groups"]}
        self.assertIn("Top Flange Bolt Group", bolts)
        self.assertEqual(bolts["Top Flange Bolt Group"]["count"], 16)
        self.assertAlmostEqual(bolts["Top Flange Bolt Group"]["diameter_mm"], 15.88, places=2)

        self.assertIn("Shear Tab Bolt Group", bolts)
        self.assertEqual(bolts["Shear Tab Bolt Group"]["count"], 4)
        self.assertAlmostEqual(bolts["Shear Tab Bolt Group"]["diameter_mm"], 19.05, places=2)

        # Detailing payload verification
        payload = result["data"]["detailing_payload"]
        self.assertIn("BFP ANSI/AISC 358-16", payload["connection_name"])
        self.assertEqual(len(payload["plates"]), 5)  # 2 flange + 1 shear + 2 continuity stiffeners
        self.assertEqual(len(payload["bolt_groups"]), 3)
        self.assertEqual(len(payload["shop_welds"]), 5)

        # All shop welds must target the column
        for weld in payload["shop_welds"]:
            self.assertEqual(weld["main_part_handle"], "COL_01")
            self.assertEqual(weld["location"], "kInShop")


if __name__ == "__main__":
    unittest.main()
