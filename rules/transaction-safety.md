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
