import json
import urllib.request

code = """
var sb = new System.Text.StringBuilder();
foreach (var h in new[] { "C2", "D1", "D3", "D5", "D7", "C6", "DB", "E6" })
{
    var fo = Autodesk.AdvanceSteel.CADAccess.FilerObject.GetFilerObjectByHandle(h);
    if (fo is Autodesk.AdvanceSteel.Modelling.StraightBeam b)
    {
        var p1 = b.GetPointAtStart();
        var p2 = b.GetPointAtEnd();
        sb.AppendLine($"Beam {h}: ProfName='{b.ProfName}', Start=({p1.x:F1}, {p1.y:F1}, {p1.z:F1}), End=({p2.x:F1}, {p2.y:F1}, {p2.z:F1})");
    }
    else if (fo is Autodesk.AdvanceSteel.Modelling.FinitRectScrewBoltPattern bp)
    {
        sb.AppendLine($"Bolt {h}: Normal=({bp.Normal.x:F1}, {bp.Normal.y:F1}, {bp.Normal.z:F1}), Standard='{bp.Standard}', Grade='{bp.Grade}'");
    }
}
return sb.ToString();
"""

def main():
    req = urllib.request.Request(
        "http://127.0.0.1:5055/api/v1/script/execute",
        data=json.dumps({"script_code": code}).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            res = json.loads(resp.read().decode("utf-8"))
        output = res.get("data", {}).get("output")
        print(output if output else res)
    except urllib.error.HTTPError as e:
        print("HTTP Error:", e.code, e.read().decode("utf-8"))

if __name__ == "__main__":
    main()
