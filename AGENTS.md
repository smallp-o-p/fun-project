# AGENTS.md

Godot 4.7.2 mono / C# tactical strategy game targeting .NET 10.

## Working approach

- Implement the requested change and verify it. Resolve routine choices from source and project contracts; ask when an unresolved decision materially affects scope or correctness. Preserve unrelated edits.
- Start with the affected subsystem and its tests. Use the [architecture map](docs/architecture-reference.md) to find its contract; do not load every guide by default.
- Historical reports, research checkouts, and ignored planning directories are background, not active instructions. User instructions take precedence over skills. Do not invent additional approval gates or import another agent's tool permissions.
- Report changes, verification actually completed, and remaining limitations concisely. Do not claim a failed or incomplete run passed.

## Verification

```sh
dotnet format
dotnet build
dotnet test fun-project.csproj --no-restore
dotnet build -c ExportRelease
```

Run focused tests while iterating. Shared runtime, execution-order, and cross-subsystem changes require the full suite. Check ExportRelease when compilation boundaries or dependencies change. Documentation-only changes need diff, link, and referenced-configuration checks, not game builds/tests.

Tests run inside Godot, not a standalone unit-test host. See [testing.md](docs/testing.md) for the machine-specific Godot path and asset-import prerequisites. Never run competing editor/CLI builds or edit sources during tests. Use the [test-audit skill](.agents/skills/test-audit/SKILL.md) for fixture ownership and test-quality rules.

## Design boundaries

- Treat authored Godot resources as immutable definitions during play; runtime wrappers own per-instance state. Shared resources must not become accidental mutable game state.
- Express invariants with proof types. Mint state-dependent proofs at use; actions still recheck facts interrupts can invalidate. Expected absence uses `Option<T>`; caller bugs throw. Exceptions are not normal control flow.
- Scenes use `BattleRuntime`: queries for reads, proof mints for action inputs, submitted actions for gameplay writes. Add queries rather than public reads on `BattleSession`. Use faction knowledge where hidden information must stay hidden.
- One executor per session, owned by `BattleRuntime`. Hooks return interrupts; they do not submit actions. Preserve event/turn ordering and committed state on failure.
- Presentation consumes committed events; it does not own tactical truth. Stat identity is its concrete subclass. Keep these boundaries when simplifying.

## Authoring and style

Use a connected `godot-ai` editor integration when available: inspect before editing, save explicitly, then verify the saved scene and runtime behavior. It is optional local tooling, not a tracked repository dependency. Without a connected session, use direct file edits and report that fallback. Do not mix file edits with unsaved editor changes.

Checked-in analyzer and formatting configuration defines enforced style; fix diagnostics rather than duplicating their rules here. LanguageExt is globally imported; `SysColGeneric` aliases `System.Collections.Generic`. Verify unfamiliar library behavior against official documentation, using Context7 when useful and available.
