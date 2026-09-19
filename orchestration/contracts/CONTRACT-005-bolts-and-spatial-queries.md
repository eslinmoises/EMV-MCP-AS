# Agentic Contract: CONTRACT-005-BOLTS-AND-SPATIAL-QUERIES

- **Assigned Worker**: **Claude Code (Terminal CLI) / Antigravity**
- **Director / Supervisor**: Antigravity (Lead Director)
- **Status**: **ACCEPTED**
- **Date Created**: 2026-09-19
- **Target Components**: `src/as_plugin/Commands`, `src/mcp_server/tools`

---

## 1. Context & Objective

Structural steel connection detailing and node verification require:
1. **`query_elements_in_box`** (`GET /api/v1/spatial/box`): Query elements located within a 3D bounding box `[min_point, max_point]` to inspect connection nodes (e.g. beam-column junctions).
2. **`create_bolt_pattern`** (`POST /api/v1/elements/bolt`): Rectangular bolt pattern generation connecting two or more parts (`BoltPattern`), setting bolt diameter, standard (DIN 931, ISO 4014), steel grade, count, and spacing.
3. **`create_poly_beam`** (`POST /api/v1/elements/poly-beam`): Multi-segment continuous polybeam or curved beam creation (`PolyBeam`).

---

## 2. Reference Specifications & Rules

- [`docs/specs/001-architecture-and-ipc.md`](../../docs/specs/001-architecture-and-ipc.md)
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md) §5
- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md) §1 & §2
- [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md)
- [`rules/transaction-safety.md`](../../rules/transaction-safety.md)

---

## 3. Strict Permitted Scope (File Whitelist)

The worker is **ONLY** permitted to modify or create:
- `src/as_plugin/Commands/Handlers/BoltCommandHandler.cs` (new)
- `src/as_plugin/Commands/Handlers/PolyBeamCommandHandler.cs` (new)
- `src/as_plugin/Commands/Handlers/SpatialCommandHandler.cs` (add Box query)
- `src/as_plugin/Commands/CommandDispatcher.cs` (route registration)
- `src/mcp_server/tools/diagnostic_tools.py`
- `src/mcp_server/tools/modeling_tools.py`
- `src/mcp_server/server.py`
- `tests/mocks/mock_as_plugin.py`
- `tests/mcp/test_tools.py`

---

## 4. Implementation Guidelines

### A. Spatial Box Query (`SpatialCommandHandler.cs`)
- Endpoint: `GET /api/v1/spatial/box?min_point=x,y,z&max_point=x,y,z`
- Parse `min_point` and `max_point` from query string. Validate `min_point[i] <= max_point[i]`.
- Iterate model objects, query their `GeomExtents`, and test bounding box intersection with `[min, max]`.
- Filter out non-physical entities. Return handles, object types, roles, sections, and bounding box coordinates.

### B. Bolt Pattern (`BoltCommandHandler.cs`)
- Endpoint: `POST /api/v1/elements/bolt`
- Must execute inside transaction (`doc.LockDocument()` + `doc.TransactionManager.StartTransaction()`).
- Resolve `connected_handles`. If fewer than 1 valid physical part, fail with `INVALID_PARAMETER` (400) or `ELEMENT_NOT_FOUND` (404).
- Create `BoltPattern`, assign standard, grade, diameter, pattern dimensions, and connect the parts.

### C. PolyBeam (`PolyBeamCommandHandler.cs`)
- Endpoint: `POST /api/v1/elements/poly-beam`
- Validate `points.Count >= 2`.
- Create `PolyBeam` through the points, set section and material, write to database.

---

## 5. Mandatory Verification Commands

```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
python -m pytest tests/ -q
```

---

## 6. Definition of Done (DoD)
- [x] `dotnet build -c Release` reports 0 errors against Advance Steel 2026 assemblies.
- [x] All three new endpoints registered in `KnownRoutes` and `Route` switch.
- [x] FastMCP tools (`query_elements_in_box`, `create_bolt_pattern`, `create_poly_beam`) exposed in `server.py`.
- [x] Mock server implements responses matching SPEC-002 §5.
- [x] Full test suite passes without regressions (36/36).
