# Protocol: Clean Plugin Deployment & Synchronization

> **Applies to**: All agents (Antigravity, Claude Code, Cursor, Codex).  
> **Target Script**: `scripts/clean_deploy_plugin.ps1`

---

## 1. Problem Statement & Root Cause

Autodesk Advance Steel loads external .NET assemblies using `PackageContents.xml` definitions located inside:
- `%APPDATA%\Autodesk\ApplicationPlugins\EMV-AdvanceSteel.bundle\`
- `%ProgramData%\Autodesk\ApplicationPlugins\EMV-AdvanceSteel.bundle\`

In `PackageContents.xml`:
```xml
<ComponentEntry 
    AppName="EMV.AdvanceSteel.Plugin" 
    Version="1.0.0" 
    ModuleName="./Contents/Release/EMV.AdvanceSteel.Plugin.dll" 
    AppType=".Net" 
    LoadOnAutoCADStartup="True" />
```

### The Conflict Anti-Pattern:
When build scripts or agents copy the plugin assembly directly to the parent folder (`Contents/EMV.AdvanceSteel.Plugin.dll`) instead of the declared `Contents/Release/` path:
1. **Silent Stale Code**: Advance Steel continues loading the outdated DLL from `Contents/Release/`.
2. **Missing Endpoints**: New API endpoints appear to be missing (returning 404), even after rebuilding.
3. **Redundant Garbage**: Duplicate loose files accumulate and create confusion.

---

## 2. Standard Deployment Protocol

Whenever compiling or updating the C# add-in, agents MUST use the standardized deployment script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/clean_deploy_plugin.ps1
```

### Automated Steps Performed:
1. **AutoCAD Process Detection**: Checks if `acad.exe` is running to prevent locked file copy errors. (Use `-ForceKillAutoCAD` if automated CI/CD shutdown is required).
2. **Deterministic Release Build**: Runs `dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release` and verifies 0 errors.
3. **Garbage Cleanup**: Automatically deletes any loose legacy files (`Contents/EMV.AdvanceSteel.Plugin.dll`, `Contents/EMV.AdvanceSteel.Plugin.pdb`).
4. **Target Synchronization**: Copies the complete Release payload (main DLL, PDB, `.deps.json`, and Roslyn script compiler dependencies) to both user `%APPDATA%` and system `%ProgramData%` bundles.
5. **Cryptographic Verification**: Computes SHA256 hash of the compiled assembly and verifies byte-for-byte match with the deployed file in both locations.

---

## 3. Automation Rule for Sub-Agents

All sub-agents (including Claude Code worker sessions) are strictly instructed:
- **NEVER** run ad-hoc manual `Copy-Item` commands to `Contents/`.
- **ALWAYS** invoke `scripts/clean_deploy_plugin.ps1` to finalize any C# plugin modifications.
