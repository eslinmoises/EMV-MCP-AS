"""Generative modeling tools for Advance Steel MCP."""

from typing import Any, Dict, List
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
