# Agent Capabilities & Assignment Matrix

| Agent | Environment / Harness | Model | Primary Responsibilities | Optimal Tasks |
| :--- | :--- | :--- | :--- | :--- |
| **Antigravity (Lead)** | IDE Workspace | Gemini 3.8 Flash | Chief Architect, Spec Owner, Orchestration Director, Contract Creator | System design, spec writing, contract authoring, acceptance verification, Python FastMCP design |
| **Claude Code** | Terminal CLI (`claude`) | Claude 3.7 Sonnet | Primary Heavy Worker, Core .NET & Python Engineer | C# .NET 8 Advance Steel Add-in, Roslyn Scripting Engine, IPC Server, complex ASTs, high-precision CAD algorithms |
| **Project Scribe (Secretary)** | IDE Subagent / Mode | Gemini 3.8 Flash | Documentation Keeper, Log Auditor, Spec Synchronizer | Continuous maintenance of `README.md`, `docs/specs/`, `orchestration/project-log.md`, contract state validation |
| ~~**Cursor AI**~~ | Editor / CLI (`cursor`) | — | **Not in use** | — |
| ~~**Codex**~~ | Terminal CLI | — | **Retired 2026-09-19** | See note below |

> **Roster decision (2026-09-19)**:
> 1. The active engineering roster is **Claude Code + Antigravity**.
> 2. Antigravity assumes the **Project Scribe (Secretary)** role to keep documentation, contracts, and specs rigorously updated with zero drift.
> 3. `CONTRACT-001`, `CONTRACT-002`, `CONTRACT-003`, and `CONTRACT-004` (both 004A and 004B) are **100% completed, verified with 0 build errors and 33/33 tests passing, and merged to master**.

---

## Tooling & Command Map for Worker Agents

- **Claude Code (Terminal)**:
  - Invoked directly in repo root: `claude`
  - Runs commands via native shell, executes `dotnet build`, `dotnet test`, `pytest`.
- **Antigravity (IDE)**:
  - Oversees workspace, runs commands, updates specs, and commits git milestones.
