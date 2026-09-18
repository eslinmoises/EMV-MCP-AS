# Claude Code Worker Guide (`CLAUDE.md`)

Welcome, Claude Code! You are assigned as the **Primary Heavy Worker & Systems Engineer** for `EMV-MCP-AS`, coordinated by **Antigravity (Orchestration Director)**.

---

## 1. Operating Rules for Claude Code

1. **Check Your Active Contract First**:
   - Look in `orchestration/contracts/` for your assigned contract (e.g. `CONTRACT-001-as-ipc-server.md`).
   - Read the reference spec in `docs/specs/` and domain rules in `rules/`.
   - **Do not modify files outside the contract whitelist.**
2. **Build and Test Commands**:
   - Build C# .NET 8 Plugin:
     ```powershell
     dotnet build src/as_plugin/EMV.AdvanceSteel.Plugin.csproj -c Release
     ```
   - Run Python Mock Server & MCP Tests:
     ```powershell
     pytest tests/mcp/ -v
     ```
   - Start Standalone Mock Server for local testing:
     ```powershell
     python tests/mocks/mock_as_plugin.py
     ```
3. **Advance Steel .NET 8 Critical Patterns**:
   - Target: `net8.0-windows`
   - Every DB write operation MUST use:
     ```csharp
     using (DocumentLock docLock = doc.LockDocument())
     using (Transaction trans = doc.TransactionManager.StartTransaction())
     {
         try
         {
             // AS Database operations
             trans.Commit();
         }
         catch (Exception ex)
         {
             trans.Abort();
             throw;
         }
     }
     ```
   - Roslyn Scripting: Use `Microsoft.CodeAnalysis.CSharp.Scripting` with `ScriptGlobals` (pre-injecting `doc`, `database`, `ucs`).
4. **Completion Report**:
   - When finishing your contract, report the modified files, test execution output, and update the contract status to `IN_REVIEW` for the Director.
