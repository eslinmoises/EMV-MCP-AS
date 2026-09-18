# Agent Capabilities & Assignment Matrix

| Agent | Environment / Harness | Model | Primary Responsibilities | Optimal Tasks |
| :--- | :--- | :--- | :--- | :--- |
| **Antigravity (Lead)** | IDE Workspace | Gemini 3.8 Flash | Chief Architect, Spec Owner, Orchestration Director, Contract Creator | System design, spec writing, contract authoring, acceptance verification, Python FastMCP design |
| **Claude Code** | Terminal CLI (`claude`) | Claude 3.7 Sonnet | Primary Heavy Worker, Core .NET & Python Engineer | C# .NET 8 Advance Steel Add-in, Roslyn Scripting Engine, IPC Server, complex ASTs, high-precision CAD algorithms |
| **Cursor AI** | Editor / CLI (`cursor`) | Claude 3.7 / GPT-4o | Specialized Worker | Refactoring, code organization, `.cursor/rules` alignment, inline test generation |
| **Codex** | Terminal CLI | Codex / GPT-4o | Scaffolding Worker | Boilerplate generation, AST validation, documentation rendering, mock data generators |

---

## Tooling & Command Map for Worker Agents

- **Claude Code (Terminal)**:
  - Invoked directly in repo root: `claude`
  - Runs commands via native shell, executes `dotnet build`, `dotnet test`, `pytest`.
- **Antigravity (IDE)**:
  - Oversees workspace, runs commands, updates specs, and commits git milestones.
