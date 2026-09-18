# Agentic Contract: CONTRACT-001-AS-IPC-SERVER

- **Assigned Worker**: **Claude Code (Terminal CLI)**
- **Director / Supervisor**: Antigravity (Gemini 3.8 Flash)
- **Status**: **ACTIVE / READY FOR CLAUDE CODE**
- **Date Created**: 2026-09-18
- **Target Component**: `src/as_plugin`

---

## 1. Context & Objective

As defined in the project architecture, `as_plugin` is the in-process .NET 8 Add-in for **Advance Steel 2025 and 2026**. 
Your mission as **Claude Code** is to implement the production handlers for beam creation, plate creation, weld & assembly verification, Main Part management, viewport capture, and the Roslyn dynamic scripting engine.

---

## 2. Reference Specifications & Rules

You MUST read and adhere strictly to:
1. [`docs/specs/001-architecture-and-ipc.md`](../../docs/specs/001-architecture-and-ipc.md) (Endpoints & JSON Envelopes)
2. [`docs/specs/002-data-models-and-schemas.md`](../../docs/specs/002-data-models-and-schemas.md) (Beams, Plates, Welds, Main Part schemas)
3. [`docs/specs/003-scripting-engine-roslyn.md`](../../docs/specs/003-scripting-engine-roslyn.md) (Roslyn Scripting context)
4. [`docs/specs/004-mcp-tools-specification.md`](../../docs/specs/004-mcp-tools-specification.md) (Tool surface contract)
5. [`rules/advance-steel-modeling.md`](../../rules/advance-steel-modeling.md) (Workshop vs Site welds, Main Part rules)
6. [`rules/transaction-safety.md`](../../rules/transaction-safety.md) (DocumentLock & Transaction rollback)

---

## 3. Strict Permitted Scope (File Whitelist)

You are **ONLY** permitted to create or modify the following files:
- `src/as_plugin/Commands/Handlers/BeamCommandHandler.cs`
- `src/as_plugin/Commands/Handlers/PlateCommandHandler.cs`
- `src/as_plugin/Commands/Handlers/WeldCommandHandler.cs`
- `src/as_plugin/Commands/Handlers/AssemblyCommandHandler.cs`
- `src/as_plugin/Commands/Handlers/ViewportCommandHandler.cs`
- `src/as_plugin/Scripting/RoslynEvaluator.cs`
- `src/as_plugin/Commands/CommandDispatcher.cs`

*Do NOT touch files in `src/mcp_server/`, `orchestration/`, or `docs/` without Director approval.*

---

## 4. Implementation Requirements

### A. Advance Steel Native API Calls
- **StraightBeam**: Use `Autodesk.AdvanceSteel.Modelling.StraightBeam(section, startPoint, endPoint, refVector)`.
- **Plate**: Use `Autodesk.AdvanceSteel.Modelling.PlateContour(plane, points, thickness)`.
- **Weld Verification**:
  - Inspect `Autodesk.AdvanceSteel.Modelling.Weld` or `WeldPoint`.
  - Read `WeldLocation` (`eWeldLocation.kWorkshop` vs `eWeldLocation.kSite`).
  - Read throat thickness (`Garganta`) and connected parts.
- **Main Part**:
  - Verify `MainPart` on the `Assembly` object.
  - Return error/warning if the Main Part is assigned to a secondary plate/stiffener instead of a primary profile.

### B. Roslyn Scripting Evaluator (`src/as_plugin/Scripting/RoslynEvaluator.cs`)
- Use `Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.RunAsync(code, options, globals)`.
- Enforce 30-second execution timeout.
- Inject `ScriptGlobals` (Document, Database, ActiveUCS, Output).

---

## 5. Mandatory Verification Commands

Before reporting completion to the Director, run:
```powershell
dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
```

---

## 6. Definition of Done (DoD) Checklist

- [ ] All handlers are guarded by `doc.LockDocument()` and `trans.Commit()` / `trans.Abort()`.
- [ ] Weld verification accurately separates Workshop welds from Site welds.
- [ ] Main Part inspection flags invalid detailing assignments.
- [ ] Roslyn evaluator aborts transaction on any uncaught script exception.
- [ ] `dotnet build` succeeds with zero errors.
- [ ] Output and modified files logged and reported to the Director.
