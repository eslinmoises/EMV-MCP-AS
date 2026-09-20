"""CONTRACT-013: Connection specification ingestor for engineering PDF reports.

Reads prequalified moment connection calculation reports (Mathcad/PTC exports such as
``IT_Ejemplo_Conexion_BFP_AISC 358-16_s2-20.pdf``) with PyMuPDF and rebuilds a complete
structured engineering specification: member profiles, plates, bolt groups, welds and
stiffeners, plus the Advance Steel detailing payload consumed by
``model_engineered_connection`` (CONTRACT-012).

Mathcad PDF exports linearise every assignment into single-token lines, e.g.::

    U+2254        (the ":=" definition glyph)
    tp
    25 mm

so the extraction works on a token stream: locate the definition glyph, read the variable
name from the following line and scan forward for the first dimensioned literal, stopping
at the next definition or numbered heading. Variable names are reused across chapters
(``db``, ``n``, ``S``, ``bp`` ... appear in both the flange and the web chapters), therefore
every lookup is scoped to the numbered section range it belongs to.
"""

import os
import re
from typing import Any, Dict, List, Optional, Sequence, Tuple

try:  # PyMuPDF is the extraction backend; keep the module importable without it.
    import fitz  # type: ignore

    PYMUPDF_AVAILABLE = True
except ImportError:  # pragma: no cover - exercised only on installs without PyMuPDF
    fitz = None  # type: ignore
    PYMUPDF_AVAILABLE = False


# --------------------------------------------------------------------------------------
# Mathcad token grammar
# --------------------------------------------------------------------------------------

DEFINITION_GLYPH = "≔"  # the Mathcad ":=" assignment glyph
UNIT_TO_MM = {"mm": 1.0, "cm": 10.0, "m": 1000.0, "in": 25.4}

VALUE_RE = re.compile(
    r"^(-?\d+(?:\.\d+)?)\s*(mm|cm|m|in|kN|MPa|kgf|ksi)(?:\s*(\d))?\s*$"
)
SCALAR_RE = re.compile(r"^(-?\d+(?:\.\d+)?)$")
HEADING_RE = re.compile(r"^(\d+(?:\.\d+)*)\.\s+\S")

# Accent tolerant patterns: the PDF text layer encodes Spanish accents inconsistently.
BEAM_PROFILE_RE = re.compile(r"Definici.n de la viga[^:]*:\s*([A-Za-z][\w\-/.]*)")
COLUMN_PROFILE_RE = re.compile(r"Definici.n de la columna[^:]*:\s*([A-Za-z][\w\-/.]*)")
STEEL_GRADE_RE = re.compile(r"Tipo de acero:\s*(ASTM\s+A\d+\w*|S\d{3}\w*)")
BOLT_GRADE_RE = re.compile(r"Calidad del perno:\s*(ASTM\s+A\d+\w*|DIN\s*\d+|Gr\.?\s*\w+)")
PROFILE_FALLBACK_RE = re.compile(
    r"\b((?:IPE|IPN|HEA|HEB|HEM|W|HP|UB|UC)[\s\-]?\d+(?:x\d+)?)\b"
)

SEARCH_WINDOW = 30  # max token lines scanned forward for an assigned literal

SUPPORTED_CONNECTION_TYPES = {
    "BFP": "Bolted Flange Plate - ANSI/AISC 358-16 Chapter 7",
}

CONNECTION_TYPE_SIGNATURES = (
    ("BFP", ("bolted flange plate", "plancha de ala", "bolted flange-plate")),
    ("EEP", ("extended end-plate", "extended end plate", "plancha extrema")),
    ("RBS", ("reduced beam section", "seccion de viga reducida", "viga de seccion reducida")),
    ("WUF-W", ("welded unreinforced flange", "ala soldada sin refuerzo")),
)

_DEFAULT_SAMPLE_ENV = "EMV_BFP_SAMPLE_PDF"


def _err(code: str, message: str, details: str = "", suggestion: str = "") -> Dict[str, Any]:
    """Build the project-wide error envelope used by the IPC client and MCP tools."""
    return {
        "success": False,
        "data": None,
        "error": {
            "code": code,
            "message": message,
            "details": details,
            "suggestion": suggestion,
        },
    }


def _section_key(section: Optional[str]) -> Tuple[int, ...]:
    if not section:
        return ()
    try:
        return tuple(int(p) for p in section.split("."))
    except ValueError:
        return ()


def _in_range(section: Optional[str], low: str, high: str) -> bool:
    """True when ``section`` falls inside the half-open numbered range [low, high)."""
    key = _section_key(section)
    if not key:
        return False
    return _section_key(low) <= key < _section_key(high)


class MathcadVariable:
    """A single ``name := value unit`` assignment recovered from the token stream."""

    __slots__ = ("name", "raw", "value", "unit", "exponent", "page", "section", "line_index")

    def __init__(self, name, raw, value, unit, exponent, page, section, line_index):
        self.name = name
        self.raw = raw
        self.value = value
        self.unit = unit
        self.exponent = exponent
        self.page = page
        self.section = section
        self.line_index = line_index

    @property
    def value_mm(self) -> Optional[float]:
        """Value converted to millimetres, or None when it is not a plain length."""
        if self.value is None or self.exponent not in (None, 1):
            return None
        if self.unit is None:
            return None
        factor = UNIT_TO_MM.get(self.unit)
        return None if factor is None else self.value * factor

    def __repr__(self) -> str:  # pragma: no cover - debugging helper
        return "MathcadVariable(%r, %r, %r, section=%r, page=%r)" % (
            self.name,
            self.value,
            self.unit,
            self.section,
            self.page,
        )


class ParsedDocument:
    """Token stream of an engineering report plus scoped variable lookups."""

    def __init__(self, lines: Sequence[Tuple[int, str]], path: str = "", page_count: int = 0):
        self.path = path
        self.lines: List[Tuple[int, str]] = list(lines)
        self.page_count = page_count or (max((p for p, _ in self.lines), default=-1) + 1)
        self.sections: List[Tuple[int, str, str]] = []  # (line_index, number, heading)
        self.variables: List[MathcadVariable] = []
        self._section_of_line: List[Optional[str]] = []
        self._index()

    # -- indexing ----------------------------------------------------------------------

    def _index(self) -> None:
        current: Optional[str] = None
        texts = [text for _, text in self.lines]
        for i, text in enumerate(texts):
            stripped = text.strip()
            heading = HEADING_RE.match(stripped)
            if heading:
                current = heading.group(1)
                self.sections.append((i, current, stripped))
            self._section_of_line.append(current)

        for i, text in enumerate(texts):
            if text.strip() != DEFINITION_GLYPH:
                continue
            if i + 1 >= len(texts):
                continue
            name = texts[i + 1].strip()
            if not name or name == DEFINITION_GLYPH:
                continue
            value, unit, exponent, raw = self._scan_value(texts, i + 2)
            self.variables.append(
                MathcadVariable(
                    name=name,
                    raw=raw,
                    value=value,
                    unit=unit,
                    exponent=exponent,
                    page=self.lines[i][0],
                    section=self._section_of_line[i],
                    line_index=i,
                )
            )

    @staticmethod
    def _scan_value(texts: Sequence[str], start: int):
        """Scan forward for the evaluated literal of an assignment.

        Dimensioned literals win over bare numbers: a Mathcad fraction such as ``5/8 in``
        linearises to the tokens ``5``, ``8``, ``in``, ``15.88 mm`` and only the last one
        carries the evaluated length.
        """
        fallback = None
        for j in range(start, min(start + SEARCH_WINDOW, len(texts))):
            token = texts[j].strip()
            if token == DEFINITION_GLYPH or HEADING_RE.match(token):
                break
            dimensioned = VALUE_RE.match(token)
            if dimensioned:
                exponent = int(dimensioned.group(3)) if dimensioned.group(3) else None
                return float(dimensioned.group(1)), dimensioned.group(2), exponent, token
            if fallback is None:
                scalar = SCALAR_RE.match(token)
                if scalar:
                    fallback = (float(scalar.group(1)), None, None, token)
        if fallback is not None:
            return fallback
        return None, None, None, ""

    # -- lookups -----------------------------------------------------------------------

    def find(
        self,
        name: str,
        section_range: Optional[Tuple[str, str]] = None,
        dimensioned: bool = True,
    ) -> List[MathcadVariable]:
        """All assignments of ``name`` inside the given numbered section range."""
        matches = []
        for var in self.variables:
            if var.name != name:
                continue
            if section_range and not _in_range(var.section, section_range[0], section_range[1]):
                continue
            if dimensioned and var.unit is None:
                continue
            matches.append(var)
        return matches

    def length_mm(
        self,
        name: str,
        section_range: Optional[Tuple[str, str]] = None,
        index: int = 0,
    ) -> Optional[float]:
        """Length assignment converted to millimetres, or None when absent."""
        matches = [v for v in self.find(name, section_range) if v.value_mm is not None]
        if len(matches) <= index:
            return None
        return matches[index].value_mm

    def quantity(
        self,
        name: str,
        section_range: Optional[Tuple[str, str]] = None,
        unit: Optional[str] = None,
        index: int = 0,
    ) -> Optional[float]:
        """Raw assignment value (stress, factor) without unit conversion."""
        matches = self.find(name, section_range, dimensioned=unit is not None)
        if unit is not None:
            matches = [m for m in matches if m.unit == unit]
        if len(matches) <= index:
            return None
        return matches[index].value

    def count(self, name: str, section_range: Optional[Tuple[str, str]] = None) -> Optional[int]:
        """Dimensionless integer assignment (bolt counts)."""
        for var in self.variables:
            if var.name != name or var.unit is not None or var.value is None:
                continue
            if section_range and not _in_range(var.section, section_range[0], section_range[1]):
                continue
            if abs(var.value - round(var.value)) < 1e-9:
                return int(round(var.value))
        return None

    def search(self, pattern, section_range: Optional[Tuple[str, str]] = None):
        """First regex match over the raw text lines, optionally scoped to a section."""
        for i, (_, text) in enumerate(self.lines):
            if section_range and not _in_range(
                self._section_of_line[i], section_range[0], section_range[1]
            ):
                continue
            found = pattern.search(text)
            if found:
                return found
        return None

    @property
    def text(self) -> str:
        return "\n".join(text for _, text in self.lines)


# --------------------------------------------------------------------------------------
# Extraction
# --------------------------------------------------------------------------------------

def extract_pdf_lines(document_path: str) -> List[Tuple[int, str]]:
    """Extract ``(page_index, line)`` tuples from a PDF using PyMuPDF."""
    if not PYMUPDF_AVAILABLE:
        raise RuntimeError("PyMuPDF (fitz) is not installed.")
    lines: List[Tuple[int, str]] = []
    doc = fitz.open(document_path)
    try:
        for page_index in range(doc.page_count):
            for line in doc[page_index].get_text().splitlines():
                lines.append((page_index, line))
    finally:
        doc.close()
    return lines


def detect_connection_type(text: str) -> Optional[str]:
    """Identify the prequalified connection family from the report wording."""
    lowered = text.lower()
    for code, signatures in CONNECTION_TYPE_SIGNATURES:
        if any(sig in lowered for sig in signatures):
            return code
    if re.search(r"\bBFP\b", text):
        return "BFP"
    return None


def _normalize_grade(grade: Optional[str], default: str = "") -> str:
    if not grade:
        return default
    return re.sub(r"\s+", " ", grade).strip()


def _material_name(grade: Optional[str], default: str = "A36") -> str:
    """Advance Steel material name: ``ASTM A36`` -> ``A36``."""
    if not grade:
        return default
    return re.sub(r"^ASTM\s+", "", _normalize_grade(grade))


def _profile(doc: ParsedDocument, pattern, section_range: Tuple[str, str]) -> Optional[str]:
    found = doc.search(pattern)
    if found:
        return found.group(1).strip()
    fallback = doc.search(PROFILE_FALLBACK_RE, section_range)
    return fallback.group(1).strip() if fallback else None


# --------------------------------------------------------------------------------------
# BFP (AISC 358-16 Chapter 7) specification builder
# --------------------------------------------------------------------------------------

BEAM_SCOPE = ("1.1", "1.2")
COLUMN_SCOPE = ("1.2", "1.3")
PLATE_STEEL_SCOPE = ("1.3", "1.4")
FLANGE_BOLT_DIA_SCOPE = ("2.2", "2.3")
FLANGE_PLATE_THK_SCOPE = ("2.3", "2.4")
FLANGE_BOLT_GRADE_SCOPE = ("2.4", "2.5")
FLANGE_LAYOUT_SCOPE = ("2.6", "2.7")
FLANGE_COUNT_SCOPE = ("2.7", "2.8")
HINGE_SCOPE = ("2.8", "2.9")
WEB_BOLT_GRADE_SCOPE = ("2.17", "2.18")
WEB_BOLT_SCOPE = ("2.18", "2.19")
WEB_LAYOUT_SCOPE = ("2.19", "2.20")
WEB_PLATE_SCOPE = ("2.20", "2.21")
DOUBLER_SCOPE = ("5", "6")
CONTINUITY_SCOPE = ("6", "7")

REQUIRED_BFP_FIELDS = (
    "beam.profile",
    "beam.depth_mm",
    "column.profile",
    "flange_plate.thickness_mm",
    "flange_plate.width_mm",
    "flange_plate.length_mm",
    "flange_bolts.diameter_mm",
    "flange_bolts.count",
    "flange_bolts.pitch_mm",
    "shear_plate.thickness_mm",
    "shear_plate.length_mm",
    "shear_bolts.diameter_mm",
    "shear_bolts.count",
)


def _build_bfp_specification(doc: ParsedDocument, document_path: str) -> Dict[str, Any]:
    """Assemble the structured BFP engineering specification from the token stream."""
    warnings: List[str] = []

    plate_grade_match = doc.search(STEEL_GRADE_RE, PLATE_STEEL_SCOPE) or doc.search(STEEL_GRADE_RE)
    beam_grade_match = doc.search(STEEL_GRADE_RE, BEAM_SCOPE)
    column_grade_match = doc.search(STEEL_GRADE_RE, COLUMN_SCOPE)
    plate_material = _material_name(plate_grade_match.group(1) if plate_grade_match else None)

    beam = {
        "profile": _profile(doc, BEAM_PROFILE_RE, BEAM_SCOPE),
        "depth_mm": doc.length_mm("d", BEAM_SCOPE),
        "flange_width_mm": doc.length_mm("bbf", BEAM_SCOPE),
        "flange_thickness_mm": doc.length_mm("tbf", BEAM_SCOPE),
        "web_thickness_mm": doc.length_mm("tbw", BEAM_SCOPE),
        "fillet_radius_mm": doc.length_mm("rb", BEAM_SCOPE),
        "steel_grade": _normalize_grade(
            beam_grade_match.group(1) if beam_grade_match else None, "ASTM A36"
        ),
        "fy_mpa": doc.quantity("Fyb", BEAM_SCOPE, unit="MPa"),
        "fu_mpa": doc.quantity("Fub", BEAM_SCOPE, unit="MPa"),
        "model_role": "Beam",
    }
    column = {
        "profile": _profile(doc, COLUMN_PROFILE_RE, COLUMN_SCOPE),
        "depth_mm": doc.length_mm("dc", COLUMN_SCOPE),
        "flange_width_mm": doc.length_mm("bcf", COLUMN_SCOPE),
        "flange_thickness_mm": doc.length_mm("tcf", COLUMN_SCOPE),
        "web_thickness_mm": doc.length_mm("tcw", COLUMN_SCOPE),
        "fillet_radius_mm": doc.length_mm("rc", COLUMN_SCOPE),
        "steel_grade": _normalize_grade(
            column_grade_match.group(1) if column_grade_match else None, "ASTM A36"
        ),
        "fy_mpa": doc.quantity("Fyc", COLUMN_SCOPE, unit="MPa"),
        "fu_mpa": doc.quantity("Fuc", COLUMN_SCOPE, unit="MPa"),
        "model_role": "Column",
    }

    # --- flange plates and their field bolt groups -------------------------------------
    tp_flange = doc.length_mm("tp", FLANGE_PLATE_THK_SCOPE)
    bp_flange = doc.length_mm("bp", FLANGE_LAYOUT_SCOPE)
    gauge = doc.length_mm("g", FLANGE_LAYOUT_SCOPE)
    pitch = doc.length_mm("S", FLANGE_LAYOUT_SCOPE)
    edge_first_row = doc.length_mm("S1", FLANGE_LAYOUT_SCOPE)
    edge_end = doc.length_mm("S2", FLANGE_LAYOUT_SCOPE)
    erection_gap = doc.length_mm("S0", FLANGE_LAYOUT_SCOPE)
    edge_transverse = doc.length_mm("S4", FLANGE_LAYOUT_SCOPE)
    n_flange = doc.count("n", FLANGE_COUNT_SCOPE)
    db_flange = doc.length_mm("db", FLANGE_BOLT_DIA_SCOPE)
    hinge_distance = doc.length_mm("Sh", HINGE_SCOPE)

    rows_flange = int(n_flange // 2) if n_flange else None
    if hinge_distance is None and rows_flange and pitch is not None and edge_first_row is not None:
        hinge_distance = edge_first_row + pitch * (rows_flange - 1)
        warnings.append(
            "Plastic hinge distance Sh was not published; derived as S1 + S*(n/2 - 1)."
        )
    lp_flange = (
        hinge_distance + edge_end
        if hinge_distance is not None and edge_end is not None
        else None
    )

    flange_bolt_grade_match = doc.search(BOLT_GRADE_RE, FLANGE_BOLT_GRADE_SCOPE)
    web_bolt_grade_match = doc.search(BOLT_GRADE_RE, WEB_BOLT_GRADE_SCOPE)
    flange_bolt_grade = _normalize_grade(
        flange_bolt_grade_match.group(1) if flange_bolt_grade_match else None, "ASTM A325"
    )
    web_bolt_grade = _normalize_grade(
        web_bolt_grade_match.group(1) if web_bolt_grade_match else None, "ASTM A325"
    )

    # --- shear tab and its field bolt group --------------------------------------------
    tp_shear = doc.length_mm("tp_corte", WEB_PLATE_SCOPE)
    lp_shear = doc.length_mm("Lp", WEB_LAYOUT_SCOPE)
    bp_shear = doc.length_mm("bp", WEB_LAYOUT_SCOPE)
    pitch_shear = doc.length_mm("S", WEB_LAYOUT_SCOPE)
    edge_shear = doc.length_mm("S1", WEB_LAYOUT_SCOPE)
    offset_shear = doc.length_mm("S2", WEB_LAYOUT_SCOPE)
    n_shear = doc.count("n", WEB_BOLT_SCOPE)
    db_shear = doc.length_mm("db", WEB_BOLT_SCOPE)

    # --- stiffeners ---------------------------------------------------------------------
    tcp = doc.length_mm("tcp", CONTINUITY_SCOPE) or doc.length_mm("tcp_est", CONTINUITY_SCOPE)
    bcp = doc.length_mm("bcp", CONTINUITY_SCOPE)
    clip = doc.length_mm("Clip", CONTINUITY_SCOPE)
    stiffener_fillet = doc.length_mm("Dreq", CONTINUITY_SCOPE)
    tpa = doc.length_mm("tpa", DOUBLER_SCOPE)

    plates = [
        {
            "name": "Top Flange Plate",
            "model_role": "Flange Plate",
            "thickness_mm": tp_flange,
            "width_mm": bp_flange,
            "length_mm": lp_flange,
            "material": plate_material,
            "position": "Above beam top flange, welded to column flange",
        },
        {
            "name": "Bottom Flange Plate",
            "model_role": "Flange Plate",
            "thickness_mm": tp_flange,
            "width_mm": bp_flange,
            "length_mm": lp_flange,
            "material": plate_material,
            "position": "Below beam bottom flange, welded to column flange",
        },
        {
            "name": "Shear Tab Plate",
            "model_role": "Shear Plate",
            "thickness_mm": tp_shear,
            "width_mm": bp_shear,
            "length_mm": lp_shear,
            "material": plate_material,
            "position": "Single plate at beam web, welded to column flange",
        },
    ]

    flange_bolt_common = {
        "standard": flange_bolt_grade,
        "diameter_mm": db_flange,
        "count": n_flange,
        "rows": rows_flange,
        "columns": 2 if n_flange else None,
        "pitch_mm": pitch,
        "gauge_mm": gauge,
        "edge_distance_end_mm": edge_end,
        "edge_distance_first_row_mm": edge_first_row,
        "edge_distance_transverse_mm": edge_transverse,
        "is_site_bolt": True,
    }
    bolt_groups = [
        dict(
            flange_bolt_common,
            name="Top Flange Bolt Group",
            connects=["Top Flange Plate", "Beam top flange"],
        ),
        dict(
            flange_bolt_common,
            name="Bottom Flange Bolt Group",
            connects=["Bottom Flange Plate", "Beam bottom flange"],
        ),
        {
            "name": "Shear Tab Bolt Group",
            "standard": web_bolt_grade,
            "diameter_mm": db_shear,
            "count": n_shear,
            "rows": n_shear,
            "columns": 1 if n_shear else None,
            "pitch_mm": pitch_shear,
            "gauge_mm": 0.0,
            "edge_distance_end_mm": edge_shear,
            "edge_distance_first_row_mm": edge_shear,
            "edge_distance_transverse_mm": offset_shear,
            "is_site_bolt": True,
            "connects": ["Shear Tab Plate", "Beam web"],
        },
    ]

    welds = [
        {
            "name": "Top Flange Plate to Column Flange",
            "weld_type": "Butt",
            "preparation": "CJP",
            "throat_thickness_mm": tp_flange,
            "location": "kInShop",
            "demand_critical": True,
            "main_part": "Column",
            "attached_part": "Top Flange Plate",
            "note": "Complete joint penetration groove weld; backing bar must be removed.",
        },
        {
            "name": "Bottom Flange Plate to Column Flange",
            "weld_type": "Butt",
            "preparation": "CJP",
            "throat_thickness_mm": tp_flange,
            "location": "kInShop",
            "demand_critical": True,
            "main_part": "Column",
            "attached_part": "Bottom Flange Plate",
            "note": "Complete joint penetration groove weld; backing bar must be removed.",
        },
        {
            "name": "Shear Tab to Column Flange",
            "weld_type": "Butt",
            "preparation": "CJP",
            "throat_thickness_mm": tp_shear,
            "location": "kInShop",
            "demand_critical": False,
            "main_part": "Column",
            "attached_part": "Shear Tab Plate",
            "alternatives": ["PJP", "DoubleFillet"],
            "note": "CJP or PJP groove weld; double fillet welds allowed if sized for the "
            "maximum probable shear demand.",
        },
    ]

    stiffeners: List[Dict[str, Any]] = []
    if tcp is not None:
        for position in ("Top", "Bottom"):
            stiffeners.append(
                {
                    "name": "%s Continuity Plate" % position,
                    "type": "Continuity Plate",
                    "model_role": "Stiffener",
                    "thickness_mm": tcp,
                    "width_mm": bcp,
                    "corner_clip_mm": clip,
                    "material": plate_material,
                    "host": "Column",
                    "alignment": "Aligned with the %s flange plate" % position.lower(),
                }
            )
        welds.append(
            {
                "name": "Continuity Plates to Column",
                "weld_type": "DoubleFillet",
                "preparation": "Fillet both sides",
                "throat_thickness_mm": stiffener_fillet,
                "location": "kInShop",
                "demand_critical": False,
                "main_part": "Column",
                "attached_part": "Continuity Plate",
                "note": "Required fillet leg from the continuity plate weld design.",
            }
        )
    if tpa is not None:
        stiffeners.append(
            {
                "name": "Web Doubler Plate",
                "type": "Doubler Plate",
                "model_role": "Stiffener",
                "thickness_mm": tpa,
                "width_mm": None,
                "corner_clip_mm": None,
                "material": plate_material,
                "host": "Column",
                "alignment": "Column panel zone, welded to the column web",
            }
        )
        warnings.append(
            "Panel zone doubler plate t=%.1f mm detected; it is reported but not modelled "
            "because the report does not publish its contour extents." % tpa
        )

    return {
        "document": {
            "path": document_path,
            "file_name": os.path.basename(document_path),
            "page_count": doc.page_count,
            "connection_type": "BFP",
            "connection_description": SUPPORTED_CONNECTION_TYPES["BFP"],
            "source_standard": "ANSI/AISC 358-16",
        },
        "members": {"beam": beam, "column": column},
        "plates": plates,
        "bolt_groups": bolt_groups,
        "welds": welds,
        "stiffeners": stiffeners,
        "geometry": {
            "erection_gap_mm": erection_gap,
            "plastic_hinge_distance_mm": hinge_distance,
            "flange_plate_length_mm": lp_flange,
            "shear_plate_vertical_offset_mm": (
                (beam["depth_mm"] - lp_shear) / 2.0
                if beam["depth_mm"] is not None and lp_shear is not None
                else None
            ),
            "shear_bolt_offset_from_column_mm": offset_shear,
            "coordinate_system": (
                "Beam local WCS: X along the beam axis from the column face, Y transverse "
                "to the beam, Z from the underside of the beam bottom flange."
            ),
        },
        "materials": {"plates": plate_material},
        "warnings": warnings,
    }


def _missing_fields(spec: Dict[str, Any]) -> List[str]:
    lookup = {
        "beam.profile": spec["members"]["beam"]["profile"],
        "beam.depth_mm": spec["members"]["beam"]["depth_mm"],
        "column.profile": spec["members"]["column"]["profile"],
        "flange_plate.thickness_mm": spec["plates"][0]["thickness_mm"],
        "flange_plate.width_mm": spec["plates"][0]["width_mm"],
        "flange_plate.length_mm": spec["plates"][0]["length_mm"],
        "flange_bolts.diameter_mm": spec["bolt_groups"][0]["diameter_mm"],
        "flange_bolts.count": spec["bolt_groups"][0]["count"],
        "flange_bolts.pitch_mm": spec["bolt_groups"][0]["pitch_mm"],
        "shear_plate.thickness_mm": spec["plates"][2]["thickness_mm"],
        "shear_plate.length_mm": spec["plates"][2]["length_mm"],
        "shear_bolts.diameter_mm": spec["bolt_groups"][2]["diameter_mm"],
        "shear_bolts.count": spec["bolt_groups"][2]["count"],
    }
    return [field for field in REQUIRED_BFP_FIELDS if not lookup.get(field)]


# --------------------------------------------------------------------------------------
# Advance Steel detailing payload
# --------------------------------------------------------------------------------------

def _rect_xy(x0, x1, y0, y1, z, offset) -> List[List[float]]:
    """Horizontal rectangular contour (plate normal along Z)."""
    ox, oy, oz = offset
    return [
        [x0 + ox, y0 + oy, z + oz],
        [x1 + ox, y0 + oy, z + oz],
        [x1 + ox, y1 + oy, z + oz],
        [x0 + ox, y1 + oy, z + oz],
    ]


def _rect_xz(x0, x1, z0, z1, y, offset) -> List[List[float]]:
    """Vertical rectangular contour (plate normal along Y)."""
    ox, oy, oz = offset
    return [
        [x0 + ox, y + oy, z0 + oz],
        [x1 + ox, y + oy, z0 + oz],
        [x1 + ox, y + oy, z1 + oz],
        [x0 + ox, y + oy, z1 + oz],
    ]


def build_detailing_payload(
    specification: Dict[str, Any],
    beam_handle: str = "BEAM_01",
    column_handle: str = "COL_01",
    origin_offset: Sequence[float] = (0.0, 0.0, 0.0),
) -> Dict[str, Any]:
    """Convert a parsed specification into the ``model_engineered_connection`` payload.

    Coordinates use the beam local WCS of CONTRACT-012: X runs along the beam axis from
    the column face, Y is transverse to the beam and Z starts at the underside of the beam
    bottom flange.
    """
    offset = tuple(float(v) for v in origin_offset)
    beam = specification["members"]["beam"]
    column = specification["members"]["column"]
    geometry = specification["geometry"]
    material = specification["materials"]["plates"]

    d = beam["depth_mm"]
    top_plate, bottom_plate, shear_plate = specification["plates"][:3]
    top_bolts, bottom_bolts, shear_bolts = specification["bolt_groups"][:3]

    tp = top_plate["thickness_mm"]
    bp = top_plate["width_mm"]
    lp = top_plate["length_mm"]
    half_bp = bp / 2.0

    plates: List[Dict[str, Any]] = [
        {
            "name": top_plate["name"],
            "thickness_mm": tp,
            "contour_points": _rect_xy(0.0, lp, -half_bp, half_bp, d, offset),
            "material": material,
            "model_role": top_plate["model_role"],
        },
        {
            "name": bottom_plate["name"],
            "thickness_mm": tp,
            "contour_points": _rect_xy(0.0, lp, -half_bp, half_bp, -tp, offset),
            "material": material,
            "model_role": bottom_plate["model_role"],
        },
    ]

    lp_shear = shear_plate["length_mm"]
    bp_shear = shear_plate["width_mm"]
    z_bottom = geometry.get("shear_plate_vertical_offset_mm")
    if z_bottom is None:
        z_bottom = (d - lp_shear) / 2.0
    plates.append(
        {
            "name": shear_plate["name"],
            "thickness_mm": shear_plate["thickness_mm"],
            "contour_points": _rect_xz(0.0, bp_shear, z_bottom, z_bottom + lp_shear, 0.0, offset),
            "material": material,
            "model_role": shear_plate["model_role"],
        }
    )

    # Continuity stiffeners sit inside the column, between its flanges, centred on the
    # mid-thickness of each flange plate (AISC 358-16 force transfer path).
    for stiffener in specification["stiffeners"]:
        if stiffener["type"] != "Continuity Plate" or column["depth_mm"] is None:
            continue
        tcp = stiffener["thickness_mm"]
        bcp = stiffener["width_mm"] or column["flange_width_mm"]
        tcf = column["flange_thickness_mm"] or 0.0
        x_inner = -(column["depth_mm"] - tcf)
        x_outer = -tcf
        z_center = d + tp / 2.0 if stiffener["name"].startswith("Top") else -tp / 2.0
        plates.append(
            {
                "name": stiffener["name"],
                "thickness_mm": tcp,
                "contour_points": _rect_xy(
                    x_inner, x_outer, -bcp / 2.0, bcp / 2.0, z_center - tcp / 2.0, offset
                ),
                "material": stiffener["material"],
                "model_role": "Stiffener",
            }
        )

    first_row = top_bolts["edge_distance_first_row_mm"]
    pitch = top_bolts["pitch_mm"]
    rows = top_bolts["rows"]
    x_center = first_row + pitch * (rows - 1) / 2.0

    bolt_groups = [
        {
            "bolt_standard": top_bolts["standard"],
            "bolt_diameter_mm": top_bolts["diameter_mm"],
            "origin": [x_center + offset[0], offset[1], d + offset[2]],
            "normal": [0.0, 0.0, 1.0],
            "nx": rows,
            "ny": top_bolts["columns"],
            "dx": pitch,
            "dy": top_bolts["gauge_mm"],
            "is_site_bolt": top_bolts["is_site_bolt"],
            "connected_part_handles": [top_plate["name"], beam_handle],
        },
        {
            "bolt_standard": bottom_bolts["standard"],
            "bolt_diameter_mm": bottom_bolts["diameter_mm"],
            "origin": [x_center + offset[0], offset[1], offset[2]],
            "normal": [0.0, 0.0, 1.0],
            "nx": rows,
            "ny": bottom_bolts["columns"],
            "dx": pitch,
            "dy": bottom_bolts["gauge_mm"],
            "is_site_bolt": bottom_bolts["is_site_bolt"],
            "connected_part_handles": [bottom_plate["name"], beam_handle],
        },
        {
            "bolt_standard": shear_bolts["standard"],
            "bolt_diameter_mm": shear_bolts["diameter_mm"],
            "origin": [
                shear_bolts["edge_distance_transverse_mm"] + offset[0],
                offset[1],
                z_bottom + lp_shear / 2.0 + offset[2],
            ],
            # Web bolts are drilled through the beam web: the pattern plane is X-Z, so
            # the single vertical line of bolts runs along the second local axis.
            "normal": [0.0, 1.0, 0.0],
            "nx": shear_bolts["columns"],
            "ny": shear_bolts["rows"],
            "dx": shear_bolts["gauge_mm"],
            "dy": shear_bolts["pitch_mm"],
            "is_site_bolt": shear_bolts["is_site_bolt"],
            "connected_part_handles": [shear_plate["name"], beam_handle],
        },
    ]

    continuity_names = [
        s["name"] for s in specification["stiffeners"] if s["type"] == "Continuity Plate"
    ]
    shop_welds = []
    for weld in specification["welds"]:
        attached = weld["attached_part"]
        targets = continuity_names if attached == "Continuity Plate" else [attached]
        for target in targets:
            shop_welds.append(
                {
                    "throat_thickness_mm": weld["throat_thickness_mm"],
                    "main_part_handle": column_handle,
                    "attached_part_name": target,
                    "weld_type": weld["weld_type"],
                    "location": "kInShop",
                    "demand_critical": weld["demand_critical"],
                }
            )

    connection_name = "%s %s %s-%s" % (
        specification["document"]["connection_type"],
        specification["document"]["source_standard"],
        beam["profile"],
        column["profile"],
    )
    return {
        "connection_name": connection_name,
        "source_system": "%s (%s)"
        % (
            specification["document"]["connection_description"],
            specification["document"]["file_name"],
        ),
        "plates": plates,
        "bolt_groups": bolt_groups,
        "shop_welds": shop_welds,
        "verify_assembly": True,
    }


# --------------------------------------------------------------------------------------
# Public entry point
# --------------------------------------------------------------------------------------

def parse_connection_pdf(
    document_path: str,
    connection_type: Optional[str] = None,
    beam_handle: str = "BEAM_01",
    column_handle: str = "COL_01",
    origin_offset: Sequence[float] = (0.0, 0.0, 0.0),
) -> Dict[str, Any]:
    """Parse an engineering connection report and build its detailing payload.

    Returns the project standard envelope ``{"success", "data", "error"}`` where ``data``
    holds ``specification`` (structured engineering data) and ``detailing_payload``
    (ready for ``model_engineered_connection``).
    """
    if not document_path:
        return _err(
            "DOCUMENT_NOT_FOUND",
            "No document path was provided.",
            suggestion="Pass the absolute path of the connection calculation report (.pdf).",
        )
    if not os.path.isfile(document_path):
        return _err(
            "DOCUMENT_NOT_FOUND",
            "Document not found: %s" % document_path,
            details="The path does not exist or is not a file.",
            suggestion="Verify the absolute path; OneDrive paths must be synced locally "
            "(not online-only placeholders).",
        )
    if os.path.splitext(document_path)[1].lower() != ".pdf":
        return _err(
            "UNSUPPORTED_FORMAT",
            "Only PDF calculation reports are supported by this ingestor.",
            details="Received: %s" % os.path.basename(document_path),
            suggestion="Export the report to PDF, or use the DXF ingestor for 2D details.",
        )
    if not PYMUPDF_AVAILABLE:
        return _err(
            "PDF_BACKEND_MISSING",
            "PyMuPDF (fitz) is required to read connection PDF reports.",
            suggestion="Install it with: pip install pymupdf",
        )

    try:
        lines = extract_pdf_lines(document_path)
    except Exception as exc:  # noqa: BLE001 - surface any backend failure cleanly
        return _err(
            "PDF_READ_ERROR",
            "PyMuPDF could not read the document.",
            details="%s: %s" % (type(exc).__name__, exc),
            suggestion="Confirm the file is a valid, non password protected PDF.",
        )

    doc = ParsedDocument(lines, path=document_path)
    if len("".join(text.strip() for _, text in doc.lines)) < 200:
        return _err(
            "NO_TEXT_LAYER",
            "The PDF has no extractable text layer (probably a scanned document).",
            details="%d page(s) produced almost no characters." % doc.page_count,
            suggestion="Run OCR over the report (e.g. ocrmypdf) and ingest the searchable PDF.",
        )

    detected = detect_connection_type(doc.text)
    requested = connection_type.strip().upper() if connection_type else None
    resolved = requested or detected
    if resolved is None:
        return _err(
            "UNSUPPORTED_CONNECTION_TYPE",
            "Could not identify the prequalified connection family in the document.",
            details="No BFP / end-plate / RBS signature found in %d page(s)." % doc.page_count,
            suggestion="Pass connection_type explicitly, e.g. connection_type='BFP'. "
            "Supported: %s." % ", ".join(sorted(SUPPORTED_CONNECTION_TYPES)),
        )
    if resolved not in SUPPORTED_CONNECTION_TYPES:
        return _err(
            "UNSUPPORTED_CONNECTION_TYPE",
            "Connection type '%s' is not implemented yet." % resolved,
            details="Type detected in the document: %s." % (detected or "none"),
            suggestion="Supported types: %s. Model other families by calling "
            "model_engineered_connection with an explicit payload."
            % ", ".join(sorted(SUPPORTED_CONNECTION_TYPES)),
        )

    specification = _build_bfp_specification(doc, document_path)
    if requested and detected and requested != detected:
        specification["warnings"].append(
            "Requested connection_type '%s' overrides the type detected in the document "
            "('%s')." % (requested, detected)
        )

    missing = _missing_fields(specification)
    if missing:
        return _err(
            "INCOMPLETE_SPECIFICATION",
            "The document was read but mandatory connection values are missing.",
            details="Missing: %s" % ", ".join(missing),
            suggestion="Check that the report follows the AISC 358-16 BFP calculation "
            "layout, or supply the missing values through model_engineered_connection.",
        )

    payload = build_detailing_payload(
        specification,
        beam_handle=beam_handle,
        column_handle=column_handle,
        origin_offset=origin_offset,
    )
    return {
        "success": True,
        "data": {"specification": specification, "detailing_payload": payload},
        "error": None,
    }


def default_sample_document() -> Optional[str]:
    """Locate the reference AISC 358-16 BFP report used by the test-suite."""
    repo_root = os.path.dirname(
        os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    )
    sample = "IT_Ejemplo_Conexion_BFP_AISC 358-16_s2-20.pdf"
    candidates = [
        os.environ.get(_DEFAULT_SAMPLE_ENV, ""),
        os.path.join(
            os.path.expanduser("~"),
            "OneDrive",
            "EDDC",
            "ECA-0126",
            "Tareas",
            "Tarea N5 - Modelado y Detallado Conexion Bolted Flange Plate - Entrega 05-10-26",
            sample,
        ),
        os.path.join(repo_root, "examples", sample),
        os.path.join(repo_root, "docs", "references", sample),
    ]
    for candidate in candidates:
        if candidate and os.path.isfile(candidate):
            return candidate
    return None
