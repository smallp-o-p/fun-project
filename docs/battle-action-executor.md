# BattleActionExecutor Design

This document describes the current role of `BattleActionExecutor` in the tactical combat runtime.

`BattleActionExecutor` is the interrupt-resolution and sequencing layer for queued `BattleAction` work.

## Purpose

The executor exists to answer two questions cleanly:

- what is the next primitive action that should execute?
- while it commits, which hook-returned interrupt actions should run before queued work continues?

It is responsible for:

- owning the pending action queue
- owning the hook registry and registering the default systems (status effects, armor regen, capability effects, buff evaluation, and `ObjectiveSystem` — all registered directly in its constructor; `ObjectiveSystem` is registered once under the `BattleEventTag` catch-all at priority +100, receives every event, and filters objectives by their declared observed keys internally. It has no self-registration or executor back-reference. An objective flips mid-dispatch when a committed event makes its `Check` return Passed or Failed; the authored directive then responds to that flip)
- accepting a submitted `BattleAction` intent and resolving the resulting action chain
- executing one step of the queued action head at a time (a step that returns `Result.Incomplete` keeps its action at the queue head)
- opening an executor-local action window around each step's execution so hooks firing during its event dispatch can return interrupt actions, collecting those interrupts once the step commits (discarding them if it fails), and throwing when a hook returns interrupts with no window open — the guard lives in the executor
- returning one `BattleActionExecResult` per submission: the submitted action plus every `BattleEvent` committed while resolving it (including interrupt actions' events)
- surfacing broken invariants: a step that returns `Result.Rejected` (or throws) fails the submission with an `InvalidOperationException` — parameters are trusted, so a rejection is always a caller bug

It is not responsible for:

- owning tactical truth
- rendering or animating battle actions
- pathfinding graph maintenance
- AI decision-making

## Authority Boundaries

- `BattleSession` remains the only owner of authoritative tactical state.
- `BattleAction` is the command object. It owns action-specific legality checks and execution against the session. It may be primitive, or it may unfold into primitive child actions.
- `BattleActionExecutor` invokes primitive actions produced by queued actions.
- scene controllers, HUD code, and AI build actions and submit them to the executor.
- visuals and HUD react to `BattleSession.BattleEventCommitted` after session state changes.

The important split is:

- `BattleAction` = queued tactical intent, which may be composite or primitive
- `BattleActionExecutor` = hook registry owner, hook-interrupt mediator, queue, and invoker
- `BattleSession` = source of truth
- `BattleEvent` = notification that authoritative state already changed

## Current Public Surface

The current executor API is:

- `Submit(BattleAction action)`
- `RegisterHook<TEventKey>(BattleHook hook, int priority = 0)` and `UnregisterHook<TEventKey>(BattleHook hook)`
- `Dispose()`

There are no executor-level lifecycle events and no `LastResult`: outcomes are observed through the returned `BattleActionExecResult` (carrying the committed `BattleEvent` stream) and through committed session state.

The hook registry lives on the executor: its constructor registers the default systems — status effects, armor regen, capability effects, buff evaluation, and `ObjectiveSystem` — directly. `ObjectiveSystem` is registered once under the `BattleEventTag` catch-all at priority +100, receives every event, and filters objectives by their declared observed keys internally; it has no self-registration or executor back-reference. An objective flips mid-dispatch when a committed event makes its `Check` return Passed or Failed, and the authored directive then interprets the flip. The `RegisterHook`/`UnregisterHook` methods above are the registration surface. `BattleRuntime.RegisterHook` is the facade door delegating to the executor — the session no longer knows hooks exist; it only announces events. Disposing the executor detaches its `BattleEventCommitted` subscription from the session.

## One Executor Per Session

A session must have exactly one executor for its lifetime. The executor owns the hook registry and registers the default systems in its constructor, so a second executor attached to the same session would double-register them: statuses would tick twice per turn end, armor regen would run twice, and objective routing would run twice. Nothing at the language level prevents a second executor — the invariant is held by ownership: production code never constructs a second one (`BattleRuntime` owns the session's single executor, and its public constructor subscribes its `BattleEventCommitted` re-raise before constructing that executor, so scene subscribers observe a cause event before any hook-born follow-up events), and test code goes through the shared helpers: `BattleActionTestHelper.ExecutorFor` caches one executor per session, and `BattleTestFactory.RuntimeFor` wraps the public runtime constructor — `RuntimeFor` returns a fresh runtime each call, so a test session gets exactly one runtime and keeps it.

## Execution Flow

The current happy-path flow is:

1. controller or AI constructs a `BattleAction`
2. the action is submitted with `Submit(action)`
3. the executor queues the submitted action internally
4. while the queue has work, the executor executes the queue head's next step inside an open, executor-local action window
5. the action validates itself against the current `BattleSession`
6. the action applies its change through session and board helpers
7. `BattleSession` raises each resulting `BattleEvent`: the session's dispatch loop broadcasts it through `BattleEventCommitted`, and the executor's `BattleEventCommitted` subscription fires that event's hooks after the broadcast — any hook firing inside the open window may return interrupt actions, which accumulate executor-locally in evaluation order
8. the executor closes the action window once the step's execution path returns, whether it completed, failed, or threw
9. a failed step's collected interrupts are discarded; nothing is enqueued (and a `Result.Rejected` throws out of `Submit`)
10. on success, the executor aggregates the window's interrupt actions and reverses that combined list once (never per event), then inserts the result ahead of paused work — so an earlier-raised event's interrupts still run before a later event's
11. if the battle ended during the step's dispatch, the executor clears remaining queued work instead: unexecuted composite steps and queued interrupts are dropped
12. otherwise, the executor continues until the submitted action and its interrupts settle
13. `Submit` returns exactly one `BattleActionExecResult`: the submitted action and every event committed during its resolution

Actions should be executed through an explicit `BattleActionExecutor` by calling `Submit`. A normal submission leaves the queue empty when it returns; the queue is non-empty only while the executor is actively resolving submitted work.

Composite actions execute across several steps (for example, `MoveUnit` commits one tile per `Execute`, returning `Result.Incomplete` until the route drains). Those intermediate steps are commit checkpoints for validation, event emission, and hook evaluation; they are not separate public results. Interrupt actions returned by hooks still mutate state and raise committed `BattleEvent`s as they are resolved inside the same submission, and their events are part of the submission's result. If a step's dispatch ends the battle, remaining composite steps and any queued interrupts are discarded rather than run against an ended session. `Submit` does not expose `Tick`; it queues the provided action, resolves the action and its interrupts immediately, and returns.

## Supported Validation Rules

Actions are constructed with trusted parameters (proof types like `AliveUnit` and `ItemWith<TCap>` gate what can even be built), and they re-check only state-dependent facts at commit time: battle phase, active side, board occupancy, attack feasibility (shared through `AttackContext.Resolve`), path legality, and stale faction-turn requests. A failed check returns `Result.Rejected`, which the executor surfaces as an `InvalidOperationException` — a submission like that is always a caller bug, not a gameplay outcome. Availability gating (AP, turn order, immobilization) belongs to the read side (action conditions, `CanUnitActNow`) before an action is ever built.

One class of state change is NOT a caller bug: the executor interleaves interrupt actions inside one submission, so facts proven at construction — liveness, item possession, equipped weapon, remaining charges, target feasibility, loaded magazine — can be invalidated by an earlier interrupt in the same submission. Actions re-check those mutable facts at `Execute` and return `Result.Interrupted` to drop the action quietly (`ApplyDamage`, `PassUnit`, `ThrowItem`, `UseItem`, `ReloadWeapon`, and `AttackUnit`, whose `AttackContext.Resolve` misses and empty-magazine checks interrupt rather than reject — construction-time staleness cannot be distinguished from interleaving at Execute time, so all feasibility misses drop silently; the caller's availability gates live on the read side). `UseItem` validates depletion before spending AP.

The split between validation and reaction: rules that *prevent* a commit ("this tile cannot be entered") are validation rules inside the action; rules that *react to an occurred fact* ("a watched tile became occupied") are hooks — they observe a settled fact after the event is broadcast. Because a hook's interrupt actions run inside the same submission, immediately after the step that raised the event, reactions still act on the committed state that caused the event: a unit stepping from `X` to `X + 1` is on `X + 1` when an overwatch shot or mine detonation resolves — the longer move's remaining steps are still pending and can be cut short by `Result.Interrupted`.

The executor does not need to know which concrete action type it is running.

## Single-Threaded Execution

Authoritative battle action execution remains single-threaded.

Reasons:

- combat outcomes stay deterministic
- ordering stays explicit
- replay and debugging remain tractable
- interrupts and future sequencing are easier to reason about

Pure read-only work such as path previews or AI scoring can still move elsewhere later. The executor and `BattleSession` action path should remain single-threaded.

## Relationship To Visuals

Visual systems should not infer tactical truth from executor internals.

Recommended rule:

- use the submitted action's `BattleActionExecResult` (its committed `BattleEvent` stream) for command outcomes
- use read-side `IBattleSessionQuery<TResult>` values for previews
- use `BattleEventCommitted` for visual updates after state has committed

Example:

- controller uses `FindPathForUnit` and related queries to decide whether to enable a move confirmation, then submits destination steps only to `MoveUnit`
- scene visuals listen for committed `UnitMovedBattleEvent`
- HUD refreshes AP and prompts from relevant committed battle events

Godot scene nodes that need actual Godot signals should adapt this C# event at the presentation boundary. `BattleSession` stays a pure runtime object; a scene/controller can subscribe to `BattleEventCommitted` and re-emit a Godot signal with Variant-compatible payloads for animation code.

## Initial Query Surface

The executor does not expose preview evaluation. Controllers and AI should use explicit read-side queries, for example:

- reachable tiles for a unit (`GetPossibleMoveTilesForUnit`)
- a movement path preview (`FindPathForUnit`)
- a hit-chance preview for an attack (`GetHitChanceForAttack`)

Those should stay read-only and should not bypass the authoritative action path.
