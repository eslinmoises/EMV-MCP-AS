# Agentic Contract: CONTRACT-002-SPATIAL-AND-AUDITING

- **Assigned Worker**: **Claude Code (Terminal CLI)**
- **Director / Supervisor**: Antigravity (Gemini 3.8 Flash)
- **Status**: **COMPLETED & ACCEPTED BY DIRECTOR** (Approved on 2026-09-18)
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
- [x] Both handlers follow `using (doc.LockDocument())` transaction boundaries.
- [x] `CommandDispatcher.cs` routes both endpoints.
- [x] `dotnet build` succeeds with 0 errors.
- [x] Python test suite continues to pass.

---

## 7. Worker Completion Report (Claude Code)

**Date**: 2026-09-18 · **Status**: `IN_REVIEW`

### 7.1 Modified files (all inside the §3 whitelist)

| File | Change |
|---|---|
| `src/as_plugin/Commands/Handlers/SpatialCommandHandler.cs` | **new** — `GET /api/v1/spatial/ucs-grids` |
| `src/as_plugin/Commands/Handlers/AuditCommandHandler.cs` | **new** — `GET /api/v1/audit/assembly-integrity`, `GET /api/v1/audit/clashes` |
| `src/as_plugin/Commands/CommandDispatcher.cs` | 3 routes added to `Route()` and to `KnownRoutes` |
| `tests/mcp/test_tools.py` | added `test_get_ucs_and_grids_payload_schema` |

No file outside the whitelist was touched (`git status` confirms 2 modified + 2 new).

### 7.2 Verification output

```
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
  Compilación correcta.
      1 Advertencia(s)      <- pre-existing MSB3277 (AutoCAD reference resolution), 0 warning CS
      0 Errores

python -m unittest tests/test_mock_server.py tests/mcp/test_tools.py
  Ran 15 tests in 1.099s
  OK
```

### 7.3 Transaction safety

Neither handler opens a lock or a transaction. Both run inside the
`DocumentLock` + AutoCAD `Transaction` + Advance Steel `Transaction` boundary that
`CommandDispatcher.ExecuteOnMainThread` already establishes, per the `CommandContext`
contract from CONTRACT-001 and `rules/transaction-safety.md` §3. Both endpoints are
read-only, so the dispatcher's commit path leaves the model untouched.

### 7.4 Implementation notes

**`spatial/ucs-grids`**
- Active UCS read from `doc.Editor.CurrentUserCoordinateSystem` (the UCS the user is
  actually drawing in), decomposed via `Matrix3d.CoordinateSystem3d`. `is_world` is
  computed so an agent can skip the transform when the model is on the WCS; `name` is
  resolved from the drawing's UCS table, falling back to `"Unnamed"` for an ad-hoc UCS.
- Grids: Advance Steel stores a grid as one `Modelling.Grid` holding N `GridElement`
  axes — the individual axis lines are **not** model objects, so `eObjectType.kGrid` is
  queried once (it covers `Grid1D` and `GridCircle`) and each axis is read through
  `GridElement.GetCurve(ref curve, grid.CS)` to lift it into world coordinates. Axis
  labels come from `GetTextAt(kStart/kEnd/kMidArc)`.
- Levels: read from the Building Structure tree (`eObjectType.kLevelObject`), name via
  `Parent.GetStructureItemName(level)`, elevation via `MainWorkingPlane.getAltitude()`
  with `CalculateMainPlaneAbsoluteHeight` as fallback. A model detailed without that tree
  reports the ±0.00 datum and sets `levels_are_default: true`, so the agent always has one
  elevation to anchor to.

**`audit/assembly-integrity`**
- Scans every physical part (`kAtomicElem` minus `WeldPattern` / `BoltPattern` /
  `Connector`). A part counts as secondary if it is a `PlateBase` or carries a secondary
  `ModelRole` (`AsQuery.IsSecondaryRole`, already shared from CONTRACT-001).
- Per `rules/advance-steel-modeling.md` §3.1, only **workshop** connections are walked:
  a plate held by a site weld is still an orphan for the shop. Two distinct findings fall
  out of that: `ORPHANED_PART` (no workshop weld at all) and `UNANCHORED_PART`
  (workshop-welded, but only to other secondary parts — never to a primary profile).
- Also reports `UNNUMBERED_PART` (missing single-part or assembly mark → skipped by BOM
  and NC export) and, per assembly mark, `ASSEMBLY_WITHOUT_MAIN_PART` /
  `MULTIPLE_MAIN_PARTS` (§3.2 — exactly one Main Part required).
- Response carries `findings[]`, `orphaned_parts[]`, `unnumbered_parts[]`, a `summary`
  block and `is_model_valid` (true when there are zero `error`-severity findings).

**`audit/clashes` — please read, this one has a caveat**
- **Advance Steel 2026 exposes no managed collision-check API.** I reflected over
  `ASObjectsMgd.dll` / `ASModelerMgd.dll`: there is no `Collision*` or `Clash*` managed
  type. The `MainPartUsedForCollisionCheck` / `SinglePartUsedForCollisionCheck` properties
  on `AtomicElement` are numbering flags, not an entry point to the checker.
- I therefore took the second option §4.B allows: axis-aligned bounding-box intersection
  from `ConstructionElement.GeomExtents`, with sweep-and-prune along X. Pairs that are
  welded or bolted together (checked in both directions, following the connection means to
  the far side) are excluded — otherwise every base plate would be reported against its
  own column.
- The response is explicit about this: `method: "axis_aligned_bounding_box"` plus a
  `method_note`. **This is conservative, not exact** — it never misses a real clash, but a
  reported pair may be clear once the true profile shapes are considered. The agent should
  confirm with `capture_viewport` before advising rework.
- Tunable via `tolerance` (default 1.0 mm, so touching faces are not a clash) and
  `max_clashes` (default 200). A whole-model scan is capped at 5000 parts to stay inside
  the 30 s SPEC-001 request budget; both the cap and the truncation surface in `warnings`.

### 7.5 Open items for the Director

1. **The two audit tools have no Python surface yet.** SPEC-004 §1 defines
   `audit_assembly_integrity` and `detect_clashes_and_clearances`, but
   `src/mcp_server/tools/diagnostic_tools.py` has no function for either, and
   `tests/mocks/mock_as_plugin.py` does not serve the two routes (it 404s them). All three
   files are **outside my §3 whitelist**, so the endpoints are reachable from the plugin but
   not yet callable through the MCP server. Requesting either a whitelist amendment or a
   CONTRACT-003 covering `diagnostic_tools.py`, `mock_as_plugin.py` and `fixtures.py`.
2. **TDD was not achievable for the C# handlers within this whitelist.** There is no C#
   test project in the repo and none is whitelisted, and the handlers need a live
   AutoCAD/Advance Steel session to execute. The C# work is therefore verified by a clean
   Release build and by API-surface verification against the installed
   `ASObjectsMgd.dll` (reflection over the real 2026 assemblies) — not by test execution.
   Flagging this rather than implying the handlers are covered. A whitelisted xUnit project
   with the AS calls behind a thin seam would fix it for future contracts.
3. **The one Python test I added is a schema contract test, written after the handler**,
   not test-first: it pins the `spatial/ucs-grids` keys (`active_ucs` axes, `grid_axes`
   `name`/`start`/`end`, `levels` `name`/`elevation`) that `SpatialCommandHandler` must
   emit. I verified it actually fails on a payload with a dropped `z_axis` and on empty
   `levels`, so it is not a vacuous assertion.
4. **Level names may come back `null`** on models where the Building Structure tree exists
   but items were never named. Worth deciding whether the MCP layer should synthesise
   `"+4.00"`-style names from the elevation; I left the raw value rather than inventing one.
