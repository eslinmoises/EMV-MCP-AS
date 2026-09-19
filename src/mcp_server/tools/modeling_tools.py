"""Generative modeling tools for Advance Steel MCP."""

from typing import Any, Dict, List, Optional
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient


def create_straight_beam(
    client: AdvanceSteelIpcClient,
    start_point: List[float],
    end_point: List[float],
    section_name: str,
    material: str = "S275JR",
    model_role: str = "Beam",
    reference_axis: str = "Center",
    rotation_deg: float = 0.0,
) -> Dict[str, Any]:
    """Create a straight structural steel profile between two 3D points."""
    payload = {
        "start_point": start_point,
        "end_point": end_point,
        "section_name": section_name,
        "material": material,
        "model_role": model_role,
        "reference_axis": reference_axis,
        "rotation_deg": rotation_deg,
    }
    return client.post("elements/beam", payload)


def create_plate(
    client: AdvanceSteelIpcClient,
    contour_points: List[List[float]],
    thickness: float,
    material: str = "S275JR",
    model_role: str = "Plate",
) -> Dict[str, Any]:
    """Create a contour plate from a list of coplanar 3D points."""
    payload = {
        "contour_points": contour_points,
        "thickness": thickness,
        "material": material,
        "model_role": model_role,
    }
    return client.post("elements/plate", payload)


def set_main_part(
    client: AdvanceSteelIpcClient,
    assembly_handle: str,
    new_main_part_handle: str,
) -> Dict[str, Any]:
    """Designate the Main Part of an assembly."""
    payload = {
        "assembly_handle": assembly_handle,
        "new_main_part_handle": new_main_part_handle,
    }
    return client.post("assembly/set-main-part", payload)


def create_standard_joint(
    client: AdvanceSteelIpcClient,
    primary_handle: str,
    joint_type: Optional[str] = None,
    rule_name: Optional[str] = None,
    secondary_handles: Optional[List[str]] = None,
    connection_point: Optional[List[float]] = None,
    primary_end: str = "Start",
) -> Dict[str, Any]:
    """Apply a standard parametric connection (e.g. BasePlate, ClipAngle, EndPlate, ApexHaunch)."""
    payload: Dict[str, Any] = {
        "primary_handle": primary_handle,
        "primary_end": primary_end,
    }
    if joint_type:
        payload["joint_type"] = joint_type
    if rule_name:
        payload["rule_name"] = rule_name
    if secondary_handles:
        payload["secondary_handles"] = secondary_handles
    if connection_point:
        payload["connection_point"] = connection_point
    return client.post("elements/joint", payload)


def apply_beam_cut_or_notch(
    client: AdvanceSteelIpcClient,
    beam_handle: str,
    cut_type: str = "shortening",
    cut_length_mm: Optional[float] = None,
    notch_width_mm: Optional[float] = None,
    notch_depth_mm: Optional[float] = None,
) -> Dict[str, Any]:
    """Apply beam shortening, flange cuts, notches, or miters."""
    payload: Dict[str, Any] = {
        "beam_handle": beam_handle,
        "cut_type": cut_type,
    }
    if cut_length_mm is not None:
        payload["cut_length_mm"] = cut_length_mm
    if notch_width_mm is not None:
        payload["notch_width_mm"] = notch_width_mm
    if notch_depth_mm is not None:
        payload["notch_depth_mm"] = notch_depth_mm
    return client.post("elements/cut", payload)


def modify_element_properties(
    client: AdvanceSteelIpcClient,
    handle: str,
    material: Optional[str] = None,
    model_role: Optional[str] = None,
    coating: Optional[str] = None,
    rotation_deg: Optional[float] = None,
) -> Dict[str, Any]:
    """Modify material grade, model role, coating, or rotation of an existing element."""
    payload: Dict[str, Any] = {"handle": handle}
    if material is not None:
        payload["material"] = material
    if model_role is not None:
        payload["model_role"] = model_role
    if coating is not None:
        payload["coating"] = coating
    if rotation_deg is not None:
        payload["rotation_deg"] = rotation_deg
    return client.post("elements/modify", payload)


def create_bolt_pattern(
    client: AdvanceSteelIpcClient,
    connected_handles: List[str],
    origin: List[float],
    normal: Optional[List[float]] = None,
    bolt_standard: str = "DIN 931",
    bolt_grade: str = "8.8",
    bolt_diameter_mm: float = 20.0,
    nx: int = 2,
    ny: int = 2,
    dx: float = 70.0,
    dy: float = 70.0,
    is_site_bolt: bool = True,
) -> Dict[str, Any]:
    """Create a rectangular bolt pattern connecting two or more structural parts."""
    payload: Dict[str, Any] = {
        "connected_handles": connected_handles,
        "origin": origin,
        "bolt_standard": bolt_standard,
        "bolt_grade": bolt_grade,
        "bolt_diameter_mm": bolt_diameter_mm,
        "nx": nx,
        "ny": ny,
        "dx": dx,
        "dy": dy,
        "is_site_bolt": is_site_bolt,
    }
    if normal is not None:
        payload["normal"] = normal
    return client.post("elements/bolt", payload)


def create_poly_beam(
    client: AdvanceSteelIpcClient,
    points: List[List[float]],
    section_name: str,
    material: str = "S275JR",
    model_role: str = "Beam",
) -> Dict[str, Any]:
    """Create a continuous multi-segment polybeam or curved member from 3D points."""
    payload: Dict[str, Any] = {
        "points": points,
        "section_name": section_name,
        "material": material,
        "model_role": model_role,
    }
    return client.post("elements/poly-beam", payload)


def create_portal_frame(
    client: AdvanceSteelIpcClient,
    span_mm: float,
    eave_height_mm: float,
    ridge_height_mm: float,
    origin_x: float = 0.0,
    origin_y: float = 0.0,
    base_elevation_mm: float = 0.0,
    column_section: str = "HEA 300",
    rafter_section: str = "IPE 300",
    material: str = "S275JR",
    create_base_plates: bool = True,
    base_plate_thickness_mm: float = 20.0,
    base_plate_dimensions_mm: Optional[List[float]] = None,
) -> Dict[str, Any]:
    """Generate a complete parametric structural portal frame (columns, rafters, base plates)."""
    payload: Dict[str, Any] = {
        "span_mm": span_mm,
        "eave_height_mm": eave_height_mm,
        "ridge_height_mm": ridge_height_mm,
        "origin_x": origin_x,
        "origin_y": origin_y,
        "base_elevation_mm": base_elevation_mm,
        "column_section": column_section,
        "rafter_section": rafter_section,
        "material": material,
        "create_base_plates": create_base_plates,
        "base_plate_thickness_mm": base_plate_thickness_mm,
    }
    if base_plate_dimensions_mm is not None:
        payload["base_plate_dimensions_mm"] = base_plate_dimensions_mm
    return client.post("elements/portal-frame", payload)


def create_structural_grid(
    client: AdvanceSteelIpcClient,
    origin: Optional[List[float]] = None,
    axis_direction: Optional[List[float]] = None,
    spacing_direction: Optional[List[float]] = None,
    line_length: float = 30000.0,
    count: int = 2,
    spacing: float = 5000.0,
    spacings: Optional[List[float]] = None,
    labels: Optional[List[str]] = None,
    label_prefix: str = "1",
    text_location: str = "Both",
) -> Dict[str, Any]:
    """Create 3D structural grids (Grid1D/Grid) for spatial reference and Revit BIM coordination."""
    payload: Dict[str, Any] = {
        "origin": origin or [0.0, 0.0, 0.0],
        "axis_direction": axis_direction or [0.0, 1.0, 0.0],
        "spacing_direction": spacing_direction or [1.0, 0.0, 0.0],
        "line_length": line_length,
        "count": count,
        "spacing": spacing,
        "label_prefix": label_prefix,
        "text_location": text_location,
    }
    if spacings is not None:
        payload["spacings"] = spacings
    if labels is not None:
        payload["labels"] = labels
    return client.post("spatial/grid", payload)


def create_structural_level(
    client: AdvanceSteelIpcClient,
    name: str,
    elevation: float = 0.0,
    level_below_handle: Optional[str] = None,
    level_above_handle: Optional[str] = None,
) -> Dict[str, Any]:
    """Create a building structure level (LevelObject) registered in Advance Steel."""
    payload: Dict[str, Any] = {
        "name": name,
        "elevation": elevation,
    }
    if level_below_handle is not None:
        payload["level_below_handle"] = level_below_handle
    if level_above_handle is not None:
        payload["level_above_handle"] = level_above_handle
    return client.post("spatial/level", payload)


def create_trussed_warehouse(
    client: AdvanceSteelIpcClient,
    span: float = 31000.0,
    length: float = 30000.0,
    bay_spacing: float = 5000.0,
    eave_height: float = 6000.0,
    ridge_height: float = 9500.0,
    column_width: float = 1000.0,
    truss_depth: float = 2000.0,
    profile: str = "RHS_Sections_square_c nach DIN#@§@#Q90X3",
    material: str = "S235JR",
    create_grids_and_levels: bool = True,
) -> Dict[str, Any]:
    """Generate a complete parametric industrial lattice warehouse (7 frames, double columns, Warren trusses)."""
    payload: Dict[str, Any] = {
        "span": span,
        "length": length,
        "bay_spacing": bay_spacing,
        "eave_height": eave_height,
        "ridge_height": ridge_height,
        "column_width": column_width,
        "truss_depth": truss_depth,
        "profile": profile,
        "material": material,
        "create_grids_and_levels": create_grids_and_levels,
    }
    return client.post("elements/trussed-warehouse", payload)
def model_engineered_connection(
    client: AdvanceSteelIpcClient,
    connection_name: str = "Engineered Connection",
    source_system: str = "Calculation Report",
    plates: Optional[List[Dict[str, Any]]] = None,
    bolt_groups: Optional[List[Dict[str, Any]]] = None,
    shop_welds: Optional[List[Dict[str, Any]]] = None,
    verify_assembly: bool = True,
) -> Dict[str, Any]:
    """Model complete fabrication connection assemblies from engineering reports (RAM Connection, IDEA StatiCa, DXF, AISC 358 BFP PDF).
    
    Creates plates, bolt patterns, and critical workshop welds (kInShop) ensuring no orphan parts and valid assembly hierarchy.
    """
    payload: Dict[str, Any] = {
        "connection_name": connection_name,
        "source_system": source_system,
        "plates": plates or [],
        "bolt_groups": bolt_groups or [],
        "shop_welds": shop_welds or [],
        "verify_assembly": verify_assembly,
    }
    return client.post("elements/engineered-joint", payload)

