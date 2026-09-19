import unittest
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import modeling_tools
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class TestGridsAndLevels(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url=f"http://127.0.0.1:{cls.server.port}")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_create_structural_grid_default(self):
        res = modeling_tools.create_structural_grid(
            self.client,
            origin=[0.0, 0.0, 0.0],
            axis_direction=[0.0, 1.0, 0.0],
            spacing_direction=[1.0, 0.0, 0.0],
            line_length=30000.0,
            count=7,
            spacing=5000.0,
        )
        self.assertTrue(res["success"])
        data = res["data"]
        self.assertEqual(data["axis_count"], 7)
        self.assertEqual(data["grid_handle"], "GRID_94E")
        self.assertEqual(len(data["axes"]), 7)
        self.assertEqual(data["axes"][0]["name"], "1")
        self.assertEqual(data["axes"][6]["name"], "7")

    def test_create_structural_grid_custom_labels_and_spacings(self):
        res = modeling_tools.create_structural_grid(
            self.client,
            line_length=20000.0,
            spacings=[6000.0, 6000.0, 6000.0],
            labels=["A", "B", "C", "D"],
            text_location="Both",
        )
        self.assertTrue(res["success"])
        data = res["data"]
        self.assertEqual(data["axis_count"], 4)
        self.assertEqual(data["axes"][0]["name"], "A")
        self.assertEqual(data["axes"][3]["name"], "D")

    def test_create_structural_level(self):
        res = modeling_tools.create_structural_level(
            self.client,
            name="Nivel +6.00m (Alero)",
            elevation=6000.0,
        )
        self.assertTrue(res["success"])
        data = res["data"]
        self.assertEqual(data["name"], "Nivel +6.00m (Alero)")
        self.assertEqual(data["elevation"], 6000.0)
        self.assertTrue(data["registered"])
        self.assertIn("handle", data)
