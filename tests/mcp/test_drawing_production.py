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
        drawings = [
            {
                "assembly_handle": handle,
                "drawing_number": f"{handle}-01",
                "status": "generated",
            }
            for handle in payload.get("assembly_handles", [])
        ]
        return {
            "success": True,
            "data": {
                "requested_count": len(drawings),
                "generated_count": len(drawings),
                "drawings": drawings,
            },
            "error": None,
        }

    def get(self, endpoint):
        self.endpoint = endpoint
        return {}


class TestDrawingProduction(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5065)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url="http://127.0.0.1:5065")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_generate_shop_drawings_forwards_options_and_returns_records(self):
        client = RecordingClient()

        response = production_tools.generate_shop_drawings(
            client,
            assembly_handles=["1B2C", "5E6F"],
            drawing_style="Assembly A3",
            sheet_size="A3",
            engine_command="AstM4CommDetailingProc",
            prototype_path=r"C:\Prototypes\Assembly-A3.dwg",
        )

        self.assertEqual(client.endpoint, "production/generate-drawings")
        self.assertEqual(
            client.payload,
            {
                "assembly_handles": ["1B2C", "5E6F"],
                "drawing_style": "Assembly A3",
                "sheet_size": "A3",
                "engine_command": "AstM4CommDetailingProc",
                "prototype_path": r"C:\Prototypes\Assembly-A3.dwg",
            },
        )
        self.assertEqual(response["data"]["generated_count"], 2)
        self.assertTrue(all(record["drawing_number"] for record in response["data"]["drawings"]))

    def test_generate_shop_drawings_omits_unsupplied_optional_values(self):
        client = RecordingClient()

        production_tools.generate_shop_drawings(client, assembly_handles=["1B2C"])

        self.assertEqual(client.payload, {"assembly_handles": ["1B2C"]})

    def test_mock_reports_existing_missing_and_stale_drawings(self):
        response = production_tools.get_drawing_status(self.client)

        self.assertTrue(response["success"])
        records = response["data"]["assemblies"]
        self.assertTrue(any(record["has_drawing"] for record in records))
        self.assertTrue(any(not record["has_drawing"] for record in records))
        self.assertTrue(any(record["has_drawing"] and not record["is_up_to_date"] for record in records))

    def test_drawing_status_supports_contract_handle_filter(self):
        client = RecordingClient()

        production_tools.get_drawing_status(
            client, assembly_marks=["C 1", "B/1"], assembly_handle="1B 2C"
        )

        self.assertEqual(
            client.endpoint,
            "production/drawing-status?assembly_marks=C%201,B%2F1&assembly_handle=1B%202C",
        )

    def test_mock_rejects_nc_export_for_unnumbered_part(self):
        response = production_tools.export_dstv_nc_files(
            self.client, element_handles=[MOCK_UNNUMBERED_ELEMENT_HANDLE]
        )

        self.assertFalse(response["success"])
        self.assertEqual(response["error"]["code"], "UNNUMBERED_MODEL")


if __name__ == "__main__":
    unittest.main()
