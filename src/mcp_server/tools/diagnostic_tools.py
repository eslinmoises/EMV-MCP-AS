"""Diagnostic and inspection tools for Advance Steel MCP."""

from typing import Any, Dict, List, Optional
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient


def get_active_model_info(client: AdvanceSteelIpcClient) -> Dict[str, Any]:
    """Retrieve metadata of the currently active drawing in Advance Steel."""
    return client.get("health")


def get_selected_elements(client: AdvanceSteelIpcClient) -> Dict[str, Any]:
    """Inspect elements currently selected in the Advance Steel 3D viewport."""
    return client.get("elements/selected")


import urllib.parse


def verify_welds_and_assemblies(
    client: AdvanceSteelIpcClient, element_handles: Optional[List[str]] = None
) -> Dict[str, Any]:
    """Inspect welds (workshop vs. site) and verify proper assembly grouping."""
    endpoint = "assembly/verify-welds"
    if element_handles:
        endpoint += f"?element_handles={','.join(element_handles)}"
    return client.get(endpoint)


def inspect_main_part(client: AdvanceSteelIpcClient, assembly_or_element_handle: str) -> Dict[str, Any]:
    """Identify and validate the Main Part of a shop assembly."""
    encoded = urllib.parse.quote(assembly_or_element_handle)
    return client.get(f"assembly/main-part?assembly_or_element_handle={encoded}")


def get_ucs_and_grids(client: AdvanceSteelIpcClient) -> Dict[str, Any]:
    """Retrieve active UCS coordinate axes, structural grids, and level elevations."""
    return client.get("spatial/ucs-grids")


def capture_viewport(client: AdvanceSteelIpcClient) -> Dict[str, Any]:
    """Capture a screenshot of the 3D viewport for multimodal visual verification."""
    return client.get("viewport/capture")
