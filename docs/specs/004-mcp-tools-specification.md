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

### `apply_detailing_repairs`
- **Endpoint**: `POST /api/v1/audit/repair`
- **Description**: Autonomous "Detailing Doctor" engine that inspects the model for common structural detailing defects and repairs them automatically:
  1. Detects elements with missing/empty Advance Steel `Role` and infers appropriate roles (`Column`, `Beam`, `Rafter`, `BasePlate`) based on geometry, vector alignment, and position.
  2. Identifies assemblies lacking an explicit `MainPart` and sets the heaviest/longest primary profile as the Main Part (`IsMainPart = true`).
  3. Detects orphaned plates that share coplanar faces with primary members and joins them with workshop welds or anchors.
- **Parameters**:
  - `element_handles` (`list[str]`, optional): Restrict repairs to specified elements. Omitted ⇒ entire model.
  - `fix_roles` (`bool`, optional, default=true): Infer and update missing model roles.
  - `fix_main_parts` (`bool`, optional, default=true): Assign missing Main Parts to assemblies.
  - `fix_orphaned_plates` (`bool`, optional, default=true): Attach orphaned connection plates.
- **Returns**: `RepairReport`:
  - `repairs_applied`: int
  - `roles_updated`: list of `{ handle: str, assigned_role: str }`
  - `main_parts_assigned`: list of `{ assembly_mark: str, main_part_handle: str }`
  - `orphans_resolved`: int
  - `warnings`: list of str

### `detect_clashes_and_clearances`
- **Description**: Runs native Advance Steel collision checking across the model or selected members.
- **Parameters**:
  - `element_handles` (`list[str]`, optional): Specific elements to check.
- **Returns**: `ClashReport`.

### `get_ucs_and_grids`
- **Description**: Returns the active coordinate system (UCS) origin, vectors, all grid lines, and building level elevations.
- **Parameters**: None.
- **Returns**: `UcsAndGridsResponse`.

### `query_elements_in_box`
- **Endpoint**: `GET /api/v1/spatial/box`
- **Description**: Finds all Advance Steel elements (beams, plates, bolts, welds) whose 3D bounding extents intersect a specified 3D box `[min_point, max_point]`.
- **Parameters**:
  - `min_point` (`list[float]`, required, query `?min_point=x,y,z`): minimum corner `[x, y, z]` in mm.
  - `max_point` (`list[float]`, required, query `?max_point=x,y,z`): maximum corner `[x, y, z]` in mm.
  - `element_types` (`list[str]`, optional): filter by type name.
- **Returns**: `{"elements": list[ElementDetails], "count": int, "box": {"min_point": list[float], "max_point": list[float]}}`.

### `query_elements`
- **Endpoint**: `POST /api/v1/elements/query`
- **Description**: Filters and queries model elements matching combined criteria: model role, material, section name, assembly mark, single part mark, element type, or specific handles.
- **Parameters**:
  - `model_role` (`str`, optional): filter by Advance Steel role (e.g. "Column", "Beam", "BasePlate").
  - `material` (`str`, optional): filter by material grade (e.g. "S275JR", "S355JR").
  - `section_name` (`str`, optional): filter by profile name (e.g. "HEB300", "IPE240").
  - `assembly_mark` (`str`, optional): filter by assigned assembly mark (e.g. "C1").
  - `single_part_mark` (`str`, optional): filter by assigned single-part mark (e.g. "p1").
  - `element_types` (`list[str]`, optional): filter by object types (e.g. `["StraightBeam", "Plate"]`).
  - `handles` (`list[str]`, optional): restrict query to a specific list of handles.
- **Returns**: `{"elements": list[ElementDetails], "count": int, "filters_applied": dict}`.

### `get_supported_joints_catalog`
- **Endpoint**: `GET /api/v1/elements/joints-catalog`
- **Description**: Returns the dictionary of Advance Steel parametric joints supported by the system, their friendly aliases, internal macro rule names, and required connection member roles.
- **Parameters**: None.
- **Returns**: `{"joints": list[JointCatalogEntry], "total_count": int}`.

### `validate_section`
- **Endpoint**: `GET /api/v1/elements/validate-section?section_name=HEB300`
- **Description**: Checks whether a profile section exists in the active Advance Steel AstorProfiles database.
- **Parameters**:
  - `section_name` (`str`, required): Profile name to validate (e.g. "HEB300", "IPE240").
- **Returns**: `{"section_name": str, "is_valid": bool, "message": str}`.

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

### `create_standard_joint`
- **Description**: Applies a standard parametric Advance Steel connection (BasePlate, ClipAngle, EndPlate, ApexHaunch, …) to a primary member and optional secondary members.
- **Parameters**:
  - `primary_handle` (`str`, required)
  - `joint_type` (`str`, optional): friendly alias resolved to an Advance Steel rule name
  - `rule_name` (`str`, optional): exact Advance Steel rule name; wins over `joint_type`
  - `secondary_handles` (`list[str]`, optional)
  - `connection_point` (`list[float]`, optional)
  - `primary_end` (`str`, optional, default="Start")
- **Returns**: `{"handle": str, "rule_name": str, "node_status": str, "created_objects": list[ElementDetails]}`.

### `apply_beam_cut_or_notch`
- **Description**: Applies a shortening, flange cut, notch or miter feature to an existing beam.
- **Parameters**:
  - `beam_handle` (`str`, required)
  - `cut_type` (`str`, optional, default="shortening")
  - `cut_length_mm`, `notch_width_mm`, `notch_depth_mm` (`float`, optional)
- **Returns**: `{"handle": str, "cut_type": str, "length_before_mm": float, "length_mm": float}`.

### `modify_element_properties`
- **Description**: Updates material, model role, coating or rotation of an existing element. Properties absent from the request are untouched; a property the element cannot carry is an error, never a silent skip.
- **Parameters**: `handle` (`str`, required), `material`, `model_role`, `coating` (`str`, optional), `rotation_deg` (`float`, optional).
- **Returns**: `{"handle": str, "modified_properties": dict, "success": true}`.

### `create_bolt_pattern`
- **Endpoint**: `POST /api/v1/elements/bolt`
- **Description**: Creates a rectangular bolt pattern connecting two or more structural parts.
- **Parameters**:
  - `connected_handles` (`list[str]`, required): Handles of beams and plates to connect.
  - `origin` (`list[float]`, required): `[x, y, z]` insertion center point.
  - `normal` (`list[float]`, optional, default=[0, 0, 1]): `[nx, ny, nz]` bolt direction vector.
  - `bolt_standard` (`str`, optional, default="DIN 931"): Standard/norm (e.g. "DIN 931", "ISO 4014", "A325").
  - `bolt_grade` (`str`, optional, default="8.8"): Steel grade (e.g. "8.8", "10.9").
  - `bolt_diameter_mm` (`float`, optional, default=20.0): Bolt nominal diameter.
  - `nx` (`int`, optional, default=2): Number of bolts along X.
  - `ny` (`int`, optional, default=2): Number of bolts along Y.
  - `dx` (`float`, optional, default=70.0): Spacing along X in mm.
  - `dy` (`float`, optional, default=70.0): Spacing along Y in mm.
  - `is_site_bolt` (`bool`, optional, default=true): Site vs. workshop assembly connection.
- **Returns**: `{"handle": str, "bolt_standard": str, "bolt_grade": str, "bolt_diameter_mm": float, "count": int, "connected_handles": list[str]}`.

### `create_poly_beam`
- **Endpoint**: `POST /api/v1/elements/poly-beam`
- **Description**: Creates a continuous multi-segment polybeam or curved member defined by a sequence of 3D points.
- **Parameters**:
  - `points` (`list[list[float]]`, required): At least 2 points `[[x,y,z], ...]`.
  - `section_name` (`str`, required): Profile section name (e.g. "HEA200", "IPE240").
  - `material` (`str`, optional, default="S275JR"): Steel material grade.
  - `model_role` (`str`, optional, default="Beam"): Advance Steel model role.
- **Returns**: `{"handle": str, "length_mm": float, "weight_kg": float}`.

### `create_portal_frame`
- **Endpoint**: `POST /api/v1/elements/portal-frame`
- **Description**: Generates a complete 3D structural steel portal frame (two vertical columns, two pitched rafters, base plates, and anchor bolts) in a single atomic transaction.
- **Parameters**:
  - `span_width_mm` (`float`, optional, default=12000.0): Center-to-center distance between columns.
  - `column_height_mm` (`float`, optional, default=5000.0): Eaves height to top of column.
  - `ridge_height_mm` (`float`, optional, default=6500.0): Peak roof ridge elevation.
  - `column_section` (`str`, optional, default="HEB300"): Profile name for columns.
  - `rafter_section` (`str`, optional, default="IPE360"): Profile name for rafters.
  - `material` (`str`, optional, default="S275JR"): Steel material grade.
  - `origin` (`list[float]`, optional, default=[0.0, 0.0, 0.0]): Insertion origin `[x, y, z]` of the left column base.
  - `include_base_plates` (`bool`, optional, default=true): Generate 25mm base plates and M20 anchor bolts.
  - `include_connections` (`bool`, optional, default=false): Apply standard joints (ClipAngle/EndPlate at eaves and ApexHaunch at ridge).
- **Returns**: `PortalFrameDetails`:
  - `left_column`: `{"handle": str, "section": str, "height_mm": float}`
  - `right_column`: `{"handle": str, "section": str, "height_mm": float}`
  - `left_rafter`: `{"handle": str, "section": str, "length_mm": float}`
  - `right_rafter`: `{"handle": str, "section": str, "length_mm": float}`
  - `base_plates`: list of plate handles
  - `bolt_patterns`: list of bolt handles
  - `total_weight_kg`: float

---

## 3. Dynamic Scripting Tool

### `execute_csharp_script`
- **Description**: Evaluates dynamic C# Roslyn code in the Advance Steel context with automatic transaction rollback on failure.
- **Parameters**:
  - `script_code` (`str`, required): C# script body.
- **Returns**: `{"success": bool, "output": str, "execution_time_ms": int}`.

---

## 4. Production & Fabrication Tools

Workshop release order is **numbering → NC export → drawings**. Each tool fails loudly when the
preceding step is missing rather than producing fabrication data that is silently wrong: an NC file
generated from an unnumbered part cannot be traced back to a mark on the shop floor.

### `run_automatic_numbering`
- **Endpoint**: `POST /api/v1/production/numbering`
- **Description**: Runs the Advance Steel numbering engine over the model (or a subset of handles), assigning single-part marks (`p1`, `p2`, …) and assembly marks (`C1`, `B1`, …) to geometrically identical parts, then reports the resulting marks read back from the model.
- **Parameters**:
  - `element_handles` (`list[str]`, optional): restrict the scope. Omitted ⇒ whole model.
  - `start_number` (`int`, optional, default=1): first position number.
  - `prefix_single_parts` (`str`, optional): prefix for single-part marks.
  - `prefix_assemblies` (`str`, optional): prefix for assembly marks.
  - `keep_existing_numbers` (`bool`, optional, default=true): preserve marks already assigned. `false` renumbers from scratch and **invalidates existing drawings and NC files**.
  - `engine_command` (`str`, optional): pins the Advance Steel command that drives the engine. Advance Steel has renamed these between releases, so the add-in probes a candidate list and this is the escape hatch when an installation names it differently.
- **Returns**: `NumberingReport`, plus `engine_command` naming the command that actually ran.
- **Errors**: `NUMBERING_ENGINE_UNAVAILABLE` (503, no candidate command is registered in the session), `ENGINE_COMMAND_FAILED` (422, the command ran and threw — command mode has no transaction to roll back, so the model is left as the command wrote it), `INVALID_PARAMETER` (400), `ELEMENT_NOT_FOUND` (404).

### `export_dstv_nc_files`
- **Endpoint**: `POST /api/v1/production/export-nc`
- **Description**: Generates DSTV / NC1 files for CNC drilling, sawing and plasma profiling from the numbered single parts of the model or a selection.
- **Parameters**:
  - `element_handles` (`list[str]`, optional): restrict the scope. Omitted ⇒ all numbered single parts.
  - `output_directory` (`str`, optional, default=`"./DSTV_NC1"`): relative paths resolve against the folder of the active DWG.
  - `file_extension` (`str`, optional, default=`"nc1"`): `"nc1"` or `"nc"`.
  - `overwrite` (`bool`, optional, default=true).
  - `engine_command` (`str`, optional): as above, for the NC creator.
- **Returns**: `NcExportReport`, plus `engine_command` naming the command that actually ran.
- **Errors**: `UNNUMBERED_MODEL` (409, numbering has not run), `OUTPUT_DIRECTORY_UNWRITABLE` (422), `NC_EXPORT_UNAVAILABLE` (503), `ENGINE_COMMAND_FAILED` (422), `EXPORT_FAILED` (422, the command ran but wrote no file into the requested directory), `MODEL_NOT_SAVED` (409, relative output directory with an unsaved DWG).

### `get_drawing_status`
- **Endpoint**: `GET /api/v1/production/drawing-status`
- **Description**: Reports, per assembly mark, whether shop fabrication drawings have been derived and whether they are still up to date with the model.
- **Parameters**:
  - `assembly_marks` (`list[str]`, optional, query `?assembly_marks=C1,B1`): restrict the report.
- **Returns**: `DrawingStatusReport`.
- **Errors**: `UNNUMBERED_MODEL` (409), `DRAWING_STATUS_UNAVAILABLE` (503).

### `get_bill_of_materials`
- **Endpoint**: `POST /api/v1/production/bom`
- **Description**: Generates a comprehensive Bill of Materials (BOM) / Material Takeoff (MTO) report for the model or a selection of element handles. Aggregates linear profiles (lengths, unit weights, total weights, paint coating surface area), plates (thicknesses, contour areas, weights), and fasteners (standards, diameters, grades, counts), computing total structural steel tonnage.
- **Parameters**:
  - `element_handles` (`list[str]`, optional): restrict takeoff scope. Omitted ⇒ entire active model.
  - `group_by` (`str`, optional, default=`"profile"`): aggregation dimension: `"profile"`, `"assembly"`, or `"material"`.
- **Returns**: `BomReport`:
  - `total_weight_kg` (`float`)
  - `total_tonnage` (`float`, metric tonnes)
  - `total_coating_area_m2` (`float`)
  - `linear_members` (`list[dict]`): items with `section_name`, `material`, `count`, `total_length_mm`, `total_weight_kg`, `coating_area_m2`.
  - `plates` (`list[dict]`): items with `thickness_mm`, `material`, `count`, `total_area_m2`, `total_weight_kg`.
  - `bolts` (`list[dict]`): items with `bolt_standard`, `bolt_grade`, `bolt_diameter_mm`, `count`.
  - `group_by` (`str`)
  - `elements_scanned` (`int`)
- **Errors**: `INVALID_PARAMETER` (400), `NO_ELEMENTS_FOUND` (404).

