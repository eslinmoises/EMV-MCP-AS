# Agentic Contract: CONTRACT-006-BULK-QUERY-AND-CATALOGS

- **Assigned Worker**: **Claude Code (Terminal CLI) / Antigravity**
- **Director / Supervisor**: Antigravity (Lead Director)
- **Status**: **ACCEPTED**
- **Date Created**: 2026-09-19
- **Target Components**: `src/as_plugin/Commands`, `src/mcp_server/tools`

---

## 1. Context & Objective

Autonomous detailing agents need to explore models systematically without guessing section names, connection rules, or iterating elements one-by-one:
1. **`query_elements`** (`POST /api/v1/elements/query`): Query and filter elements in bulk by role (`Column`, `Beam`), material (`S275JR`), section, assembly mark (`C1`), single part mark (`p1`), element types, or handles.
2. **`get_supported_joints_catalog`** (`GET /api/v1/elements/joints-catalog`): Discover all supported parametric Advance Steel connection macros (`BasePlate`, `ClipAngle`, `EndPlate`, `ApexHaunch`), their underlying rule names, and primary/secondary member roles.
3. **`validate_section`** (`GET /api/v1/elements/validate-section?section_name=HEB300`): Probe Advance Steel's active `AstorProfiles` database to verify that a profile name exists before attempting modeling.

---

## 2. Reference Specifications & Rules

- [`docs/specs/001-architecture-and-ipc.md`](../../docs/specs/001-architecture-and-ipc.md)
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md) §6
- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md) §1
- [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md)
- [`rules/transaction-safety.md`](../../rules/transaction-safety.md)

---

## 3. Strict Permitted Scope (File Whitelist)

The worker is **ONLY** permitted to modify or create:
- `src/as_plugin/Commands/Handlers/QueryCommandHandler.cs` (new)
- `src/as_plugin/Commands/CommandDispatcher.cs` (route registration)
- `src/mcp_server/tools/diagnostic_tools.py`
- `src/mcp_server/server.py`
- `tests/mocks/mock_as_plugin.py`
- `tests/mcp/test_tools.py`

---

## 4. Implementation Guidelines

### A. Element Bulk Query (`QueryCommandHandler.cs`)
- Endpoint: `POST /api/v1/elements/query`
- Extract filters: `model_role`, `material`, `section_name`, `assembly_mark`, `single_part_mark`, `element_types`, `handles`.
- Iterate model objects (`AsQuery.ModelObjectIds(eObjectType.kAtomicElem)` or filter by `handles`).
- Apply criteria. Return matching elements described via `AsQuery.Describe(atomic)`.

### B. Supported Joints Catalog (`QueryCommandHandler.cs`)
- Endpoint: `GET /api/v1/elements/joints-catalog`
- Returns static catalog mapping friendly aliases to `JointCommandHandler` supported macros (`BasePlate`, `ClipAngle`, `EndPlate`, `ApexHaunch`), descriptions, and input role requirements.

### C. Section Name Validation (`QueryCommandHandler.cs`)
- Endpoint: `GET /api/v1/elements/validate-section?section_name=...`
- Checks whether `section_name` is non-empty. Performs safe verification via `ProfilesManager` or `ProfTypeAsDefault`.

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
- [x] FastMCP tools (`query_elements`, `get_supported_joints_catalog`, `validate_section`) exposed in `server.py`.
- [x] Mock server implements responses matching SPEC-002 §6.
- [x] Full test suite passes without regressions (39/39 passing).
