# Agentic Contract: CONTRACT-004B-PRODUCTION-MCP-SURFACE

- **Parent Contract**: [`CONTRACT-004`](./CONTRACT-004-numbering-and-dstv.md)
- **Assigned Worker**: **Codex CLI** (scaffolding worker, mock data generator)
- **Director / Supervisor**: Claude Code acting as Orchestration Director
- **Status**: **ACTIVE**
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
- [ ] `pytest tests/ -q` is green, with the 26 pre-existing tests still passing.
- [ ] The three tools are registered on the FastMCP server with typed signatures and docstrings.
- [ ] Mock responses match SPEC-002 §4 key for key.
- [ ] No file outside the whitelist was touched.
