"""Main entrypoint for EMV-MCP-AS (Advance Steel Model Context Protocol Server)."""

import os
import sys
from typing import Any, Dict, List, Optional

from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import diagnostic_tools, modeling_tools, scripting_tools

# Default IPC connection
IPC_PORT = int(os.environ.get("AS_MCP_PORT", "5055"))
IPC_HOST = os.environ.get("AS_MCP_HOST", "127.0.0.1")
client = AdvanceSteelIpcClient(base_url=f"http://{IPC_HOST}:{IPC_PORT}")

try:
    from mcp.server.fastmcp import FastMCP
    mcp = FastMCP("emv-mcp-as", description="Advance Steel Model Context Protocol Server")

    @mcp.tool()
    def get_active_model_info() -> Dict[str, Any]:
        """Retrieve metadata of the currently active drawing in Advance Steel."""
        return diagnostic_tools.get_active_model_info(client)

    @mcp.tool()
    def get_selected_elements() -> Dict[str, Any]:
        """Inspect elements currently selected in the Advance Steel 3D viewport."""
        return diagnostic_tools.get_selected_elements(client)

    @mcp.tool()
    def verify_welds_and_assemblies(element_handles: Optional[List[str]] = None) -> Dict[str, Any]:
        """Inspect welds (workshop vs. site) and verify proper assembly grouping."""
        return diagnostic_tools.verify_welds_and_assemblies(client, element_handles)

    @mcp.tool()
    def inspect_main_part(assembly_or_element_handle: str) -> Dict[str, Any]:
        """Identify and validate the Main Part of a shop assembly."""
        return diagnostic_tools.inspect_main_part(client, assembly_or_element_handle)

    @mcp.tool()
    def get_ucs_and_grids() -> Dict[str, Any]:
        """Retrieve active UCS coordinate axes, structural grids, and level elevations."""
        return diagnostic_tools.get_ucs_and_grids(client)

    @mcp.tool()
    def capture_viewport() -> Dict[str, Any]:
        """Capture a screenshot of the 3D viewport for multimodal visual verification."""
        return diagnostic_tools.capture_viewport(client)

    @mcp.tool()
    def audit_assembly_integrity(element_handles: Optional[List[str]] = None) -> Dict[str, Any]:
        """Audit the model for orphaned workshop plates/stiffeners and unnumbered parts."""
        return diagnostic_tools.audit_assembly_integrity(client, element_handles)

    @mcp.tool()
    def detect_clashes_and_clearances(element_handles: Optional[List[str]] = None) -> Dict[str, Any]:
        """Detect 3D spatial collisions and clearances between structural members."""
        return diagnostic_tools.detect_clashes_and_clearances(client, element_handles)

    @mcp.tool()
    def create_straight_beam(
        start_point: List[float],
        end_point: List[float],
        section_name: str,
        material: str = "S275JR",
        model_role: str = "Beam",
        reference_axis: str = "Center",
        rotation_deg: float = 0.0,
    ) -> Dict[str, Any]:
        """Create a straight structural steel profile between two 3D points."""
        return modeling_tools.create_straight_beam(
            client, start_point, end_point, section_name, material, model_role, reference_axis, rotation_deg
        )

    @mcp.tool()
    def create_plate(
        contour_points: List[List[float]],
        thickness: float,
        material: str = "S275JR",
        model_role: str = "Plate",
    ) -> Dict[str, Any]:
        """Create a contour plate from a list of coplanar 3D points."""
        return modeling_tools.create_plate(client, contour_points, thickness, material, model_role)

    @mcp.tool()
    def set_main_part(assembly_handle: str, new_main_part_handle: str) -> Dict[str, Any]:
        """Designate the Main Part of an assembly."""
        return modeling_tools.set_main_part(client, assembly_handle, new_main_part_handle)

    @mcp.tool()
    def execute_csharp_script(script_code: str) -> Dict[str, Any]:
        """Execute dynamic C# Roslyn script in Advance Steel with automatic transaction rollback."""
        return scripting_tools.execute_csharp_script(client, script_code)

except ImportError:
    mcp = None


def main():
    if mcp is not None:
        mcp.run()
    else:
        print("MCP SDK not installed in current environment. Install with: pip install mcp httpx", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
