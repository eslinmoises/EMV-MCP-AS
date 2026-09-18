# Agentic Contract: CONTRACT-004A-PRODUCTION-PLUGIN

- **Parent Contract**: [`CONTRACT-004`](./CONTRACT-004-numbering-and-dstv.md)
- **Assigned Worker**: **Codex CLI** (reassigned from Claude Code, see Director note)
- **Director / Supervisor**: Claude Code acting as Orchestration Director
- **Status**: **ACTIVE**
- **Date Created**: 2026-09-19
- **Target Component**: `src/as_plugin/Commands`

> **Director note (2026-09-19)**: originally assigned to Claude Code. Reassigned to Codex CLI once
> its authentication was restored, to spread the work across workers while the Claude Code budget is
> capped. The Director keeps acceptance: build, tests and whitelist are verified independently of the
> worker's own report.

---

## 1. Objective

Expose the three production endpoints of SPEC-004 §4 inside the Advance Steel add-in, in
**command mode** (`rules/transaction-safety.md` §5), and report the state the model actually holds
after the engine ran.

---

## 2. Reference Specifications & Rules

- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md) §4
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md) §4
- [`rules/transaction-safety.md`](../../rules/transaction-safety.md) §5
- [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md) §2 (Main Part drives the DSTV origin)

---

## 3. Strict Permitted Scope (File Whitelist)

- `src/as_plugin/Commands/Handlers/ProductionCommandHandler.cs` (new)
- `src/as_plugin/Commands/CommandDispatcher.cs` (route registration + command-mode execution path)

No other file. The Python surface belongs to CONTRACT-004B.

---

## 4. Implementation Guidelines

### A. Dispatcher — command mode
- Add a `CommandRoutes` set holding `production/numbering`, `production/export-nc`,
  `production/drawing-status`; register the three routes in `KnownRoutes` and in the `Route` switch.
- A command-mode route executes under `doc.LockDocument()` **without** opening the AutoCAD or
  Advance Steel transaction. A route must never be in both execution paths.

### B. `POST production/numbering`
- Drive the Advance Steel numbering engine, then **read the marks back** through
  `AtomicElement.GetSinglePartPositionNumber()` / `GetMainPartPositionNumber()` /
  `GetNumberingStatus()` and count what the model holds.
- Emit `NumberingReport` (SPEC-002 §4). Duplicate marks on geometrically different parts go to
  `conflicts` and do **not** fail the call.
- The engine being unavailable is `NUMBERING_ENGINE_UNAVAILABLE` (503) — never a silent success.

### C. `POST production/export-nc`
- Refuse with `UNNUMBERED_MODEL` (409) when no part carries a single-part mark.
- Resolve a relative `output_directory` against the folder of the active DWG; an unsaved drawing is
  `MODEL_NOT_SAVED` (409).
- Report only files that exist on disk after the run, with their real `size_bytes` read via `FileInfo`.
- A part without a single-part mark is listed in `skipped`, never exported.

### D. `GET production/drawing-status`
- Group atomic elements by assembly mark, report `quantity`, `has_drawing`, `drawing_numbers` and
  `is_up_to_date` per mark. If the derived-document layer cannot be reached, answer
  `DRAWING_STATUS_UNAVAILABLE` (503).

---

## 5. Mandatory Verification Commands

```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
python -m pytest tests/ -q
```

---

## 6. Definition of Done (DoD)
- [ ] `dotnet build -c Release` reports 0 errors against the installed AutoCAD 2026 / ADVS assemblies.
- [ ] The three routes appear in `KnownRoutes`, in the `Route` switch and in `CommandRoutes`.
- [ ] No command-mode route opens a transaction; no transactional route was moved into command mode.
- [ ] Every response payload matches SPEC-002 §4 key for key.
- [ ] Reported counts and file sizes are read back from the model / filesystem, never echoed from the request.
