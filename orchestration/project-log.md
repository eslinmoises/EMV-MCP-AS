# Project Audit & Milestone Log (`orchestration/project-log.md`)

> **Maintained by**: Project Scribe (Antigravity)  
> **Philosophy**: Zero documentation drift. Every contract, endpoint, schema change, and domain finding is recorded with evidence.

---

## 📅 Chronological Milestone Ledger

### [2026-09-18] Foundation & Scaffolding
- **Milestone**: Core architecture, specs, and initial mock server established.
- **Specifications Authored**:
  - `docs/specs/001-architecture-and-ipc.md`: REST Wire Protocol on `http://127.0.0.1:5055/api/v1/`.
  - `docs/specs/002-data-models-and-schemas.md`: Structural data contracts (Beams, Plates, Welds, Assemblies).
  - `docs/specs/003-scripting-engine-roslyn.md`: In-memory Roslyn execution context.
  - `docs/specs/004-mcp-tools-specification.md`: Formal catalog of MCP diagnostic and modeling tools.
- **Governance**: `AGENTS.md`, `CLAUDE.md`, `.cursor/rules/advance-steel.mdc`, `rules/advance-steel-modeling.md`, `rules/transaction-safety.md`.
- **Offline Mock**: `tests/mocks/mock_as_plugin.py` creating realistic simulated Advance Steel session.

### [2026-09-18] CONTRACT-001: Core Modeling & Diagnostic Engine
- **Worker**: Claude Code
- **Scope**:
  - `POST /api/v1/elements/beam`: `StraightBeam` creation with reference axis alignment.
  - `POST /api/v1/elements/plate`: Coplanar contour plates with minimum 3 mm thickness validation.
  - `GET /api/v1/assembly/verify-welds`: Workshop vs. Site welds, throat thickness, connected parts.
  - `GET /api/v1/assembly/main-part`: Identification and validation of assembly Main Parts.
  - `POST /api/v1/assembly/set-main-part`: Reassigning Main Part of an assembly.
  - `GET /api/v1/viewport/capture`: PNG screenshot base64 encoding.
  - `POST /api/v1/script/execute`: Dynamic Roslyn execution sandbox.
- **Verification**: `dotnet build` passed (0 errors), 14/14 tests passing.

### [2026-09-18] CONTRACT-002: Spatial Geometry & Clash Auditing
- **Worker**: Claude Code
- **Scope**:
  - `GET /api/v1/spatial/ucs-grids`: Active UCS, structural grids, level elevations.
  - `GET /api/v1/audit/assembly-integrity`: Detection of orphaned plates/stiffeners and unnumbered elements.
  - `GET /api/v1/audit/clashes`: Native AABB Sweep-and-Prune collision detection excluding shop-welded and shop-bolted parts.
- **Verification**: `dotnet build` passed (0 errors), 17/17 tests passing.

### [2026-09-19] CONTRACT-003: Connections, Joints & Feature Processing
- **Worker**: Claude Code
- **Scope**:
  - `POST /api/v1/elements/joint`: Standard connection macro generation (`BasePlate`, `ClipAngle`, `EndPlate`, `ApexHaunch`) via `UserAutoConstructionObject`.
  - `POST /api/v1/elements/cut`: Beam shortening, flange notches, and contour processings (`BeamShortening`, `BeamNotch2Ortho`, `BeamMultiContourNotch`).
  - `POST /api/v1/elements/modify`: In-place modification of material, role, coating, and profile orientation.
- **Verification**: `dotnet build` passed (0 errors), 26/26 tests passing.

### [2026-09-19] CONTRACT-004: Automatic Numbering & DSTV/NC1 Production
- **Decomposition**:
  - **CONTRACT-004B (Python MCP Surface)**: Implemented `run_automatic_numbering`, `export_dstv_nc_files`, `get_drawing_status` with `RecordingClient` tests and mock endpoints.
  - **CONTRACT-004A (C# Plugin Command Mode)**:
    - Implemented `ProductionCommandHandler.cs` running in **Command Mode** (`rules/transaction-safety.md §5`): `doc.LockDocument()` held without outer transaction to prevent deadlocks with native command transactions.
    - Direct typed integration with `Autodesk.AdvanceSteel.Services.EqualPartsParameters` (`SinglePartStartValue`, `MainPartStartValue`, `ReuseNumbers`).
    - Post-execution model readback: `AtomicElement.GetSinglePartPositionNumber()`, `GetMainPartPositionNumber()`, `IsMainPart`, and filesystem validation of generated `.nc1`/`.nc` files.
    - Honest 503 `DRAWING_STATUS_UNAVAILABLE` when the native derived document manager cannot be safely queried.
- **Verification**: `dotnet build` passed (0 errors), 33/33 tests passing.
- **Bundle Packaging**: Automated deployment via `scripts/deploy_bundle.py` to `%APPDATA%\Autodesk\ApplicationPlugins\EMV-AdvanceSteel.bundle\`.

### [2026-09-19] CONTRACT-005: Bolting, PolyBeams & Spatial Bounding Box Queries
- **Worker**: Antigravity (Lead Director / Orchestrator)
- **Scope**:
  - `GET /api/v1/spatial/box`: 3D AABB intersection query (`SpatialCommandHandler.QueryBox`) supporting `min_point`, `max_point` and optional `element_types` filtering.
  - `POST /api/v1/elements/bolt`: Rectangular bolt patterns (`BoltCommandHandler.Create`) utilizing `FinitRectScrewBoltPattern`, setting standard, grade, diameter, rectangular grid (`nx`, `ny`, `dx`, `dy`), and binding parts with `eAssemblyLocation.kOnSite` or `kInShop`.
  - `POST /api/v1/elements/poly-beam`: Multi-segment continuous polybeams and curved members (`PolyBeamCommandHandler.Create`) utilizing `Polyline3d` and `PolyBeam`.
  - Python MCP tools: `query_elements_in_box`, `create_bolt_pattern`, `create_poly_beam` exposed in `server.py`.
  - Mock server endpoints and offline tests in `test_tools.py`.
- **Verification**:
  - `dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release` passed (0 errors, 1 benign warning).
  - `python -m pytest tests/` passed (36/36 tests green).
  - Automated deployment of updated bundle via `python scripts/deploy_bundle.py`.

---

## 🧠 Key Advance Steel Domain Findings

1. **Assembly Hierarchy**:
   Advance Steel does not have a managed `Assembly` class. Assemblies are defined dynamically by the shop-connected component graph (`GetConnectedObjects(..., kInShop)`) anchored by the member with `AtomicElement.IsMainPart == true`.
2. **Command Mode vs. Transaction Mode**:
   Numbering and NC generation are internal commands that execute their own transactions. Wrapping them inside an outer AutoCAD transaction causes deadlocks or rollbacks of committed work. They must run in command mode (`CommandRoutes`).
3. **EqualPartsParameters**:
   Provided by `ASMgd.dll` under `Autodesk.AdvanceSteel.Services.EqualPartsParameters`. Parameters are modified on an instance returned by `GetCurrentParameters()` and saved with `instance.SetAsCurrent()`.
4. **Drawing Derivation Layer**:
   `DocumentManager.GetDerivedDocumentsForDwg()` is native C++ only; not exposed in the managed API. Handlers must avoid unmanaged reflection crashes and return clean status codes.
5. **Bolting Geometry & Pattern Hierarchy**:
   Advance Steel models rectangular bolt grids via `Autodesk.AdvanceSteel.Modelling.FinitRectScrewBoltPattern` (deriving from `CountableScrewBoltPattern` -> `ScrewBoltPattern` -> `BoltPattern`). The pattern defines two opposite corner points and plane vectors, then binds to structural parts via `pattern.Connect(FilerObject[] elems, eAssemblyLocation location)`.
6. **PolyBeams & Polylines**:
   Continuous multi-segment beams and curved members are created using `Autodesk.AdvanceSteel.Modelling.PolyBeam`, which wraps `Autodesk.AdvanceSteel.Geometry.Polyline3d` and an orientation reference vector `Vector3d`.

