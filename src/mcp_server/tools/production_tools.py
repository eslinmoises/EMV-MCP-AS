"""Production and fabrication tools for Advance Steel MCP."""

import urllib.parse
from typing import Any, Dict, List, Optional

from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient


def run_automatic_numbering(
    client: AdvanceSteelIpcClient,
    element_handles: Optional[List[str]] = None,
    start_number: Optional[int] = None,
    prefix_single_parts: Optional[str] = None,
    prefix_assemblies: Optional[str] = None,
    keep_existing_numbers: Optional[bool] = None,
    engine_command: Optional[str] = None,
) -> Dict[str, Any]:
    """Run Advance Steel numbering for the model or a selected set of elements."""
    payload: Dict[str, Any] = {}
    if element_handles is not None:
        payload["element_handles"] = element_handles
    if start_number is not None:
        payload["start_number"] = start_number
    if prefix_single_parts is not None:
        payload["prefix_single_parts"] = prefix_single_parts
    if prefix_assemblies is not None:
        payload["prefix_assemblies"] = prefix_assemblies
    if keep_existing_numbers is not None:
        payload["keep_existing_numbers"] = keep_existing_numbers
    if engine_command is not None:
        payload["engine_command"] = engine_command
    return client.post("production/numbering", payload)


def export_dstv_nc_files(
    client: AdvanceSteelIpcClient,
    element_handles: Optional[List[str]] = None,
    output_directory: Optional[str] = None,
    file_extension: Optional[str] = None,
    overwrite: Optional[bool] = None,
    engine_command: Optional[str] = None,
) -> Dict[str, Any]:
    """Export numbered single parts as DSTV/NC1 files."""
    payload: Dict[str, Any] = {}
    if element_handles is not None:
        payload["element_handles"] = element_handles
    if output_directory is not None:
        payload["output_directory"] = output_directory
    if file_extension is not None:
        payload["file_extension"] = file_extension
    if overwrite is not None:
        payload["overwrite"] = overwrite
    if engine_command is not None:
        payload["engine_command"] = engine_command
    return client.post("production/export-nc", payload)


def get_drawing_status(
    client: AdvanceSteelIpcClient,
    assembly_marks: Optional[List[str]] = None,
) -> Dict[str, Any]:
    """Report drawing availability and freshness for numbered assemblies."""
    endpoint = "production/drawing-status"
    if assembly_marks is not None:
        encoded_marks = ",".join(urllib.parse.quote(mark, safe="") for mark in assembly_marks)
        endpoint += f"?assembly_marks={encoded_marks}"
    return client.get(endpoint)


def get_bill_of_materials(
    client: AdvanceSteelIpcClient,
    element_handles: Optional[List[str]] = None,
    group_by: Optional[str] = "profile",
) -> Dict[str, Any]:
    """Generate a comprehensive Bill of Materials (BOM) / Material Takeoff (MTO) report."""
    payload: Dict[str, Any] = {}
    if element_handles is not None:
        payload["element_handles"] = element_handles
    if group_by is not None:
        payload["group_by"] = group_by
    return client.post("production/bom", payload)

