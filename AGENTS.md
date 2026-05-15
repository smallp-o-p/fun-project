# CLAUDE.md
This project is a turn-based, X-COM style game made with Godot 4 and C# .NET 10.0.
Players control a fixed-number combatants to participate in skirmishes against enemy combatants.

## Architecture
- `scripts/` Core logic and classes. Data should be Godot Resources to be easily serializable, and each Resource subclass should be in its own file that has the same name as the class.
- `scenes/` Godot-related items, so anything that needs to be included in a Godot Scene.
- `fun-project-test/test/` Tests. Keep test-only code in the `fun-project-test` subproject so IDE test discovery can run it separately from the Godot game project.

## Coding guidelines
- Prefer default C# styling.
- For any function that may throw an error or return null, always add a check for it.
- Add tests for any added code, except for throwaway demo code.
- Avoid C-style out params, instead just return an optional type.
- If considering nullable class members, or making a global "Object" that does multiple things depending on its fields, considering creating derived classes to represent each individual behavior.
- Avoid nullable types. Use LanguageExt.Option<T> instead.
- Enforce non-nullability for function parameters by leveraging ArgumentException.ThrowIfNull(...).
- When checking an Option<T>, for an Option<T> Foo, if there is a previous !Foo.IsNone check, then you may access the underlying value with .ValueUnsafe()!.
- Design for simplicity and reusability. If a particular component is expected to have multiple changes or additional behavior, design for ease of expansion.
- Use foreach loops rather than raw for loops whenever possible.

## Implementation guidelines
- Before implementing a change, iterate on the implementation details with the user.
- For larger changes, split the task into multiple subtasks and evaluate whether they can be done in parallel using agents. If so, use agents to expedite implementation.
- After implementing a change, launch a review agent that focuses on code simplicity and code readability before marking the change as complete.
