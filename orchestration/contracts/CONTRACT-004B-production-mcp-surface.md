# Agentic Contract: CONTRACT-004B-PRODUCTION-MCP-SURFACE

- **Parent Contract**: [`CONTRACT-004`](./CONTRACT-004-numbering-and-dstv.md)
- **Assigned Worker**: **Codex CLI** (scaffolding worker, mock data generator)
- **Director / Supervisor**: Claude Code acting as Orchestration Director
- **Status**: **COMPLETED & ACCEPTED BY DIRECTOR** (Accepted 2026-09-19)
- **Date Created**: 2026-09-19
- **Target Component**: `src/mcp_server`, `tests`

---

## 1. Objective

Expose the three production tools of SPEC-004 §4 through the FastMCP surface, extend the mock
Advance Steel server so they can be exercised without AutoCAD, and cover them with tests.

---

## 2. Reference Specifications & Rules

- [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md) §4
- [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md) §4

The payload keys in SPEC-002 §4 are the interface between this contract and CONTRACT-004A. They are
not negotiable: a renamed key silently breaks the plugin that is being built in parallel.

---

## 3. Strict Permitted Scope (File Whitelist)

- `src/mcp_server/tools/production_tools.py` (new)
- `src/mcp_server/server.py` (tool registration only)
- `tests/mocks/fixtures.py` (production fixtures)
- `tests/mocks/mock_as_plugin.py` (three new endpoints)
- `tests/mcp/test_production_tools.py` (new)

No C# file. `src/as_plugin/**` belongs to CONTRACT-004A and is being modified concurrently.

---

## 4. Implementation Guidelines

- Follow the existing idiom of `src/mcp_server/tools/diagnostic_tools.py` and `modeling_tools.py`:
  each tool is a module-level function taking `client: AdvanceSteelIpcClient` first and returning the
  raw response envelope. No new dependency, standard library only.
- `run_automatic_numbering` and `export_dstv_nc_files` are `client.post`; `get_drawing_status` is
  `client.get` with `assembly_marks` URL-encoded as a comma separated query argument.
- Omit optional parameters from the POST body when the caller did not supply them, so the add-in
  applies its own defaults rather than receiving `null`.
- Mock endpoints must be deterministic and must honour the request: exporting two handles returns
  two file entries, and an `export-nc` call against the unnumbered-part fixture returns the
  `UNNUMBERED_MODEL` error envelope with status 409.
- Tests assert behaviour, not restated constants: mark traceability (every exported file maps to a
  single-part mark), the numbering conflict path, and the 409 refusal before export.

---

## 5. Mandatory Verification Commands

```powershell
python -m pytest tests/ -q
```

---

## 6. Definition of Done (DoD)
- [x] `pytest tests/ -q` is green, with the 26 pre-existing tests still passing.
- [x] The three tools are registered on the FastMCP server with typed signatures and docstrings.
- [x] Mock responses match SPEC-002 §4 key for key.
- [x] No file outside the whitelist was touched.

---

## 7. Director Acceptance (2026-09-19)

**Verification run by the Director**, because the worker could not produce it: `python` is not on the
PATH of the Codex shell, so its own DoD evidence was missing. The acceptance does not rest on the
worker's word.

```text
$ python -m pytest tests/ -q
................................                                         [100%]
32 passed in 2.42s
```

26 pre-existing + 5 delivered by the worker + 1 added during acceptance.

**Whitelist**: clean. Exactly the five contracted files. The concurrent `CommandDispatcher.cs` change
in the working tree belongs to CONTRACT-004A and was correctly left alone.

**Two notes on the delivery:**
1. The worker bound its mock to port 5056 instead of 5055, so the new suite cannot collide with the
   existing one. Not asked for, correct anyway.
2. **Gap closed by the Director**: `engine_command` (SPEC-004 §4) was not exposed by the tools. The
   worker read the spec before that escape hatch was added to it, so this is a Director sequencing
   error, not a worker defect. Added as a pass-through on both POST tools with a test pinning that it
   reaches the add-in — a silently dropped parameter would be indistinguishable from a missing engine.
