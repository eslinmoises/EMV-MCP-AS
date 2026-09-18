# Agentic Contract: CONTRACT-003-JOINTS-AND-FEATURES

- **Assigned Worker**: **Claude Code (Terminal CLI)**
- **Director / Supervisor**: Antigravity (Gemini 3.8 Flash)
- **Status**: **COMPLETED & ACCEPTED BY DIRECTOR** (Approved on 2026-09-19)
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
- [x] All handlers are guarded by `using (doc.LockDocument())` and `using (var trans = doc.TransactionManager.StartTransaction())`.
- [x] Unknown handles return `HANDLE_NOT_FOUND` (404) with corrective suggestion.
- [x] `dotnet build` succeeds with 0 errors.
- [x] Python test suite continues to pass.

---

## 7. Worker Completion Report (Claude Code — 2026-09-19)

### 7.1 Checklist

- [x] `src/as_plugin/Commands/Handlers/JointCommandHandler.cs` — `POST /api/v1/elements/joint`
- [x] `src/as_plugin/Commands/Handlers/FeatureCommandHandler.cs` — `POST /api/v1/elements/cut`
- [x] `src/as_plugin/Commands/Handlers/ModifyCommandHandler.cs` — `POST /api/v1/elements/modify`
- [x] All three wired into `CommandDispatcher.Route` **and** `KnownRoutes`, inside the existing
      application-context / DocumentLock / transaction boundary
- [x] `dotnet build ... -c Release` → **0 errors**, 0 C# warnings
- [x] `python -m unittest tests/test_mock_server.py tests/mcp/test_tools.py` → **21/21 OK** (was 17)

### 7.2 Modified / created files

| File | Change |
| --- | --- |
| `src/as_plugin/Commands/Handlers/JointCommandHandler.cs` | new (412 lines) |
| `src/as_plugin/Commands/Handlers/FeatureCommandHandler.cs` | new (544 lines) |
| `src/as_plugin/Commands/Handlers/ModifyCommandHandler.cs` | new (370 lines) |
| `src/as_plugin/Commands/CommandDispatcher.cs` | +3 route cases, +3 `KnownRoutes` entries, +`AsQuery.TryResolve<T>` |
| `tests/mcp/test_tools.py` | +`TestDispatcherRouting` (4 tests) |

All five files are inside the §3 whitelist. Nothing else was touched.

### 7.3 Verification output

```
$ dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
    1 Advertencia(s)      <- pre-existing MSB3277 (AutoCAD reference-assembly version skew)
    0 Errores

$ python -m unittest tests/test_mock_server.py tests/mcp/test_tools.py
.....................
Ran 21 tests in 1.07s
OK
```

Handler types confirmed present in the emitted assembly by reflecting over
`bin/Release/net8.0-windows/EMV.AdvanceSteel.Plugin.dll`.

### 7.4 API grounding

Every Advance Steel type, constructor, property and enum used below was verified by reflecting
over the **installed** `ASObjectsMgd.dll` / `ASGeometryMgd.dll` of AutoCAD 2026 + Advance Steel
2026 (`C:\Program Files\Autodesk\AutoCAD 2026\ADVS\`) with a `MetadataLoadContext` dumper — not
from memory.

- **Joint**: `ConstructionTypes.UserAutoConstructionObject(string ruleName,
  List<Tuple<FilerObject, Point3d>> inputDrivers, List<Point3d> additionalInputPoints)`, then
  `WriteToDb()`, `UpdateDrivenConstruction()`, `CheckStatus` (`NodeStatus`), `CreatedObjects`.
- **Cuts**: `Modelling.BeamShortening(Beam.eEnd, double)` + `Set(Point3d, Vector3d, eEnd)` /
  `AngleOnY` / `AngleOnZ`; `Modelling.BeamNotch2Ortho(eEnd, eSide, length, depth)` and
  `Modelling.BeamNotchEx` (+`AxisAngle`/`ZAngle`/`XAngle`) with `BeamNotch.SetCorner(...)`;
  `Modelling.BeamMultiContourNotch(Beam, eEnd, …)` in its circle / rectangle / polygon overloads.
  Features are bound with `AtomicElement.AddFeature(FeatureObject)`.
- **Modify**: `AtomicElement.Material` / `.Coating`, `ConstructionElement.Role`, `Beam.ProfName`,
  `Beam.Angle` (radians), `PlateBase.Thickness`, persisted with `FilerObject.WriteToDb()`.

### 7.5 Endpoint contracts implemented

**`POST /api/v1/elements/joint`**
```json
{ "joint_type": "BasePlate",        // or "rule_name": "<exact Advance Steel rule>"
  "primary_handle": "1B2C",
  "secondary_handles": ["3D4E"],    // optional
  "primary_end": "Start",           // Start | End | Mid, default Start
  "connection_point": [0,0,0],      // optional; snapped onto each member's system line
  "input_points": [[x,y,z]] }       // optional extra rule inputs
```
Returns `handle`, `rule_name`, `node_status`, `created_objects[]` (full `ElementDetails` per
generated plate/bolt/weld), `created_count`, `warnings[]`.

**`POST /api/v1/elements/cut`** — one endpoint, three `cut_type` families:
- `shortening` (aliases `plane`, `miter`, `trim`): `length` mm off `end`, **or** a cutting plane
  via `plane_point` + `plane_normal`; optional `angle_y_deg` / `angle_z_deg` for mitres.
- `notch` (aliases `cope`, `recess`): `end`, `side` (Upper/Lower), `length`, `depth`; optional
  `corner_type` (Straight/Round/BoringOut) + `corner_radius`; any of `axis_angle_deg` /
  `z_angle_deg` / `x_angle_deg` promotes it to a skewed `BeamNotchEx`.
- `contour` (aliases `box`, `rectangle`, `circle`, `polygon`, `pocket`): `center` + `radius`, or
  `center` + `length` + `width`, or an explicit `contour_points` outline; `normal` defaults to the
  member axis and `x_axis` to a perpendicular.

Returns `feature_handle`, `feature_type`, `length_before_mm` / `length_mm` and `weight_kg`, so the
agent can confirm the member actually got shorter rather than trusting a bare 200.

**`POST /api/v1/elements/modify`** — `element_handle` or `element_handles` (batch), plus any of
`material`, `coating`, `model_role`, `section_name`, `rotation_deg`, `thickness`. Returns a
before/after `changes[]` per element.

### 7.6 Domain rules applied

- **`rules/transaction-safety.md` §3/§4** — none of the three handlers opens a lock or a
  transaction; they all run inside the dispatcher's single boundary. Every structured failure
  aborts it, so a joint whose rule reports an unsupported node, or a batch modify whose third
  element rejects a property, leaves **zero** partial geometry. A regression test asserts the
  handlers contain no `LockDocument()` / `StartTransaction(`.
- **`rules/advance-steel-modeling.md` §2** — `modify` warns (does not fail) when `model_role` is
  outside the nine roles of §2, because an unknown role still applies but will not trigger the
  numbering prefix or drawing style keyed on it.
- **§3.2** — `modify` deliberately **does not** expose `is_main_part`. Reassigning the Main Part
  stays behind `set_main_part`, which is where the "never a stiffener / clip / end plate"
  validation lives. `joint` warns when the primary driver is not a profile.
- **§4 tolerances** — 0.1 mm floor on every cut dimension; the 3.0 mm minimum plate thickness is
  re-enforced on `modify.thickness`; a shortening longer than the member itself is rejected 422.

### 7.7 Points for the Director

1. **Joint rule names are the one thing I could not fully ground.**
   `UserAutoConstructionObject` takes the rule name as a bare string, and Advance Steel exposes
   no managed API to enumerate the installed rules. I extracted the real names from the
   `RULE_*` tables of the shipped `AstorRules` database (AS 2026, INT country pack) and built the
   `joint_type` → rule-name alias table from **verified-present** names only — e.g. `BasePlate`,
   `BasePlateExtended`, `AnchorBasePlate`, `USClipAngleNew`, `DoubleSideClipAngle`,
   `SingleSideEndPlate`, `DoubleSideEndPlate`, `ApexJoint`, `ApexDoubleHaunch`, `FlangeHaunch`.
   **What I could not verify without a live session is whether the ctor wants exactly that string
   or a decorated form.** The handler therefore (a) tries each alias candidate in order,
   (b) always honours an explicit `rule_name` passthrough that bypasses the table entirely, and
   (c) on total failure returns `JOINT_RULE_NOT_FOUND` (422) listing every name tried and the
   exception each one raised. That makes the first live smoke test self-diagnosing rather than
   opaque. **Please have the first Advance Steel session run one `BasePlate` joint and report the
   `details` field back** — I will pin the table from that.
2. **Feature attachment order is defensive on purpose.** `BeamShortening` is constructed without
   a reference to its owner while `BeamMultiContourNotch` takes the beam in its ctor, and the SDK
   documents neither the required `AddFeature` / `WriteToDb` order. `AttachFeature` tries
   `AddFeature` first and falls back to `WriteToDb` + `AddFeature`, so either convention works.
   Worth simplifying once a live run confirms which one AS 2026 actually needs.
3. **Still no Python surface for these three tools** — same blocker I raised as CONTRACT-002 open
   item 1, now larger. `src/mcp_server/tools/modeling_tools.py`, `tests/mocks/mock_as_plugin.py`
   and `tests/mocks/fixtures.py` are outside the §3 whitelist, so `create_standard_joint`,
   `apply_beam_cut_or_notch` and `modify_element_properties` are reachable on the plugin but not
   yet callable through FastMCP. **Recommend a CONTRACT-004 covering the MCP/mock layer for
   CONTRACT-002 + CONTRACT-003 together** (5 tools: 2 audit + 3 modelling).
4. **SPEC-004 does not yet define these three tools.** §2 stops at `set_main_part`. The request
   and response shapes in §7.5 above are my design, kept stylistically consistent with the
   CONTRACT-001/002 handlers. They should be folded back into `docs/specs/004` and
   `docs/specs/002` — both outside my whitelist.
5. **The C# handlers are still not execution-tested** (unchanged from CONTRACT-002 open item 2):
   no C# test project exists or is whitelisted, and these calls need a live AutoCAD session. What
   the 4 new Python tests *do* cover is the route wiring — the genuine two-place registration bug
   in `CommandDispatcher` (a route in `Route` but not in `KnownRoutes` 404s before it is ever
   reached). I confirmed the test fails when `"elements/cut"` is removed from `KnownRoutes`, so it
   is not a vacuous assertion. Everything downstream of the wiring remains unverified until
   someone runs it on a workstation with Advance Steel.
