# Agentic Contract: [CONTRACT-ID] - [Contract Title]

- **Assigned Worker**: [Claude Code (Terminal) | Cursor AI | Codex]
- **Director / Supervisor**: Antigravity (Gemini 3.8 Flash)
- **Status**: [PROPOSED | ACTIVE | IN_REVIEW | COMPLETED]
- **Date Created**: YYYY-MM-DD
- **Target Branch**: feat/[contract-id-slug]

---

## 1. Context & Objective
[Brief explanation of what this contract achieves and why it is needed.]

## 2. Reference Specifications
- Primary Spec: [`docs/specs/XXX.md`](../../docs/specs/)
- Domain Rules: [`rules/XXX.md`](../../rules/)

## 3. Strict Permitted Scope (File Whitelist)
The assigned worker is ONLY permitted to create or modify the following files:
- `exact/path/to/file1`
- `exact/path/to/file2`
- `tests/exact/path/to/test_file`

*Modifications outside this whitelist are strictly forbidden and will cause contract rejection.*

## 4. Interface & Implementation Details
- **Inputs**: [JSON schemas, parameters, types]
- **Outputs**: [Return types, expected JSON responses]
- **Error Handling**: [Error codes to return on invalid inputs]

## 5. Mandatory Verification Commands
The worker MUST execute and include output for:
```bash
# Example
pytest tests/path/to/test.py -v
dotnet test tests/plugin/
```

## 6. Definition of Done (DoD)
- [ ] Code strictly follows referenced specifications.
- [ ] No files outside the permitted whitelist were altered.
- [ ] Unit & integration tests pass with 100% success.
- [ ] Zero unhandled exceptions or thread-locking violations.
- [ ] Self-review complete.
