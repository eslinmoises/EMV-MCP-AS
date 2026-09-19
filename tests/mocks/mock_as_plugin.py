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
        elif path == "/api/v1/spatial/box":
            query = parse_qs(parsed.query, keep_blank_values=True)
            min_raw = query.get("min_point", ["0,0,0"])[0].split(",")
            max_raw = query.get("max_point", ["1000,1000,1000"])[0].split(",")
            min_pt = [float(c) for c in min_raw]
            max_pt = [float(c) for c in max_raw]
            self._send_envelope(
                data={
                    "elements": [
                        {
                            "handle": "1B2C",
                            "type": "StraightBeam",
                            "section_name": "HEB300",
                            "material": "S275JR",
                            "model_role": "Column",
                            "bounding_box": {
                                "min_point": [0.0, 0.0, 0.0],
                                "max_point": [300.0, 300.0, 4000.0],
                            },
                        }
                    ],
                    "count": 1,
                    "box": {
                        "min_point": min_pt,
                        "max_point": max_pt,
                    },
                }
            )
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
        elif path == "/api/v1/elements/joints-catalog":
            self._send_envelope(
                data={
                    "total_count": 4,
                    "joints": [
                        {
                            "joint_type": "BasePlate",
                            "rule_name": "AstorJoints.BasePlate",
                            "description": "Column base plate with anchor bolts, stiffeners, and grout bed.",
                            "primary_roles": ["Column"],
                            "secondary_roles": [],
                        },
                        {
                            "joint_type": "ClipAngle",
                            "rule_name": "AstorJoints.ClipAngle",
                            "description": "Beam to column web/flange or beam to beam clip angle connection.",
                            "primary_roles": ["Column", "Beam"],
                            "secondary_roles": ["Beam"],
                        },
                        {
                            "joint_type": "EndPlate",
                            "rule_name": "AstorJoints.EndPlate",
                            "description": "Bolted end plate connection between beam and column or beam splice.",
                            "primary_roles": ["Column", "Beam"],
                            "secondary_roles": ["Beam"],
                        },
                        {
                            "joint_type": "ApexHaunch",
                            "rule_name": "AstorJoints.ApexHaunch",
                            "description": "Gable roof ridge apex connection with haunch reinforcement.",
                            "primary_roles": ["Rafter"],
                            "secondary_roles": ["Rafter"],
                        },
                    ],
                }
            )
        elif path == "/api/v1/elements/validate-section":
            query = parse_qs(parsed.query, keep_blank_values=True)
            section_name = query.get("section_name", query.get("name", [""]))[0]
            if not section_name:
                self._send_envelope(
                    error={"code": "MISSING_PARAMETER", "message": "Parameter 'section_name' is required."},
                    status_code=400,
                )
            else:
                is_valid = section_name.upper() in [
                    "HEB300", "HEA200", "HEA240", "IPE300", "IPE200", "UB203X133X25", "CHS114.3X6.3"
                ]
                self._send_envelope(
                    data={
                        "section_name": section_name,
                        "is_valid": is_valid,
                        "message": f"Section '{section_name}' exists in AstorProfiles catalogue."
                        if is_valid
                        else f"Section '{section_name}' was not found in AstorProfiles database.",
                    }
                )
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
        elif path == "/api/v1/elements/bolt":
            handles = body_json.get("connected_handles", ["1B2C", "2D3E"])
            standard = body_json.get("bolt_standard", "DIN 931")
            grade = body_json.get("bolt_grade", "8.8")
            diam = float(body_json.get("bolt_diameter_mm", 20.0))
            nx = int(body_json.get("nx", 2))
            ny = int(body_json.get("ny", 2))
            is_site = bool(body_json.get("is_site_bolt", True))
            self._send_envelope(
                data={
                    "handle": "BOLT_501",
                    "bolt_standard": standard,
                    "bolt_grade": grade,
                    "bolt_diameter_mm": diam,
                    "count": nx * ny,
                    "connected_handles": handles,
                    "is_site_bolt": is_site,
                }
            )
        elif path == "/api/v1/elements/poly-beam":
            points = body_json.get("points", [[0, 0, 0], [1000, 0, 0]])
            section = body_json.get("section_name", "HEA200")
            self._send_envelope(
                data={
                    "handle": "PBEAM_601",
                    "section_name": section,
                    "length_mm": 6283.18,
                    "weight_kg": 265.8,
                    "vertex_count": len(points),
                }
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
        elif path == "/api/v1/elements/query":
            mock_elements = [
                {
                    "handle": "1B2C",
                    "type": "StraightBeam",
                    "role": "Column",
                    "section_name": "HEB300",
                    "material": "S275JR",
                    "lot_phase": "Phase 1",
                    "single_part_mark": "c1",
                    "assembly_mark": "C1",
                    "length_mm": 4000.0,
                    "weight_kg": 468.0,
                    "center_point": [0.0, 0.0, 2000.0],
                },
                {
                    "handle": "2D3E",
                    "type": "StraightBeam",
                    "role": "Beam",
                    "section_name": "IPE300",
                    "material": "S275JR",
                    "lot_phase": "Phase 1",
                    "single_part_mark": "b1",
                    "assembly_mark": "B1",
                    "length_mm": 6000.0,
                    "weight_kg": 253.2,
                    "center_point": [3000.0, 0.0, 4000.0],
                },
            ]
            role = body_json.get("model_role")
            types = body_json.get("element_types")
            sect = body_json.get("section_name")
            filtered = mock_elements
            if role:
                filtered = [e for e in filtered if e["role"].lower() == role.lower()]
            if types:
                type_set = set(t.lower() for t in types)
                filtered = [e for e in filtered if e["type"].lower() in type_set]
            if sect:
                filtered = [e for e in filtered if sect.lower() in e["section_name"].lower()]
            self._send_envelope(
                data={
                    "elements": filtered,
                    "count": len(filtered),
                    "filters_applied": body_json,
                }
            )
        elif path == "/api/v1/production/bom":
            handles = body_json.get("element_handles")
            group_by = body_json.get("group_by", "profile")
            scanned = 6 if handles is None else len(handles)
            self._send_envelope(
                data={
                    "total_weight_kg": 1845.6,
                    "total_tonnage": 1.846,
                    "total_coating_area_m2": 32.45,
                    "linear_members": [
                        {
                            "section_name": "HEB300",
                            "material": "S275JR",
                            "count": 2,
                            "total_length_mm": 8000.0,
                            "total_weight_kg": 936.4,
                            "coating_area_m2": 15.2,
                        },
                        {
                            "section_name": "IPE300",
                            "material": "S275JR",
                            "count": 2,
                            "total_length_mm": 12000.0,
                            "total_weight_kg": 506.4,
                            "coating_area_m2": 14.1,
                        },
                    ],
                    "plates": [
                        {
                            "thickness_mm": 25.0,
                            "material": "S275JR",
                            "count": 2,
                            "total_area_m2": 0.32,
                            "total_weight_kg": 62.8,
                        }
                    ],
                    "bolts": [
                        {
                            "bolt_standard": "DIN 931",
                            "bolt_grade": "8.8",
                            "bolt_diameter_mm": 20.0,
                            "count": 8,
                        }
                    ],
                    "group_by": group_by,
                    "elements_scanned": scanned,
                }
            )
        elif path == "/api/v1/elements/portal-frame":
            span = float(body_json.get("span_mm", 12000.0))
            eave = float(body_json.get("eave_height_mm", 6000.0))
            ridge = float(body_json.get("ridge_height_mm", 7500.0))
            col_sec = body_json.get("column_section", "HEA 300")
            raf_sec = body_json.get("rafter_section", "IPE 300")
            mat = body_json.get("material", "S275JR")
            has_bp = bool(body_json.get("create_base_plates", True))
            bp_handles = ["BP_L_01", "BP_R_02"] if has_bp else []
            self._send_envelope(
                data={
                    "portal_frame_id": "PF_001",
                    "column_left_handle": "COL_L_01",
                    "column_right_handle": "COL_R_02",
                    "rafter_left_handle": "RAF_L_01",
                    "rafter_right_handle": "RAF_R_02",
                    "base_plate_handles": bp_handles,
                    "geometry": {
                        "span_mm": span,
                        "eave_height_mm": eave,
                        "ridge_height_mm": ridge,
                        "column_section": col_sec,
                        "rafter_section": raf_sec,
                        "material": mat,
                    },
                    "total_weight_kg": 1450.0,
                }
            )
        elif path == "/api/v1/audit/repair":
            actions = body_json.get("repair_actions", [])
            dry_run = bool(body_json.get("dry_run", False))
            repairs = []
            if "assign_orphaned_plates" in actions:
                repairs.append({
                    "action": "assign_orphaned_plates",
                    "element_handle": "PL_ORPHAN_01",
                    "target_assembly_handle": "COL_01",
                    "description": "Attached orphaned workshop plate to closest column assembly.",
                })
            if "infer_missing_roles" in actions:
                repairs.append({
                    "action": "infer_missing_roles",
                    "element_handle": "BEAM_NOROLE_02",
                    "target_assembly_handle": None,
                    "description": "Inferred model role 'Column' based on vertical orientation (z-dir > 0.75).",
                })
            if "standardize_coatings" in actions:
                repairs.append({
                    "action": "standardize_coatings",
                    "element_handle": "BEAM_COAT_03",
                    "target_assembly_handle": None,
                    "description": "Updated coating from 'None' to 'Galvanized'.",
                })
            self._send_envelope(
                data={
                    "repairs_applied": repairs,
                    "count": len(repairs),
                    "dry_run": dry_run,
                    "summary": f"{len(repairs)} detailing issues identified and {'previewed' if dry_run else 'repaired'}.",
                }
            )
        elif path == "/api/v1/spatial/grid":
            line_len = float(body_json.get("line_length", 30000.0))
            count = int(body_json.get("count", 2))
            spacing = float(body_json.get("spacing", 5000.0))
            spacings = body_json.get("spacings")
            if spacings:
                count = len(spacings) + 1
            labels = body_json.get("labels") or [str(i + 1) for i in range(count)]
            axes = [
                {
                    "name": labels[i] if i < len(labels) else str(i + 1),
                    "start": [i * spacing, 0.0, 0.0],
                    "end": [i * spacing, line_len, 0.0],
                    "length": line_len,
                    "grid_handle": "GRID_94E",
                    "grid_type": "k1DGrid",
                }
                for i in range(count)
            ]
            self._send_envelope(
                data={
                    "grid_handle": "GRID_94E",
                    "grid_type": "k1DGrid",
                    "axis_count": len(axes),
                    "axes": axes,
                }
            )
        elif path == "/api/v1/spatial/level":
            name = body_json.get("name", "Nivel +0.00m")
            elev = float(body_json.get("elevation", 0.0))
            self._send_envelope(
                data={
                    "handle": "LVL_955",
                    "name": name,
                    "elevation": elev,
                    "tree_parent": "BuildingStructureTreeObject",
                    "registered": True,
                }
            )
        elif path == "/api/v1/elements/trussed-warehouse":
            span = float(body_json.get("span", 31000.0))
            length = float(body_json.get("length", 30000.0))
            bay_spacing = float(body_json.get("bay_spacing", 5000.0))
            frames_count = int(length / bay_spacing) + 1
            self._send_envelope(
                data={
                    "frames_count": frames_count,
                    "total_bars": 532,
                    "total_weight_kg": 25420.0,
                    "columns": {
                        "count": frames_count * 2,
                        "handles": [f"COL_{i}" for i in range(frames_count * 2)],
                    },
                    "rafters": {
                        "count": frames_count * 2,
                        "handles": [f"RAF_{i}" for i in range(frames_count * 2)],
                    },
                    "base_plates": {
                        "count": frames_count * 4,
                        "handles": [f"BP_{i}" for i in range(frames_count * 4)],
                    },
                    "all_bars_count": 532,
                    "grids_created": {
                        "transverse_handle": "GRID_94E",
                        "longitudinal_handle": "GRID_94F",
                        "axes_count": frames_count + 2,
                    },
                    "levels_created": [
                        {"name": "Nivel 0.00m (Cimentación)", "elevation": 0.0, "handle": "LVL_0"},
                        {"name": "Nivel +6.00m (Alero)", "elevation": 6000.0, "handle": "LVL_1"},
                        {"name": "Nivel +9.50m (Cumbrera)", "elevation": 9500.0, "handle": "LVL_2"},
                    ],
                }
            )
        elif path == "/api/v1/elements/engineered-joint":
            conn_name = body_json.get("connection_name", "Engineered Connection")
            source_sys = body_json.get("source_system", "Calculation Report")
            plates = body_json.get("plates", [])
            bolts = body_json.get("bolt_groups", [])
            welds = body_json.get("shop_welds", [])
            self._send_envelope(
                data={
                    "connection_name": conn_name,
                    "source_system": source_sys,
                    "plates_created": [
                        {"name": p.get("name", "Plate"), "handle": f"PL_{i}", "thickness_mm": p.get("thickness_mm", 20.0)}
                        for i, p in enumerate(plates)
                    ],
                    "bolt_groups_created": [
                        {"handle": f"BLT_{i}", "count": b.get("nx", 2) * b.get("ny", 2), "diameter_mm": b.get("bolt_diameter_mm", 20.0)}
                        for i, b in enumerate(bolts)
                    ],
                    "welds_created": [
                        {
                            "handle": f"WLD_{i}",
                            "location": "Workshop",
                            "throat_thickness_mm": w.get("throat_thickness_mm", 6.0),
                            "assembly_bound": True,
                        }
                        for i, w in enumerate(welds)
                    ],
                    "verified": True,
                    "success": True,
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
        try:
            self.server = ThreadingHTTPServer((self.host, self.port), MockAdvanceSteelHandler)
        except (PermissionError, OSError):
            import socket
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                s.bind((self.host, 0))
                self.port = s.getsockname()[1]
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
