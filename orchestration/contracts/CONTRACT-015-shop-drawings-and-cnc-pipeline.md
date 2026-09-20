# CONTRACT-015: Automated 2D Shop Drawings & CNC Fabrication Pipeline

- **Contract ID**: CONTRACT-015
- **Status**: ACTIVE
- **Assigned Worker Agent**: Codex CLI / Claude Code CLI
- **Supervising Architect**: Antigravity (Lead Director)
- **Spec Reference**: `docs/specs/004-mcp-tools-specification.md` §13, `docs/specs/002-data-models-and-schemas.md` §15

---

## 1. Context & Motivation

Once a 3D structural model or joint is detailed and numbered, the final deliverables for fabrication are:
1. **2D Shop Drawings (Assembly & Single Part Drawings)**: Dimensioned drawing sheets with bills of materials, weld symbols, and title blocks.
2. **DSTV / NC1 Files**: Standard Numerical Control files for automated sawing, drilling, and thermal cutting machines (Ficep, Peddinghaus, Voortman).

Advance Steel includes native engines (`DrawingProcessManager`, `NcDstvExport`) to produce these artifacts.

`CONTRACT-015` formalizes and exposes this pipeline through MCP:
- `generate_shop_drawings(assembly_handles, drawing_style, sheet_size)`: Invokes drawing creation processes within document locks.
- `export_dstv_nc_files(element_handles, output_directory)`: Produces valid `.nc` and `.dxf` files for CNC production.
- `get_drawing_status(assembly_handle)`: Checks if drawing sheets are up to date, pending update, or missing.

---

## 2. Whitelist of Files

```text
src/as_plugin/Commands/Handlers/DrawingProductionHandler.cs
src/as_plugin/Commands/CommandDispatcher.cs
src/mcp_server/tools/production_tools.py
src/mcp_server/server.py
tests/mocks/mock_as_plugin.py
tests/mcp/test_drawing_production.py
```

---

## 3. Scope & Requirements

1. **Advance Steel Drawing Execution (`DrawingProductionHandler.cs`)**:
   - Guarded by single-thread `DocumentLock` and AutoCAD/Advance Steel transaction.
   - Triggers `DrawingProcess` or calls native command strings via `doc.SendStringToExecute`.
   - Handles both single parts (`kSinglePart`) and assemblies (`kMainPart`).
2. **MCP Tool Integration**:
   - `generate_shop_drawings`: Creates drawings and registers drawing numbers.
   - `export_dstv_nc_files`: Ensures numbering has been run first, then produces NC1 files.
3. **Automated Testing**:
   - Unit tests against `mock_as_plugin.py` verifying status reporting, error handling for unnumbered models, and drawing record generation.

---

## 4. Acceptance Criteria

1. 100% test pass rate in pytest (`tests/mcp/test_drawing_production.py`).
2. Robust error handling when drawing templates or prototypes are missing.
3. Preserves AutoCAD/Advance Steel single-threaded stability.
