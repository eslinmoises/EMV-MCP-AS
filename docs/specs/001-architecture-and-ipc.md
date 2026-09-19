# Specification 001: System Architecture and IPC Protocol

- **Spec ID**: SPEC-001
- **Status**: APPROVED
- **Target Components**: `src/as_plugin`, `src/mcp_server`

---

## 0. Project Vision & Strategic Objectives

The primary mission of **EMV-MCP-AS** is to empower AI agents (Claude Code, Antigravity 2.0 / IDE, Cursor, Codex, ChatGPT) to perform production-grade structural detailing and parametric engineering inside Autodesk Advance Steel.

### Core Objectives:
1. **Automated Workshop Connection Modeling from Engineering Sources**:
   - Enable agents to ingest connection calculations and detailing sheets from **PDF calculation reports** (e.g. AISC 358-16 Pre-qualified Connections such as Bolted Flange Plate - BFP), **IDEA StatiCa Connection**, **RAM Connection**, and **AutoCAD DXF**.
   - Model the exact geometry: beams, columns, top/bottom flange plates, shear tabs, continuity stiffeners, web doubler plates, bolt groups, and edge preparations.
   - Place all **workshop welds (`kInShop`)** in the exact contact surfaces so Advance Steel merges attached plates into the main member's fabrication assembly (`MainPart`), eliminating orphan single parts and ensuring accurate workshop drawings (`PosNum`, Single Part Marks, Assembly Marks).
2. **Generative Parametric Structures**:
   - One-shot generation of complex lattice warehouses and portal frames parameterized from real-world fabrication models (`version1.dwg`).
3. **Spatial Referencing & BIM Synchronization**:
   - Parametric 3D structural grids (`Grid1D`) and elevation levels (`LevelObject`) for 2D GA drawings and Revit BIM alignment.
4. **Detailing Doctor & Quality Assurance**:
   - Automated detection and repair of detached plates, weld inconsistencies, clash detection, and unnumbered elements.
5. **Universal Multi-Client MCP Support**:
   - Native integration across Antigravity, Claude Code, Cursor, Codex, and ChatGPT Desktop.

```text
+-----------------------+               +-----------------------------------+
|  Python FastMCP Host  |               |  Advance Steel Process (acad.exe) |
|  - MCP Stdio Transport|               |  - Advance Steel 2025 / 2026      |
|  - JSON-RPC Interface |               |  - .NET 8 Runtime                 |
|  - httpx Client       |               |  - EMV-AdvanceSteel.bundle        |
+-----------+-----------+               +-----------------+-----------------+
            |                                             ^
            | HTTP/1.1 POST/GET                           |
            | http://127.0.0.1:5055/api/v1/...            |
            +---------------------------------------------+
```

## 2. IPC Wire Protocol
- **Transport**: HTTP/1.1 REST over Loopback (`127.0.0.1`).
- **Port**: `5055` (configurable via `AS_MCP_PORT` env var).
- **Encoding**: UTF-8 JSON (`application/json`).
- **Timeout**: Default 30,000 ms per request.

## 3. Standard JSON Response Envelope

### Success Envelope
```json
{
  "success": true,
  "data": {},
  "error": null,
  "execution_time_ms": 24
}
```

### Error Envelope
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "ERROR_CODE_ENUM",
    "message": "Human readable message",
    "details": "Technical or stack information",
    "suggestion": "Corrective action for the LLM agent"
  },
  "execution_time_ms": 12
}
```

## 4. Standard Healthcheck Endpoint
- **URL**: `GET /api/v1/health`
- **Response Data**:
  ```json
  {
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
  ```
