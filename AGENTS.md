# AGENTS.md

A Godot 4.6.2 (mono) C# tactical skirmish game targeting .NET 10. Inspired by X-COM, see `.openxcom-research/` for reference material. `docs/` holds detailed design notes.

## Commands

```sh
dotnet build                                                   # build the main project
dotnet test fun-project-test\fun-project-test.csproj --no-restore   # run the test suite
dotnet test fun-project.sln --no-restore                       # solution-level verification
dotnet test fun-project-test\fun-project-test.csproj --filter "FullyQualifiedName~WeaponSystemTest"  # run one test class
```

Open `project.godot` in the Godot editor for scene/resource editing and playtesting.

### Testing
- Tests use **GdUnit4** and run *inside* a Godot runtime, not plain xUnit/NUnit. They require the `GODOT_BIN` env var pointing at the Godot mono binary; it is hardcoded in `.runsettings` to `C:\Program Files\Godot_v4.6.2-stable_mono_win64\...`. Update that path if Godot lives elsewhere — tests fail to launch otherwise.
- Tests live in the **separate** `fun-project-test` project (`fun-project-test/test/`), which references the main project. The main `fun-project.csproj` explicitly excludes test files, `DemoBattle*` scenes, and the gdUnit adapter from its own compilation. Always put tests in `fun-project-test/test/`.
- `.runsettings` is picked up automatically by the test project (`RunSettingsFilePath`). It caps at 2 CPUs and disables the HTML logger.
- Shared test helpers live in `fun-project-test/test/helpers/` (namespace `FunProject.Tests`; `BattleTestFactory`/`BattleActionTestHelper`/`BattleQueryTestHelper` are static-imported via the test project's `GlobalUsings.cs`). Build new tests on these instead of ad-hoc setup.

## Architecture

### Type-driven Design: Parse, don't validate
- Keep validation of types outside of low-level core systems as much as possible. Core systems that require an invariant to be satisfied should have arguments whose type ensures that invariant is satisfied.
- Create wrapper types that serve as "Tokens" to pass into functions that expect a certain condition to be met if needed.
- If the above is impossible, guard impossible states with exceptions that are **not** meant to be caught.

### Data / Runtime split
Every game concept is split into two layers, and new gameplay should follow the same pattern:
- **`*Data` (authored, serializable `Godot.Resource`)** — designer-editable templates saved as `.tres` files under `resources/`. Immutable definition: names, stats, payloads, constraints. Hierarchy roots at `NamedEntityData` → `EquippableItemData` (→ `WeaponData` → `AmmunitionedWeaponData` → `FirearmWeaponData`), plus `CombatantData`, `FactionData`, and `Ammunition` (was `AmmunitionData`, collapsed in `6dca1f4` and now extends `NamedEntityData` directly). Non-weapon item kinds are NOT subclasses: an item is one `EquippableItemData` plus capability resources in its `Capabilities` array (`ThrowableCapabilityData`, `BlastCapabilityData`, `ChargesCapabilityData`, `ModSlotsCapabilityData`, `ArmorCapabilityData` in `scripts/items/capabilities/`). `ArmorCapabilityData` references a `BaseArmorStat` (armor value + `Element`) plus regen config (`RegenDelayTurns`, `RegenPerTurn`).
- **Runtime wrapper (plain C# object)** — mutable per-instance state (charges, ammo, equipped mods, current health). Mirrors the data hierarchy: `EquippableItem` (→ `Weapon` subclasses) holding runtime capabilities (`ThrowableCapability`, `ChargesCapability`, `ArmorCapability`, …) created from capability data; lookup via `item.FindCapability<TCap>() : Option<TCap>`, and battle actions take capability proofs — `item.With<TCap>() : Option<ItemWith<TCap>>` (e.g. `ThrowItem` requires `ItemWith<ThrowableCapability>`). Plus `Combatant`, `Faction`. Inventory/equipment code depends on the runtime base types, not the concrete data classes. `BattleUnitState.EquippedArmor` carries the armor capability proof as `Option<ItemWith<ArmorCapability>>`, threaded through `SpawnUnit`/`AddUnit`.

See `docs/equippable-item-architecture.md` and `docs/stat-system.md`.

### Stat system
Stats are identified by **concrete subclass**. `Stat` is the abstract resource base; `HealthStat`, `DamageStat`, `AimStat`, etc. are concrete resources in `scripts/stats/concrete_stats/`. A field typed `DamageStat` only accepts a `DamageStat`, pushing authoring mistakes to compile/edit time. Runtime lookup goes through the `HasStats` interface: `GetStat<TStat>()` / `TryGetStat<TStat>(out _)`. Modifiers layer as `StatModifier` (raw numeric op) → `StatMod` (single-target; `TypedStatMod<TStat>` binds it to a concrete stat) → `EquippableMod` (slot-mounted, one or many stats via `MultiStatMod`). When adding a stat or modifier, add a concrete subclass — do not reintroduce an enum or a generic `Stat` field. Armor lives on items — `BaseArmorStat` (value + `Element`) is authored on `ArmorCapabilityData`, not on `CombatantData`.

### Battle runtime — command / query / session (pure C#, no scene deps)
The tactical runtime in `scripts/battle/` is engine-agnostic C# and is the single source of truth. Presentation (Godot scenes) observes it but never owns tactical state. Full detail in `docs/battlescape-architecture.md`, `docs/battle-session-command-pattern.md`, `docs/battle-action-executor.md`.

- **`BattleSession`** — authoritative state and bookkeeping: phase, turn number, active side, faction order, unit pool, alive/dead storage, per-faction visibility/explored-tile memory. Constructed from a `BattleBoardState` and a faction order.
- **`BattleBoardState`** — tile grid (`BattleTileState[x,y,z]`), occupancy, adjacency, and board-local pathfinding (Godot `AStar3D`). Coordinates are `Vector3I` with **X = width, Y = levels/height, Z = depth**.
- **Read side — `IBattleSessionQuery<TResult>`.** GUI/AI reads go through reified query objects in `scripts/battle/queries/` (`public TResult Execute(BattleSession session)`), executed via `runtime.Query(...)`. The result shape is part of the contract: proof-typed inputs (`AliveUnit`, `ValidatedPoint`) make most queries total, so they return bare results; absence-is-normal questions return `Option<T>` (`GetUnitAtTile`, `GetOperationForFaction`); only genuinely fallible questions return `Either<BattleQueryFailure, TResult>` (`GetHitChanceForAttack`, `GetFactionEndOfBattleSummary`). The one bare-result query without proof inputs is `CanUnitActNow` — it takes a raw `BattleUnitState` because `false` is a valid answer for any unit, and asking it outside an in-progress battle throws (trusted-core guard). Scene code mints the proofs at the runtime boundary — `BattleRuntime.TryGetAlive(unit) : Option<AliveUnit>` / `TryGetTile(coords) : Option<ValidatedPoint>`; the `None` path replaced what used to surface as query `Left`s. Don't grow public read methods on `BattleSession` — add a query.
- **Write side — `BattleAction` + `BattleActionExecutor`.** All *gameplay-initiated* mutation goes through actions submitted to `executor.Submit(action)`, which returns `IReadOnlyList<BattleActionResult>`. **Composite** actions (e.g. `MoveUnit`) yield **primitive** actions one at a time (`MoveUnitStep`); primitives are the atomic commit layer that mutate session state and emit `BattleEvent`s. The executor opens an action window around each primitive's execution (`BeginActionExecution`/`EndActionExecution`); hooks firing during that primitive's event dispatch may return interrupt actions, which accumulate in the window and are front-queued once the primitive commits, so reactions (overwatch, mines) act on post-step state. Composite child results are hidden from callers; the committed `BattleEvent` stream is the per-step source of truth. Session-internal bookkeeping systems (`BattleHook`s registered `After`, e.g. `ArmorRegenSystem`) may mutate state directly and raise follow-up events.
- **Combat** — tiles carry directional cover (`CoverDirections` flags + `TileCover` on `BattleTileState`, geometry in `CoverRules`); `AttackUnit` resolves an XCOM-style percent roll via the session's injected `IHitChanceCalculator` (default `StandardHitChanceCalculator`: Aim − cover, clamped 0–100) and the session's seedable RNG. `GetHitChanceForAttack` previews the same breakdown. Swap the to-hit algorithm by injecting a different calculator into `BattleSession`. Attack target-feasibility (weapon/self/ally/liveness/visibility/range) resolves once through `AttackContext.Resolve` — the shared gate for `AttackUnit`, `GetHitChanceForAttack`, and `HasAttackableTargetCondition`, and the sole production mint site for `AttackContext`. Firing spends ammo via `weapon.TrySpendShot(attacker.ActiveBuffDamageMods)` before the roll (a miss still consumes the round), yielding a `List<Damage>` (amount + element) derived from the weapon's `WeaponFrameData` packets × base `DamageStat`, folded through the weapon's `DamageContributions` (slot mods, then ammunition — mirrors `StatContributions`) then external buff mods; on a hit `ApplyDamageTo` applies the bundle and `UnitDamagedBattleEvent` carries it. Damage resolves through the target's equipped armor first (`BattleUnitState.EquippedArmor`): the pure `DamageResolver` (scripts/battle/combat/) splits a bundle into armor damage — 1.5x floored when a packet's `Element` matches the armor's element — and health damage, always computed from the un-multiplied amount, so a match never increases health spill; `UnitDamagedBattleEvent` carries the `ArmorDamage`/`HealthDamage` split. Damage re-arms the armor's regen delay; `ArmorRegenSystem` ticks regen on the owning faction's turn end and raises `UnitArmorRegeneratedBattleEvent`. Status effects: `Damage` packets optionally carry a `StatusEffectSpecData` (`scripts/items/effects/`; concrete subclasses `ImmobilizeStatusSpecData`, `DamageOverTimeStatusSpecData` — behavior by subclass, identity = the authored resource). `ApplyDamageTo` applies them after the unit survives, gated per spec by `ApplyChancePercent` and `RequiresHealthDamage` (per-packet spill via `DamageResolver.ResolvePackets`). Units hold `ActiveStatusEffect`s (re-apply refreshes duration); `IsImmobilized` blocks actions in `ValidateActingUnit` and `CanUnitActNow`. `StatusEffectSystem` (a `BattleHook` registered `After` on `TurnEndedBattleEvent`, priority -100, before `ArmorRegenSystem` by registration order) ticks statuses at the owning faction's turn end — decrement, DoT damage through the normal pipeline (re-arming armor regen delay), expiry — raising `UnitStatusEffectApplied/Ticked/ExpiredBattleEvent`.
- **Buffs** — condition-driven unit buffs (`scripts/buffs/` authored `BuffData` + `BuffConditionData` subclasses; runtime `Buff`/`BuffCondition`/`BuffHooks` in `scripts/battle/`). Granted at spawn from `CombatantData.InnateBuffs` plus `BuffGrantCapabilityData` on the equipped weapon/armor, deduped by resource identity. Lifecycle is a pure condition mirror (no durations): buff evaluation is an ordinary `Before`-phase hook (`BuffHooks.cs`) — `TurnStartBuffHook` on `TurnStartedBattleEvent`, `UnitSpawnedBuffHook` on `UnitAddedBattleEvent` — so a flip lands before that event's own broadcast, and, for turn start, before the AP refresh that runs once the whole turn-start dispatch completes and reads `MaxActionPoints`; flips raise `UnitBuffActivated/DeactivatedBattleEvent` and clamp `CurrentHealth` down to a reduced max (never heal, never below 1 — buffs cannot kill; death flows only through the damage pipeline). While active, a buff's `StatMod`s join unit stat resolution via `GatherStatContributions` and its `DamageBundleMod`s fold last (after slot and ammo mods) into weapon damage via `TrySpendShot(externalMods)`.
- **`BattleRuntime`** — the disposable facade over session + executor: the read entry point for session state (`Query(IBattleSessionQuery<TResult>)`), the proof mint doors (`TryGetAlive`/`TryGetTile`), and re-raising `BattleEventCommitted` / `ActionStarted` / `ActionCompleted`. Scene code generally talks to `BattleRuntime`, not the parts directly.
- **Hooks** — one substrate: `BattleHook` (+ generic `BattleHook<TEvent>`), matched to an
  event-tag *type* (`BattleEventKeys`; a tag is a concrete event record or a marker interface
  like `IPositionedBattleEvent`) and registered with a `HookPhase` (`Before`/`After`) and an
  int priority (lower first, then registration order) — one door,
  `RegisterHook<TEventKey>(hook, phase, priority = 0)`, internal on the session and public on
  `BattleRuntime`, plus `UnregisterHook` for one-shots (mines self-unregister from inside
  `OnEvent`; matches are materialized per firing, so this is safe). `Before` fires as an event
  is about to land — before the public `BattleEventCommitted` broadcast, so a Before hook's
  mutations are visible to every broadcast observer of that event ("the event lands fully
  formed"). `After` fires once the event is history — the old listener window. Either phase
  may mutate `BattleSession` directly, raise follow-up events (queued, keeping the committed
  stream linear), and/or RETURN interrupt actions from
  `OnEvent(HookContext { Session, Phase, SourceAction: Option<BattleAction> }, BattleEvent)`
  (`SourceAction` = the executor's in-flight primitive when one is open, else `None`).
  Interrupts from both phases accumulate, across a primitive's whole dispatch, into the
  executor's action window (`BeginActionExecution`/`EndActionExecution`), front-queued
  (aggregate `.Reverse()`d once) after the primitive commits, discarded if the primitive
  fails; a hook returning one outside that window throws — interrupts only mean something
  inside an executor action. Turn start is uniform at both invocation sites: availability
  refresh → `RaiseEvents` (buffs fire in `TurnStarted`'s Before window) → AP refresh; there is
  no non-event-keyed upkeep phase anymore. Systems: `StatusEffectSystem`/`ArmorRegenSystem`
  (`After`, `TurnEndedBattleEvent`, priority -100 — status ticks before armor regen, order
  preserved by registration stamp so a DoT's re-armed regen delay lands before regen runs),
  `ObjectiveSystem` (`After`, `TurnEndedBattleEvent`, priority +100, after all upkeep),
  `CapabilityEffectSystem` (`After`, `ItemThrownBattleEvent`), buff evaluation (`Before` — see
  Buffs bullet). There is no phase-by-subclass hook split anymore, no separate reaction/
  bookkeeping/upkeep hierarchy, and no `BattleEventType` enum — one `BattleHook` base, one
  registration door, phase as registration data (collapsed 2026-07).

### Presentation boundary
Godot scene scripts live in `scenes/` — currently `GameCamera`, `MovementLine`/`MovementLineBuilder`, `Interactable`/`IInteractable`, and the battle bridge in `scenes/battle/`; the `BattleSceneController`/`BattleScene`/HUD layer is not built yet. `scenes/battle/BattleEventSignalHandler` is the one adapter that binds a `BattleRuntime` and re-emits committed events as Godot signals (wrapping payloads in `BattleEventAdapter`/`BattleActionResultAdapter` `RefCounted` envelopes). Scene code reads state via `BattleRuntime.Query(...)` and never mutates the session directly. Keep this handler limited to signal adaptation — no battle-legality checks, no caching of authoritative state, no selection UX (selection is presentation state and is *not* tracked by `BattleSession`).

## Conventions
- Prefer Collection expressions over explicitly instantiating any container types.
- **LanguageExt** is globally imported (`GlobalUsings.cs`: `using LanguageExt; using static LanguageExt.Prelude;`). Prefer `Option<T>` over nullable returns for optional domain data.
- `SysColGeneric` is the alias for `System.Collections.Generic` (LanguageExt shadows some collection names).
- File-scoped namespaces under the `FunProject.*` root (`FunProject.Battle`, `FunProject.Stats`, `FunProject.Items`, …); the assembly root namespace is `funproject`. Two-space indentation. PascalCase types/members, camelCase locals/params, `_camelCase` private fields.
- After any code change, run `dotnet format` on the changed code.
