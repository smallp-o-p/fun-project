# AGENTS.md
This project is a turn-based, X-COM style game made with Godot 4 and C# .NET 10.0. Players control a fixed-number combatants to in skirmishes against enemy combatants.

## Architecture
- `scripts/`: Core backend runtime, items that can be exposed to a designer should be easily serializable as Godot resources
- `scenes/`: Scripts that can be present in a Godot scene and edited in the Godot editor
- `fun-project-test/test/`: Unit tests

## Build and Test
- Build the project with `dotnet build`
- Run tests with `dotnet test fun-project-test\fun-project-test.csproj --no-restore`

## Coding guidelines
- Use the common .NET Coding Conventions and prefer modern constructs e.g. `foreach(...)` over `for(...)`.
- Add tests for any added code, except for throwaway demo code
- Prefer `Option<T>` over `T?`
- Guard invariants behind exceptions that don't get caught
- Design for simplicity, reusability and ease of expansion
- Before implementing a change, iterate on the implementation details with the developer
- For larger changes, split the task into multiple subtasks and use subagents if they can meaningfully accelerate implementation time
- After implementing a change, launch a review agent that focuses on code simplicity, clarity, and maintainability and implement its suggested changes
