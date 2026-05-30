# Repository Guidelines

## Project Structure & Module Organization
This is a Godot 4.6 C# tactical skirmish project targeting .NET 10. Core game/runtime code lives in `scripts/`, with battle flow in `scripts/battle/`, stats in `scripts/stats/`, items in `scripts/items/`, combatants in `scripts/combatants/`, and weapons in `scripts/weapons/`. Godot scene-facing scripts and `.tscn` files live in `scenes/`. Designer-editable data assets live in `resources/`, and architecture notes live in `docs/`. Unit tests are in `fun-project-test/test/`; keep new tests there so the dedicated test project and VS Code discovery continue to work.

## Build, Test, and Development Commands
- `dotnet build`: compiles the main Godot C# project.
- `dotnet test fun-project-test\fun-project-test.csproj --no-restore`: runs the GdUnit-backed test suite through the test project.
- `dotnet test fun-project.sln --no-restore`: use for broader solution-level verification after cross-project changes.

Open `project.godot` in the Godot editor for scene/resource editing and local playtesting.

## Coding Style & Naming Conventions
Use common .NET naming: PascalCase for public types and members, camelCase for locals and parameters, and `_camelCase` for private fields. Existing C# uses two-space indentation and file-scoped namespaces. Prefer modern C# constructs such as `foreach`, pattern matching where clear, and expression-level simplicity over compatibility glue. Prefer `Option<T>` from LanguageExt over nullable return values for optional domain data. Guard impossible states with exceptions that are not caught locally.

## Testing Guidelines
Add focused tests for new runtime behavior unless the code is throwaway demo code. Name test files after the behavior or type under test, for example `BattleRuntimeTest.cs` or `WeaponSystemTest.cs`. Reuse existing helpers in `fun-project-test/test/` only when they already match the scenario; keep one-off setup local when that is clearer.

## Commit & Pull Request Guidelines
Recent commits use short one-line summaries such as `Add AssemblyInfo file`, `Try serena-MCP`, and `Reduce visibility system a bit`. Prefer imperative wording for new commits and keep each commit focused. Pull requests should include a brief intent summary, important design notes, test results, and screenshots or clips for visible scene/UI changes. Link issues when applicable.

## Agent-Specific Instructions
For codebase questions, run `graphify query "<question>"` when `graphify-out/graph.json` exists. After code changes, run `graphify update .` to refresh the graph. Before implementing non-trivial changes, discuss the approach with the developer. After implementation, request a review focused on simplicity, clarity, and maintainability, then address actionable feedback.
