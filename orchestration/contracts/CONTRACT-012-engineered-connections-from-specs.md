# CONTRACT-012: Engineered Connections from Calculation Reports & Drawings

- **Contract ID**: CONTRACT-012
- **Status**: ACTIVE
- **Assigned Worker Agent**: Lead Director / Claude Code
- **Spec Reference**: `docs/specs/004-mcp-tools-specification.md` §10, `docs/specs/002-data-models-and-schemas.md` §12

---

## 1. Context & Motivation

Engineers and detailers frequently receive structural connection designs from:
- Finite element connection design software: **IDEA StatiCa Connection**, **RAM Connection**
- 2D engineering drawings: **AutoCAD DXF/DWG**
- Engineering calculation reports / PDF detail sheets.

Modeling these connections manually in Advance Steel is error-prone:
- Plates might be placed without shop welds, leaving them as "orphaned" loose items.
- Assembly main parts are often not designated, resulting in incorrect piece marks (`?` or unnumbered).
- Welds set to `kOnSite` instead of `kInShop` fail to bond plates to beams/columns for shop fabrication drawings.

This contract provides the high-fidelity `model_engineered_connection` endpoint (`POST /api/v1/elements/engineered-joint`) and MCP tool, allowing AI agents to consume structured connection specifications extracted from PDFs, reports, or CAD, and construct the complete assembly in Advance Steel with verified workshop welds.

---

## 2. API Endpoint & Data Model

### `POST /api/v1/elements/engineered-joint`

#### Key Workflow:
1. **Geometric Preparations**: Performs member end shortening, notches, or cuts on incoming members.
2. **Plates Creation**: Creates plates (`Plate`) with specified contour points, thickness, material, and model role (`EndPlate`, `Stiffener`, `BasePlate`, `FinPlate`, `GussetPlate`).
3. **Fasteners**: Installs `BoltPattern` with given standard, diameter, and hole clearance through connected parts.
4. **Workshop Welds (`WeldPattern`)**:
   - Critically sets `AssemblyLocation = eAssemblyLocation.kInShop`.
   - Connects plate to the assembly main part (beam or column).
   - Advance Steel's connection graph then unites them into a single physical fabrication mark.
5. **Assembly Integrity Verification**:
   - Verifies `AtomicElement.IsMainPart` is true for the primary member.
   - Verifies all attached plates appear in `GetConnectedObjects(..., kInShop)`.

---

## 3. Whitelist of Files

```text
src/as_plugin/Commands/Handlers/EngineeredJointCommandHandler.cs
src/as_plugin/Commands/CommandDispatcher.cs
src/mcp_server/tools/modeling_tools.py
src/mcp_server/server.py
tests/mocks/mock_as_plugin.py
tests/mcp/test_engineered_joint.py
```

---

## 4. Acceptance Criteria

1. Endpoint `POST /api/v1/elements/engineered-joint` registered in `CommandDispatcher.cs`.
2. FastMCP tool `model_engineered_connection` exposed in `server.py` and `modeling_tools.py`.
3. Offline mock server replies with valid payload matching SPEC-002 §12.
4. Pytest suite passes: `pytest tests/mcp/test_engineered_joint.py -v`.
5. C# compiles with 0 errors: `dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release`.
