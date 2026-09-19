import unittest
from examples.portal_frame_example import model_portal_frame
from examples.diagnose_and_audit_example import run_model_health_audit
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class TestExamples(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url=f"http://127.0.0.1:{cls.server.port}")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_portal_frame_example(self):
        success = model_portal_frame(self.client)
        self.assertTrue(success)

    def test_diagnose_audit_example(self):
        run_model_health_audit(self.client)


if __name__ == "__main__":
    unittest.main()
