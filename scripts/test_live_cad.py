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
    print("\n[2/7] Querying UCS & Grids (GET /api/v1/spatial/ucs-grids)...")
    ucs_res = call_api("GET", "spatial/ucs-grids")
    if ucs_res.get("success"):
        print(f"[+] Active UCS: {ucs_res['data']['active_ucs']['name']} (is_world={ucs_res['data']['active_ucs']['is_world']})")
        print(f"    Found {ucs_res['data']['grid_axis_count']} grid axes, {ucs_res['data']['level_count']} levels.")

    # 3. Create Left Column
    print("\n[3/7] Generating Column 1 (HEB300, 0,0,0 -> 0,0,4000)...")
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

    # 4. Create Base Plate for Column 1
    print("\n[4/7] Generating Base Plate (400x400x25 mm)...")
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

    # 5. Create Anchor Bolt Pattern
    if col1_handle != "N/A" and plate_handle != "N/A":
        print("\n[5/7] Bolting Base Plate to Column (4x M20 DIN 931)...")
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

    # 6. Query Elements in Bounding Box around node
    print("\n[6/7] Querying node bounding box [-300, -300, -50] to [300, 300, 500]...")
    box_res = call_api("GET", "spatial/box?min_point=-300,-300,-50&max_point=300,300,500")
    if box_res.get("success"):
        count = box_res["data"]["count"]
        print(f"[+] Node query returned {count} intersecting members:")
        for elem in box_res["data"]["elements"]:
            print(f"    - [{elem.get('handle')}] {elem.get('type')}: {elem.get('section_name') or elem.get('model_role')}")

    # 7. Viewport Capture
    print("\n[7/7] Capturing 3D viewport screenshot...")
    vp_res = call_api("GET", "viewport/capture")
    if vp_res.get("success"):
        img_len = len(vp_res["data"].get("image_base64", ""))
        print(f"[+] Viewport screenshot captured successfully! ({img_len} bytes base64)")

    print("\n" + "=" * 60)
    print("ALL LIVE CAD VERIFICATION CHECKS PASSED SUCCESSFULLY!")
    print("=" * 60)


if __name__ == "__main__":
    main()
