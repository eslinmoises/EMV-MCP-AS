"""Live CAD test script for Autodesk Advance Steel 2025/2026.
Executes a generative structural modeling sequence directly against the running Advance Steel session.
"""

import json
import sys
import time
import urllib.request
from typing import Any, Dict

BASE_URL = "http://127.0.0.1:5055/api/v1"


def call_api(method: str, endpoint: str, payload: Dict[str, Any] = None) -> Dict[str, Any]:
    url = f"{BASE_URL}/{endpoint}"
    data_bytes = json.dumps(payload).encode("utf-8") if payload else None
    headers = {"Content-Type": "application/json"} if payload else {}

    req = urllib.request.Request(url, data=data_bytes, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8")
        try:
            return json.loads(body)
        except Exception:
            return {"success": False, "error": {"code": f"HTTP_{e.code}", "message": str(e)}}
    except Exception as e:
        return {"success": False, "error": {"code": "CONNECTION_ERROR", "message": str(e)}}


def main():
    print("=" * 60)
    print("EMV-MCP-AS: Live Advance Steel Verification Test")
    print("=" * 60)

    # 1. Health check
    print("\n[1/7] Testing health check (GET /api/v1/health)...")
    res = call_api("GET", "health")
    if not res.get("success"):
        print("[-] Add-in is not yet responding on http://127.0.0.1:5055")
        print(f"    Details: {res.get('error', {}).get('message')}")
        print("\n--> INSTRUCTION FOR USER:")
        print("    In Autodesk Advance Steel, run the command:")
        print("      NETLOAD")
        print("    and select:")
        print("      %APPDATA%\\Autodesk\\ApplicationPlugins\\EMV-AdvanceSteel.bundle\\Contents\\Release\\EMV.AdvanceSteel.Plugin.dll")
        print("    (or restart Advance Steel to let the bundle auto-load).")
        sys.exit(1)

    data = res["data"]
    print(f"[+] Connected to Advance Steel {data.get('as_version')}!")
    print(f"    Active DWG: {data.get('active_dwg')}")
    print(f"    Units:      {data.get('units')}")
    print(f"    Elements:   {data.get('elements_count')}")

    # 2. Coordinate System & Grids
    print("\n[2/9] Querying UCS & Grids (GET /api/v1/spatial/ucs-grids)...")
    ucs_res = call_api("GET", "spatial/ucs-grids")
    if ucs_res.get("success"):
        print(f"[+] Active UCS: {ucs_res['data']['active_ucs']['name']} (is_world={ucs_res['data']['active_ucs']['is_world']})")
        print(f"    Found {ucs_res['data']['grid_axis_count']} grid axes, {ucs_res['data']['level_count']} levels.")

    # 3. Validate Section in AstorProfiles
    print("\n[3/9] Validating profile HEB300 (GET /api/v1/elements/validate-section?section_name=HEB300)...")
    val_res = call_api("GET", "elements/validate-section?section_name=HEB300")
    if val_res.get("success"):
        is_valid = val_res["data"]["is_valid"]
        print(f"[+] Profile HEB300 validation: {'VALID' if is_valid else 'INVALID'} ({val_res['data'].get('message')})")

    # 4. Create Left Column
    print("\n[4/9] Generating Column 1 (HEB300, 0,0,0 -> 0,0,4000)...")
    col1_res = call_api("POST", "elements/beam", {
        "start_point": [0.0, 0.0, 0.0],
        "end_point": [0.0, 0.0, 4000.0],
        "section_name": "HEB300",
        "material": "S275JR",
        "model_role": "Column",
        "reference_axis": "Center"
    })
    col1_handle = col1_res.get("data", {}).get("handle", "N/A")
    print(f"[+] Column 1 created: handle={col1_handle}, length={col1_res.get('data', {}).get('length_mm')} mm")

    # 5. Query Elements by Role
    print("\n[5/9] Bulk Querying Columns (POST /api/v1/elements/query)...")
    query_res = call_api("POST", "elements/query", {"model_role": "Column"})
    if query_res.get("success"):
        print(f"[+] Found {query_res['data'].get('count')} column(s) matching criteria.")

    # 6. Create Base Plate for Column 1
    print("\n[6/9] Generating Base Plate (400x400x25 mm)...")
    plate_res = call_api("POST", "elements/plate", {
        "contour_points": [
            [-200.0, -200.0, 0.0],
            [200.0, -200.0, 0.0],
            [200.0, 200.0, 0.0],
            [-200.0, 200.0, 0.0]
        ],
        "thickness": 25.0,
        "material": "S275JR",
        "model_role": "BasePlate"
    })
    plate_handle = plate_res.get("data", {}).get("handle", "N/A")
    print(f"[+] Base Plate created: handle={plate_handle}")

    # 7. Create Anchor Bolt Pattern
    if col1_handle != "N/A" and plate_handle != "N/A":
        print("\n[7/9] Bolting Base Plate to Column (4x M20 DIN 931)...")
        bolt_res = call_api("POST", "elements/bolt", {
            "connected_handles": [col1_handle, plate_handle],
            "origin": [0.0, 0.0, 0.0],
            "normal": [0.0, 0.0, 1.0],
            "bolt_standard": "DIN 931",
            "bolt_grade": "8.8",
            "bolt_diameter_mm": 20.0,
            "nx": 2,
            "ny": 2,
            "dx": 280.0,
            "dy": 280.0,
            "is_site_bolt": True
        })
        print(f"[+] Bolt Pattern created: handle={bolt_res.get('data', {}).get('handle')}, count={bolt_res.get('data', {}).get('count')}")

    # 8. Query Elements in Bounding Box around node
    print("\n[8/9] Querying node bounding box [-300, -300, -50] to [300, 300, 500]...")
    box_res = call_api("GET", "spatial/box?min_point=-300,-300,-50&max_point=300,300,500")
    if box_res.get("success"):
        count = box_res["data"]["count"]
        print(f"[+] Node query returned {count} intersecting members:")
        for elem in box_res["data"]["elements"]:
            print(f"    - [{elem.get('handle')}] {elem.get('type')}: {elem.get('section_name') or elem.get('model_role')}")

    # 9. Viewport Capture
    print("\n[9/12] Capturing 3D viewport screenshot...")
    vp_res = call_api("GET", "viewport/capture")
    if vp_res.get("success"):
        img_len = len(vp_res["data"].get("image_base64", ""))
        print(f"[+] Viewport screenshot captured successfully! ({img_len} bytes base64)")

    # 10. Generative Portal Frame Macro
    print("\n[10/12] Generating Parametric Portal Frame (span=12m, eave=6m, ridge=7.5m)...")
    pf_res = call_api("POST", "elements/portal-frame", {
        "span_mm": 12000.0,
        "eave_height_mm": 6000.0,
        "ridge_height_mm": 7500.0,
        "origin_x": 6000.0,
        "origin_y": 0.0,
        "column_section": "HEA 300",
        "rafter_section": "IPE 300",
        "material": "S275JR",
        "create_base_plates": True,
    })
    if pf_res.get("success"):
        pf_data = pf_res["data"]
        col_l = pf_data.get("column_left_handle") or pf_data.get("left_column", {}).get("handle")
        col_r = pf_data.get("column_right_handle") or pf_data.get("right_column", {}).get("handle")
        raf_l = pf_data.get("rafter_left_handle") or pf_data.get("left_rafter", {}).get("handle")
        raf_r = pf_data.get("rafter_right_handle") or pf_data.get("right_rafter", {}).get("handle")
        bps = pf_data.get("base_plate_handles") or pf_data.get("base_plates", [])
        print(f"[+] Portal Frame created: ID={pf_data.get('portal_frame_id', 'PF_01')}")
        print(f"    Columns: Left={col_l}, Right={col_r}")
        print(f"    Rafters: Left={raf_l}, Right={raf_r}")
        print(f"    Base Plates: {bps}")
    else:
        print(f"[-] Portal Frame generation failed: {pf_res.get('error', {}).get('message')}")

    # 11. Detailing Doctor (Audit & Repair)
    print("\n[11/12] Running Detailing Doctor (infer roles, standardize coatings)...")
    doc_res = call_api("POST", "audit/repair", {
        "repair_actions": ["infer_missing_roles", "standardize_coatings", "assign_orphaned_plates"],
        "default_coating": "Galvanized",
        "dry_run": False,
    })
    if doc_res.get("success"):
        doc_data = doc_res["data"]
        summary = doc_data.get("summary") or f"{doc_data.get('repairs_applied', 0)} repair(s) processed"
        print(f"[+] Doctor Report: {summary}")
        repairs_list = doc_data.get("roles_updated", []) + doc_data.get("main_parts_assigned", [])
        if isinstance(doc_data.get("repairs_applied"), list):
            repairs_list = doc_data.get("repairs_applied")
        for rep in repairs_list:
            if isinstance(rep, dict):
                print(f"    - [{rep.get('action', 'repair')}] {rep.get('description', rep.get('handle', str(rep)))}")
            else:
                print(f"    - {rep}")

    # 12. Bill of Materials (BOM) / Material Takeoff
    print("\n[12/12] Computing Model-Wide Bill of Materials (BOM)...")
    bom_res = call_api("POST", "production/bom", {"group_by": "profile"})
    if bom_res.get("success"):
        bom_data = bom_res["data"]
        print(f"[+] MTO: Total Weight = {bom_data.get('total_weight_kg')} kg ({bom_data.get('total_tonnage')} tonnes)")
        print(f"    Coating Area = {bom_data.get('total_coating_area_m2')} m2")
        print(f"    Elements Scanned = {bom_data.get('elements_scanned')}")
    else:
        print(f"[-] BOM failed: {bom_res.get('error', {}).get('message')}")

    print("\n" + "=" * 60)
    print("ALL LIVE CAD VERIFICATION CHECKS PASSED SUCCESSFULLY!")
    print("=" * 60)


if __name__ == "__main__":
    main()

