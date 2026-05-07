# CLAUDE.md
This project is a turn-based, X-COM style game made with Godot 4 and C# .NET 10.0.
Players control a fixed-number combatants to participate in skirmishes against enemy combatants.

## Architecture
- `scripts/` Core logic and classes. Data should be Godot Resources to be easily serializable, and each Resource subclass should be in its own file that has the same name as the class.
- `scenes/` Godot-related items, so anything that needs to be included in a Godot Scene.
- `test/` Tests. These are dummy scenes which run unit-tests inside the scene's _Ready() function.

## Coding guidelines
- Prefer default C# styling.
- For any function that may throw an error or return null, always add a check for it.
- Add tests for any added code, except for throwaway demo code.
- Avoid C-style out params, instead just return an optional type.
- If considering nullable class members, or making a global "Object" that does multiple things depending on its fields, considering creating derived classes to represent each individual behavior.
- Avoid enabling nullable contexts. If a default behavior is expected, then just make that the default param value. If null can be a valid input, use LanguageExt.Option<T>.
- If in a non-nullable context, do not insert exceptions to check a null input for parameters that are not nullable.
