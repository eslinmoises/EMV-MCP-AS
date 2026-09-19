# Agentic Contract: CONTRACT-007-BILL-OF-MATERIALS-BOM

- **Assigned Worker**: **Antigravity (Lead Director / Worker)**
- **Director / Supervisor**: Antigravity (Lead Director)
- **Status**: **ACCEPTED**
- **Date Created**: 2026-09-19
- **Target Components**: `src/as_plugin/Commands`, `src/mcp_server/tools`, `tests/mcp`

---

## 1. Context & Objective

Autonomous detailing agents and structural engineers require accurate procurement and estimation data directly from the 3D model:
1. **`get_bill_of_materials`** (`POST /api/v1/production/bom`):
   - Computes structural steel takeoffs (cutting lists, plate schedules, bolt counts).
   - Aggregates weights in kilograms and metric tonnes.
   - Calculates paint coating surface areas ($m^2$) for protective treatment specification.
   - Allows grouping by profile, assembly mark, or material.
   - Operates on the full model or a subset of element handles.

---

## 2. Reference Specifications & Rules

- [`docs/specs/001-architecture-and-ipc.md`](../../docs/specs/001-architecture-and-ipc.md)
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md) §7
- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md) §4
- [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md)
- [`rules/transaction-safety.md`](../../rules/transaction-safety.md)

---

## 3. Strict Permitted Scope (File Whitelist)

The worker is **ONLY** permitted to modify or create:
- `src/as_plugin/Commands/Handlers/BomCommandHandler.cs` (new)
- `src/as_plugin/Commands/CommandDispatcher.cs` (route registration)
- `src/mcp_server/tools/production_tools.py`
- `src/mcp_server/server.py`
- `tests/mocks/mock_as_plugin.py`
- `tests/mcp/test_production_tools.py`
- `tests/mcp/test_tools.py`

---

## 4. Implementation Guidelines

### A. C# BOM Handler (`BomCommandHandler.cs`)
- Endpoint: `POST /api/v1/production/bom`
- Inputs: `element_handles` (optional), `group_by` (optional, default `"profile"`).
- Scan model elements via `AsQuery.ModelObjectIds(eObjectType.kAtomicElem)` or filter by `element_handles`.
- For each `AtomicElement`:
  - If `Beam` (StraightBeam or PolyBeam): extract section name, material, length, weight, coating area.
  - If `Plate`: extract thickness, contour area, material, weight.
  - If `BoltPattern`: extract standard, grade, diameter, bolt count.
- Calculate totals: `total_weight_kg`, `total_tonnage` (`total_weight_kg / 1000.0`), `total_coating_area_m2`.
- Zero transactions required (Read-Only query using `AsQuery`).

### B. Python FastMCP Tool (`production_tools.py`)
- Define `get_bill_of_materials(client, element_handles=None, group_by="profile") -> Dict[str, Any]`.
- Expose via `@mcp.tool()` in `server.py`.

---

## 5. Mandatory Verification Commands

```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
python -m pytest tests/ -q
```

---

## 6. Definition of Done (DoD)
- [x] `BomCommandHandler.cs` implemented with read-only AsQuery helpers and zero transaction locks.
- [x] `production/bom` registered in `KnownRoutes` and `Route` switch in `CommandDispatcher.cs`.
- [x] FastMCP tool `get_bill_of_materials` exposed in `server.py`.
- [x] Mock server implements `POST /api/v1/production/bom` matching SPEC-002 §7.
- [x] Unit tests pass covering model-wide BOM and filtered BOM by handles.
- [x] `dotnet build -c Release` compiles with 0 errors.
- [x] `pytest tests/` passes cleanly (41/41 passing).

