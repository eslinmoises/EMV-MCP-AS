"""Test fixtures and mock payloads matching SPEC-001 and SPEC-002."""

MOCK_HEALTH_DATA = {
    "status": "online",
    "as_version": "2026",
    "acad_version": "25.0",
    "active_dwg": "C:\\Projects\\Model_01.dwg",
    "units": "Metric",
    "element_count": {
        "beams": 142,
        "plates": 88,
        "welds": 310,
        "bolts": 420
    }
}

MOCK_SELECTED_ELEMENTS = [
    {
        "handle": "1B2C",
        "type": "StraightBeam",
        "section_name": "HEB300",
        "material": "S275JR",
        "model_role": "Column",
        "layer": "0",
        "length_mm": 4000.0,
        "weight_kg": 468.0,
        "start_point": [0.0, 0.0, 0.0],
        "end_point": [0.0, 0.0, 4000.0],
        "bounding_box": {
            "min_point": [-150.0, -150.0, 0.0],
            "max_point": [150.0, 150.0, 4000.0]
        }
    }
]

MOCK_WELD_VERIFICATION = {
    "welds": [
        {
            "weld_handle": "2F4A",
            "weld_type": "Fillet",
            "throat_thickness": 6.0,
            "location": "Workshop",
            "main_part_handle": "1B2C",
            "connected_part_handle": "3D4E",
            "is_same_assembly": True
        },
        {
            "weld_handle": "2F4B",
            "weld_type": "Butt",
            "throat_thickness": 10.0,
            "location": "Site",
            "main_part_handle": "1B2C",
            "connected_part_handle": "5E6F",
            "is_same_assembly": False
        }
    ],
    "total_workshop_welds": 1,
    "total_site_welds": 1
}

MOCK_MAIN_PART_INSPECTION = {
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
    "is_valid_main_part": True,
    "warning": None
}

MOCK_UCS_AND_GRIDS = {
    "active_ucs": {
        "origin": [0.0, 0.0, 0.0],
        "x_axis": [1.0, 0.0, 0.0],
        "y_axis": [0.0, 1.0, 0.0],
        "z_axis": [0.0, 0.0, 1.0]
    },
    "grid_axes": [
        {"name": "A", "start": [0.0, 0.0, 0.0], "end": [0.0, 20000.0, 0.0]},
        {"name": "B", "start": [6000.0, 0.0, 0.0], "end": [6000.0, 20000.0, 0.0]},
        {"name": "1", "start": [0.0, 0.0, 0.0], "end": [18000.0, 0.0, 0.0]},
        {"name": "2", "start": [0.0, 6000.0, 0.0], "end": [18000.0, 6000.0, 0.0]}
    ],
    "levels": [
        {"name": "+0.00", "elevation": 0.0},
        {"name": "+4.00", "elevation": 4000.0}
    ]
}

MOCK_NUMBERING_MARKS = [
    {
        "handle": "1B2C",
        "single_part_mark": "p1",
        "assembly_mark": "C1",
        "is_main_part": True,
        "quantity": 4,
    },
    {
        "handle": "3D4E",
        "single_part_mark": "p2",
        "assembly_mark": "C1",
        "is_main_part": False,
        "quantity": 1,
    },
    {
        "handle": "5E6F",
        "single_part_mark": "p3",
        "assembly_mark": "B1",
        "is_main_part": True,
        "quantity": 2,
    },
    {
        "handle": "7A8B",
        "single_part_mark": "p4",
        "assembly_mark": "C1",
        "is_main_part": False,
        "quantity": 1,
    },
]

MOCK_NUMBERING_CONFLICTS = [
    {
        "handle": "7A8B",
        "mark": "C1",
        "reason": "Two geometrically different parts share the assembly mark C1.",
    }
]

MOCK_UNNUMBERED_ELEMENT_HANDLE = "9C0D"

MOCK_DRAWING_STATUS_ASSEMBLIES = [
    {
        "assembly_mark": "C1",
        "main_part_handle": "1B2C",
        "main_part_section": "HEB300",
        "quantity": 4,
        "has_drawing": True,
        "drawing_numbers": ["C1-01"],
        "is_up_to_date": True,
    },
    {
        "assembly_mark": "B1",
        "main_part_handle": "5E6F",
        "main_part_section": "IPE240",
        "quantity": 2,
        "has_drawing": False,
        "drawing_numbers": [],
        "is_up_to_date": False,
    },
    {
        "assembly_mark": "C2",
        "main_part_handle": "0A1B",
        "main_part_section": "HEA200",
        "quantity": 1,
        "has_drawing": True,
        "drawing_numbers": ["C2-01"],
        "is_up_to_date": False,
    },
]
