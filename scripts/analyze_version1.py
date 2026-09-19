import json
import urllib.request
from collections import Counter

def analyze_model():
    req = urllib.request.Request(
        "http://127.0.0.1:5055/api/v1/elements/query",
        data=b"{}",
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        res = json.loads(resp.read().decode("utf-8"))

    if not res.get("success"):
        print("Error al consultar:", res)
        return

    elements = res["data"].get("elements", [])
    print(f"=== ANÁLISIS DE LA ESTRUCTURA EN VERSION1.DWG ({len(elements)} BARRAS) ===\n")

    sections = Counter()
    roles = Counter()
    types = Counter()

    for el in elements:
        sections[el.get("section_name", "Sin perfil")] += 1
        roles[el.get("role", "None")] += 1
        types[el.get("type", "None")] += 1

    print("1. PERFILES UTILIZADOS:")
    for sec, count in sections.most_common():
        print(f"   - {sec}: {count} elementos")

    print("\n2. TIPOS DE ELEMENTOS:")
    for t, count in types.most_common():
        print(f"   - {t}: {count}")

    # Inspect coordinates using Roslyn script
    code = """
    var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
    var filter = new Autodesk.AdvanceSteel.CADAccess.ClassTypeFilter(false);
    filter.AppendAcceptedBaseClass(Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType.kBeam);
    Autodesk.AdvanceSteel.CADAccess.DatabaseManager.GetModelObjectIds(out var ids, filter);
    
    double minX = double.MaxValue, maxX = double.MinValue;
    double minY = double.MaxValue, maxY = double.MinValue;
    double minZ = double.MaxValue, maxZ = double.MinValue;

    var yPositions = new System.Collections.Generic.HashSet<double>();

    int count = 0;
    foreach (var id in ids)
    {
        var fo = Autodesk.AdvanceSteel.CADAccess.DatabaseManager.Open(id);
        if (fo is Autodesk.AdvanceSteel.Modelling.StraightBeam b)
        {
            count++;
            var p1 = b.GetPointAtStart();
            var p2 = b.GetPointAtEnd();
            minX = System.Math.Min(minX, System.Math.Min(p1.x, p2.x));
            maxX = System.Math.Max(maxX, System.Math.Max(p1.x, p2.x));
            minY = System.Math.Min(minY, System.Math.Min(p1.y, p2.y));
            maxY = System.Math.Max(maxY, System.Math.Max(p1.y, p2.y));
            minZ = System.Math.Min(minZ, System.Math.Min(p1.z, p2.z));
            maxZ = System.Math.Max(maxZ, System.Math.Max(p1.z, p2.z));

            // Round Y to nearest 10mm to group portal frame planes
            yPositions.Add(System.Math.Round(p1.y / 10.0) * 10.0);
        }
    }

    var sortedY = new System.Collections.Generic.List<double>(yPositions);
    sortedY.Sort();

    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"Beams evaluated: {count}");
    sb.AppendLine($"Extents X (Luz): min={minX:F1} mm, max={maxX:F1} mm, total={maxX - minX:F1} mm ({ (maxX-minX)/1000.0:F2} m)");
    sb.AppendLine($"Extents Y (Longitud nave): min={minY:F1} mm, max={maxY:F1} mm, total={maxY - minY:F1} mm ({ (maxY-minY)/1000.0:F2} m)");
    sb.AppendLine($"Extents Z (Altura): min={minZ:F1} mm, max={maxZ:F1} mm, total={maxZ - minZ:F1} mm ({ (maxZ-minZ)/1000.0:F2} m)");
    sb.AppendLine($"Portal frames detected on Y planes: {sortedY.Count}");
    sb.AppendLine($"Y planes: {string.Join(", ", sortedY)}");

    Print(sb.ToString());
    return sb.ToString();
    """

    req2 = urllib.request.Request(
        "http://127.0.0.1:5055/api/v1/script/execute",
        data=json.dumps({"script_code": code}).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req2, timeout=20) as resp2:
            res2 = json.loads(resp2.read().decode("utf-8"))
        print("\n3. GEOMETRÍA Y DIMENSIONES:")
        data = res2.get("data", {})
        out = data.get("output") or data.get("return_value")
        print(out if out else res2)
    except urllib.error.HTTPError as e:
        print("Roslyn error:", e.read().decode("utf-8"))

if __name__ == "__main__":
    analyze_model()
