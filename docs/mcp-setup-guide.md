# Multi-Client MCP Setup Guide: EMV-AdvanceSteel

`EMV-MCP-AS` provides a native **Model Context Protocol (MCP)** server enabling AI coding agents to control Autodesk Advance Steel 2025/2026 for structural steel detailing, fabrication drawing generation, Detailing Doctor diagnostics, and BIM coordination.

---

## Architecture Summary

```text
┌──────────────────────────────────────────────────────────────────┐
│                          AI Clients                              │
│   Antigravity 2.0 / IDE  │  Claude Desktop / Code  │  Cursor     │
│   OpenAI Codex CLI       │  ChatGPT Desktop (Dev)                │
└─────────────────────────────────┬────────────────────────────────┘
                                  │ stdio / SSE (FastMCP)
                                  ▼
┌──────────────────────────────────────────────────────────────────┐
│                   EMV FastMCP Server (Python)                    │
│   src/mcp_server/server.py                                       │
└─────────────────────────────────┬────────────────────────────────┘
                                  │ HTTP REST (Port 5055)
                                  ▼
┌──────────────────────────────────────────────────────────────────┐
│             Autodesk Advance Steel In-Process Add-in             │
│   EMV.AdvanceSteel.Plugin.dll (AutoCAD/AS main thread IPC)       │
└──────────────────────────────────────────────────────────────────┘
```

---

## 1. Antigravity 2.0 & Antigravity IDE

Antigravity natively discovers workspace MCP servers configured in `.agents/mcp_config.json`:

File: `.agents/mcp_config.json`
```json
{
  "mcpServers": {
    "emv-advance-steel": {
      "command": "python",
      "args": ["-m", "src.mcp_server.server"],
      "cwd": "C:\\Proyectos\\EMV-MCP-AS",
      "env": {
        "PYTHONPATH": "C:\\Proyectos\\EMV-MCP-AS",
        "EMV_AS_API_BASE_URL": "http://127.0.0.1:5055/api/v1"
      }
    }
  }
}
```

---

## 2. Claude Desktop

Add `emv-mcp-as` to your Claude Desktop configuration file:
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "emv-advance-steel": {
      "command": "python",
      "args": ["-m", "src.mcp_server.server"],
      "cwd": "C:\\Proyectos\\EMV-MCP-AS",
      "env": {
        "PYTHONPATH": "C:\\Proyectos\\EMV-MCP-AS",
        "EMV_AS_API_BASE_URL": "http://127.0.0.1:5055/api/v1"
      }
    }
  }
}
```

---

## 3. Claude Code CLI

Add the server via Claude Code CLI in terminal:

```bash
claude mcp add emv-advance-steel python -m src.mcp_server.server --cwd C:\Proyectos\EMV-MCP-AS
```

Verify connection:
```bash
claude mcp list
```

---

## 4. Cursor AI

Cursor AI reads workspace MCP servers from `.cursor/mcp.json`:

File: `.cursor/mcp.json`
```json
{
  "mcpServers": {
    "emv-advance-steel": {
      "command": "python",
      "args": ["-m", "src.mcp_server.server"],
      "cwd": "C:\\Proyectos\\EMV-MCP-AS",
      "env": {
        "PYTHONPATH": "C:\\Proyectos\\EMV-MCP-AS",
        "EMV_AS_API_BASE_URL": "http://127.0.0.1:5055/api/v1"
      }
    }
  }
}
```

---

## 5. OpenAI Codex CLI

Codex CLI reads `.codex/config.json`:

File: `.codex/config.json`
```json
{
  "mcpServers": {
    "emv-advance-steel": {
      "command": "python",
      "args": ["-m", "src.mcp_server.server"],
      "cwd": "C:\\Proyectos\\EMV-MCP-AS",
      "env": {
        "PYTHONPATH": "C:\\Proyectos\\EMV-MCP-AS",
        "EMV_AS_API_BASE_URL": "http://127.0.0.1:5055/api/v1"
      }
    }
  }
}
```

---

## 6. ChatGPT Desktop App (Developer Mode)

ChatGPT Desktop connects to MCP servers running via SSE (Server-Sent Events) or stdio adapters:

1. Launch FastMCP in SSE mode:
   ```bash
   python -m src.mcp_server.server --transport sse --port 8000
   ```
2. In ChatGPT Desktop -> Settings -> Developer -> MCP Servers -> Add Server:
   - **Name**: `EMV Advance Steel`
   - **URL**: `http://127.0.0.1:8000/sse`

---

## 7. Verifying the MCP Server

Test the server offline using the mock server or live with Advance Steel open:

```bash
# Terminal 1: Launch mock server (for offline testing)
python -m tests.mocks.mock_as_plugin

# Terminal 2: Run test suite
python -m pytest tests/ -v
```
