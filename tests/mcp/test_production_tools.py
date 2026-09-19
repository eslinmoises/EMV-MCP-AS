import unittest

from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import production_tools
from tests.mocks.fixtures import MOCK_UNNUMBERED_ELEMENT_HANDLE
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class RecordingClient:
    def __init__(self):
        self.endpoint = None
        self.payload = None

    def post(self, endpoint, payload):
        self.endpoint = endpoint
        self.payload = payload
        return {}

    def get(self, endpoint):
        self.endpoint = endpoint
        return {}


class TestProductionTools(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5056)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url="http://127.0.0.1:5056")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_numbering_reports_conflicts_without_failing(self):
        response = production_tools.run_automatic_numbering(self.client)

        self.assertTrue(response["success"])
        self.assertTrue(response["data"]["conflicts"])
        self.assertTrue(response["data"]["marks"])

    def test_exported_nc_files_are_traceable_to_numbered_single_part_marks(self):
        numbering = production_tools.run_automatic_numbering(self.client)
        marks_by_handle = {
            mark["handle"]: mark["single_part_mark"]
            for mark in numbering["data"]["marks"]
        }

        response = production_tools.export_dstv_nc_files(self.client, element_handles=["1B2C", "5E6F"])

        self.assertTrue(response["success"])
        self.assertEqual(response["data"]["exported_count"], 2)
        for exported_file in response["data"]["files"]:
            self.assertEqual(
                exported_file["single_part_mark"],
                marks_by_handle[exported_file["element_handle"]],
            )

    def test_export_refuses_unnumbered_model_before_producing_files(self):
        response = production_tools.export_dstv_nc_files(
            self.client, element_handles=[MOCK_UNNUMBERED_ELEMENT_HANDLE]
        )

        self.assertFalse(response["success"])
        self.assertEqual(response["error"]["code"], "UNNUMBERED_MODEL")
        self.assertIsNone(response["data"])

    def test_drawing_status_counts_add_up(self):
        response = production_tools.get_drawing_status(self.client, assembly_marks=["C1", "B1"])

        self.assertTrue(response["success"])
        report = response["data"]
        self.assertEqual(report["with_drawings"] + report["without_drawings"], report["total_assemblies"])
        self.assertEqual({assembly["assembly_mark"] for assembly in report["assemblies"]}, {"C1", "B1"})

    def test_optional_post_parameters_are_omitted_when_not_supplied(self):
        client = RecordingClient()

        production_tools.run_automatic_numbering(client)
        self.assertEqual(client.payload, {})
        production_tools.export_dstv_nc_files(client)
        self.assertEqual(client.payload, {})

    def test_engine_command_escape_hatch_reaches_the_add_in(self):
        """SPEC-004 §4: Advance Steel renames these commands between releases.

        The pinned name is useless unless it survives the trip to the add-in, and a silently
        dropped parameter would look exactly like an engine that does not exist.
        """
        client = RecordingClient()

        production_tools.run_automatic_numbering(client, engine_command="AstM2Numbering")
        self.assertEqual(client.payload["engine_command"], "AstM2Numbering")

        production_tools.export_dstv_nc_files(client, engine_command="AstM2NcFiles")
        self.assertEqual(client.payload["engine_command"], "AstM2NcFiles")

    def test_get_bill_of_materials_model_wide(self):
        res = production_tools.get_bill_of_materials(self.client)
        self.assertTrue(res["success"])
        data = res["data"]
        self.assertIn("total_weight_kg", data)
        self.assertIn("total_tonnage", data)
        self.assertIn("total_coating_area_m2", data)
        self.assertEqual(len(data["linear_members"]), 2)
        self.assertEqual(len(data["plates"]), 1)
        self.assertEqual(len(data["bolts"]), 1)
        self.assertEqual(data["group_by"], "profile")
        self.assertEqual(data["elements_scanned"], 6)

    def test_get_bill_of_materials_with_filter(self):
        res = production_tools.get_bill_of_materials(self.client, element_handles=["1B2C", "PLATE_202"])
        self.assertTrue(res["success"])
        data = res["data"]
        self.assertEqual(data["elements_scanned"], 2)



if __name__ == "__main__":
    unittest.main()
