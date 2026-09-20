# CONTRACT-013: Connection Specs Ingestor from Engineering Documents (PDF/DXF)

- **Contract ID**: CONTRACT-013
- **Status**: ACTIVE
- **Assigned Worker Agent**: Claude Code CLI
- **Supervising Architect**: Antigravity (Lead Director)
- **Spec Reference**: `docs/specs/004-mcp-tools-specification.md` §11, `docs/specs/002-data-models-and-schemas.md` §13

---

## 1. Context & Motivation

In current practice, engineers provide calculation reports in PDF format (e.g. AISC 358-16 Bolted Flange Plate, Extended End-Plate, IDEA StatiCa Connection reports) or 2D DXF connection details.

While `CONTRACT-012` enabled the engine to model 3D fabrication connections from structured JSON payloads, human or agent effort was previously needed to manually read the PDF, extract plate thicknesses, bolt patterns, weld sizes, and compute local Advance Steel WCS coordinates.

`CONTRACT-013` closes this gap by providing an automated ingestor tool:
`ingest_connection_from_document(document_path: str, connection_type: Optional[str] = None)`
which parses the document using PyMuPDF / regex / layout analysis, synthesizes the complete connection detailing payload, and feeds it directly into `model_engineered_connection`.

---

## 2. Whitelist of Files

```text
src/mcp_server/parsers/__init__.py
src/mcp_server/parsers/pdf_connection_parser.py
src/mcp_server/tools/ingest_tools.py
src/mcp_server/server.py
tests/mcp/test_pdf_connection_parser.py
```

---

## 3. Scope & Requirements

1. **Document Extraction Engine (`pdf_connection_parser.py`)**:
   - Ingest PDF documents (such as `IT_Ejemplo_Conexion_BFP_AISC 358-16_s2-20.pdf`).
   - Extract:
     - Primary Member Profiles (e.g., Column: HEB-400 / W14x90, Beam: IPE-360 / W18x50).
     - Flange Plates / End Plates dimensions: thickness ($t_p$), width ($b_p$), length ($L_p$).
     - Shear Tab dimensions: thickness, width, length.
     - Stiffeners: thickness, position (continuity stiffeners aligned with flanges).
     - Bolts: standard (e.g., ASTM A325, DIN 931), diameter ($d_b$), count, longitudinal pitch ($s$), transverse gauge ($g$).
     - Welds: throat thickness ($a$ or $t_w$), type (CJP Butt, Fillet), location (`kInShop`).
2. **MCP Tool Integration (`ingest_tools.py`)**:
   - Provide `ingest_connection_from_document(client, document_path, connection_type)`.
   - Returns both the parsed structured engineering specification and the execution payload ready for Advance Steel.
3. **Automated Testing**:
   - `test_pdf_connection_parser.py` validating exact extraction against the AISC 358-16 BFP sample document.

---

## 4. Acceptance Criteria

1. 100% test pass rate in pytest (`tests/mcp/test_pdf_connection_parser.py`).
2. No modification to Advance Steel native single-thread laws.
3. Clean fallback when unparseable documents are provided, reporting informative error messages with suggestions.
