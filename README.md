# EMV-MCP-AS: Advance Steel Model Context Protocol (MCP)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Advance Steel](https://img.shields.io/badge/Autodesk%20Advance%20Steel-2025%20%7C%202026-orange.svg)](https://www.autodesk.com/products/advance-steel)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.11+-brightgreen.svg)](https://www.python.org/)
[![Model Context Protocol](https://img.shields.io/badge/MCP-Standard-black.svg)](https://modelcontextprotocol.io/)

> **Open-Source AI Agent Bridge for Autodesk Advance Steel 2025 & 2026**  
> Model structural steel directly from blueprints, inspect assemblies, verify welds, and troubleshoot detailing challenges using **Claude Code**, **Antigravity**, **Cursor**, and **Codex**.

---

## 🚀 Overview

**EMV-MCP-AS** is an open-source ecosystem that connects modern Large Language Models (LLMs) to **Autodesk Advance Steel**. It brings **autonomous structural modeling** and **expert detailing diagnosis** to terminal and IDE-based AI agents.

### Core Capabilities

1. **Generative Shop Detailing from Blueprints**:
   - Provide project drawings, structural calculations, and framing plans to Claude Code or Antigravity.
   - The agent creates columns, beams, rafters, bracing, base plates, and standard connections directly inside Advance Steel.
2. **Detailing Doctor & Troubleshooting Copilot**:
   - Inspect active selections, verify workshop vs. site welds, and detect disconnected parts.
   - Audit **Main Part** assignments to prevent misoriented workshop drawings and corrupt CNC/DSTV exports.
   - Run clash detection and clearance checks in real time.
3. **Dynamic Scripting (C# Roslyn)**:
   - Execute procedural and parametric C# code in-memory without compiling or restarting Advance Steel.
4. **Multimodal Vision Feedback**:
   - Capture live 3D viewport screenshots so multimodal agents (Claude 3.7 Sonnet, Gemini) can compare the 3D model against blueprints.

---

## 🏗️ Architecture

The system uses a **Decoupled Hybrid Architecture** engineered for maximum stability inside AutoCAD's single-threaded environment:

```text
┌──────────────────────────────────────────────────────────┐
│                      Client Tier                         │
│       Claude Code (CLI)  |  Antigravity (IDE/CLI)        │
│             Cursor AI    |  Codex Terminal               │
└────────────────────────────┬─────────────────────────────┘
                             │  MCP Standard (stdio / JSON-RPC)
┌────────────────────────────▼─────────────────────────────┐
│             mcp_server (Python / FastMCP)                │
│  - Tool definitions (model, read, diagnose, script)     │
│  - Local HTTP Client (httpx)                             │
└────────────────────────────┬─────────────────────────────┘
                             │  Local REST IPC (http://127.0.0.1:5055)
┌────────────────────────────▼─────────────────────────────┐
│           as_plugin (Advance Steel In-Process)           │
│  - .NET 8 ApplicationPlugin (.bundle for AS 2025/2026)   │
│  - Threading Dispatcher (SynchronizationContext + Lock) │
│  - Roslyn C# Scripting Sandbox                          │
│  - Advance Steel .NET API (ASObjectsMgd / ASGeometryMgd) │
│  - Active DWG Database                                   │
└──────────────────────────────────────────────────────────┘
```

---

## 🛠️ Tool Catalog

### 🔍 Diagnostic & Reading Tools
- `get_active_model_info`: General model metadata, units (metric/imperial), AS version, and element counts.
- `get_selected_elements`: Read full properties of whatever is currently selected in Advance Steel.
- `verify_welds_and_assemblies`: Check workshop vs. site welds, throat sizes, and connected parts.
- `inspect_main_part` / `set_main_part`: Identify and validate Main Parts; reassign them when needed.
- `audit_assembly_integrity`: Scan for orphaned plates, disconnected joints, or unnumbered parts.
- `detect_clashes_and_clearances`: Run collision and clearance checks.
- `get_ucs_and_grids`: Inspect active coordinate systems, grid axes, and elevations.
- `query_elements_in_box`: Find all members inside a 3D bounding box (ideal for checking connection nodes).
- `capture_viewport`: Capture a PNG screenshot of the current 3D viewport for visual AI verification.

### 📐 Generative Modeling & Editing Tools
- `create_straight_beam`: Create standard profiles (HEA, HEB, IPE, UPN, tubes) between 3D points.
- `create_poly_beam`: Create continuous multi-segment polybeams or curved members defined by 3D points.
- `create_plate`: Create rectangular or polygonal contour plates.
- `create_bolt_pattern`: Create rectangular bolt patterns connecting two or more structural parts (standards DIN 931, ISO 4014, A325, etc.).
- `create_standard_joint`: Apply standard Advance Steel connection macros (BasePlate, ClipAngle, EndPlate, ApexHaunch).
- `apply_beam_cut_or_notch`: Apply cuts, miters, and notches to profiles (shortening, web/flange notches).
- `modify_element_properties`: Update material, model role, coating, and rotation by handle.

### 🏭 Production & Fabrication Tools
- `run_automatic_numbering`: Run the Advance Steel numbering engine to assign single-part (`p1`, `p2`) and assembly (`C1`, `B1`) marks with conflict reporting.
- `export_dstv_nc_files`: Generate standard DSTV (.nc / .nc1) files for CNC sawing, drilling, and plasma profiling machines.
- `get_drawing_status`: Inspect whether workshop fabrication drawings exist and are up to date with the 3D model.

### ⚡ Procedural Scripting
- `execute_csharp_script`: Execute in-memory dynamic C# code using Roslyn with transactional rollback.

---

## 🤖 Multi-Agent Orchestration (Spec-Driven Development)

This repository is built using **Spec-Driven Development (SDD)** with formal **Agentic Contracts**:

| Role | Agent | Responsibility |
| :--- | :--- | :--- |
| **Director / Lead Architect** | **Antigravity (Gemini 3.8 Flash)** | Specs management (`docs/specs/`), contract design (`orchestration/contracts/`), task decomposition, and PR approval. |
| **Worker Agent** | **Claude Code (CLI)** | Primary C# .NET 8 plugin engine, FastMCP server logic, IPC protocol client. |

### Contract Workflow
```text
Spec in docs/specs/ ➔ Contract in orchestration/contracts/ ➔ Worker Implements ➔ Tests Pass ➔ Director Verifies
```

---

## ⚡ Quickstart

### Prerequisites
- **Windows 10 / 11 (x64)**
- **Autodesk Advance Steel 2025 or 2026**
- **.NET 8 SDK** (8.0.400+)
- **Python 3.11+** with `uv` installed (`pip install uv`)

### 1. Build and Install the Add-in
Build the C# plugin and deploy the `.bundle` directly into Autodesk ApplicationPlugins:
```powershell
# Build C# add-in in Release mode
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release

# Deploy bundle to %APPDATA%/Autodesk/ApplicationPlugins/EMV-AdvanceSteel.bundle
python scripts/deploy_bundle.py
```
- **If Advance Steel is starting**: The bundle loads automatically on startup.
- **If Advance Steel is already running**: In the CAD command line, run `NETLOAD` and select:
  `%APPDATA%\Autodesk\ApplicationPlugins\EMV-AdvanceSteel.bundle\Contents\Release\EMV.AdvanceSteel.Plugin.dll`
  Verify with CAD command `EMV_MCP_STATUS`.

### 2. Configure Claude Code
Add `emv-mcp-as` to your Claude Code configuration (`~/.claude.json` or project MCP config):
```json
{
  "mcpServers": {
    "advance-steel": {
      "command": "uvx",
      "args": ["emv-mcp-as"]
    }
  }
}
```

### 3. Configure Antigravity IDE / Cursor
Configure in your IDE's MCP settings:
```json
{
  "mcpServers": {
    "advance-steel": {
      "command": "uv",
      "args": ["run", "--with", "emv-mcp-as", "emv-mcp-as"]
    }
  }
}
```

---

## 🧪 Testing Without AutoCAD

You do not need Advance Steel running to test the MCP server or build client agents. A lightweight mock server is included:
```bash
# Start mock IPC server
python tests/mocks/mock_as_plugin.py

# Run MCP test suite
pytest tests/mcp/
```

---

## 📄 Documentation

- [Architecture & Design Spec](docs/superpowers/specs/2026-09-18-advance-steel-mcp-design.md)
- [Agentic Orchestration Framework](orchestration/workflow.md)
- [Contributor Guidelines](CONTRIBUTING.md)

---

## ⚖️ License

Distributed under the **MIT License**. See [LICENSE](LICENSE) for details.
