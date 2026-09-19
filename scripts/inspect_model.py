import json
import urllib.request

def inspect():
    url = "http://127.0.0.1:5055/api/v1/elements/query"
    req = urllib.request.Request(
        url,
        data=b"{}",
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    with urllib.request.urlopen(req, timeout=10) as resp:
        res = json.loads(resp.read().decode("utf-8"))
    
    if not res.get("success"):
        print("Error al consultar el modelo:", res)
        return

    elements = res["data"].get("elements", [])
    print(f"=== TOTAL ELEMENTOS EN EL MODELO: {len(elements)} ===\n")
    for el in elements:
        handle = el.get("handle", "")
        el_type = el.get("type", "")
        role = el.get("role", "None")
        sec = el.get("section_name", "-")
        mat = el.get("material", "-")
        length = el.get("length_mm", 0.0)
        weight = el.get("weight_kg", 0.0)
        center = el.get("center_point", [])
        c_str = f"[{center[0]:.1f}, {center[1]:.1f}, {center[2]:.1f}]" if len(center) >= 3 else str(center)
        print(f"Handle: {handle:6} | Tipo: {el_type:18} | Rol: {str(role):10} | Perfil: {str(sec):12} | L: {length:6.1f} mm | Centro: {c_str}")

if __name__ == "__main__":
    inspect()
