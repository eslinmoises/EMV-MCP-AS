"""Mock Advance Steel IPC Server implementing SPEC-001 and SPEC-002 endpoints.
Runs on http://127.0.0.1:5055 (or configured port) without requiring AutoCAD.
"""

import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

from tests.mocks.fixtures import (
    MOCK_HEALTH_DATA,
    MOCK_MAIN_PART_INSPECTION,
    MOCK_SELECTED_ELEMENTS,
    MOCK_UCS_AND_GRIDS,
    MOCK_WELD_VERIFICATION,
)


class MockAdvanceSteelHandler(BaseHTTPRequestHandler):
    def _send_envelope(self, data=None, error=None, status_code=200):
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.end_headers()
        payload = {
            "success": error is None,
            "data": data,
            "error": error,
            "execution_time_ms": 15,
        }
        self.wfile.write(json.dumps(payload).encode("utf-8"))

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        if path == "/api/v1/health":
            self._send_envelope(data=MOCK_HEALTH_DATA)
        elif path == "/api/v1/elements/selected":
            self._send_envelope(data=MOCK_SELECTED_ELEMENTS)
        elif path == "/api/v1/assembly/verify-welds":
            self._send_envelope(data=MOCK_WELD_VERIFICATION)
        elif path == "/api/v1/assembly/main-part":
            self._send_envelope(data=MOCK_MAIN_PART_INSPECTION)
        elif path == "/api/v1/spatial/ucs-grids":
            self._send_envelope(data=MOCK_UCS_AND_GRIDS)
        elif path == "/api/v1/viewport/capture":
            # 1x1 transparent PNG Base64
            dummy_png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
            self._send_envelope(data={"image_base64": dummy_png, "format": "png"})
        else:
            self._send_envelope(
                error={"code": "ENDPOINT_NOT_FOUND", "message": f"Unknown endpoint: {path}"},
                status_code=404,
            )

    def do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path
        content_length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(content_length).decode("utf-8") if content_length > 0 else "{}"
        try:
            body_json = json.loads(body)
        except json.JSONDecodeError:
            self._send_envelope(
                error={"code": "INVALID_JSON", "message": "Malformed JSON payload"},
                status_code=400,
            )
            return

        if path == "/api/v1/elements/beam":
            section = body_json.get("section_name", "IPE300")
            self._send_envelope(
                data={"handle": "BEAM_101", "section_name": section, "length_mm": 4000.0, "weight_kg": 420.0}
            )
        elif path == "/api/v1/elements/plate":
            thickness = body_json.get("thickness", 20.0)
            self._send_envelope(
                data={"handle": "PLATE_202", "thickness_mm": thickness, "area_m2": 0.16, "weight_kg": 25.1}
            )
        elif path == "/api/v1/assembly/set-main-part":
            new_handle = body_json.get("new_main_part_handle", "1B2C")
            self._send_envelope(
                data={"assembly_mark": "C1", "main_part_handle": new_handle, "success": True}
            )
        elif path == "/api/v1/script/execute":
            code = body_json.get("script_code", "")
            if "throw" in code or "Exception" in code:
                self._send_envelope(
                    error={"code": "SCRIPT_EXCEPTION", "message": "Simulated script execution error"},
                    status_code=500,
                )
            else:
                self._send_envelope(
                    data={"success": True, "output": "Roslyn execution completed successfully."}
                )
        else:
            self._send_envelope(
                error={"code": "ENDPOINT_NOT_FOUND", "message": f"Unknown endpoint: {path}"},
                status_code=404,
            )

    def log_message(self, format, *args):
        pass  # Quiet logging for tests


class MockAdvanceSteelServer:
    def __init__(self, host="127.0.0.1", port=5055):
        self.host = host
        self.port = port
        self.server = None
        self.thread = None

    def start(self):
        self.server = ThreadingHTTPServer((self.host, self.port), MockAdvanceSteelHandler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        print(f"Mock Advance Steel server listening on http://{self.host}:{self.port}")

    def stop(self):
        if self.server:
            self.server.shutdown()
            self.server.server_close()
            self.thread.join(timeout=2)
            print("Mock Advance Steel server stopped.")


if __name__ == "__main__":
    import time
    server = MockAdvanceSteelServer()
    server.start()
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        server.stop()
