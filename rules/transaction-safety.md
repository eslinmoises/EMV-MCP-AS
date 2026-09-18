# Transaction Safety and Threading Rules (`rules/transaction-safety.md`)

## 1. The AutoCAD Single-Thread Law
- The AutoCAD/Advance Steel database (`Database`, `TransactionManager`, `ASObjectsMgd`) is **STRICTLY SINGLE-THREADED**.
- Attempting to query or modify any DWG or Advance Steel object from a background thread (such as an incoming HTTP thread from `HttpListener`) will trigger an immediate, fatal `AccessViolationException` that crashes `acad.exe` with loss of drawing data.

## 2. Dispatching to the Main UI Thread
All incoming commands from the local IPC must be dispatched to the AutoCAD main thread:
```csharp
// Inside IpcHttpServer request handler:
Application.DocumentManager.MdiActiveDocument.SendStringToExecute(...) 
// OR via SynchronizationContext / Application.Idle Queue
```

## 3. The Atomic Transaction Pattern
Every read/write operation inside the dispatched action must follow this exact pattern:

```csharp
Document doc = Application.DocumentManager.MdiActiveDocument;
if (doc == null)
{
    return CommandResult.Fail("NO_ACTIVE_DOCUMENT", "No active drawing open in Advance Steel.");
}

using (DocumentLock docLock = doc.LockDocument())
using (Transaction trans = doc.TransactionManager.StartTransaction())
{
    try
    {
        // 1. Advance Steel Database operations
        // 2. Element creation or modification
        
        trans.Commit();
        return CommandResult.Ok(responsePayload);
    }
    catch (Exception ex)
    {
        trans.Abort();
        return CommandResult.Fail("TRANSACTION_FAILED", ex.Message, ex.StackTrace);
    }
}
```

## 4. Rollback and Integrity Guarantees
- If any sub-element or parameter validation fails during a batch creation, the entire transaction must abort.
- Never leave dangling entities in the AutoCAD BlockTableRecord.
- Return structured error JSON with troubleshooting suggestions back to the MCP server.

## 5. Command-Mode Routes (Numbering, NC Export, Drawings)
The Advance Steel numbering engine and the DSTV/NC creator are **not** exposed as managed classes in
AS 2026. They are driven through the Advance Steel command layer, and a command opens and commits
its own transactions. Running one inside the dispatcher's transaction (§3) either deadlocks on the
document lock or commits work the dispatcher believes it can still roll back.

Therefore routes under `production/` run in **command mode**:
- They still marshal onto the AutoCAD main thread and still take `doc.LockDocument()`.
- They do **not** open an outer `Transaction` / Advance Steel `Transaction`.
- The dispatcher registers them in `CommandRoutes`; a route may be in `CommandRoutes` **or** run
  under the §3 transaction pattern, never both.
- Because there is no transaction to abort, a command-mode handler must **verify the model after the
  engine ran** (read the marks back, stat the produced files) and report what the model actually
  holds. It must never report the intent of the request as if it were the result.
