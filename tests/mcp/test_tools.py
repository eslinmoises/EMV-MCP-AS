import unittest
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import diagnostic_tools, modeling_tools, scripting_tools
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class TestMcpTools(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url=f"http://127.0.0.1:5055")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_get_active_model_info(self):
        res = diagnostic_tools.get_active_model_info(self.client)
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["as_version"], "2026")
        self.assertEqual(res["data"]["units"], "Metric")

    def test_get_selected_elements(self):
        res = diagnostic_tools.get_selected_elements(self.client)
        self.assertTrue(res["success"])
        self.assertEqual(len(res["data"]), 1)
        self.assertEqual(res["data"][0]["handle"], "1B2C")
        self.assertEqual(res["data"][0]["model_role"], "Column")

    def test_verify_welds_and_assemblies(self):
        res = diagnostic_tools.verify_welds_and_assemblies(self.client)
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["total_workshop_welds"], 1)
        self.assertEqual(res["data"]["total_site_welds"], 1)
        # Verify workshop weld marks same assembly
        welds = res["data"]["welds"]
        self.assertTrue(welds[0]["is_same_assembly"])
        self.assertFalse(welds[1]["is_same_assembly"])

    def test_inspect_main_part(self):
        res = diagnostic_tools.inspect_main_part(self.client, "C1")
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["assembly_mark"], "C1")
        self.assertEqual(res["data"]["main_part_role"], "Column")
        self.assertTrue(res["data"]["is_valid_main_part"])

    def test_get_ucs_and_grids(self):
        res = diagnostic_tools.get_ucs_and_grids(self.client)
        self.assertTrue(res["success"])
        self.assertIn("active_ucs", res["data"])
        self.assertEqual(len(res["data"]["grid_axes"]), 4)

    def test_capture_viewport(self):
        res = diagnostic_tools.capture_viewport(self.client)
        self.assertTrue(res["success"])
        self.assertIn("image_base64", res["data"])

    def test_create_straight_beam(self):
        res = modeling_tools.create_straight_beam(
            self.client,
            start_point=[0, 0, 0],
            end_point=[0, 0, 4000],
            section_name="HEA240",
            material="S275JR",
            model_role="Column",
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "BEAM_101")
        self.assertEqual(res["data"]["section_name"], "HEA240")

    def test_create_plate(self):
        points = [[0, 0, 0], [400, 0, 0], [400, 400, 0], [0, 400, 0]]
        res = modeling_tools.create_plate(self.client, contour_points=points, thickness=25.0)
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "PLATE_202")
        self.assertEqual(res["data"]["thickness_mm"], 25.0)

    def test_set_main_part(self):
        res = modeling_tools.set_main_part(self.client, assembly_handle="ASS_01", new_main_part_handle="BEAM_101")
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["main_part_handle"], "BEAM_101")

    def test_execute_csharp_script(self):
        script = "var beam = new StraightBeam();"
        res = scripting_tools.execute_csharp_script(self.client, script)
        self.assertTrue(res["success"])
        self.assertIn("Roslyn", res["data"]["output"])


if __name__ == "__main__":
    unittest.main()
