# Specification 004: MCP Tools Formal Specification

- **Spec ID**: SPEC-004
- **Status**: APPROVED
- **Target Components**: `src/mcp_server/tools`, `src/mcp_server/server.py`

---

## 1. Diagnostic & Reading Tools

### `get_active_model_info`
- **Description**: Returns metadata for the currently active Advance Steel DWG (name, path, AS version, units, element count).
- **Parameters**: None.
- **Returns**: `ModelInfoResponse`.

### `get_selected_elements`
- **Description**: Inspects elements currently selected in the Advance Steel viewport.
- **Parameters**: None.
- **Returns**: `list[ElementDetails]`.

### `verify_welds_and_assemblies`
- **Description**: Inspects welds connected to specified elements (or all in selection), checking Workshop vs. Site status, throat thickness, and assembly grouping.
- **Parameters**:
  - `element_handles` (`list[str]`, optional): Target element handles. If omitted, checks all welds in selection or model.
- **Returns**: `WeldVerificationResponse`.

### `inspect_main_part`
- **Description**: Identifies the Main Part of a given assembly mark or handle, checking whether it violates detailing rules.
- **Parameters**:
  - `assembly_or_element_handle` (`str`, required): Assembly mark (e.g. "C1") or handle of any member in the assembly.
- **Returns**: `MainPartInspectionResponse`.

### `audit_assembly_integrity`
- **Description**: Scans the model for orphaned parts (plates or stiffeners lacking workshop welds), broken joints, or unnumbered parts.
- **Parameters**: None.
- **Returns**: `AuditReport`.

### `detect_clashes_and_clearances`
- **Description**: Runs native Advance Steel collision checking across the model or selected members.
- **Parameters**:
  - `element_handles` (`list[str]`, optional): Specific elements to check.
- **Returns**: `ClashReport`.

### `get_ucs_and_grids`
- **Description**: Returns the active coordinate system (UCS) origin, vectors, all grid lines, and building level elevations.
- **Parameters**: None.
- **Returns**: `UcsAndGridsResponse`.

### `capture_viewport`
- **Description**: Captures a PNG screenshot of the current 3D viewport rendered frame for visual verification by multimodal LLMs.
- **Parameters**: None.
- **Returns**: Base64 encoded PNG image data.

---

## 2. Generative Modeling Tools

### `create_straight_beam`
- **Description**: Creates a straight structural steel profile between two 3D points.
- **Parameters**:
  - `start_point` (`list[float]`, required): `[x, y, z]`
  - `end_point` (`list[float]`, required): `[x, y, z]`
  - `section_name` (`str`, required): e.g. "HEB300", "IPE240", "HEA200"
  - `material` (`str`, optional, default="S275JR"): Steel material grade
  - `model_role` (`str`, optional, default="Beam"): Advance Steel model role
  - `reference_axis` (`str`, optional, default="Center"): Alignment axis
- **Returns**: `{"handle": str, "length": float, "weight_kg": float}`.

### `create_plate`
- **Description**: Creates a contour plate from a closed list of coplanar 3D points.
- **Parameters**:
  - `contour_points` (`list[list[float]]`, required): At least 3 points `[[x,y,z], ...]`
  - `thickness` (`float`, required): Plate thickness in mm
  - `material` (`str`, optional, default="S275JR"): Steel grade
  - `model_role` (`str`, optional, default="Plate"): Role
- **Returns**: `{"handle": str, "area_m2": float, "weight_kg": float}`.

### `set_main_part`
- **Description**: Reassigns the Main Part of an assembly to a designated beam or plate handle.
- **Parameters**:
  - `assembly_handle` (`str`, required)
  - `new_main_part_handle` (`str`, required)
- **Returns**: `{"assembly_mark": str, "main_part_handle": str, "success": true}`.

---

## 3. Dynamic Scripting Tool

### `execute_csharp_script`
- **Description**: Evaluates dynamic C# Roslyn code in the Advance Steel context with automatic transaction rollback on failure.
- **Parameters**:
  - `script_code` (`str`, required): C# script body.
- **Returns**: `{"success": bool, "output": str, "execution_time_ms": int}`.
