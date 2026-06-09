# CLAUDE.md

A Godot 4.6.2 (mono) C# tactical skirmish game targeting .NET 10. Inspired by X-COM-style battlescape combat (see `.openxcom-research/` for reference material). `AGENTS.md` holds conventions; `docs/` holds detailed design notes.

## Commands

```sh
dotnet build                                                   # build the main project
dotnet test fun-project-test\fun-project-test.csproj --no-restore   # run the test suite
dotnet test fun-project.sln --no-restore                       # solution-level verification
dotnet test fun-project-test\fun-project-test.csproj --filter "FullyQualifiedName~WeaponSystemTest"  # run one test class
```

Open `project.godot` in the Godot editor for scene/resource editing and playtesting.

### Testing notes
- Tests use **GdUnit4** and run *inside* a Godot runtime, not plain xUnit/NUnit. They require the `GODOT_BIN` env var pointing at the Godot mono binary; it is hardcoded in `.runsettings` to `C:\Program Files\Godot_v4.6.2-stable_mono_win64\...`. Update that path if Godot lives elsewhere — tests fail to launch otherwise.
- Tests live in the **separate** `fun-project-test` project (`fun-project-test/test/`), which references the main project. The main `fun-project.csproj` explicitly excludes test files, `DemoBattle*` scenes, and the gdUnit adapter from its own compilation. Put new tests in `fun-project-test/test/` so the test project and VS Code discovery keep working.
- `.runsettings` is picked up automatically by the test project (`RunSettingsFilePath`). It caps at 2 CPUs and disables the HTML logger (Godot emits raw ANSI that the HTML serializer chokes on).

## Architecture

### The pervasive Data / Runtime split
Every game concept is split into two layers, and new gameplay should follow the same pattern:
- **`*Data` (authored, serializable `Godot.Resource`)** — designer-editable templates saved as `.tres` files under `resources/`. Immutable definition: names, stats, payloads, constraints. Hierarchy roots at `NamedEntityData` → `EquippableItemData` → `WeaponData`/`ThrowableItemData`/`ArmorItemData`/`UtilityItemData`, plus `CombatantData`, `FactionData`, and `Ammunition` (was `AmmunitionData`, collapsed in `6dca1f4` and now extends `NamedEntityData` directly).
- **Runtime wrapper (plain C# object)** — mutable per-instance state (charges, ammo, equipped mods, current health). Mirrors the data hierarchy: `EquippableItem` → `Weapon`/`ThrowableItem`/`Grenade`/etc., plus `Combatant`, `Faction`. Inventory/equipment code depends on the runtime base types, not the concrete data classes.

See `docs/equippable-item-architecture.md` and `docs/stat-system.md`.

### Stat system — concrete classes, no enums
Stats are identified by **concrete subclass**, never by a `StatType` enum (an enum-based version was deliberately removed). `Stat` is the abstract resource base; `HealthStat`, `DamageStat`, `AimStat`, etc. are concrete resources in `scripts/stats/concrete_stats/`. A field typed `DamageStat` only accepts a `DamageStat`, pushing authoring mistakes to compile/edit time. Runtime lookup goes through the `HasStats` interface: `GetStat<TStat>()` / `TryGetStat<TStat>(out _)`. Modifiers layer as `StatModifier` (raw numeric op) → `StatMod` (single-target; `TypedStatMod<TStat>` binds it to a concrete stat) → `EquippableMod` (slot-mounted, one or many stats via `MultiStatMod`). When adding a stat or modifier, add a concrete subclass — do not reintroduce an enum or a generic `Stat` field.

### Battle runtime — command / query / session (pure C#, no scene deps)
The tactical runtime in `scripts/battle/` is engine-agnostic C# and is the single source of truth. Presentation (Godot scenes) observes it but never owns tactical state. Full detail in `docs/battlescape-architecture.md`, `docs/battle-session-command-pattern.md`, `docs/battle-action-executor.md`.

- **`BattleSession`** — authoritative state and bookkeeping: phase, turn number, active side, faction order, unit pool, alive/dead storage, per-faction visibility/explored-tile memory. Constructed from a `BattleBoardState`, a faction order, and faction rosters.
- **`BattleBoardState`** — tile grid (`BattleTileState[x,y,z]`), occupancy, adjacency, and board-local pathfinding (Godot `AStar3D`). Coordinates are `Vector3I` with **X = width, Y = levels/height, Z = depth**.
- **Write side — `BattleAction` + `BattleActionExecutor`.** All mutation goes through actions submitted to `executor.Submit(action)`, which returns `IReadOnlyList<BattleActionResult>`. **Composite** actions (e.g. `MoveUnit`) yield **primitive** actions one at a time (`MoveUnitStep`); primitives are the atomic commit layer that mutate session state and emit `BattleEvent`s. The executor resolves trigger reactions from committed events between primitive commits, so reactions (overwatch, mines) act on post-step state. Composite child results are hidden from callers; the committed `BattleEvent` stream is the per-step source of truth.
- **Read side — `BattleSessionQuery<TResult>` + `BattleQueryRunner`.** Read-only questions are typed query objects (`FindPathForUnit`, `GetPossibleMoveTilesForUnit`, `GetVisibleEnemiesForUnit`, …) executed via `runtime.Query(query)`, which returns `Either<BattleQueryFailure, TResult>` (the `BattleQueryRunner` is internal to `BattleRuntime`; `BattleSession` has no public `Queries` property). **Do not** add a public read method to `BattleSession` for every question — add a query class (one question per class; no enum-mode catch-all queries).
- **`BattleRuntime`** — the disposable facade tying session + executor + query runner together and re-raising `BattleEventCommitted` / `ActionStarted` / `ActionCompleted`. Scene code generally talks to `BattleRuntime`, not the parts directly.
- **Triggers** — `BattleTrigger` is serializable, mediated battle state registered by event key via `RegisterTrigger<TEventKey>() where TEventKey : BattleEventTag`. The key is a tag *type* — either a concrete event record (e.g. `TileOccupiedBattleEvent`) or a shared marker interface (e.g. `IPositionedBattleEvent` / `IUnitBattleEvent`), which fires the trigger for every committed event implementing that tag. There is no `BattleEventType` enum (removed in commit `b68ad05`).

### Presentation boundary
Godot scene scripts live in `scenes/` (`BattleSceneController`, `BattleScene`, HUD, `GameCamera`, `MovementLine`, etc.). `scenes/battle/BattleEventSignalHandler` is the one adapter that binds a `BattleRuntime` and re-emits committed events as Godot signals (wrapping payloads in `BattleEventAdapter`/`BattleActionResultAdapter` `RefCounted` envelopes). Scene code reads state via `BattleRuntime.Query(...)` and never mutates the session directly. Keep this handler limited to signal adaptation — no battle-legality checks, no caching of authoritative state, no selection UX (selection is presentation state and is *not* tracked by `BattleSession`).

## Conventions
- **LanguageExt** is globally imported (`GlobalUsings.cs`: `using LanguageExt; using static LanguageExt.Prelude;`). Prefer `Option<T>` over nullable returns for optional domain data; query results surface as `Either<BattleQueryFailure, TResult>`. Note `SysColGeneric` is the alias for `System.Collections.Generic` (LanguageExt shadows some collection names).
- File-scoped namespaces under the `FunProject.*` root (`FunProject.Battle`, `FunProject.Stats`, `FunProject.Items`, …); the assembly root namespace is `funproject`. Two-space indentation. PascalCase types/members, camelCase locals/params, `_camelCase` private fields.
- Guard impossible states with exceptions that are **not** caught locally (let them surface).

## Codebase queries
A `graphify` code graph exists at `graphify-out/graph.json`. For "where is X / what calls Y" questions run `graphify query "<question>"`; after non-trivial code changes run `graphify update .` to refresh it.
