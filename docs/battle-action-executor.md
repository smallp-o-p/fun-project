# BattleActionExecutor Design

`BattleActionExecutor` is the sequencing and reaction layer for battle mutations. It owns the pending action stack, the hook registry with the default systems, and the interrupt machinery. It runs one primitive step at a time and collects the interrupt actions that hooks return.

Detail: [battlescape-architecture.md](battlescape-architecture.md) (runtime overview), [battle-session-command-pattern.md](battle-session-command-pattern.md) (command and query shapes).

## Authority Boundaries

- `BattleSession` is the only owner of authoritative tactical state.
- `BattleAction` is the command object. It owns its legality checks and application against the session. It may be primitive or composite.
- `BattleActionExecutor` invokes the primitives a submitted action yields, in order, and routes hook-returned interrupts back through the same path.
- Controllers, HUD code, and AI build actions and submit them. Presentation reacts to committed events.
- `BattleEvent` reports a state change that already committed.

The executor runs each action through its `Execute` method.

## Public Surface

- `Submit(BattleAction action)` — `Submit` resolves the action and its reactions. It returns one `BattleActionExecResult`. The result holds the submitted action plus every committed `BattleEvent`. The span is a zero-copy `ReadOnlySpan<BattleEvent>` into the executor's append-only log. One submission is one `Submit` call and all the work it resolves.
- `RegisterHook<TEventKey>(hook, priority = 0)` / `UnregisterHook<TEventKey>(hook)` — These methods are the registration surface. `BattleRuntime.RegisterHook` delegates here.
- `Dispose()` — `Dispose` detaches the executor's `BattleEventCommitted` subscription.

Read outcomes from the returned event stream and committed session state. `BattleRuntime` raises `ActionStarted` and `ActionCompleted` around each submission; `EventPlaybackDirector` consumes `ActionCompleted`.

Submitting to a battle whose outcome is already decided is a caller error. Both submission paths, `BattleRuntime.ExecuteAction` and a direct executor `Submit`, reject it with an `InvalidOperationException` through one shared internal admission guard. `ExecuteAction` rejects before its `ActionStarted` signal. The rejection happens before any queue mutation, action signal, or committed event.

Completion during a submission is a normal case: it finishes the current step's atomic effect and causal event delivery. The executor then drops queued interrupts and remaining composite steps. The original submission returns its committed events. The result is normal when no actual failure occurred.

## One Executor Per Session

The constructor registers the default systems:

- `StatusEffectSystem` and `ArmorRegenSystem` — `TurnEndedBattleEvent`, priority −100.
- `StunRecoverySystem` — priority −90.
- `CapabilityEffectSystem` — `ItemThrownBattleEvent`.
- `TurnStartBuffHook` — `TurnStartedBattleEvent`.
- `UnitSpawnedBuffHook` — `UnitAddedBattleEvent`.
- `ObjectiveSystem` — catch-all `IBattleEventTag` at +100. It receives every event and filters each objective's declared observed keys internally.

Keep one live executor per session through ownership. Duplicate executors register duplicate upkeep handlers, causing statuses, armor regen, and stun recovery to tick twice. Production `BattleRuntime` owns the session's single executor. It subscribes the `BattleEventCommitted` re-raise before it constructs the executor, so subscribers observe cause events before follow-up events from hooks. Tests reuse `BattleFixture.Runtime`. Code disposes the previous owner before creating a lifecycle replacement.

## Execution Flow

1. A controller or AI constructs a `BattleAction` and submits it immediately against the same battle state.
2. `Submit` pushes the action onto the stack and resolves work until the stack is empty.
3. Each loop takes the stack head as the in-flight action and calls its `Execute(session)`. The action validates itself against current session state. It applies its change through session and board helpers and raises its events.
4. `BattleSession` broadcasts each event after refreshing visibility and invalidating affected action-option availability. The executor's subscription appends the event to the log and fires matching hooks. A hook may mutate state and raise follow-up events that queue and run after the current event. A hook may also return interrupt actions; interrupts require an in-flight action. The executor throws on an invalid return. Hooks request queued actions by returning them from `OnEvent`; this convention relies on caller discipline.
5. On `Result.Completed` the executor pops the action and pushes the step's captured interrupts ahead of paused work. The executor reverses the combined list once, so earlier events' interrupts run first. On `Result.Incomplete` the action stays at the head and the executor pushes its interrupts. On `Result.Interrupted` the executor drops the action and raises no exception.
6. Only `Result.Rejected` fails the submission as a converted error. `Submit` converts it to `InvalidOperationException`, because parameters are trusted and a rejection is a caller bug. Arbitrary action or hook exceptions propagate unchanged. In both cases the executor clears queued work and invalidates availability. Committed state and events remain.
7. When the battle ends mid-dispatch, the executor drops remaining composite steps and queued interrupts and ends execution.
8. `Submit` returns exactly one result: the submitted action and every event committed while resolving it, including interrupt events.

Composite actions such as `MoveUnit` commit one step per `Execute` and return `Result.Incomplete` between steps. Those steps are commit checkpoints for validation, event emission, and hook evaluation. Their events belong to the submission's single result. Interrupt actions mutate state and raise committed events inside the same submission, and their events belong to the submission's result. A normal submission leaves the stack empty.

## Validation and Interruption

Code constructs actions with trusted parameters. Proof types such as `AliveUnit` and `ItemWith<TCap>` restrict what code can construct. At commit time an action re-checks only state-dependent facts. The facts cover battle completion (outcome), active side, board occupancy, path legality, and stale faction-turn requests. Actions share attack feasibility through `AttackContext.Resolve`. A failed check returns `Result.Rejected`, which `Submit` throws as an `InvalidOperationException`.

Availability checks for action points (AP), turn order, and incapacity belong to the read side. They live in action conditions and `CanUnitActNow`, before code builds an action. Mutable facts can still change between construction and commit: liveness, incapacity, item possession, equipped weapon, remaining charges, target feasibility, loaded magazine. Actions re-check these at `Execute` and return `Result.Interrupted` to stop without an exception.

`MoveUnit` validates its whole route once (total AP, adjacency, occupancy) and then re-checks liveness, incapacity, and next-cell occupancy on every step. `AttackEntity`, `ReloadWeapon` (before `CanReload`), `ThrowItem`, `UseItem`, `InteractWithObject`, and `PassUnit` interrupt on lost liveness or incapacity (unconsciousness and immobilization included). The interrupt happens before spending AP, ammunition, charges, items, or mutating objects. `EndFactionTurn` stays independent of any unit's capacity; a stale faction-turn request is a validation rejection. `ApplyDamage` checks target liveness only and can still damage unconscious bodies. A reaction can stop a route after its already-committed step and preserves the AP for later steps.

Actions validate legality before commit. Hooks react to committed changes, such as a watched tile becoming occupied.

A hook's interrupts run in the same submission, immediately after the step that raised the event. Reactions act on the committed state that caused the event. Example: a unit steps from X to X + 1. The unit stands on X + 1 when an overwatch shot resolves. The reaction can stop the remaining steps.

`AliveUnit` includes unconscious bodies: it proves life and board presence. `IsIncapacitated` is `IsDead || IsUnconscious || IsImmobilized`. Test interruptions through executor submissions with hooks.

## Turn-End Upkeep

The executor registers owner-turn upkeep on `TurnEndedBattleEvent` in this order:

1. `StatusEffectSystem`, priority −100.
2. `ArmorRegenSystem`, priority −100 (registration order breaks the tie).
3. `StunRecoverySystem`, priority −90.

`StunRecoverySystem` owns its fixed `uint` rate of `5u`, its stop-on-completion rule, and its event emission. The hook uses the session's existing unit access and event dispatch. Recovery clamps to the unit's current stun before narrowing from `uint`. `UnitStunRecoveredBattleEvent.AmountRecovered` is `uint` and requires a positive reduction.

Recovery requires a living conscious unit. AP and immobilization are independent of recovery eligibility. Status damage can cause unconsciousness before recovery runs. Ended sessions skip recovery.

When the designated player loses all conscious units, `ObjectiveSystem` suppresses routing on the unit death or unconscious event. The suppression keeps the player-wipe defeat rule authoritative. Other events still update objective history and can queue follow-ups after the battle ends. The first `EndBattle` freezes victory capture membership before `SessionEndedBattleEvent` observers query it. Executor queue clearing then drops remaining work. Applying captures to the campaign is deferred (see [battlescape-architecture.md](battlescape-architecture.md)).

## Single-Threaded Execution

Authoritative execution is single-threaded: outcomes stay deterministic, ordering stays explicit, and interrupts stay tractable. A later change may move pure read-only work such as path previews to other threads. The executor and the session action path must stay single-threaded.

## Relationship To Visuals

Presentation uses the runtime's public results and queries:

- use the submission's `BattleActionExecResult` for command outcomes;
- use read-side queries for previews;
- use `BattleEventCommitted` (via the runtime re-raise) for post-commit visual updates.

Scene nodes that need Godot signals adapt the C# event at the presentation boundary. `EventPlaybackDirector` is that adapter for playback. The session stays a pure runtime object.

Controllers and AI preview actions through read-only queries: `GetPossibleMoveTilesForUnit`, `FindPathForUnit`, and `GetHitChanceForAttack`. Submit gameplay changes as actions.
