"""Ingest tools exposing document parsing capabilities via FastMCP."""

from typing import Any, Dict, Optional, Sequence
from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.parsers.pdf_connection_parser import parse_connection_pdf


def ingest_connection_from_document(
    client: AdvanceSteelIpcClient,
    document_path: str,
    connection_type: Optional[str] = None,
    beam_handle: str = "BEAM_01",
    column_handle: str = "COL_01",
    model_immediately: bool = False,
    origin_offset: Sequence[float] = (0.0, 0.0, 0.0),
) -> Dict[str, Any]:
    """Ingest a connection calculation report (PDF) and optionally model it immediately in Advance Steel.
    
    If model_immediately is True, the extracted detailing payload is sent directly to
    model_engineered_connection in the active CAD session.
    """
    parse_result = parse_connection_pdf(
        document_path=document_path,
        connection_type=connection_type,
        beam_handle=beam_handle,
        column_handle=column_handle,
        origin_offset=origin_offset,
    )

    if not parse_result.get("success"):
        return parse_result

    if model_immediately:
        payload = parse_result["data"]["detailing_payload"]
        model_result = client.post("elements/engineered-joint", payload)
        return {
            "success": model_result.get("success", False),
            "data": {
                "specification": parse_result["data"]["specification"],
                "detailing_payload": payload,
                "modeling_result": model_result.get("data"),
            },
            "error": model_result.get("error"),
        }

    return parse_result
