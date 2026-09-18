# Spec-Driven Multi-Agent Lifecycle Workflow

This document defines the exact phase progression for all new features and bug fixes in `EMV-MCP-AS`.

```text
┌─────────────────────────────────────────────────────────────┐
│ Phase 1: Specification (Antigravity Director)               │
│ - Identify need, write/update spec in docs/specs/           │
│ - Define JSON Schemas, Error Envelopes, Domain Rules        │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ Phase 2: Contract Formulation (Antigravity Director)         │
│ - Create orchestration/contracts/CONTRACT-XXX.md            │
│ - Define explicit Whitelist, Interface signatures & DoD    │
│ - Assign to Worker Agent (e.g. Claude Code CLI)             │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ Phase 3: Contract Execution (Worker: Claude Code / Cursor)   │
│ - Worker reads Contract & Spec                              │
│ - Worker implements tests (TDD) and minimal code            │
│ - Worker runs mandatory verification suite                  │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ Phase 4: Acceptance & Verification (Antigravity Director)   │
│ - Antigravity inspects diff against Contract Whitelist      │
│ - Antigravity verifies test suite passes                    │
│ - Antigravity updates Contract to [COMPLETED] & commits     │
└─────────────────────────────────────────────────────────────┘
```

## Ground Rules
1. **Never Bypass Contracts**: No code modification without an assigned contract.
2. **Never Violate Whitelists**: If a worker modifies files outside the contract whitelist, the submission is rejected.
3. **Evidence Before Assertions**: A worker must present real command output proving tests passed before claiming completion.
