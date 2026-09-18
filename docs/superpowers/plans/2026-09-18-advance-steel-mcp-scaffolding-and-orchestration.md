# Advance Steel MCP Foundation & Agentic Orchestration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete Spec-Driven Development (SDD) foundation, multi-agent orchestration framework (Antigravity Director + Claude Code Worker), domain specifications, Python FastMCP mockable server, and the first executable Agentic Contract for the Advance Steel .NET 8 plugin.

**Architecture:** A decoupled hybrid architecture where an external Python FastMCP server (`mcp_server`) communicates via local REST IPC (`http://127.0.0.1:5055`) with an in-process C# .NET 8 Add-in (`as_plugin`) inside Advance Steel 2025/2026. A mock IPC server enables 100% test coverage offline without requiring a running AutoCAD instance.

**Tech Stack:**
- Python 3.11+ / FastMCP / `httpx` / `pydantic` / `pytest`
- C# 12 / .NET 8 (`net8.0-windows`) / Autodesk Advance Steel 2025 & 2026 .NET API / `Microsoft.CodeAnalysis.CSharp.Scripting`
- Multi-Agent Orchestration: Antigravity IDE (Lead Director), Claude Code CLI (Primary Worker), Cursor AI, Codex

## Global Constraints
- All Advance Steel database access inside the CAD plugin must use `doc.LockDocument()` and `doc.TransactionManager.StartTransaction()`.
- The MCP server must communicate via standard `stdio` and expose JSON-RPC compliant tools.
- IPC between MCP and Add-in runs on `http://127.0.0.1:5055/api/v1` with a strict JSON envelope (`success`, `data`, `error`, `execution_time_ms`).
- Tests must pass offline via the mock IPC server without requiring an active Advance Steel or AutoCAD license.
- No code shall be implemented without an active Agentic Contract in `orchestration/contracts/` referencing a spec in `docs/specs/`.

---

### Task 1: Agentic Orchestration Framework & Director Governance

**Files:**
- Create: `orchestration/director-handbook.md`
- Create: `orchestration/agent-matrix.md`
- Create: `orchestration/workflow.md`
- Create: `orchestration/contracts/template-contract.md`
- Create: `AGENTS.md`
- Create: `CLAUDE.md`

**Interfaces:**
- Consumes: `docs/superpowers/specs/2026-09-18-advance-steel-mcp-design.md`
- Produces: Formal multi-agent governance, role definitions, and the standard Agentic Contract template for delegation to Claude Code.

- [ ] **Step 1: Create the Director Handbook**
  Define Antigravity's role as Director, rules of task decomposition, definition of done verification, and git branching strategy in `orchestration/director-handbook.md`.

- [ ] **Step 2: Create the Agent Matrix**
  Document the strengths, runtimes, and assignments for Antigravity (IDE), Claude Code (CLI), Cursor, and Codex in `orchestration/agent-matrix.md`.

- [ ] **Step 3: Create the SDD Lifecycle Workflow**
  Document the lifecycle (Spec -> Contract -> Worker Implementation -> Verification -> Merge) in `orchestration/workflow.md`.

- [ ] **Step 4: Create the Agentic Contract Template**
  Provide the reusable contract format in `orchestration/contracts/template-contract.md`.

- [ ] **Step 5: Create Universal AGENTS.md & CLAUDE.md**
  Write project-wide instructions for Claude Code and all AI agents.

- [ ] **Step 6: Commit Framework Files**
  Run: `git add orchestration/ AGENTS.md CLAUDE.md && git commit -m "feat(orchestration): add agentic governance and contract framework"`

---

### Task 2: Domain Specifications and Modeling Rules

**Files:**
- Create: `rules/advance-steel-modeling.md`
- Create: `rules/transaction-safety.md`
- Create: `docs/specs/001-architecture-and-ipc.md`
- Create: `docs/specs/002-data-models-and-schemas.md`
- Create: `docs/specs/003-scripting-engine-roslyn.md`
- Create: `docs/specs/004-mcp-tools-specification.md`

**Interfaces:**
- Consumes: `docs/superpowers/specs/2026-09-18-advance-steel-mcp-design.md`
- Produces: Concrete JSON schemas, REST endpoint specifications, welding/assembly rules, and Roslyn sandbox limits.

- [ ] **Step 1: Write Domain Rules for Modeling and Welds**
  Detail workshop vs. site welds, Main Part rules, standard profiles (HEA, HEB, IPE), and coordinate tolerances in `rules/advance-steel-modeling.md`.

- [ ] **Step 2: Write Transaction Safety Rules**
  Detail AutoCAD single-threading, DocumentLock patterns, and rollback mechanisms in `rules/transaction-safety.md`.

- [ ] **Step 3: Write Specs 001 to 004**
  Write detailed specifications for IPC protocol, JSON schemas, Roslyn scripting context, and MCP tools in `docs/specs/`.

- [ ] **Step 4: Commit Specifications**
  Run: `git add rules/ docs/specs/ && git commit -m "docs(specs): add core system specifications and domain rules"`

---

### Task 3: Mock IPC Server & Offline Test Fixtures

**Files:**
- Create: `tests/mocks/mock_as_plugin.py`
- Create: `tests/mocks/fixtures.py`
- Create: `tests/conftest.py`

**Interfaces:**
- Consumes: `docs/specs/001-architecture-and-ipc.md` and `docs/specs/002-data-models-and-schemas.md`
- Produces: An in-process / standalone mock HTTP server listening on `127.0.0.1:5055` returning realistic Advance Steel responses.

- [ ] **Step 1: Write failing mock server test**
  Write test ensuring the mock server can start, respond to `/api/v1/health`, `/api/v1/elements/selected`, `/api/v1/assembly/verify-welds`, and handle simulated errors.

- [ ] **Step 2: Run test to verify it fails**
  Run: `pytest tests/test_mock_server.py -v` (fails because file doesn't exist).

- [ ] **Step 3: Implement Mock Server with Mock Endpoints**
  Implement `mock_as_plugin.py` using Python standard library `http.server` or `fastapi`/`aiohttp` (zero external dependencies using `http.server` preferred for lightweight tests).

- [ ] **Step 4: Run test to verify it passes**
  Run: `pytest tests/test_mock_server.py -v`

- [ ] **Step 5: Commit Mock Server**
  Run: `git add tests/ && git commit -m "test(mocks): add mock Advance Steel IPC server and test fixtures"`

---

### Task 4: Python FastMCP Server & Client Implementation

**Files:**
- Create: `src/mcp_server/pyproject.toml`
- Create: `src/mcp_server/client/ipc_client.py`
- Create: `src/mcp_server/tools/diagnostic_tools.py`
- Create: `src/mcp_server/tools/modeling_tools.py`
- Create: `src/mcp_server/tools/scripting_tools.py`
- Create: `src/mcp_server/server.py`
- Create: `tests/mcp/test_tools.py`

**Interfaces:**
- Consumes: `docs/specs/004-mcp-tools-specification.md`, `tests/mocks/mock_as_plugin.py`
- Produces: Fully functional FastMCP server with diagnostic, modeling, weld/assembly verification, and script execution tools.

- [ ] **Step 1: Write failing tool tests against mock server**
  Write tests in `tests/mcp/test_tools.py` calling `get_active_model_info`, `verify_welds_and_assemblies`, `inspect_main_part`, `create_straight_beam`, and `execute_csharp_script`.

- [ ] **Step 2: Run tests to verify they fail**
  Run: `pytest tests/mcp/test_tools.py -v`

- [ ] **Step 3: Implement IPC Client & FastMCP Server Tools**
  Build `ipc_client.py` using `httpx`, and register all MCP tools in `server.py` mapping to `diagnostic_tools.py`, `modeling_tools.py`, and `scripting_tools.py`.

- [ ] **Step 4: Run tests to verify they pass**
  Run: `pytest tests/mcp/test_tools.py -v`

- [ ] **Step 5: Commit MCP Server**
  Run: `git add src/mcp_server/ tests/mcp/ && git commit -m "feat(mcp): implement FastMCP server with full tool catalog and IPC client"`

---

### Task 5: C# .NET 8 Plugin Scaffolding & Autodesk Bundle Structure

**Files:**
- Create: `src/as_plugin/EMV.AdvanceSteel.Plugin.csproj`
- Create: `src/as_plugin/EMV-AdvanceSteel.bundle/PackageContents.xml`
- Create: `src/as_plugin/Host/ExtensionApplication.cs`
- Create: `src/as_plugin/Server/IpcHttpServer.cs`
- Create: `src/as_plugin/Commands/CommandDispatcher.cs`

**Interfaces:**
- Consumes: `docs/specs/001-architecture-and-ipc.md`
- Produces: Compilable C# .NET 8 project with Autodesk bundle manifest and HTTP listener skeleton.

- [ ] **Step 1: Create .csproj and PackageContents.xml**
  Configure `net8.0-windows`, reference conditional AutoCAD 2025/2026 paths, include `System.Text.Json` and `Microsoft.CodeAnalysis.CSharp.Scripting`.

- [ ] **Step 2: Implement ExtensionApplication & IpcHttpServer**
  Implement `IExtensionApplication` entrypoint and background `HttpListener` on `127.0.0.1:5055` with dispatch queue to the AutoCAD main thread.

- [ ] **Step 3: Commit Plugin Scaffolding**
  Run: `git add src/as_plugin/ && git commit -m "feat(plugin): scaffold C# .NET 8 Advance Steel Add-in and bundle"`

---

### Task 6: First Executable Agentic Contract for Claude Code (CLI)

**Files:**
- Create: `orchestration/contracts/CONTRACT-001-as-ipc-server.md`

**Interfaces:**
- Consumes: All specifications and task definitions
- Produces: The formal assignment contract for **Claude Code** in terminal to take over and implement the deep Advance Steel .NET API integration.

- [ ] **Step 1: Author CONTRACT-001**
  Write `CONTRACT-001-as-ipc-server.md` assigning Claude Code to implement the command handlers, weld verification, and Roslyn evaluator against real Advance Steel DLLs.

- [ ] **Step 2: Commit CONTRACT-001**
  Run: `git add orchestration/contracts/ && git commit -m "feat(orchestration): create CONTRACT-001 for Claude Code worker"`
