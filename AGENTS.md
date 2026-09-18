# Universal Agent Guidelines (`AGENTS.md`)

> **Applies to**: Antigravity, Claude Code, Cursor AI, Codex.

## Core Philosophy: Spec-Driven Development (SDD)

1. **Specs are King**: All implementation behavior must conform to specifications located in `docs/specs/`. Never invent ad-hoc APIs or endpoints.
2. **Contract Governance**: Work is coordinated via **Agentic Contracts** in `orchestration/contracts/`. If you are acting as a worker agent, adhere strictly to your assigned contract's scope and file whitelist.
3. **Advance Steel Threading Law**:
   - AutoCAD/Advance Steel is single-threaded.
   - Any database modification MUST be guarded by `using (DocumentLock docLock = doc.LockDocument())` and `using (Transaction trans = doc.TransactionManager.StartTransaction())`.
   - Never call Advance Steel geometry or object methods from an unmanaged background thread.
4. **Offline Testing First**: Always test tools and logic against the mock server in `tests/mocks/mock_as_plugin.py` before requiring a live CAD session.
5. **No Code Without Tests**: Every tool and command handler must have automated test coverage (`pytest` or `xUnit`).
