"""Main entrypoint for EMV-MCP-AS (Advance Steel Model Context Protocol Server)."""

import os
import sys
from typing import Any, Dict, List, Optional

from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import diagnostic_tools, modeling_tools, production_tools, scripting_tools

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
    def query_elements_in_box(
        min_point: List[float],
        max_point: List[float],
        element_types: Optional[List[str]] = None,
    ) -> Dict[str, Any]:
        """Find elements whose 3D bounding extents intersect a bounding box [min_point, max_point]."""
        return diagnostic_tools.query_elements_in_box(client, min_point, max_point, element_types)

    @mcp.tool()
    def query_elements(
        model_role: Optional[str] = None,
        material: Optional[str] = None,
        section_name: Optional[str] = None,
        assembly_mark: Optional[str] = None,
        single_part_mark: Optional[str] = None,
        element_types: Optional[List[str]] = None,
        handles: Optional[List[str]] = None,
    ) -> Dict[str, Any]:
        """Filter and query model elements by role, material, profile, mark, type, or handles."""
        return diagnostic_tools.query_elements(
            client,
            model_role,
            material,
            section_name,
            assembly_mark,
            single_part_mark,
            element_types,
            handles,
        )

    @mcp.tool()
    def get_supported_joints_catalog() -> Dict[str, Any]:
        """Retrieve the catalog of supported Advance Steel connection macros and their requirements."""
        return diagnostic_tools.get_supported_joints_catalog(client)

    @mcp.tool()
    def validate_section(section_name: str) -> Dict[str, Any]:
        """Check whether a profile section exists in the active Advance Steel AstorProfiles catalogue."""
        return diagnostic_tools.validate_section(client, section_name)

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
    def create_standard_joint(
        primary_handle: str,
        joint_type: Optional[str] = None,
        rule_name: Optional[str] = None,
        secondary_handles: Optional[List[str]] = None,
        connection_point: Optional[List[float]] = None,
        primary_end: str = "Start",
    ) -> Dict[str, Any]:
        """Apply a standard parametric connection (e.g. BasePlate, ClipAngle, EndPlate, ApexHaunch)."""
        return modeling_tools.create_standard_joint(
            client, primary_handle, joint_type, rule_name, secondary_handles, connection_point, primary_end
        )

    @mcp.tool()
    def apply_beam_cut_or_notch(
        beam_handle: str,
        cut_type: str = "shortening",
        cut_length_mm: Optional[float] = None,
        notch_width_mm: Optional[float] = None,
        notch_depth_mm: Optional[float] = None,
    ) -> Dict[str, Any]:
        """Apply beam shortening, flange cuts, notches, or miters."""
        return modeling_tools.apply_beam_cut_or_notch(
            client, beam_handle, cut_type, cut_length_mm, notch_width_mm, notch_depth_mm
        )

    @mcp.tool()
    def modify_element_properties(
        handle: str,
        material: Optional[str] = None,
        model_role: Optional[str] = None,
        coating: Optional[str] = None,
        rotation_deg: Optional[float] = None,
    ) -> Dict[str, Any]:
        """Modify material grade, model role, coating, or rotation of an existing element."""
        return modeling_tools.modify_element_properties(
            client, handle, material, model_role, coating, rotation_deg
        )

    @mcp.tool()
    def create_bolt_pattern(
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
        return modeling_tools.create_bolt_pattern(
            client,
            connected_handles,
            origin,
            normal,
            bolt_standard,
            bolt_grade,
            bolt_diameter_mm,
            nx,
            ny,
            dx,
            dy,
            is_site_bolt,
        )

    @mcp.tool()
    def create_poly_beam(
        points: List[List[float]],
        section_name: str,
        material: str = "S275JR",
        model_role: str = "Beam",
    ) -> Dict[str, Any]:
        """Create a continuous multi-segment polybeam or curved member from 3D points."""
        return modeling_tools.create_poly_beam(
            client, points, section_name, material, model_role
        )

    @mcp.tool()
    def run_automatic_numbering(
        element_handles: Optional[List[str]] = None,
        start_number: Optional[int] = None,
        prefix_single_parts: Optional[str] = None,
        prefix_assemblies: Optional[str] = None,
        keep_existing_numbers: Optional[bool] = None,
        engine_command: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Run Advance Steel numbering for the model or a selected set of elements."""
        return production_tools.run_automatic_numbering(
            client,
            element_handles,
            start_number,
            prefix_single_parts,
            prefix_assemblies,
            keep_existing_numbers,
            engine_command,
        )

    @mcp.tool()
    def export_dstv_nc_files(
        element_handles: Optional[List[str]] = None,
        output_directory: Optional[str] = None,
        file_extension: Optional[str] = None,
        overwrite: Optional[bool] = None,
        engine_command: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Export numbered single parts as DSTV/NC1 files."""
        return production_tools.export_dstv_nc_files(
            client, element_handles, output_directory, file_extension, overwrite, engine_command
        )

    @mcp.tool()
    def get_drawing_status(assembly_marks: Optional[List[str]] = None) -> Dict[str, Any]:
        """Report drawing availability and freshness for numbered assemblies."""
        return production_tools.get_drawing_status(client, assembly_marks)

    @mcp.tool()
    def get_bill_of_materials(
        element_handles: Optional[List[str]] = None,
        group_by: str = "profile",
    ) -> Dict[str, Any]:
        """Generate a comprehensive Bill of Materials (BOM) / Material Takeoff (MTO) report."""
        return production_tools.get_bill_of_materials(client, element_handles, group_by)

    @mcp.tool()
    def create_portal_frame(
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
        return modeling_tools.create_portal_frame(
            client,
            span_mm=span_mm,
            eave_height_mm=eave_height_mm,
            ridge_height_mm=ridge_height_mm,
            origin_x=origin_x,
            origin_y=origin_y,
            base_elevation_mm=base_elevation_mm,
            column_section=column_section,
            rafter_section=rafter_section,
            material=material,
            create_base_plates=create_base_plates,
            base_plate_thickness_mm=base_plate_thickness_mm,
            base_plate_dimensions_mm=base_plate_dimensions_mm,
        )

    @mcp.tool()
    def apply_detailing_repairs(
        repair_actions: List[str],
        element_handles: Optional[List[str]] = None,
        default_coating: str = "Galvanized",
        default_material: str = "S275JR",
        dry_run: bool = False,
    ) -> Dict[str, Any]:
        """Detailing Doctor: automatically fix orphaned parts, missing model roles, or invalid coatings."""
        return diagnostic_tools.apply_detailing_repairs(
            client,
            repair_actions=repair_actions,
            element_handles=element_handles,
            default_coating=default_coating,
            default_material=default_material,
            dry_run=dry_run,
        )

    @mcp.tool()
    def create_structural_grid(
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
        """Create 3D structural grids (Grid1D/Grid) for spatial reference, drawings, and Revit BIM coordination."""
        return modeling_tools.create_structural_grid(
            client,
            origin=origin,
            axis_direction=axis_direction,
            spacing_direction=spacing_direction,
            line_length=line_length,
            count=count,
            spacing=spacing,
            spacings=spacings,
            labels=labels,
            label_prefix=label_prefix,
            text_location=text_location,
        )

    @mcp.tool()
    def create_structural_level(
        name: str,
        elevation: float = 0.0,
        level_below_handle: Optional[str] = None,
        level_above_handle: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Create a building structure level (LevelObject) registered in Advance Steel."""
        return modeling_tools.create_structural_level(
            client,
            name=name,
            elevation=elevation,
            level_below_handle=level_below_handle,
            level_above_handle=level_above_handle,
        )

    @mcp.tool()
    def create_trussed_warehouse(
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
        return modeling_tools.create_trussed_warehouse(
            client,
            span=span,
            length=length,
            bay_spacing=bay_spacing,
            eave_height=eave_height,
            ridge_height=ridge_height,
            column_width=column_width,
            truss_depth=truss_depth,
            profile=profile,
            material=material,
            create_grids_and_levels=create_grids_and_levels,
        )

    @mcp.tool()
    def model_engineered_connection(
        connection_name: str = "Engineered Connection",
        source_system: str = "Calculation Report",
        plates: Optional[List[Dict[str, Any]]] = None,
        bolt_groups: Optional[List[Dict[str, Any]]] = None,
        shop_welds: Optional[List[Dict[str, Any]]] = None,
        verify_assembly: bool = True,
    ) -> Dict[str, Any]:
        """Model complete fabrication connection assemblies from engineering reports (RAM Connection, IDEA StatiCa, DXF, AISC 358 BFP PDF)."""
        return modeling_tools.model_engineered_connection(
            client,
            connection_name=connection_name,
            source_system=source_system,
            plates=plates,
            bolt_groups=bolt_groups,
            shop_welds=shop_welds,
            verify_assembly=verify_assembly,
        )

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
