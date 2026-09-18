# Agentic Contract: CONTRACT-004-NUMBERING-AND-DSTV

- **Assigned Worker**: **Claude Code (Terminal CLI) / Codex**
- **Director / Supervisor**: Antigravity (Gemini 3.8 Flash)
- **Status**: **PROPOSED / QUEUED**
- **Date Created**: 2026-09-18
- **Target Component**: `src/as_plugin/Commands/Handlers`

---

## 1. Context & Objective

After structural modeling, joint generation, and clash auditing are complete, steel workshops require:
1. **Automatic Numbering**: Assigning identical single-part marks (e.g. `p1`, `p2`) and assembly marks (`C1`, `B1`, `R1`) to identical pieces based on geometric tolerance.
2. **CNC / DSTV (NC1) Export**: Generating standard DSTV files for CNC drilling, saw cutting, and plasma plate profiling machines.
3. **Drawing Derivation Status**: Checking whether shop fabrication drawings (DWT / DWG) exist for each assembly mark.

---

## 2. Reference Specifications & Rules

- [`docs/specs/001-architecture-and-ipc.md`](../../docs/specs/001-architecture-and-ipc.md)
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md)
- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md)
- [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md)

---

## 3. Strict Permitted Scope (File Whitelist)

- `src/as_plugin/Commands/Handlers/ProductionCommandHandler.cs`
- `src/as_plugin/Commands/CommandDispatcher.cs`
- `src/mcp_server/tools/production_tools.py`
- `tests/mcp/test_production_tools.py`

---

## 4. Implementation Guidelines

### A. Automatic Numbering (`POST /api/v1/production/numbering`)
- Execute `Autodesk.AdvanceSteel.CADAccess.Numbering` or active drawing numbering engine.
- Report count of numbered single parts and assemblies, and flag any conflicts.

### B. DSTV / NC1 Generation (`POST /api/v1/production/export-nc`)
- Target output directory (default: `./DSTV_NC1/` relative to DWG).
- Generates `.nc` or `.nc1` files for selected or all workshop assemblies.
- Returns list of generated NC files, element marks, and byte sizes.

---

## 5. Mandatory Verification Commands

```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
python -m unittest discover -s tests -p "test_*.py"
```

---

## 6. Definition of Done (DoD)
- [ ] Production handler executes inside `doc.LockDocument()` transaction.
- [ ] Generates valid NC files or clean mock simulation.
- [ ] Full test suite passes.
