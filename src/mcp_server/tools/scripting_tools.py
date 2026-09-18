"""Roslyn C# scripting execution tool for Advance Steel MCP."""

from typing import Any, Dict
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient


def execute_csharp_script(client: AdvanceSteelIpcClient, script_code: str) -> Dict[str, Any]:
    """Execute dynamic C# Roslyn script in Advance Steel with automatic transaction rollback."""
    payload = {"script_code": script_code}
    return client.post("script/execute", payload)
