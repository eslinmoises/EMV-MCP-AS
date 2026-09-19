# Agentic Contract: CONTRACT-004-NUMBERING-AND-DSTV

- **Assigned Worker**: decomposed (CONTRACT-004A: Claude Code/Antigravity; CONTRACT-004B: Codex/Claude Code)
- **Director / Supervisor**: Antigravity (Lead Director)
- **Status**: **COMPLETED & ACCEPTED BY DIRECTOR** (Approved on 2026-09-19)
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

## 2b. Decomposition (Director, 2026-09-19)

The parent contract crossed two runtimes and five files, which the handbook forbids in a single
unit of work. It is split into two sub-contracts with **disjoint whitelists** so both workers can
run in parallel, joined only by the payload schemas of SPEC-002 §4:

| Sub-contract | Worker | Scope |
| :--- | :--- | :--- |
| [`CONTRACT-004A`](./CONTRACT-004A-production-plugin.md) | Claude Code | C# add-in: command-mode dispatcher path + `ProductionCommandHandler` |
| [`CONTRACT-004B`](./CONTRACT-004B-production-mcp-surface.md) | Codex CLI | Python: FastMCP tools, mock endpoints, tests |

Two amendments to the original scope, both recorded in the specs before any code was written:
1. **Command mode** (`rules/transaction-safety.md` §5). The numbering and NC engines are not
   managed classes in AS 2026; they run through the command layer and manage their own
   transactions, so `production/` routes take the document lock but no outer transaction. The
   original §6 DoD item "executes inside `doc.LockDocument()` transaction" is superseded by it.
2. **Drawing status** was named in §1 of this contract but had no endpoint; it is now specified as
   `GET /api/v1/production/drawing-status` (SPEC-004 §4).

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
- [x] Command-mode routes execute inside `doc.LockDocument()` without outer transactions (§2b.1).
- [x] Generates valid NC files or clean mock simulation.
- [x] Full test suite passes (0 build errors, 33/33 tests passing).
