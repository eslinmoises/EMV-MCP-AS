# Agentic Contract: CONTRACT-008-GENERATIVE-PORTAL-FRAME-AND-DOCTOR

- **Assigned Worker**: **Antigravity (Lead Director / Worker)**
- **Director / Supervisor**: Antigravity (Lead Director)
- **Status**: **ACCEPTED**
- **Date Created**: 2026-09-19
- **Target Components**: `src/as_plugin/Commands`, `src/mcp_server/tools`, `tests/mcp`

---

## 1. Context & Objective

1. **`create_portal_frame`** (`POST /api/v1/elements/portal-frame`):
   - One-shot generative macro creating an entire structural steel portal frame (2 columns, 2 pitched rafters, 2 base plates, 2 bolt patterns) with parametric dimensions and orientation.
   - Calculates exact geometry points, validates profiles, and commits all elements in an atomic Advance Steel transaction.
2. **`apply_detailing_repairs`** (`POST /api/v1/audit/repair`):
   - The "Detailing Doctor": inspects model elements, infers and fixes missing model roles (`Column`, `Beam`, `Rafter`, `BasePlate`), assigns missing Main Parts on assemblies, and links orphaned plates.

---

## 2. Reference Specifications & Rules

- [`docs/specs/001-architecture-and-ipc.md`](../../docs/specs/001-architecture-and-ipc.md)
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md) §8
- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md) §1 & §2
- [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md)
- [`rules/transaction-safety.md`](../../rules/transaction-safety.md)

---

## 3. Strict Permitted Scope (File Whitelist)

The worker is **ONLY** permitted to modify or create:
- `src/as_plugin/Commands/Handlers/PortalFrameCommandHandler.cs` (new)
- `src/as_plugin/Commands/Handlers/DoctorCommandHandler.cs` (new)
- `src/as_plugin/Commands/CommandDispatcher.cs` (route registration)
- `src/as_plugin/Host/ExtensionApplication.cs` (threading fix)
- `src/mcp_server/tools/modeling_tools.py`
- `src/mcp_server/tools/diagnostic_tools.py`
- `src/mcp_server/server.py`
- `tests/mocks/mock_as_plugin.py`
- `tests/mcp/test_tools.py`

---

## 4. Implementation Guidelines

### A. Portal Frame Command Handler (`PortalFrameCommandHandler.cs`)
- Endpoint: `POST /api/v1/elements/portal-frame`
- Parameters: `span_width_mm`, `column_height_mm`, `ridge_height_mm`, `column_section`, `rafter_section`, `material`, `origin`, `include_base_plates`.
- Columns:
  - Left: `[origin.x, origin.y, origin.z]` -> `[origin.x, origin.y, origin.z + column_height]`
  - Right: `[origin.x + span, origin.y, origin.z]` -> `[origin.x + span, origin.y, origin.z + column_height]`
- Rafters:
  - Left: `[origin.x, origin.y, origin.z + column_height]` -> `[origin.x + span/2, origin.y, origin.z + ridge_height]`
  - Right: `[origin.x + span/2, origin.y, origin.z + ridge_height]` -> `[origin.x + span, origin.y, origin.z + column_height]`
- Base Plates & Bolts:
  - When `include_base_plates = true`, creates 400x400x25 plates centered under each column and 4x M20 bolt patterns (`FinitRectScrewBoltPattern`).

### B. Detailing Doctor Handler (`DoctorCommandHandler.cs`)
- Endpoint: `POST /api/v1/audit/repair`
- Parameters: `element_handles`, `fix_roles`, `fix_main_parts`, `fix_orphaned_plates`.
- Role inference:
  - Beams with vertical vector ($|dz| > |dx| + |dy|$) -> `Column`.
  - Beams with horizontal or sloping vector -> `Rafter` (if $dz \neq 0$) or `Beam`.
  - Plates near $z \approx 0$ with normal pointing upwards -> `BasePlate`.
- Main Part assignment:
  - If an assembly has no `IsMainPart == true`, find the primary beam with maximum weight and set `atomic.IsMainPart = true`.

---

## 5. Mandatory Verification Commands

```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
python -m pytest tests/ -q
```

---

## 6. Definition of Done (DoD)
- [x] `PortalFrameCommandHandler.cs` and `DoctorCommandHandler.cs` implemented.
- [x] Routes `elements/portal-frame` and `audit/repair` registered in `KnownRoutes` and `Route` switch.
- [x] FastMCP tools `create_portal_frame` and `apply_detailing_repairs` exposed in `server.py`.
- [x] Mock server implements endpoints matching SPEC-002 §8.
- [x] Unit tests added in `tests/mcp/test_tools.py` and pass.
- [x] `dotnet build -c Release` compiles with 0 errors.
- [x] All 43 tests pass cleanly.
