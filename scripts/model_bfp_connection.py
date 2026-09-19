"""Script to model the Bolted Flange Plate (BFP) connection according to ANSI/AISC 358-16.

Specs taken directly from:
C:\\Users\\eslin\\OneDrive\\EDDC\\ECA-0126\\Tareas\\Tarea N5 - Modelado y Detallado Conexion Bolted Flange Plate - Entrega 05-10-26\\IT_Ejemplo_Conexion_BFP_AISC 358-16_s2-20.pdf

Key elements:
1. Column: HEB-400 (ASTM A36)
2. Beam: IPE-360 (ASTM A36)
3. Top Flange Plate: 25mm x 270mm x 450mm, CJP welded in shop to column, 16 bolts 5/8" A325 to beam
4. Bottom Flange Plate: 25mm x 270mm x 450mm, CJP welded in shop to column, 16 bolts 5/8" A325 to beam
5. Shear Tab Plate: 12mm x 80mm x 260mm, fillet welded in shop to column, 4 bolts 3/4" A325 to beam web
6. Continuity Stiffeners: 16mm plates aligned with beam flanges inside column cavity, shop welded
7. Web Doubler Plate: 16mm plate on column web, shop welded
"""

import json
import os
import sys
import urllib.request

API_BASE = "http://127.0.0.1:5055/api/v1"


def post(endpoint: str, payload: dict) -> dict:
    url = f"{API_BASE}/{endpoint.lstrip('/')}"
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def get(endpoint: str) -> dict:
    url = f"{API_BASE}/{endpoint.lstrip('/')}"
    with urllib.request.urlopen(url, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main():
    print("=== MODELING AISC 358-16 BOLTED FLANGE PLATE (BFP) CONNECTION ===")

    # 1. Healthcheck
    health = get("health")
    if not health.get("success"):
        print("Error: Advance Steel IPC is not online!")
        sys.exit(1)
    print(f"Connected to Advance Steel: {health['data']['as_version']} | DWG: {health['data']['active_dwg']}")

    # 2. Create Column: HEB 400 (Strong axis facing +X)
    col_payload = {
        "start_point": [0.0, 0.0, 0.0],
        "end_point": [0.0, 0.0, 4500.0],
        "section_name": "HEB_Sections_c nach DIN#@§@#HEB400",
        "material": "A36",
        "model_role": "Column",
        "rotation_deg": 90.0,  # Rotates flange to face +X
    }
    col_res = post("elements/beam", col_payload)
    if not col_res.get("success"):
        print("Failed to create Column:", col_res)
        return
    col_handle = col_res["data"]["handle"]
    print(f"Column created: Handle = {col_handle} (HEB-400, L=4500mm)")

    # 3. Create Beam: IPE 360 (Centred at Z = 2500 mm, X starting at column face + gap = 200 + 10 = 210 mm)
    beam_payload = {
        "start_point": [210.0, 0.0, 2500.0],
        "end_point": [4500.0, 0.0, 2500.0],
        "section_name": "IPE_Sections_c nach DIN#@§@#IPE360",
        "material": "A36",
        "model_role": "Beam",
    }
    beam_res = post("elements/beam", beam_payload)
    if not beam_res.get("success"):
        print("Failed to create Beam:", beam_res)
        return
    beam_handle = beam_res["data"]["handle"]
    print(f"Beam created: Handle = {beam_handle} (IPE-360, L=4290mm, Z=2500mm)")

    # 4. Connection Details from Calculation Report
    # Beam geometry: d = 360mm -> Top flange at Z = 2500 + 180 = 2680mm; Bottom flange at Z = 2500 - 180 = 2320mm.
    # Column face at X = 200mm.
    # Flange Plates: tp = 25mm, bp = 270mm (Y = -135 to +135), Lp = 450mm (X = 200 to 650).
    # Shear Tab: tp_corte = 12mm, bp = 80mm (X = 200 to 280), Lp = 260mm (Z = 2370 to 2630).
    # Continuity Plates: 16mm stiffeners inside column flanges at Z = 2680mm and Z = 2320mm.

    plates = [
        # Top Flange Plate
        {
            "name": "Top Flange Plate",
            "thickness_mm": 25.0,
            "contour_points": [
                [200.0, -135.0, 2680.0],
                [650.0, -135.0, 2680.0],
                [650.0, 135.0, 2680.0],
                [200.0, 135.0, 2680.0],
            ],
            "material": "A36",
            "model_role": "Flange Plate",
        },
        # Bottom Flange Plate
        {
            "name": "Bottom Flange Plate",
            "thickness_mm": 25.0,
            "contour_points": [
                [200.0, -135.0, 2295.0],
                [650.0, -135.0, 2295.0],
                [650.0, 135.0, 2295.0],
                [200.0, 135.0, 2295.0],
            ],
            "material": "A36",
            "model_role": "Flange Plate",
        },
        # Shear Tab Plate
        {
            "name": "Shear Tab Plate",
            "thickness_mm": 12.0,
            "contour_points": [
                [200.0, 4.0, 2370.0],
                [280.0, 4.0, 2370.0],
                [280.0, 4.0, 2630.0],
                [200.0, 4.0, 2630.0],
            ],
            "material": "A36",
            "model_role": "Shear Plate",
        },
        # Top Continuity Plate (Left)
        {
            "name": "Top Continuity Stiffener Left",
            "thickness_mm": 16.0,
            "contour_points": [
                [-176.0, 7.0, 2680.0],
                [176.0, 7.0, 2680.0],
                [176.0, 140.0, 2680.0],
                [-176.0, 140.0, 2680.0],
            ],
            "material": "A36",
            "model_role": "Stiffener",
        },
        # Top Continuity Plate (Right)
        {
            "name": "Top Continuity Stiffener Right",
            "thickness_mm": 16.0,
            "contour_points": [
                [-176.0, -140.0, 2680.0],
                [176.0, -140.0, 2680.0],
                [176.0, -7.0, 2680.0],
                [-176.0, -7.0, 2680.0],
            ],
            "material": "A36",
            "model_role": "Stiffener",
        },
        # Bottom Continuity Plate (Left)
        {
            "name": "Bottom Continuity Stiffener Left",
            "thickness_mm": 16.0,
            "contour_points": [
                [-176.0, 7.0, 2320.0],
                [176.0, 7.0, 2320.0],
                [176.0, 140.0, 2320.0],
                [-176.0, 140.0, 2320.0],
            ],
            "material": "A36",
            "model_role": "Stiffener",
        },
        # Bottom Continuity Plate (Right)
        {
            "name": "Bottom Continuity Stiffener Right",
            "thickness_mm": 16.0,
            "contour_points": [
                [-176.0, -140.0, 2320.0],
                [176.0, -140.0, 2320.0],
                [176.0, -7.0, 2320.0],
                [-176.0, -7.0, 2320.0],
            ],
            "material": "A36",
            "model_role": "Stiffener",
        },
    ]

    bolt_groups = [
        # Top Flange Bolts: 16 bolts (2 cols x 8 rows), dia 16mm, pitch 50mm, gauge 90mm
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": 16.0,
            "origin": [430.0, 0.0, 2680.0],
            "normal": [0.0, 0.0, 1.0],
            "nx": 8,
            "ny": 2,
            "dx": 50.0,
            "dy": 90.0,
            "is_site_bolt": True,
            "connected_part_handles": ["Top Flange Plate", beam_handle],
        },
        # Bottom Flange Bolts: 16 bolts (2 cols x 8 rows), dia 16mm, pitch 50mm, gauge 90mm
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": 16.0,
            "origin": [430.0, 0.0, 2320.0],
            "normal": [0.0, 0.0, 1.0],
            "nx": 8,
            "ny": 2,
            "dx": 50.0,
            "dy": 90.0,
            "is_site_bolt": True,
            "connected_part_handles": ["Bottom Flange Plate", beam_handle],
        },
        # Shear Tab Bolts: 4 bolts in 1 vertical line, dia 20mm, pitch 60mm along Z, drilling along Y
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": 20.0,
            "origin": [240.0, 4.0, 2500.0],
            "normal": [0.0, 1.0, 0.0],
            "nx": 1,
            "ny": 4,
            "dx": 0.0,
            "dy": 60.0,
            "is_site_bolt": True,
            "connected_part_handles": ["Shear Tab Plate", beam_handle],
        },
    ]

    shop_welds = [
        # Top Flange Plate to Column Flange (CJP Butt Weld, shop welded)
        {
            "throat_thickness_mm": 16.0,
            "main_part_handle": col_handle,
            "attached_part_name": "Top Flange Plate",
            "weld_type": "Butt",
        },
        # Bottom Flange Plate to Column Flange (CJP Butt Weld, shop welded)
        {
            "throat_thickness_mm": 16.0,
            "main_part_handle": col_handle,
            "attached_part_name": "Bottom Flange Plate",
            "weld_type": "Butt",
        },
        # Shear Tab to Column Flange (Fillet Weld, shop welded)
        {
            "throat_thickness_mm": 8.0,
            "main_part_handle": col_handle,
            "attached_part_name": "Shear Tab Plate",
            "weld_type": "DoubleFillet",
        },
        # Continuity Stiffeners to Column (Shop Welds)
        {
            "throat_thickness_mm": 8.0,
            "main_part_handle": col_handle,
            "attached_part_name": "Top Continuity Stiffener Left",
            "weld_type": "DoubleFillet",
        },
        {
            "throat_thickness_mm": 8.0,
            "main_part_handle": col_handle,
            "attached_part_name": "Top Continuity Stiffener Right",
            "weld_type": "DoubleFillet",
        },
        {
            "throat_thickness_mm": 8.0,
            "main_part_handle": col_handle,
            "attached_part_name": "Bottom Continuity Stiffener Left",
            "weld_type": "DoubleFillet",
        },
        {
            "throat_thickness_mm": 8.0,
            "main_part_handle": col_handle,
            "attached_part_name": "Bottom Continuity Stiffener Right",
            "weld_type": "DoubleFillet",
        },
    ]

    joint_payload = {
        "connection_name": "BFP ANSI/AISC 358-16 IPE360-HEB400",
        "source_system": "AISC 358-16 BFP Calculation Report",
        "plates": plates,
        "bolt_groups": bolt_groups,
        "shop_welds": shop_welds,
        "verify_assembly": True,
    }

    print("Sending connection detailing payload to Advance Steel...")
    res = post("elements/engineered-joint", joint_payload)
    if not res.get("success"):
        print("Failed to model engineered connection:", res)
        return

    data = res["data"]
    print(f"\n>>> CONNECTION MODELED SUCCESSFULLY! <<<")
    print(f"Connection: {data['connection_name']}")
    print(f"Plates created: {len(data['plates_created'])}")
    for p in data["plates_created"]:
        print(f"  - {p['name']}: Handle={p['handle']}, Thk={p['thickness_mm']}mm, Material={p['material']}")
    print(f"Bolt groups created: {len(data['bolt_groups_created'])}")
    for b in data["bolt_groups_created"]:
        print(f"  - Handle={b['handle']}, Count={b['count']} bolts, Dia={b['diameter_mm']}mm, Standard={b['standard']}")
    print(f"Shop welds created (kInShop): {len(data['welds_created'])}")
    for w in data["welds_created"]:
        print(f"  - Handle={w['handle']}, MainPart={w['main_part_handle']}, Throat={w['throat_thickness_mm']}mm, Bound={w['assembly_bound']}")

    # 5. Capture Viewport Screenshot
    print("\nCapturing 3D Viewport...")
    capture_res = post("viewport/capture", {"output_filename": "bfp_connection_3d.png"})
    if capture_res.get("success"):
        cdata = capture_res.get("data", {})
        if "image_base64" in cdata:
            import base64
            img_bytes = base64.b64decode(cdata["image_base64"])
            out_path = os.path.abspath("bfp_connection_3d.png")
            with open(out_path, "wb") as f:
                f.write(img_bytes)
            print(f"Screenshot saved to: {out_path} ({len(img_bytes)} bytes)")
        elif "filepath" in cdata:
            print(f"Screenshot saved to: {cdata['filepath']}")


if __name__ == "__main__":
    main()
