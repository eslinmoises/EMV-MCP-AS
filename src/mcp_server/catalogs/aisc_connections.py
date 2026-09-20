"""AISC 358-16 and Standard Structural Fabrication Connection Recipes."""

from typing import Any, Dict, List, Optional, Tuple


def get_section_dimensions(section_name: str) -> Dict[str, float]:
    """Provide standard dimensions (depth d, flange width bf, flange thk tf, web thk tw) in mm.
    
    Covers European HEB/IPE and US W-shapes used in AISC 358 prequalified connections.
    """
    s = section_name.upper()
    if "HEB400" in s or "HEB 400" in s:
        return {"d": 400.0, "bf": 300.0, "tf": 24.0, "tw": 13.5, "r": 27.0}
    elif "HEB300" in s or "HEB 300" in s:
        return {"d": 300.0, "bf": 300.0, "tf": 19.0, "tw": 11.0, "r": 27.0}
    elif "HEB200" in s or "HEB 200" in s:
        return {"d": 200.0, "bf": 200.0, "tf": 15.0, "tw": 9.0, "r": 18.0}
    elif "IPE360" in s or "IPE 360" in s:
        return {"d": 360.0, "bf": 170.0, "tf": 12.7, "tw": 8.0, "r": 18.0}
    elif "IPE300" in s or "IPE 300" in s:
        return {"d": 300.0, "bf": 150.0, "tf": 10.7, "tw": 7.1, "r": 15.0}
    elif "IPE240" in s or "IPE 240" in s:
        return {"d": 240.0, "bf": 120.0, "tf": 9.8, "tw": 6.2, "r": 15.0}
    elif "W14X90" in s:
        return {"d": 356.0, "bf": 368.0, "tf": 18.0, "tw": 11.2, "r": 20.0}
    elif "W18X50" in s:
        return {"d": 457.0, "bf": 190.0, "tf": 14.5, "tw": 9.0, "r": 20.0}
    # Fallback generic I-beam
    return {"d": 350.0, "bf": 200.0, "tf": 15.0, "tw": 9.0, "r": 15.0}


def build_bfp_recipe(
    beam_handle: str,
    column_handle: str,
    beam_section: str = "IPE360",
    column_section: str = "HEB400",
    tp_flange: float = 25.0,
    bp_flange: float = 270.0,
    lp_flange: float = 450.0,
    tp_shear: float = 12.0,
    bp_shear: float = 80.0,
    lp_shear: float = 260.0,
    tp_stiffener: float = 16.0,
    bolt_dia_flange: float = 16.0,
    bolt_dia_shear: float = 20.0,
    beam_center_z: float = 2500.0,
    column_face_x: float = 200.0,
    material: str = "A36",
) -> Dict[str, Any]:
    """Generates the detailing payload for AISC 358-16 Bolted Flange Plate (BFP) connection."""
    b_dim = get_section_dimensions(beam_section)
    c_dim = get_section_dimensions(column_section)

    top_flange_z = beam_center_z + b_dim["d"] / 2.0
    bot_flange_z = beam_center_z - b_dim["d"] / 2.0

    plates = [
        {
            "name": "Top Flange Plate",
            "thickness_mm": tp_flange,
            "contour_points": [
                [column_face_x, -bp_flange / 2.0, top_flange_z],
                [column_face_x + lp_flange, -bp_flange / 2.0, top_flange_z],
                [column_face_x + lp_flange, bp_flange / 2.0, top_flange_z],
                [column_face_x, bp_flange / 2.0, top_flange_z],
            ],
            "material": material,
            "model_role": "Flange Plate",
        },
        {
            "name": "Bottom Flange Plate",
            "thickness_mm": tp_flange,
            "contour_points": [
                [column_face_x, -bp_flange / 2.0, bot_flange_z - tp_flange],
                [column_face_x + lp_flange, -bp_flange / 2.0, bot_flange_z - tp_flange],
                [column_face_x + lp_flange, bp_flange / 2.0, bot_flange_z - tp_flange],
                [column_face_x, bp_flange / 2.0, bot_flange_z - tp_flange],
            ],
            "material": material,
            "model_role": "Flange Plate",
        },
        {
            "name": "Shear Tab Plate",
            "thickness_mm": tp_shear,
            "contour_points": [
                [column_face_x, b_dim["tw"] / 2.0, beam_center_z - lp_shear / 2.0],
                [column_face_x + bp_shear, b_dim["tw"] / 2.0, beam_center_z - lp_shear / 2.0],
                [column_face_x + bp_shear, b_dim["tw"] / 2.0, beam_center_z + lp_shear / 2.0],
                [column_face_x, b_dim["tw"] / 2.0, beam_center_z + lp_shear / 2.0],
            ],
            "material": material,
            "model_role": "Shear Plate",
        },
    ]

    # Continuity stiffeners inside column cavity (left and right of web, top and bottom flanges)
    half_col_cavity_x = c_dim["d"] / 2.0 - c_dim["tf"]
    half_col_cavity_y = c_dim["bf"] / 2.0 - 10.0
    web_clearance = c_dim["tw"] / 2.0

    stiffener_positions = [
        ("Top Continuity Left", top_flange_z, web_clearance, half_col_cavity_y),
        ("Top Continuity Right", top_flange_z, -half_col_cavity_y, -web_clearance),
        ("Bottom Continuity Left", bot_flange_z, web_clearance, half_col_cavity_y),
        ("Bottom Continuity Right", bot_flange_z, -half_col_cavity_y, -web_clearance),
    ]

    for name, z, y1, y2 in stiffener_positions:
        plates.append({
            "name": name,
            "thickness_mm": tp_stiffener,
            "contour_points": [
                [-half_col_cavity_x, y1, z],
                [half_col_cavity_x, y1, z],
                [half_col_cavity_x, y2, z],
                [-half_col_cavity_x, y2, z],
            ],
            "material": material,
            "model_role": "Stiffener",
        })

    bolt_groups = [
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": bolt_dia_flange,
            "origin": [column_face_x + lp_flange / 2.0, 0.0, top_flange_z],
            "normal": [0.0, 0.0, 1.0],
            "nx": 8,
            "ny": 2,
            "dx": 50.0,
            "dy": 90.0,
            "is_site_bolt": True,
            "connected_part_handles": ["Top Flange Plate", beam_handle],
        },
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": bolt_dia_flange,
            "origin": [column_face_x + lp_flange / 2.0, 0.0, bot_flange_z],
            "normal": [0.0, 0.0, 1.0],
            "nx": 8,
            "ny": 2,
            "dx": 50.0,
            "dy": 90.0,
            "is_site_bolt": True,
            "connected_part_handles": ["Bottom Flange Plate", beam_handle],
        },
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": bolt_dia_shear,
            "origin": [column_face_x + bp_shear / 2.0, b_dim["tw"] / 2.0, beam_center_z],
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
        {"throat_thickness_mm": 16.0, "main_part_handle": column_handle, "attached_part_name": "Top Flange Plate", "weld_type": "Butt"},
        {"throat_thickness_mm": 16.0, "main_part_handle": column_handle, "attached_part_name": "Bottom Flange Plate", "weld_type": "Butt"},
        {"throat_thickness_mm": 8.0, "main_part_handle": column_handle, "attached_part_name": "Shear Tab Plate", "weld_type": "DoubleFillet"},
        {"throat_thickness_mm": 8.0, "main_part_handle": column_handle, "attached_part_name": "Top Continuity Left", "weld_type": "DoubleFillet"},
        {"throat_thickness_mm": 8.0, "main_part_handle": column_handle, "attached_part_name": "Top Continuity Right", "weld_type": "DoubleFillet"},
        {"throat_thickness_mm": 8.0, "main_part_handle": column_handle, "attached_part_name": "Bottom Continuity Left", "weld_type": "DoubleFillet"},
        {"throat_thickness_mm": 8.0, "main_part_handle": column_handle, "attached_part_name": "Bottom Continuity Right", "weld_type": "DoubleFillet"},
    ]

    return {
        "connection_name": f"BFP AISC 358-16 {beam_section}-{column_section}",
        "source_system": "AISC 358-16 Parametric Catalog",
        "plates": plates,
        "bolt_groups": bolt_groups,
        "shop_welds": shop_welds,
        "verify_assembly": True,
    }


def build_extended_end_plate_recipe(
    beam_handle: str,
    column_handle: str,
    beam_section: str = "IPE360",
    column_section: str = "HEB400",
    end_plate_type: str = "4E",  # 4E, 4ES, 8ES
    tp: float = 25.0,
    bp: float = 200.0,
    extension_mm: float = 100.0,
    bolt_dia: float = 20.0,
    beam_center_z: float = 2500.0,
    column_face_x: float = 200.0,
    material: str = "A36",
) -> Dict[str, Any]:
    """Generates AISC 358-16 Extended End-Plate Moment Connection (4E, 4ES, 8ES)."""
    b_dim = get_section_dimensions(beam_section)

    plate_height = b_dim["d"] + 2.0 * extension_mm
    z_bot = beam_center_z - plate_height / 2.0
    z_top = beam_center_z + plate_height / 2.0

    plates = [
        {
            "name": f"Extended End Plate {end_plate_type}",
            "thickness_mm": tp,
            "contour_points": [
                [column_face_x, -bp / 2.0, z_bot],
                [column_face_x, bp / 2.0, z_bot],
                [column_face_x, bp / 2.0, z_top],
                [column_face_x, -bp / 2.0, z_top],
            ],
            "material": material,
            "model_role": "End Plate",
        }
    ]

    shop_welds = [
        # In an end-plate connection, the end plate is shop-welded to the BEAM!
        {
            "throat_thickness_mm": 12.0,
            "main_part_handle": beam_handle,
            "attached_part_name": f"Extended End Plate {end_plate_type}",
            "weld_type": "DoubleFillet",
        }
    ]

    # If stiffened (4ES or 8ES), add gusset/rib stiffener on beam flange
    if "S" in end_plate_type:
        stiff_len = 150.0
        stiff_height = extension_mm - 10.0
        plates.append({
            "name": "End Plate Gusset Stiffener",
            "thickness_mm": 12.0,
            "contour_points": [
                [column_face_x, 0.0, beam_center_z + b_dim["d"] / 2.0],
                [column_face_x + stiff_len, 0.0, beam_center_z + b_dim["d"] / 2.0],
                [column_face_x, 0.0, beam_center_z + b_dim["d"] / 2.0 + stiff_height],
            ],
            "material": material,
            "model_role": "Stiffener",
        })
        shop_welds.append({
            "throat_thickness_mm": 8.0,
            "main_part_handle": beam_handle,
            "attached_part_name": "End Plate Gusset Stiffener",
            "weld_type": "DoubleFillet",
        })

    # Bolts through End Plate and Column Flange (Site bolts)
    ny_bolts = 8 if end_plate_type == "8ES" else 4
    bolt_groups = [
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": bolt_dia,
            "origin": [column_face_x, 0.0, beam_center_z],
            "normal": [1.0, 0.0, 0.0],
            "nx": 2,
            "ny": ny_bolts // 2,
            "dx": 100.0,
            "dy": 120.0,
            "is_site_bolt": True,
            "connected_part_handles": [f"Extended End Plate {end_plate_type}", column_handle],
        }
    ]

    return {
        "connection_name": f"AISC 358-16 {end_plate_type} {beam_section}-{column_section}",
        "source_system": "AISC 358-16 End-Plate Catalog",
        "plates": plates,
        "bolt_groups": bolt_groups,
        "shop_welds": shop_welds,
        "verify_assembly": True,
    }


def build_shear_tab_recipe(
    beam_handle: str,
    column_handle: str,
    beam_section: str = "IPE360",
    column_section: str = "HEB400",
    tp: float = 10.0,
    bp: float = 90.0,
    lp: float = 240.0,
    num_bolts: int = 3,
    bolt_dia: float = 20.0,
    beam_center_z: float = 2500.0,
    column_face_x: float = 200.0,
    material: str = "A36",
) -> Dict[str, Any]:
    """Generates standard AISC Single-Plate Shear Connection (Shear Tab)."""
    b_dim = get_section_dimensions(beam_section)

    plates = [
        {
            "name": "Shear Tab",
            "thickness_mm": tp,
            "contour_points": [
                [column_face_x, b_dim["tw"] / 2.0, beam_center_z - lp / 2.0],
                [column_face_x + bp, b_dim["tw"] / 2.0, beam_center_z - lp / 2.0],
                [column_face_x + bp, b_dim["tw"] / 2.0, beam_center_z + lp / 2.0],
                [column_face_x, b_dim["tw"] / 2.0, beam_center_z + lp / 2.0],
            ],
            "material": material,
            "model_role": "Shear Plate",
        }
    ]

    shop_welds = [
        {
            "throat_thickness_mm": 6.0,
            "main_part_handle": column_handle,
            "attached_part_name": "Shear Tab",
            "weld_type": "DoubleFillet",
        }
    ]

    bolt_groups = [
        {
            "bolt_standard": "DIN 931",
            "bolt_diameter_mm": bolt_dia,
            "origin": [column_face_x + bp / 2.0, b_dim["tw"] / 2.0, beam_center_z],
            "normal": [0.0, 1.0, 0.0],
            "nx": 1,
            "ny": num_bolts,
            "dx": 0.0,
            "dy": 70.0,
            "is_site_bolt": True,
            "connected_part_handles": ["Shear Tab", beam_handle],
        }
    ]

    return {
        "connection_name": f"AISC Shear Tab {num_bolts}xM{int(bolt_dia)} {beam_section}",
        "source_system": "AISC Manual Part 10 Shear Tab Catalog",
        "plates": plates,
        "bolt_groups": bolt_groups,
        "shop_welds": shop_welds,
        "verify_assembly": True,
    }
