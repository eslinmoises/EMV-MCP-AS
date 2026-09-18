# Specification 003: Roslyn C# Scripting Engine

- **Spec ID**: SPEC-003
- **Status**: APPROVED
- **Target Components**: `src/as_plugin/Scripting`

---

## 1. Objective
Enable AI agents to execute dynamic procedural and parametric C# code directly within Advance Steel without compiling binaries to disk or restarting AutoCAD.

## 2. Technical Stack & Engine
- **Runtime**: `Microsoft.CodeAnalysis.CSharp.Scripting` on .NET 8.
- **Evaluation Mechanism**: `CSharpScript.RunAsync(code, options, globals)`.

## 3. Injected Global Context (`ScriptGlobals`)
Every script executes with the following variables in scope:
- `Document`: `Autodesk.AutoCAD.ApplicationServices.Document` (MdiActiveDocument).
- `Database`: `Autodesk.AutoCAD.DatabaseServices.Database`.
- `ActiveUCS`: `Autodesk.AutoCAD.Geometry.Matrix3d`.
- `Output`: `StringBuilder` for collecting script console output.

## 4. Pre-imported Namespaces
- `System`, `System.Collections.Generic`, `System.Linq`
- `Autodesk.AutoCAD.DatabaseServices`, `Autodesk.AutoCAD.Geometry`
- `Autodesk.AdvanceSteel.CADAccess`, `Autodesk.AdvanceSteel.CADLink.Database`
- `Autodesk.AdvanceSteel.Modelling`, `Autodesk.AdvanceSteel.Geometry`
- `Autodesk.AdvanceSteel.ConnectionVault`

## 5. Security and Transaction Boundaries
1. **AutoCAD Main Thread**: Roslyn script execution is enqueued to the AutoCAD main thread.
2. **Transaction Wrap**: The entire script runs inside a `using (DocumentLock ...)` and `using (Transaction trans = ...)`.
3. **Rollback Guarantee**: If the script throws any uncaught exception, `trans.Abort()` is automatically triggered, preventing partial or corrupted geometry.
4. **Execution Timeout**: Max execution limit is 30,000 ms.
