# Specification 001: System Architecture and IPC Protocol

- **Spec ID**: SPEC-001
- **Status**: APPROVED
- **Target Components**: `src/as_plugin`, `src/mcp_server`

---

## 1. Physical Architecture

```text
+-----------------------+               +-----------------------------------+
|  Python FastMCP Host  |               |  Advance Steel Process (acad.exe) |
|  - MCP Stdio Transport|               |  - Advance Steel 2025 / 2026      |
|  - JSON-RPC Interface |               |  - .NET 8 Runtime                 |
|  - httpx Client       |               |  - EMV-AdvanceSteel.bundle        |
+-----------+-----------+               +-----------------+-----------------+
            |                                             ^
            | HTTP/1.1 POST/GET                           |
            | http://127.0.0.1:5055/api/v1/...            |
            +---------------------------------------------+
```

## 2. IPC Wire Protocol
- **Transport**: HTTP/1.1 REST over Loopback (`127.0.0.1`).
- **Port**: `5055` (configurable via `AS_MCP_PORT` env var).
- **Encoding**: UTF-8 JSON (`application/json`).
- **Timeout**: Default 30,000 ms per request.

## 3. Standard JSON Response Envelope

### Success Envelope
```json
{
  "success": true,
  "data": {},
  "error": null,
  "execution_time_ms": 24
}
```

### Error Envelope
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "ERROR_CODE_ENUM",
    "message": "Human readable message",
    "details": "Technical or stack information",
    "suggestion": "Corrective action for the LLM agent"
  },
  "execution_time_ms": 12
}
```

## 4. Standard Healthcheck Endpoint
- **URL**: `GET /api/v1/health`
- **Response Data**:
  ```json
  {
    "status": "online",
    "as_version": "2026",
    "acad_version": "25.0",
    "active_dwg": "C:\\Projects\\Model_01.dwg",
    "units": "Metric",
    "element_count": {
      "beams": 142,
      "plates": 88,
      "welds": 310,
      "bolts": 420
    }
  }
  ```
