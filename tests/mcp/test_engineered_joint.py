"""Test suite for CONTRACT-012: model_engineered_connection FastMCP tool and endpoint.

Verifies creation of fabrication workshop connections from engineering specs (BFP, RAM, IDEA StatiCa).
Ensures plates, bolt patterns, and workshop welds (kInShop) form unified assemblies without orphan parts.
"""
import unittest
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools.modeling_tools import model_engineered_connection
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class TestEngineeredJoint(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url=f"http://127.0.0.1:{cls.server.port}")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_model_engineered_bfp_connection(self):
        """Test modeling a Bolted Flange Plate (BFP) connection according to AISC 358-16 spec."""
        plates = [
            {
                "name": "Top Flange Plate",
                "thickness_mm": 25.0,
                "contour_points": [
                    [0.0, -135.0, 360.0],
                    [450.0, -135.0, 360.0],
                    [450.0, 135.0, 360.0],
                    [0.0, 135.0, 360.0],
                ],
                "material": "A36",
                "model_role": "Flange Plate",
            },
            {
                "name": "Bottom Flange Plate",
                "thickness_mm": 25.0,
                "contour_points": [
                    [0.0, -135.0, -25.0],
                    [450.0, -135.0, -25.0],
                    [450.0, 135.0, -25.0],
                    [0.0, 135.0, -25.0],
                ],
                "material": "A36",
                "model_role": "Flange Plate",
            },
            {
                "name": "Shear Tab Plate",
                "thickness_mm": 12.0,
                "contour_points": [
                    [0.0, 0.0, 50.0],
                    [80.0, 0.0, 50.0],
                    [80.0, 0.0, 310.0],
                    [0.0, 0.0, 310.0],
                ],
                "material": "A36",
                "model_role": "Shear Plate",
            },
        ]

        bolt_groups = [
            {
                "bolt_standard": "ASTM A325",
                "bolt_diameter_mm": 15.88,
                "origin": [225.0, 0.0, 360.0],
                "nx": 8,
                "ny": 2,
                "dx": 50.0,
                "dy": 90.0,
                "is_site_bolt": True,
                "connected_part_handles": ["Top Flange Plate", "BEAM_101"],
            },
            {
                "bolt_standard": "ASTM A325",
                "bolt_diameter_mm": 19.05,
                "origin": [40.0, 0.0, 180.0],
                "nx": 4,
                "ny": 1,
                "dx": 60.0,
                "dy": 0.0,
                "is_site_bolt": True,
                "connected_part_handles": ["Shear Tab Plate", "BEAM_101"],
            },
        ]

        shop_welds = [
            {
                "throat_thickness_mm": 16.0,
                "main_part_handle": "COL_01",
                "attached_part_name": "Top Flange Plate",
                "weld_type": "Butt",
            },
            {
                "throat_thickness_mm": 16.0,
                "main_part_handle": "COL_01",
                "attached_part_name": "Bottom Flange Plate",
                "weld_type": "Butt",
            },
            {
                "throat_thickness_mm": 8.0,
                "main_part_handle": "COL_01",
                "attached_part_name": "Shear Tab Plate",
                "weld_type": "DoubleFillet",
            },
        ]

        res = model_engineered_connection(
            self.client,
            connection_name="BFP AISC 358-16 IPE360-HEB400",
            source_system="AISC 358-16 BFP Report",
            plates=plates,
            bolt_groups=bolt_groups,
            shop_welds=shop_welds,
            verify_assembly=True,
        )

        self.assertTrue(res["success"])
        data = res["data"]
        self.assertEqual(data["connection_name"], "BFP AISC 358-16 IPE360-HEB400")
        self.assertEqual(len(data["plates_created"]), 3)
        self.assertEqual(len(data["bolt_groups_created"]), 2)
        self.assertEqual(len(data["welds_created"]), 3)
        self.assertTrue(all(w["assembly_bound"] is True for w in data["welds_created"]))
