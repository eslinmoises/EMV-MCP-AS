import json
import unittest
import urllib.request
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class TestMockAdvanceSteelServer(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
        cls.server.start()

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_mock_server_health(self):
        url = f"http://{self.server.host}:{self.server.port}/api/v1/health"
        req = urllib.request.Request(url)
        with urllib.request.urlopen(req) as response:
            self.assertEqual(response.status, 200)
            body = json.loads(response.read().decode("utf-8"))
            self.assertTrue(body["success"])
            self.assertEqual(body["data"]["status"], "online")
            self.assertEqual(body["data"]["as_version"], "2026")

    def test_mock_server_beam_creation(self):
        url = f"http://{self.server.host}:{self.server.port}/api/v1/elements/beam"
        payload = json.dumps({"start_point": [0, 0, 0], "end_point": [0, 0, 4000], "section_name": "HEB300"}).encode("utf-8")
        req = urllib.request.Request(url, data=payload, headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req) as response:
            self.assertEqual(response.status, 200)
            body = json.loads(response.read().decode("utf-8"))
            self.assertTrue(body["success"])
            self.assertEqual(body["data"]["handle"], "BEAM_101")
            self.assertEqual(body["data"]["section_name"], "HEB300")

    def test_mock_server_weld_verification(self):
        url = f"http://{self.server.host}:{self.server.port}/api/v1/assembly/verify-welds"
        req = urllib.request.Request(url)
        with urllib.request.urlopen(req) as response:
            self.assertEqual(response.status, 200)
            body = json.loads(response.read().decode("utf-8"))
            self.assertTrue(body["success"])
            self.assertEqual(len(body["data"]["welds"]), 2)
            self.assertEqual(body["data"]["total_workshop_welds"], 1)

    def test_mock_server_script_execution(self):
        url = f"http://{self.server.host}:{self.server.port}/api/v1/script/execute"
        payload = json.dumps({"script_code": "var x = 1 + 1;"}).encode("utf-8")
        req = urllib.request.Request(url, data=payload, headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req) as response:
            self.assertEqual(response.status, 200)
            body = json.loads(response.read().decode("utf-8"))
            self.assertTrue(body["success"])
            self.assertIn("Roslyn", body["data"]["output"])


if __name__ == "__main__":
    unittest.main()
