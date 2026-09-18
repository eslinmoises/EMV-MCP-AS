"""Example 02: Detailing Doctor & Shop Model Auditing with EMV-MCP-AS.

This script demonstrates how an AI agent unblocks manual steel detailers by diagnosing:
- Elements currently selected in viewport
- Active coordinate systems (WCS vs rotated UCS) and structural grids
- Orphaned workshop parts (stiffeners or plates without workshop welds)
- Broken shop assemblies and Main Part violations
- 3D spatial collisions and member clashes
"""

from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import diagnostic_tools


def run_model_health_audit(client: AdvanceSteelIpcClient):
    print("=========================================================")
    print("   ADVANCE STEEL DETAILING DOCTOR - MODEL HEALTH AUDIT   ")
    print("=========================================================")

    # 1. Model Info
    info = diagnostic_tools.get_active_model_info(client)
    if not info.get("success"):
        print(f"Connection failed: {info.get('error')}")
        return

    data = info["data"]
    print(f"\n[1] DWG: {data.get('active_dwg')} | Units: {data.get('units')} | Version: {data.get('as_version')}")

    # 2. Inspect Active Selection
    selected = diagnostic_tools.get_selected_elements(client)
    if selected.get("success") and selected.get("data"):
        items = selected["data"]
        print(f"\n[2] Active Viewport Selection ({len(items)} items):")
        for item in items:
            print(f"    - Handle: {item.get('handle')} | Role: {item.get('model_role')} | Section: {item.get('section_name')}")
    else:
        print("\n[2] No elements currently selected in viewport.")

    # 3. UCS and Grids Check
    spatial = diagnostic_tools.get_ucs_and_grids(client)
    if spatial.get("success"):
        ucs_data = spatial["data"]
        print(f"\n[3] Coordinate System & Grids:")
        print(f"    - Active UCS: {ucs_data.get('active_ucs', {}).get('name')}")
        print(f"    - Structural Grid Axes: {len(ucs_data.get('grid_axes', []))} axes detected")

    # 4. Assembly Integrity Audit
    audit = diagnostic_tools.audit_assembly_integrity(client)
    if audit.get("success"):
        rep = audit["data"]
        findings = rep.get("findings", [])
        print(f"\n[4] Assembly Integrity Scan ({len(findings)} issues found):")
        if not findings:
            print("    [OK] Clean: All workshop plates are bound to primary members.")
        for f in findings:
            print(f"    [WARN] [{f.get('code')}] Handle {f.get('handle')}: {f.get('message')}")
            print(f"       Suggestion: {f.get('suggestion')}")

    # 5. Clash Detection
    clashes = diagnostic_tools.detect_clashes_and_clearances(client)
    if clashes.get("success"):
        rep = clashes["data"]
        items = rep.get("clashes", [])
        print(f"\n[5] Clash Detection ({len(items)} collisions found, Method: {rep.get('method')}):")
        if not items:
            print("    [OK] Clean: No member geometry clashes detected.")
        for c in items:
            print(f"    [CLASH] Clash between {c.get('element_a')} and {c.get('element_b')} at {c.get('intersection_point')}")

    print("\n=========================================================")
    print("                    AUDIT COMPLETED                      ")
    print("=========================================================")


if __name__ == "__main__":
    ipc_client = AdvanceSteelIpcClient()
    run_model_health_audit(ipc_client)
