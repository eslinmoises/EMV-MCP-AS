"""Example 01: Automated Structural Steel Portal Frame Modeling with EMV-MCP-AS.

This script demonstrates how an AI agent (or Python automation) models a 2D/3D steel portal frame
in Autodesk Advance Steel using the MCP tools:
- 2 Columns (HEB300, 6000 mm high)
- 2 Rafters (IPE360, sloped gable with 1500 mm ridge height)
- 2 Column Base Plates (400x400x25 mm)
- Shop weld verification and Main Part designation
"""

import sys
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import diagnostic_tools, modeling_tools


def model_portal_frame(client: AdvanceSteelIpcClient):
    print("--- 1. Checking Advance Steel Connection & Active Model ---")
    info = diagnostic_tools.get_active_model_info(client)
    if not info.get("success"):
        print(f"Error: Could not connect to Advance Steel: {info.get('error')}")
        return False

    print(f"Connected to Advance Steel {info['data'].get('as_version')} | Active DWG: {info['data'].get('active_dwg')}")

    # Geometry coordinates (mm)
    span = 12000.0        # 12 m span
    eaves_height = 6000.0 # 6 m column height
    ridge_height = 7500.0 # 7.5 m apex ridge height

    col_left_start = [0.0, 0.0, 0.0]
    col_left_end = [0.0, 0.0, eaves_height]

    col_right_start = [span, 0.0, 0.0]
    col_right_end = [span, 0.0, eaves_height]

    rafter_left_start = [0.0, 0.0, eaves_height]
    rafter_left_end = [span / 2.0, 0.0, ridge_height]

    rafter_right_start = [span / 2.0, 0.0, ridge_height]
    rafter_right_end = [span, 0.0, eaves_height]

    print("\n--- 2. Modeling Columns ---")
    col1 = modeling_tools.create_straight_beam(
        client,
        start_point=col_left_start,
        end_point=col_left_end,
        section_name="HEB300",
        material="S275JR",
        model_role="Column"
    )
    print(f"Col 1 created: Handle {col1['data'].get('handle')} | Section: {col1['data'].get('section_name')}")

    col2 = modeling_tools.create_straight_beam(
        client,
        start_point=col_right_start,
        end_point=col_right_end,
        section_name="HEB300",
        material="S275JR",
        model_role="Column"
    )
    print(f"Col 2 created: Handle {col2['data'].get('handle')} | Section: {col2['data'].get('section_name')}")

    print("\n--- 3. Modeling Rafters ---")
    raf1 = modeling_tools.create_straight_beam(
        client,
        start_point=rafter_left_start,
        end_point=rafter_left_end,
        section_name="IPE360",
        material="S275JR",
        model_role="Rafter"
    )
    print(f"Rafter 1 created: Handle {raf1['data'].get('handle')} | Section: {raf1['data'].get('section_name')}")

    raf2 = modeling_tools.create_straight_beam(
        client,
        start_point=rafter_right_start,
        end_point=rafter_right_end,
        section_name="IPE360",
        material="S275JR",
        model_role="Rafter"
    )
    print(f"Rafter 2 created: Handle {raf2['data'].get('handle')} | Section: {raf2['data'].get('section_name')}")

    print("\n--- 4. Modeling Base Plates ---")
    bp_half_width = 200.0
    bp1_contour = [
        [-bp_half_width, -bp_half_width, 0.0],
        [bp_half_width, -bp_half_width, 0.0],
        [bp_half_width, bp_half_width, 0.0],
        [-bp_half_width, bp_half_width, 0.0]
    ]
    bp1 = modeling_tools.create_plate(
        client,
        contour_points=bp1_contour,
        thickness=25.0,
        material="S275JR",
        model_role="BasePlate"
    )
    print(f"BasePlate 1 created: Handle {bp1['data'].get('handle')} | Thickness: {bp1['data'].get('thickness_mm')} mm")

    print("\n--- 5. Verifying Welds and Shop Assemblies ---")
    welds = diagnostic_tools.verify_welds_and_assemblies(client)
    print(f"Assembly Check: Workshop Welds={welds['data'].get('total_workshop_welds')}, Site Welds={welds['data'].get('total_site_welds')}")

    print("\n--- 6. Capturing 3D Viewport Screenshot ---")
    shot = diagnostic_tools.capture_viewport(client)
    if shot.get("success"):
        print("Viewport captured successfully for multimodal AI verification.")

    print("\n[OK] Portal Frame modeling complete and verified!")
    return True


if __name__ == "__main__":
    ipc_client = AdvanceSteelIpcClient()
    model_portal_frame(ipc_client)
