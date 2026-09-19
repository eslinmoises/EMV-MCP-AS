import unittest
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import modeling_tools
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class TestTrussedWarehouse(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url=f"http://127.0.0.1:{cls.server.port}")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_create_trussed_warehouse_default(self):
        res = modeling_tools.create_trussed_warehouse(
            self.client,
            span=31000.0,
            length=30000.0,
            bay_spacing=5000.0,
            eave_height=6000.0,
            ridge_height=9500.0,
            column_width=1000.0,
            truss_depth=2000.0,
            profile="RHS_Sections_square_c nach DIN#@§@#Q90X3",
            material="S235JR",
            create_grids_and_levels=True,
        )
        self.assertTrue(res["success"])
        data = res["data"]
        self.assertEqual(data["frames_count"], 7)
        self.assertEqual(data["columns"]["count"], 14)
        self.assertEqual(data["rafters"]["count"], 14)
        self.assertEqual(data["all_bars_count"], 532)
        self.assertIsNotNone(data["grids_created"])
        self.assertEqual(len(data["levels_created"]), 3)
