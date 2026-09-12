# AGENTS.md

Godot 4.7.2 (mono) C# tactical skirmish game targeting .NET 10. X-COM-inspired; reference material in `.openxcom-research/`, design notes in `docs/`.

## Working approach

- Carry requested changes through implementation and relevant verification. Resolve routine, reversible choices from the code and project contracts; ask only when missing information materially changes the outcome or blocks safe progress. Preserve unrelated local edits.
- Start with the affected subsystem and its tests. Read the relevant architecture-reference sections; expand exploration when dependencies require it. Generated output, research checkouts, and past agent reports are not default context.
- Treat historical plans and reports in `.pi/`, `.pi-subagents/`, `.superpowers/`, and `docs/superpowers/` as background, not active instructions. `CLAUDE.md` contains Claude-specific additions; do not import its tool permissions into Codex.
- User instructions take precedence over skill guidelines. If a skill requires a pause, identify the exact file and instruction and explain the unresolved decision; do not infer extra approval gates.
- Keep updates and final responses concise: describe the result, relevant verification, and any remaining limitation. Answer follow-up questions while retaining the active task unless the user changes it.

## Documentation

For library, framework, SDK, API, CLI, or cloud-service behavior, resolve the library with Context7 and query its official documentation with the full question. If unavailable or incomplete, use official documentation directly. Skip this for general programming, business logic, refactoring, and review unless library-specific behavior needs verification.

## Commands

```sh
dotnet build                                                     # build
dotnet test fun-project.csproj --no-restore                     # tests (Debug only)
dotnet test fun-project.sln --no-restore                         # solution-level
dotnet test fun-project.csproj --filter "FullyQualifiedName~WeaponSystemTest"  # one test class
```

Open `project.godot` in the Godot editor for scene/resource editing and playtesting; while it runs, do all scene and editor work through the `godot-ai` MCP server (see *Godot scene and editor work* below).

### Testing
- For code changes, run `dotnet format`, build, and run tests covering the affected behavior. Use the full suite (`dotnet test fun-project.csproj --no-restore`) for shared runtime, executor, hook-ordering, or cross-subsystem changes. Broaden or repeat checks when changes, failures, or unresolved concerns justify it.
- Add regression tests for meaningful behavior changes using the shared helpers. Documentation-only and Codex-configuration changes need diff, link, and configuration checks; they do not require game tests or formatting.
- **GdUnit4** tests run *inside* a Godot runtime (not xUnit/NUnit); require `GODOT_BIN` → Godot mono binary, hardcoded in `.runsettings` (`C:\Program Files\Godot_v4.7.2-stable_mono_win64\...`; update if Godot moves — tests fail to launch otherwise). `.runsettings` is auto-picked-up (2 CPUs, no HTML logger, 5-minute session timeout that bounds a crashed Godot host instead of hanging; Godot verbose logging is off to keep runs and the TRX lean).
- Do not run `dotnet test` while the editor is rebuilding the project: both write to `.godot/mono/temp/bin`, and the test host can crash with exit code -1073741819 (0xC0000005). If that happens, rerun once the editor build finishes. Likewise, avoid alternating editor and CLI builds when you can — concurrent invocations invalidate each other's incremental state and force full recompiles.
- Tests live in the root `test/` directory and compile into `fun-project.csproj` **only in Debug** (development) configuration; export configurations exclude `test/` sources and all test NuGet packages from the exported assembly. The csproj always excludes the retired `fun-project-test/` leftovers (ignored caches behind a `.gdignore`) and `scenes/DemoBattle*.cs`; the gdUnit generated test runner (`gdunit4_testadapter_v5/`) compiles into the Debug assembly and is excluded from export configurations. Use shared helpers in `test/helpers/` — never ad-hoc setup.
- Shared test support lives in `test/helpers/`: `TestData` builds authored/runtime data, `BattleFixture` and `GeoscapeFixture` own per-test simulations, and `Assert` supplies assertions/extraction/extensions.
- Use `using var` for fixtures. `BattleFixture.Runtime` owns the session's sole executor; fixture operations reuse it. Use fixture `Alive`/`At` methods for fresh proofs and raw `Submit` for tests of explicit/stale actions.
- Fixture event capture begins before setup. Use `ClearEvents()` explicitly to start an assertion window; actions and start methods do not reset it.
- Direct factory/constructor/lifecycle tests retain and dispose their subject directly. Do not create a second live executor for a fixture session.

### Godot scene and editor work

The project ships the `godot_ai` editor plugin (`addons/godot_ai`) with its `godot-ai` MCP server. Whenever anything needs to be done with a Godot scene or the editor — opening, inspecting the tree hierarchy, editing, running — use these tools rather than hand-editing `.tscn`/`.tres` text or directing the user through the editor UI.
- Confirm the session first (`editor_state`, or `session_manage` op `list`): readiness, open scene, play state. Writes are rejected while the game runs; after stopping, one `editor_state` call refreshes the cache.
- Inspect before writing: `scene_get_hierarchy` for the tree, `node_find` to locate nodes, `node_get_properties` (pass `fields` to trim the read) to confirm exact property names — `node_set_property` needs Godot's exact identifier, which often differs from intuition.
- Edit via `scene_open`, `node_create`, `node_set_property`, `node_manage` (rename/reparent/reorder/delete/groups), and the specialized managers (`ui_manage`, `theme_manage`, `animation_create`/`animation_manage`, `camera_manage`, `audio_manage`, `tilemap_manage`, `tileset_manage`, `gridmap_manage`, `resource_manage`, `signal_manage`, `input_map_manage`). Use `batch_execute` for multi-step edits so a later failure rolls back the earlier ones.
- Scene paths are relative to the edited scene root (e.g. `/Main/Camera3D`), never runtime `/root/...` paths. Mutations stay in editor memory until `scene_save` (or `scene_manage` op `save_as`) persists them — save explicitly.
- Verify from the same surface: `project_run` / `project_manage` op `stop`, `game_manage` for runtime inspection and simulated input, `logs_read` (plugin/editor/game sources) for errors, `editor_screenshot` for visual checks.
- With no editor session connected (e.g. headless CI), fall back to direct file edits and say so.

## Architecture

Read the relevant sections of [docs/architecture-reference.md](docs/architecture-reference.md) before changing a subsystem. It preserves the detailed combat, hooks, buffs, status, presentation, strategic, and engineering contracts. Other design references are in `docs/`.

- Parse, don't validate: accept proof/token types that guarantee invariants. If this is impossible, throw for caller bugs; exceptions are not normal control flow.
- Authored `*Data` types are immutable Godot resources; runtime wrappers own per-instance state. Non-weapon equipment uses capability resources, with `FindCapability<TCap>()` / `With<TCap>()` proofs. Buff resources are the documented exception to the mirrored hierarchy.
- Stat identity is its concrete subclass. Use typed stat fields and `GetStat<TStat>()`; never replace this with enums or generic stat fields. Armor belongs to items.
- The scene-independent battle runtime owns tactical state. Scene code uses `BattleRuntime`: queries for reads, proof mints for inputs, and submitted actions for gameplay writes. Never add public read methods to `BattleSession`; add a query. Proof-typed/total queries return bare results, normal absence returns `Option<T>`, and genuine failure returns `Either`.
- One executor per session. `BattleRuntime` owns it; fixtures reuse it. Actions commit atomic steps; interrupted mutable facts are normal, rejected trusted parameters are caller bugs. Failed submissions clear queued work without rolling back committed state/events.
- Hooks fire once per committed event after broadcast, ordered by priority then registration. Use event-tag types and the single registration door. Never submit actions inside a hook. Preserve default-system ordering and turn-start availability/events/AP ordering.
- `AttackContext.Resolve` is the shared target-feasibility gate. Preview and attack share the hit calculator. Spend ammunition before rolling; damage, armor spill, statuses, and buffs follow the detailed reference.
- `EventPlaybackDirector` only plays committed events. Selection belongs to the UI; presentation never caches authoritative tactical state or mutates the session directly.
- Engineering owns parsed manufacturing projects and one active job. `GeoscapeSession` routes starts and applies completed Armory stock effects before broadcasting. Preserve captured stock policy, tick order, and session-rebuild behavior.

## Conventions
- Prefer collection expressions over explicitly instantiating container types.
- **LanguageExt** globally imported (`GlobalUsings.cs`); prefer `Option<T>` over nullable returns for optional domain data.
- No `System.Linq` — ZLinq; every chain starts with `.AsValueEnumerable()`.
- `SysColGeneric` = alias for `System.Collections.Generic` (LanguageExt shadows some names).
- File-scoped namespaces under `FunProject.*` (assembly root `funproject`); two-space indent; PascalCase types/members, camelCase locals/params, `_camelCase` private fields.
- Run `dotnet format` after any code change.
