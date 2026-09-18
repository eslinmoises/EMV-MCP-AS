# Agentic Contract: CONTRACT-002-SPATIAL-AND-AUDITING

- **Assigned Worker**: **Claude Code (Terminal CLI)**
- **Director / Supervisor**: Antigravity (Gemini 3.8 Flash)
- **Status**: **READY FOR CLAUDE CODE**
- **Date Created**: 2026-09-18
- **Target Component**: `src/as_plugin/Commands/Handlers`

---

## 1. Context & Objective

Following the successful execution and acceptance of `CONTRACT-001`, this contract implements the diagnostic tools from SPEC-004:
1. **`spatial/ucs-grids`**: Return active UCS axes, structural grid lines, and building levels.
2. **`audit/assembly-integrity`**: Detect orphaned workshop plates/stiffeners and unnumbered parts.
3. **`audit/clashes`**: Execute Advance Steel collision checking across elements.

---

## 2. Reference Specifications & Rules

- [`docs/specs/001-architecture-and-ipc.md`](../../docs/specs/001-architecture-and-ipc.md)
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md)
- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md)
- [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md)
- [`rules/transaction-safety.md`](../../rules/transaction-safety.md)

---

## 3. Strict Permitted Scope (File Whitelist)

The worker is **ONLY** permitted to create or modify:
- `src/as_plugin/Commands/Handlers/SpatialCommandHandler.cs`
- `src/as_plugin/Commands/Handlers/AuditCommandHandler.cs`
- `src/as_plugin/Commands/CommandDispatcher.cs`
- `tests/mcp/test_tools.py`

---

## 4. Implementation Details

### A. SpatialCommandHandler (`GET /api/v1/spatial/ucs-grids`)
- Read active UCS: `doc.Editor.CurrentUserCoordinateSystem`.
- Query Grids: Iterate `eObjectType.kGrid` or `eObjectType.kGrid1D` using `AsQuery.ModelObjectIds(...)`.
- Query Levels: Read level elevations if available or return default model level.

### B. AuditCommandHandler
- `GET /api/v1/audit/assembly-integrity`:
  - Scan `Modelling.Plate` and `Beam` objects.
  - Flag any plate with `ModelRole` in (`Stiffener`, `EndPlate`, `BasePlate`, `GussetPlate`) that has NO workshop weld attached to a main profile.
- `GET /api/v1/audit/clashes`:
  - Invoke collision check / clearance check via Advance Steel collision API or bounding box spatial intersections.

---

## 5. Mandatory Verification Commands

```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
python -m unittest tests/test_mock_server.py tests/mcp/test_tools.py
```

---

## 6. Definition of Done (DoD)
- [ ] Both handlers follow `using (doc.LockDocument())` transaction boundaries.
- [ ] `CommandDispatcher.cs` routes both endpoints.
- [ ] `dotnet build` succeeds with 0 errors.
- [ ] Python test suite continues to pass.
