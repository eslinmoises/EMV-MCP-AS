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
