import urllib.request
import json

code = """
using System;
using System.Reflection;
using System.Linq;
using Autodesk.AdvanceSteel.Modelling;
using Autodesk.AdvanceSteel.BuildingStructure;
using System;
using System.Reflection;
using Autodesk.AdvanceSteel.BuildingStructure;
using Autodesk.AdvanceSteel.CADAccess;

var tGrid = typeof(Grid);
var seqMethods = string.Join("\\n", tGrid.GetMethods(BindingFlags.Public | BindingFlags.Instance)
    .Where(m => m.Name.Contains("Sequence") || m.Name.Contains("Element") || m.Name.Contains("Delta"))
    .Select(m => m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")"));

return seqMethods;
"""

req = urllib.request.Request('http://127.0.0.1:5055/api/v1/script/execute',
    data=json.dumps({'script_code': code}).encode('utf-8'),
    headers={'Content-Type': 'application/json'})
try:
    res = urllib.request.urlopen(req)
    data = json.loads(res.read())
    print(json.dumps(data, indent=2))
except urllib.error.HTTPError as e:
    print("HTTPError:", e.code, e.read().decode('utf-8'))

