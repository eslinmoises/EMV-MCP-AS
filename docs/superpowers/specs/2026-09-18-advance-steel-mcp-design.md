# Architecture and Design Specification: Autodesk Advance Steel MCP (`EMV-MCP-AS`)

- **Author / Lead Orchestrator**: Antigravity (Gemini 3.8 Flash)
- **Target Workers**: Claude Code (Terminal), Cursor AI, Codex
- **Date**: 2026-09-18
- **Status**: Draft - Pending Spec Review
- **Repository Visibility**: Open Source (Community AEC / Structural Detailing)

---

## 1. Executive Summary & Vision

`EMV-MCP-AS` is an open-source Model Context Protocol (MCP) ecosystem designed to connect Large Language Models (specifically **Claude Code**, **Antigravity**, **Cursor**, and **Codex**) with **Autodesk Advance Steel 2025 and 2026** running on modern **.NET 8**.

The project addresses two primary operational missions:
1. **Generative Shop Detailing (Modeling from Blueprints)**: Enable AI agents to consume structural engineering drawings, architectural blueprints, and engineering specifications to model 3D structural steel elements (beams, plates, bolts, welds, joints) directly inside an active Advance Steel session.
2. **Diagnostic & Detailing Copilot (Troubleshooting Manual Work)**: Serve as an intelligent inspection doctor that unblocks steel detailers by diagnosing broken joints, verifying workshop vs. site welds, auditing assembly composition and Main Part assignments, checking numbering conflicts, and detecting spatial clashes.

Development follows strict **Spec-Driven Development (SDD)** with a formal **Agentic Orchestration Framework** where Antigravity acts as Director, decomposing specifications into atomic Agentic Contracts executed by terminal-based workers (Claude Code, Codex, Cursor).

---

## 2. High-Level Architecture

The system uses a **Decoupled Hybrid Architecture** comprising three core tiers:

```mermaid
graph TD
    subgraph Client Tier
        CC["Claude Code (CLI)"]
        AGY["Antigravity (IDE / CLI)"]
        CUR["Cursor AI / Codex"]
    end

    subgraph MCP Server Tier [Python 3.11+ / FastMCP]
        MCP["EMV MCP Server (emv-mcp-as)"]
        STDIO["Transport: stdio / JSON-RPC"]
        CLI["uvx emv-mcp-as"]
        HTTP_CLIENT["Local REST Client (httpx)"]
    end

    subgraph CAD Process Tier [Autodesk Advance Steel 2025/2026 - acad.exe]
        ADDIN["Advance Steel .NET 8 Add-in (.bundle)"]
        IPC["In-Process IPC Server (HttpListener: 127.0.0.1:5055)"]
        DISP["AutoCAD Main Thread Dispatcher (DocumentLock)"]
        ROSLYN["Roslyn C# Scripting Engine"]
        AS_API["Autodesk.AdvanceSteel.API (ASObjectsMgd / ASGeometryMgd)"]
        DWG[("Active DWG Database")]
    end

    CC -->|stdio| MCP
    AGY -->|stdio| MCP
    CUR -->|stdio| MCP
    MCP --> HTTP_CLIENT
    HTTP_CLIENT -->|HTTP / JSON (localhost:5055)| IPC
    IPC --> DISP
    DISP --> ROSLYN
    DISP --> AS_API
    AS_API --> DWG
```

### Key Components

1. **`as_plugin` (Advance Steel In-Process .NET 8 Add-in)**:
   - Packaged as an Autodesk ApplicationPlugin (`EMV-AdvanceSteel.bundle`).
   - Implements `IExtensionApplication` (`Initialize()` and `Terminate()`).
   - Runs a lightweight background HTTP server on `127.0.0.1:5055`.
   - Dispatches incoming requests safely to the AutoCAD main execution thread via `SynchronizationContext` and `doc.LockDocument()`.
   - Contains the Roslyn C# Scripting Engine and Viewport Frame Capture mechanism.

2. **`mcp_server` (External MCP Server in Python with FastMCP)**:
   - Installed and executed via `uvx emv-mcp-as` or `pip install -e .`.
   - Communicates with AI agents via standard MCP `stdio`.
   - Exposes clean, typed tools for reading, modeling, verifying welds/assemblies, and executing scripts.
   - Forwards commands to the internal `as_plugin` IPC endpoint with automatic retry, timeout, and structured error reporting.

3. **`orchestration` & `specs` (Spec-Driven Agentic Governance)**:
   - `docs/specs/`: System specifications defining API contracts, schemas, and modeling rules.
   - `orchestration/contracts/`: Discrete contracts assigned to worker agents with explicit input/output definitions and acceptance tests.
   - `rules/`: Domain-specific steel detailing constraints (units, profile families, welding standards, transaction safety).

---

## 3. Communication & IPC Protocol Specification

All internal IPC communication between `mcp_server` and `as_plugin` uses standard HTTP/1.1 with JSON payloads over `http://127.0.0.1:5055/api/v1`.

### Standard Response Envelope

```json
{
  "success": true,
  "data": {},
  "error": null,
  "execution_time_ms": 42
}
```

In case of error:
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "SECTION_NOT_FOUND",
    "message": "Profile section 'HEA300_INVALID' was not found in Advance Steel AstorProfiles database.",
    "details": "Table: AstorProfiles.mdb, Searched Family: HEA",
    "suggestion": "Call get_available_profiles(family='HEA') to inspect valid section names."
  },
  "execution_time_ms": 15
}
```

### IPC Endpoints

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/v1/health` | Session status, Advance Steel version, active DWG path, unit system. |
| `GET` | `/api/v1/elements/selected` | Returns all properties of currently selected elements in viewport. |
| `POST` | `/api/v1/elements/query` | Filter elements by handle, type, layer, model role, or 3D bounding box. |
| `POST` | `/api/v1/elements/beam` | Create straight or curved beam with section, material, and alignment. |
| `POST` | `/api/v1/elements/plate` | Create rectangular or polygonal plate with thickness and material. |
| `POST` | `/api/v1/elements/joint` | Create parametric joint/connection between existing members. |
| `POST` | `/api/v1/elements/modify` | Update properties (role, material, coating, rotation) of existing elements. |
| `GET` | `/api/v1/assembly/verify-welds` | Inspect welds, shop vs. site location, throat thickness, and connected parts. |
| `GET` | `/api/v1/assembly/main-part` | Identify current Main Part of an assembly and validate standard rules. |
| `POST` | `/api/v1/assembly/set-main-part` | Reassign Main Part of an assembly to a specific beam/plate handle. |
| `GET` | `/api/v1/audit/assembly-integrity` | Scan for orphaned workshop parts, broken assemblies, or unnumbered items. |
| `GET` | `/api/v1/audit/clashes` | Execute native collision/clearance detection on active model or sub-selection. |
| `GET` | `/api/v1/spatial/ucs-grids` | Inspect active UCS, grid lines, and building levels. |
| `GET` | `/api/v1/viewport/capture` | Capture current 3D viewport rendered frame as Base64 PNG. |
| `POST` | `/api/v1/script/execute` | Execute C# script dynamically via Roslyn with pre-injected context. |

---

## 4. Advance Steel Add-in Internals (`as_plugin`)

### Target Framework & References
- **Target**: `net8.0-windows`
- **AutoCAD / Advance Steel Assemblies**:
  - `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll`
  - `ASObjectsMgd.dll` (Beams, Plates, Welds, Bolts, Connections, Grids)
  - `ASGeometryMgd.dll` (Point3d, Vector3d, Plane, Matrix3d)
  - `CoreMgd.dll`
- **Nuget Packages**:
  - `Microsoft.CodeAnalysis.CSharp.Scripting` (Roslyn in-memory evaluator)
  - `System.Text.Json`

### Threading & Transaction Lifecycle
AutoCAD's database is single-threaded. Any access outside the main UI thread will cause an `AccessViolationException` or crash.
1. The background `HttpListener` accepts incoming HTTP requests on a worker pool thread.
2. The payload is parsed into an internal `IModelCommand`.
3. The command is queued to AutoCAD's execution dispatcher (`Application.DocumentManager.MdiActiveDocument`).
4. Inside the dispatched delegate on the main thread:
   ```csharp
   Document doc = Application.DocumentManager.MdiActiveDocument;
   using (DocumentLock docLock = doc.LockDocument())
   using (Transaction trans = doc.TransactionManager.StartTransaction())
   {
       try
       {
           // 1. Advance Steel Database Context access
           // 2. Element creation / modification / inspection
           trans.Commit();
           return CommandResult.Ok(responseObject);
       }
       catch (Exception ex)
       {
           trans.Abort();
           return CommandResult.Fail(ex);
       }
   }
   ```

### Dynamic Roslyn Scripting Sandbox
The `execute_csharp_script` tool allows LLMs to write procedural logic when atomic tools are insufficient.
- The script is evaluated with a pre-configured `ScriptGlobals` context:
  - `ASDocument Document`: Active AutoCAD document.
  - `ASDatabase Database`: Active Advance Steel model database.
  - `Matrix3d ActiveUCS`: Active User Coordinate System transformation matrix.
  - Pre-imported namespaces: `Autodesk.AdvanceSteel.CADAccess`, `Autodesk.AdvanceSteel.Modelling`, `Autodesk.AdvanceSteel.Geometry`.

---

## 5. MCP Tool Surface Specification

### Domain 1: Diagnostic & Detailing Doctor (Read & Verification)
1. **`get_active_model_info`**: Retrieves DWG filename, AS version, active units, total beam/plate/weld count.
2. **`get_selected_elements`**: Reads properties of user's current selection in AS viewport (handles, profile, material, model role, length, weight, bounding box).
3. **`get_ucs_and_grids`**: Returns current UCS origin/vectors, all grid axes (e.g. A-E, 1-8), and level elevations.
4. **`query_elements_in_box`**: Finds all members intersecting a specified 3D bounding box (essential for inspecting joint nodes).
5. **`verify_welds_and_assemblies`**:
   - Parameters: `element_handles: list[str] | None`
   - Returns: List of connected welds, weld type (fillet, butt), location (`Workshop` vs `Site`), throat size, and whether both connected parts share the same assembly mark.
6. **`inspect_main_part`**:
   - Parameters: `assembly_handle_or_mark: str`
   - Returns: Handle and role of the Main Part, list of attached secondary parts, weight distribution, and warning if the Main Part violates standard rules (e.g., if a stiffener is mistakenly assigned as Main Part instead of the main column/beam).
7. **`audit_assembly_integrity`**: Scans the model for unnumbered parts, orphaned secondary parts without workshop welds, and assemblies with missing main parts.
8. **`detect_clashes_and_clearances`**: Invokes Advance Steel collision check, returning collision volumes, interfering element handles, and conflict coordinates.
9. **`capture_viewport`**: Captures a PNG screenshot of the current 3D model view to feed into multimodal vision models.

### Domain 2: Generative Modeling & Editing (Write)
1. **`create_straight_beam`**:
   - Parameters: `start_point: [x,y,z]`, `end_point: [x,y,z]`, `section_name: str` (e.g. "IPE300", "HEB240"), `material: str` (default "S275JR"), `model_role: str` (e.g. "Column", "Beam", "Rafter"), `reference_axis: str` (default "Center").
2. **`create_curved_beam`**:
   - Parameters: `start_point: [x,y,z]`, `mid_point: [x,y,z]`, `end_point: [x,y,z]`, `section_name: str`, `material: str`, `model_role: str`.
3. **`create_plate`**:
   - Parameters: `contour_points: list[[x,y,z]]`, `thickness: float`, `material: str`, `model_role: str`.
4. **`set_main_part`**:
   - Parameters: `assembly_handle: str`, `new_main_part_handle: str`.
   - Explicitly designates the main part of an assembly.
5. **`apply_beam_cut_or_notch`**:
   - Parameters: `beam_handle: str`, `cutting_plane: dict`, `cut_type: str`.
6. **`modify_element_properties`**:
   - Parameters: `element_handle: str`, `properties: dict`.

### Domain 3: Procedural Scripting
1. **`execute_csharp_script`**:
   - Parameters: `script_code: str`
   - Executes dynamic Roslyn C# code with rollback safety on exception.

---

## 6. Multi-Agent Orchestration & Agentic Contracts (SDD Governance)

### Agent Roles Matrix

| Agent | Environment | Assigned Responsibility |
| :--- | :--- | :--- |
| **Antigravity (Director)** | IDE / Gemini 3.8 Flash | Chief Architect. Manages `docs/specs/`, crafts Agentic Contracts, verifies compliance against specifications, and conducts code review / approval. |
| **Claude Code** | Terminal | Primary Implementation Worker. Executes C# .NET 8 plugin development, Roslyn scripting engine, FastMCP server logic, and IPC client. |
| **Cursor AI** | Terminal / Editor | Worker for specialized refactoring, test generation, and `.cursor/rules` integration. |
| **Codex** | Terminal | Worker for boilerplate creation, AST validation, and documentation generation. |

### Lifecycle of an Agentic Contract

```text
[Feature Need] 
       │
       ▼
[Director (Antigravity)]: Writes / Updates Spec in docs/specs/
       │
       ▼
[Director (Antigravity)]: Creates Contract in orchestration/contracts/CONTRACT-XXX.md
       │
       ▼
[Worker Agent (Claude Code)]: Reads Contract -> Implements Code -> Runs Verification Tests
       │
       ▼
[Director (Antigravity)]: Reviews Output against DoD -> Merges -> Closes Contract
```

### Agentic Contract Structure (`orchestration/contracts/template-contract.md`)
Every contract contains:
1. **Contract ID & Title**: e.g., `CONTRACT-001-AS-IPC-SERVER`.
2. **Assigned Worker**: e.g., Claude Code (Terminal).
3. **Reference Spec**: e.g., `docs/specs/001-architecture-and-ipc.md`.
4. **Permitted Scope**: Explicit whitelist of files the agent is allowed to create or modify.
5. **Interface Contract**: Exact method signatures, endpoints, and JSON schemas to implement.
6. **Mandatory Verification Commands**: Exact commands to run (e.g. `dotnet build`, `pytest tests/test_ipc.py`).
7. **Definition of Done (DoD)**: Checklist of conditions required for contract completion.

---

## 7. Testing & Quality Assurance Strategy

To enable agents to build and verify code rapidly without requiring a running Autodesk license at every step:

1. **Mock IPC Server (`tests/mocks/mock_as_plugin.py`)**:
   - A standalone Python server mocking the `127.0.0.1:5055` IPC endpoints.
   - Provides realistic responses for beam creation, query responses, weld verifications, and simulated errors.
   - Used by CI/CD workflows and local `pytest` test runs.
2. **Unit Testing**:
   - **`tests/mcp/`**: Pytest suite testing all FastMCP tools against the mock server.
   - **`tests/plugin/`**: C# xUnit suite testing serialization, Roslyn compilation, and command validation logic.
3. **Integration Testing (`tests/integration/`)**:
   - End-to-end tests validating the full pipeline from MCP tool call -> HTTP IPC -> Mock/Live Engine -> JSON response.

---

## 8. Directory Layout & Initial Scaffolding Plan

```text
EMV-MCP-AS/
├── .github/
│   └── workflows/
│       ├── test-mcp.yml
│       └── build-plugin.yml
├── docs/
│   ├── specs/
│   │   ├── 001-architecture-and-ipc.md
│   │   ├── 002-data-models-and-schemas.md
│   │   ├── 003-scripting-engine-roslyn.md
│   │   └── 004-mcp-tools-specification.md
│   └── guides/
│       ├── installation-bundle.md
│       └── quickstart-claude-code.md
├── orchestration/
│   ├── director-handbook.md
│   ├── agent-matrix.md
│   ├── workflow.md
│   └── contracts/
│       ├── template-contract.md
│       └── CONTRACT-000-scaffolding.md
├── rules/
│   ├── advance-steel-modeling.md
│   └── transaction-safety.md
├── src/
│   ├── as_plugin/
│   │   ├── EMV-AdvanceSteel.bundle/
│   │   │   └── PackageContents.xml
│   │   ├── Host/
│   │   ├── Server/
│   │   ├── Commands/
│   │   ├── Scripting/
│   │   └── EMV.AdvanceSteel.Plugin.csproj
│   └── mcp_server/
│       ├── tools/
│       ├── client/
│       ├── pyproject.toml
│       └── server.py
├── tests/
│   ├── mocks/
│   ├── mcp/
│   └── plugin/
├── .cursor/rules/
├── AGENTS.md
├── CLAUDE.md
├── README.md
└── LICENSE
```
