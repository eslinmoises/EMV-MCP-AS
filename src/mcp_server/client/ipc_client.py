"""IPC Client connecting the MCP Server to the Advance Steel Add-in on localhost:5055."""

import json
import urllib.error
import urllib.request
from typing import Any, Dict, Optional


class AdvanceSteelIpcClient:
    def __init__(self, base_url: str = "http://127.0.0.1:5055", timeout: float = 30.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    def _request(self, method: str, endpoint: str, data: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        url = f"{self.base_url}/api/v1/{endpoint.lstrip('/')}"
        headers = {"Content-Type": "application/json; charset=utf-8"}
        body_bytes = json.dumps(data).encode("utf-8") if data is not None else None

        req = urllib.request.Request(url, data=body_bytes, headers=headers, method=method)

        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as response:
                resp_text = response.read().decode("utf-8")
                return json.loads(resp_text)
        except urllib.error.HTTPError as e:
            err_text = e.read().decode("utf-8")
            try:
                return json.loads(err_text)
            except Exception:
                return {
                    "success": False,
                    "data": None,
                    "error": {
                        "code": f"HTTP_{e.code}",
                        "message": str(e),
                        "details": err_text,
                        "suggestion": "Check Advance Steel connection and parameters."
                    }
                }
        except urllib.error.URLError as e:
            return {
                "success": False,
                "data": None,
                "error": {
                    "code": "CONNECTION_REFUSED",
                    "message": f"Could not connect to Advance Steel IPC at {self.base_url}",
                    "details": str(e),
                    "suggestion": "Verify Advance Steel 2025/2026 is open and the EMV-AdvanceSteel add-in is loaded."
                }
            }

    def get(self, endpoint: str) -> Dict[str, Any]:
        return self._request("GET", endpoint)

    def post(self, endpoint: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        return self._request("POST", endpoint, payload)
