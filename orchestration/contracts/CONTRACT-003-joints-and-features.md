# Agentic Contract: CONTRACT-003-JOINTS-AND-FEATURES

- **Assigned Worker**: **Claude Code (Terminal CLI)**
- **Director / Supervisor**: Antigravity (Gemini 3.8 Flash)
- **Status**: **READY FOR CLAUDE CODE**
- **Date Created**: 2026-09-18
- **Target Component**: `src/as_plugin/Commands/Handlers`

---

## 1. Context & Objective

With modeling of profiles (`StraightBeam`), contour plates (`Plate`), weld verification, spatial queries, and clash auditing now in place, `CONTRACT-003` implements the connection features from SPEC-004 §2:
1. **`create_standard_joint`** (`POST /api/v1/elements/joint`): Parametric connection generation between structural members (BasePlate, ClipAngle, EndPlate, ApexHaunch).
2. **`apply_beam_cut_or_notch`** (`POST /api/v1/elements/cut`): Apply beam end miters, flange cuts, notches, and contour processings using Advance Steel features.
3. **`modify_element_properties`** (`POST /api/v1/elements/modify`): Update material grade, coating, model role, and rotation of existing elements by handle.

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
- `src/as_plugin/Commands/Handlers/JointCommandHandler.cs`
- `src/as_plugin/Commands/Handlers/FeatureCommandHandler.cs`
- `src/as_plugin/Commands/Handlers/ModifyCommandHandler.cs`
- `src/as_plugin/Commands/CommandDispatcher.cs`
- `tests/mcp/test_tools.py`

---

## 4. Implementation Guidelines

### A. Beam Cuts and Processings (`FeatureCommandHandler.cs`)
- Straight cuts & miters: `Autodesk.AdvanceSteel.Modelling.BeamMultiContourNotch` or `BeamShortening`.
- Plane cut: Shorten beam along cutting plane with offset.

### B. Element Property Modification (`ModifyCommandHandler.cs`)
- Open object by handle via `AsQuery.ResolveObject(handle)`.
- Update `Material`, `Role`, or `Angle`.
- Call `WriteToDb()` to persist changes.

### C. Standard Joint / Macro (`JointCommandHandler.cs`)
- Parametric connection application between primary and secondary members.

---

## 5. Mandatory Verification Commands

```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
python -m unittest tests/test_mock_server.py tests/mcp/test_tools.py
```

---

## 6. Definition of Done (DoD)
- [ ] All handlers are guarded by `using (doc.LockDocument())` and `using (var trans = doc.TransactionManager.StartTransaction())`.
- [ ] Unknown handles return `HANDLE_NOT_FOUND` (404) with corrective suggestion.
- [ ] `dotnet build` succeeds with 0 errors.
- [ ] Python test suite continues to pass.
