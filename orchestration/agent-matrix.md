# Agent Capabilities & Assignment Matrix

| Agent | Environment / Harness | Model | Primary Responsibilities | Optimal Tasks |
| :--- | :--- | :--- | :--- | :--- |
| **Antigravity (Lead)** | IDE Workspace | Gemini 3.8 Flash | Chief Architect, Spec Owner, Orchestration Director, Contract Creator | System design, spec writing, contract authoring, acceptance verification, Python FastMCP design |
| **Claude Code** | Terminal CLI (`claude`) | Claude 3.7 Sonnet | Primary Heavy Worker, Core .NET & Python Engineer | C# .NET 8 Advance Steel Add-in, Roslyn Scripting Engine, IPC Server, complex ASTs, high-precision CAD algorithms |
| ~~**Cursor AI**~~ | Editor / CLI (`cursor`) | — | **Not in use** | — |
| ~~**Codex**~~ | Terminal CLI | — | **Retired 2026-09-19** | See note below |

> **Roster decision (2026-09-19)**: the active roster is **Claude Code + Antigravity only**.
>
> Codex CLI was trialled as a worker on CONTRACT-004A and 004B. It delivered 004B correctly
> (accepted, 32/32 tests) but is on a free tier, so it is retired rather than kept as a paid path.
> Two lessons from the trial, worth keeping for any future worker:
> 1. **A worker's shell is not this shell.** Codex could not run `python -m pytest`, so it returned
>    no evidence for its own DoD. Acceptance must always re-run the verification suite in the
>    Director's environment; a worker's word is not evidence.
> 2. **Pin the spec before dispatching.** The `engine_command` parameter was added to SPEC-004 after
>    the worker had read it, so the delivery could not contain it. A spec must be frozen at dispatch
>    time, not edited while a contract is in flight.

---

## Tooling & Command Map for Worker Agents

- **Claude Code (Terminal)**:
  - Invoked directly in repo root: `claude`
  - Runs commands via native shell, executes `dotnet build`, `dotnet test`, `pytest`.
- **Antigravity (IDE)**:
  - Oversees workspace, runs commands, updates specs, and commits git milestones.
