import json
import urllib.request

code = """
var filter = new Autodesk.AdvanceSteel.CADAccess.ClassTypeFilter(false);
filter.AppendAcceptedBaseClass(Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType.kBeam);
Autodesk.AdvanceSteel.CADAccess.DatabaseManager.GetModelObjectIds(out var ids, filter);

var frameBeams = new System.Collections.Generic.List<string>();

foreach (var id in ids)
{
    var fo = Autodesk.AdvanceSteel.CADAccess.DatabaseManager.Open(id);
    if (fo is Autodesk.AdvanceSteel.Modelling.StraightBeam b)
    {
        var p1 = b.GetPointAtStart();
        var p2 = b.GetPointAtEnd();
        if (System.Math.Abs(p1.y) < 100.0 && System.Math.Abs(p2.y) < 100.0)
        {
            var len = p1.DistanceTo(p2);
            var dir = p2.Subtract(p1).Normalize();
            frameBeams.Add($"Start=({p1.x:F0},{p1.z:F0}) -> End=({p2.x:F0},{p2.z:F0}) | L={len:F0} mm | dz={dir.z:F2}, dx={dir.x:F2}");
        }
    }
}

var sb = new System.Text.StringBuilder();
sb.AppendLine($"=== BARRAS DEL PÓRTICO EN Y=0 ({frameBeams.Count} BARRAS) ===");
foreach (var s in frameBeams)
{
    sb.AppendLine(s);
}

Print(sb.ToString());
return sb.ToString();
"""

req = urllib.request.Request(
    "http://127.0.0.1:5055/api/v1/script/execute",
    data=json.dumps({"script_code": code}).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST"
)
with urllib.request.urlopen(req, timeout=20) as resp:
    res = json.loads(resp.read().decode("utf-8"))

out = res.get("data", {}).get("output") or res.get("data", {}).get("return_value")
with open(r"C:\Users\eslin\.gemini\antigravity-ide\brain\2df0b78a-7282-4559-8088-e6a24795e4cd\frame_bars.txt", "w", encoding="utf-8") as f:
    f.write(out)
print(f"Barras analizadas y guardadas en frame_bars.txt ({len(out.splitlines())} líneas)")
