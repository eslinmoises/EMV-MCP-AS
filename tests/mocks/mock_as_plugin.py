"""Mock Advance Steel IPC Server implementing SPEC-001 and SPEC-002 endpoints.
Runs on http://127.0.0.1:5055 (or configured port) without requiring AutoCAD.
"""

import json
import ntpath
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

from tests.mocks.fixtures import (
    MOCK_DRAWING_STATUS_ASSEMBLIES,
    MOCK_HEALTH_DATA,
    MOCK_MAIN_PART_INSPECTION,
    MOCK_NUMBERING_CONFLICTS,
    MOCK_NUMBERING_MARKS,
    MOCK_SELECTED_ELEMENTS,
    MOCK_UCS_AND_GRIDS,
    MOCK_UNNUMBERED_ELEMENT_HANDLE,
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

    @staticmethod
    def _numbering_report(body_json):
        element_handles = body_json.get("element_handles")
        if element_handles is None:
            marks = list(MOCK_NUMBERING_MARKS)
        else:
            requested_handles = set(element_handles)
            marks = [mark for mark in MOCK_NUMBERING_MARKS if mark["handle"] in requested_handles]

        included_handles = {mark["handle"] for mark in marks}
        conflicts = [
            conflict for conflict in MOCK_NUMBERING_CONFLICTS
            if conflict["handle"] in included_handles
        ]
        return {
            "scope": "selection" if "element_handles" in body_json else "model",
            "numbered_single_parts": len(marks),
            "numbered_assemblies": len({mark["assembly_mark"] for mark in marks}),
            "already_numbered": 0,
            "marks": marks,
            "conflicts": conflicts,
            "warnings": [],
        }

    @staticmethod
    def _drawing_status_report(assembly_marks):
        if assembly_marks is None:
            assemblies = list(MOCK_DRAWING_STATUS_ASSEMBLIES)
        else:
            requested_marks = set(assembly_marks)
            assemblies = [
                assembly for assembly in MOCK_DRAWING_STATUS_ASSEMBLIES
                if assembly["assembly_mark"] in requested_marks
            ]

        with_drawings = sum(assembly["has_drawing"] for assembly in assemblies)
        return {
            "total_assemblies": len(assemblies),
            "with_drawings": with_drawings,
            "without_drawings": len(assemblies) - with_drawings,
            "assemblies": assemblies,
            "warnings": [],
        }

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
        elif path == "/api/v1/audit/assembly-integrity":
            self._send_envelope(data={"findings": [], "orphaned_parts": 0, "total_parts_scanned": 12})
        elif path == "/api/v1/audit/clashes":
            self._send_envelope(data={"clash_count": 0, "clashes": [], "method": "AABB_SweepAndPrune"})
        elif path == "/api/v1/viewport/capture":
            # 1x1 transparent PNG Base64
            dummy_png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
            self._send_envelope(data={"image_base64": dummy_png, "format": "png"})
        elif path == "/api/v1/production/drawing-status":
            query = parse_qs(parsed.query, keep_blank_values=True)
            requested_marks = query.get("assembly_marks")
            assembly_marks = None
            if requested_marks is not None:
                assembly_marks = [
                    mark for value in requested_marks for mark in value.split(",") if mark
                ]
            self._send_envelope(data=self._drawing_status_report(assembly_marks))
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
        elif path == "/api/v1/elements/joint":
            joint_type = body_json.get("joint_type", "BasePlate")
            self._send_envelope(
                data={"handle": "JOINT_303", "joint_type": joint_type, "created_objects": ["PLATE_202", "WELD_01"]}
            )
        elif path == "/api/v1/elements/cut":
            cut_type = body_json.get("cut_type", "shortening")
            self._send_envelope(
                data={"handle": "CUT_404", "cut_type": cut_type, "length_before_mm": 4000.0, "length_mm": 3900.0}
            )
        elif path == "/api/v1/elements/modify":
            handle = body_json.get("handle", "BEAM_101")
            self._send_envelope(
                data={"handle": handle, "modified_properties": body_json, "success": True}
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
        elif path == "/api/v1/production/numbering":
            self._send_envelope(data=self._numbering_report(body_json))
        elif path == "/api/v1/production/export-nc":
            requested_handles = body_json.get("element_handles")
            if requested_handles is not None and MOCK_UNNUMBERED_ELEMENT_HANDLE in requested_handles:
                self._send_envelope(
                    error={
                        "code": "UNNUMBERED_MODEL",
                        "message": "Numbering must run before NC export.",
                        "details": "Part 9C0D has no single-part mark.",
                        "suggestion": "Run automatic numbering before exporting DSTV/NC files.",
                    },
                    status_code=409,
                )
                return

            if requested_handles is None:
                marks = list(MOCK_NUMBERING_MARKS)
            else:
                requested_handle_set = set(requested_handles)
                marks = [mark for mark in MOCK_NUMBERING_MARKS if mark["handle"] in requested_handle_set]

            output_directory = body_json.get("output_directory", "./DSTV_NC1")
            if not ntpath.isabs(output_directory):
                output_directory = ntpath.join(ntpath.dirname(MOCK_HEALTH_DATA["active_dwg"]), output_directory)
            output_directory = ntpath.normpath(output_directory)
            file_extension = body_json.get("file_extension", "nc1")
            files = [
                {
                    "file_name": f"{mark['single_part_mark']}.{file_extension}",
                    "path": ntpath.join(output_directory, f"{mark['single_part_mark']}.{file_extension}"),
                    "element_handle": mark["handle"],
                    "single_part_mark": mark["single_part_mark"],
                    "assembly_mark": mark["assembly_mark"],
                    "size_bytes": 4096,
                }
                for mark in marks
            ]
            self._send_envelope(
                data={
                    "output_directory": output_directory,
                    "file_extension": file_extension,
                    "exported_count": len(files),
                    "total_bytes": sum(file["size_bytes"] for file in files),
                    "files": files,
                    "skipped": [],
                    "warnings": [],
                }
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
