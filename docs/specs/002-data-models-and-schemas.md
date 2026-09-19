# Specification 002: Data Models and Schemas

- **Spec ID**: SPEC-002
- **Status**: APPROVED
- **Target Components**: `src/as_plugin/Commands/Models`, `src/mcp_server/client/models.py`

---

## 1. Geometric Primitives

### Point3D
A 3D coordinate array `[X, Y, Z]` in millimeters (or inches if imperial model).
```json
[1000.0, 2500.0, 4000.0]
```

### BoundingBox3D
```json
{
  "min_point": [0.0, 0.0, 0.0],
  "max_point": [1000.0, 300.0, 6000.0]
}
```

---

## 2. Element Creation Schemas

### Straight Beam Request (`POST /api/v1/elements/beam`)
```json
{
  "start_point": [0.0, 0.0, 0.0],
  "end_point": [0.0, 0.0, 4000.0],
  "section_name": "HEB300",
  "material": "S275JR",
  "model_role": "Column",
  "reference_axis": "Center",
  "rotation_deg": 0.0
}
```

### Plate Request (`POST /api/v1/elements/plate`)
```json
{
  "contour_points": [
    [-200.0, -200.0, 0.0],
    [200.0, -200.0, 0.0],
    [200.0, 200.0, 0.0],
    [-200.0, 200.0, 0.0]
  ],
  "thickness": 20.0,
  "material": "S275JR",
  "model_role": "BasePlate"
}
```

---

## 3. Diagnostic and Assembly Schemas

### Weld Verification Response (`GET /api/v1/assembly/verify-welds`)
```json
{
  "welds": [
    {
      "weld_handle": "2F4A",
      "weld_type": "Fillet",
      "throat_thickness": 6.0,
      "location": "Workshop",
      "main_part_handle": "1B2C",
      "connected_part_handle": "3D4E",
      "is_same_assembly": true
    },
    {
      "weld_handle": "2F4B",
      "weld_type": "Butt",
      "throat_thickness": 10.0,
      "location": "Site",
      "main_part_handle": "1B2C",
      "connected_part_handle": "5E6F",
      "is_same_assembly": false
    }
  ],
  "total_workshop_welds": 1,
  "total_site_welds": 1
}
```

### Main Part Inspection Response (`GET /api/v1/assembly/main-part`)
```json
{
  "assembly_mark": "C1",
  "main_part_handle": "1B2C",
  "main_part_role": "Column",
  "main_part_section": "HEB300",
  "attached_parts": [
    {
      "handle": "3D4E",
      "role": "BasePlate",
      "weld_handle": "2F4A",
      "weight_kg": 35.2
    }
  ],
  "is_valid_main_part": true,
  "warning": null
}
```

---

## 4. Production & Fabrication Schemas

Marks are read back from the model after the engine has run: a report never echoes what was
requested, it states what the model now holds. `execution_time_ms` and the `success`/`error`
envelope are added by the transport layer (SPEC-001 §4) and are not part of these payloads.

### Numbering Report (`POST /api/v1/production/numbering`)
```json
{
  "scope": "model",
  "numbered_single_parts": 42,
  "numbered_assemblies": 11,
  "already_numbered": 0,
  "marks": [
    {
      "handle": "1B2C",
      "single_part_mark": "p1",
      "assembly_mark": "C1",
      "is_main_part": true,
      "quantity": 4
    }
  ],
  "conflicts": [
    {
      "handle": "7A8B",
      "mark": "C1",
      "reason": "Two geometrically different parts share the assembly mark C1."
    }
  ],
  "warnings": []
}
```
- `scope`: `"model"` when the whole model was numbered, `"selection"` when `element_handles` was given.
- `already_numbered`: parts that kept a pre-existing mark (`keep_existing_numbers = true`).
- `quantity`: how many identical parts share this single-part mark — the shop order quantity.
- `conflicts` is non-empty **without** failing the call: the model is numbered but the detailer must resolve the duplicates before release.

### NC Export Report (`POST /api/v1/production/export-nc`)
```json
{
  "output_directory": "C:\Projects\DSTV_NC1",
  "file_extension": "nc1",
  "exported_count": 2,
  "total_bytes": 8192,
  "files": [
    {
      "file_name": "p1.nc1",
      "path": "C:\Projects\DSTV_NC1\p1.nc1",
      "element_handle": "1B2C",
      "single_part_mark": "p1",
      "assembly_mark": "C1",
      "size_bytes": 4096
    }
  ],
  "skipped": [
    {
      "handle": "9C0D",
      "reason": "UNNUMBERED_PART",
      "message": "Part has no single part mark; it would produce an untraceable NC file."
    }
  ],
  "warnings": []
}
```
- A part without a single-part mark is **skipped, never exported**: an NC file whose name cannot be traced to a mark is worse on the shop floor than a missing one.
- `path` is always absolute, so an agent can hand it to a downstream CAM step verbatim.

### Drawing Status Report (`GET /api/v1/production/drawing-status`)
```json
{
  "total_assemblies": 11,
  "with_drawings": 8,
  "without_drawings": 3,
  "assemblies": [
    {
      "assembly_mark": "C1",
      "main_part_handle": "1B2C",
      "main_part_section": "HEB300",
      "quantity": 4,
      "has_drawing": true,
      "drawing_numbers": ["C1-01"],
      "is_up_to_date": true
    }
  ],
  "warnings": []
}
```
- `is_up_to_date` is `false` when the model changed after the drawing was derived — the drawing exists but must not be released to the shop.

---

## 5. Bolting, Node Queries & PolyBeam Schemas

### Spatial Box Query (`GET /api/v1/spatial/box`)
```json
{
  "elements": [
    {
      "handle": "1B2C",
      "type": "StraightBeam",
      "section_name": "HEB300",
      "material": "S275JR",
      "model_role": "Column",
      "bounding_box": {
        "min_point": [0.0, 0.0, 0.0],
        "max_point": [300.0, 300.0, 4000.0]
      }
    }
  ],
  "count": 1,
  "box": {
    "min_point": [-100.0, -100.0, -100.0],
    "max_point": [500.0, 500.0, 1000.0]
  }
}
```

### Bolt Pattern Data (`POST /api/v1/elements/bolt`)
```json
{
  "handle": "BOLT_501",
  "bolt_standard": "DIN 931",
  "bolt_grade": "8.8",
  "bolt_diameter_mm": 20.0,
  "count": 4,
  "connected_handles": ["1B2C", "2D3E"],
  "is_site_bolt": true
}
```

### PolyBeam Data (`POST /api/v1/elements/poly-beam`)
```json
{
  "handle": "PBEAM_601",
  "section_name": "HEA200",
  "length_mm": 6283.18,
  "weight_kg": 265.8,
  "vertex_count": 3
}
```

---

## 6. Advance Steel 2026 .NET API Mapping Reference
As extracted by assembly reflection:
- **Plates**: `Autodesk.AdvanceSteel.Modelling.Plate(Plane, Point3d[], double)`
- **Beams**: `Autodesk.AdvanceSteel.Modelling.StraightBeam(section, startPoint, endPoint, refVector)` with `Beam.eRefAxis`
- **PolyBeams**: `Autodesk.AdvanceSteel.Modelling.PolyBeam`
- **Bolts**: `Autodesk.AdvanceSteel.Modelling.BoltPattern`
- **Welds**: `Autodesk.AdvanceSteel.Modelling.WeldPattern`
- **Location**: `Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement.eAssemblyLocation` (`kInShop`, `kOnSite`)
- **Assembly & Main Part**: Managed via `AtomicElement.IsMainPart` and connection graphs (`GetConnectedObjects(..., kInShop)`).
- **Numbering (read back)**: `AtomicElement.GetSinglePartPositionNumber()`, `GetMainPartPositionNumber()`, `GetNumberingStatus()`.
- **Numbering / NC engines**: not exposed as managed classes in AS 2026; driven through the Advance Steel command layer and verified afterwards through the properties above (see `rules/transaction-safety.md` §5).
