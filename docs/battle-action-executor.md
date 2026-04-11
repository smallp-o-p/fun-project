# BattleActionExecutor Design

This document defines the first implementation slice of `BattleActionExecutor` for the tactical combat runtime.

It is a narrower design than the long-term architecture in [battlescape-architecture.md](./battlescape-architecture.md). The goal is to introduce the correct authority boundaries now, while keeping the implementation simple enough to use immediately.

## Purpose

`BattleActionExecutor` is the orchestration layer between action requests and authoritative battle-state mutation.

It exists to answer one question cleanly:

- given a `BattleActionIntent`, what happens next in the battle runtime?

It is responsible for:

- owning the pending action queue
- executing at most one action step at a time
- translating action intents into calls against `BattleSession`
- returning structured execution results
- providing one place to add future interrupt checks, animation pacing, and action-level sequencing

It is not responsible for:

- owning tactical truth
- rendering or animating battle actions
- pathfinding, visibility, or damage math
- AI decision-making

## Authority Boundaries

- `BattleSceneController` and `BattleAIController` create `BattleActionIntent` objects.
- `BattleActionExecutor` consumes those intents.
- `BattleSession` remains the only owner of authoritative tactical state.
- `BattleSession` emits `BattleEvent` values after state changes occur.
- visuals and HUD listen to `BattleEvent` and re-query `BattleSession` as needed.

The important split is:

- `BattleActionIntent` = command/request
- `BattleActionExecutor` = sequencer/orchestrator
- `BattleSession` = source of truth
- `BattleEvent` = notification that state already changed

## First Implementation Slice

The first usable executor should support the action types the runtime already knows how to resolve:

- `move_step`
- `pass_unit`
- `end_faction_turn`
- `throw_item`

This first slice is intentionally synchronous and single-threaded:

- `Tick()` resolves at most one queued action
- one action returns one `BattleActionExecutionResult`
- the executor does not yet wait for presentation acknowledgements
- the executor does not yet suspend and resume interrupted actions

That is enough to establish the correct battle-runtime boundary now.

## Why Single-Threaded

Authoritative battle mutation should remain single-threaded.

Reasons:

- combat outcomes stay deterministic
- interrupts are easier to reason about
- state mutation order is explicit
- replay, debugging, and future save/load remain tractable

If expensive work appears later, only pure read-only calculations such as pathfinding previews, LOS candidate queries, or AI scoring should be moved off-thread. The executor and `BattleSession` mutation path should remain single-threaded.

## Incremental Execution Model

Even though the first slice resolves one whole intent per `Tick()`, the executor API should already be shaped for incremental execution.

The intended evolution path is:

1. queue action intent
2. `Tick()` begins resolving it
3. executor commits one micro-step
4. session emits `BattleEvent`
5. executor either:
   - continues next tick
   - waits for presentation
   - pauses for interrupt handling
   - completes the action

That means a long move, overwatch interrupt, or reaction shot can later be spread across frames without changing the core ownership model.

## Intent Shape

`BattleActionIntent` should be an abstract base type with concrete subclasses for real action shapes.

Built-in intents should be concrete types, for example:

- `MoveStepBattleActionIntent`
- `PassUnitBattleActionIntent`
- `EndFactionTurnBattleActionIntent`
- `ThrowItemBattleActionIntent`

That gives each action only the fields it actually needs and lets the executor dispatch by intent type instead of a nullable property bag.

The first executor implementation may keep a `CustomBattleActionIntent` escape hatch for prototype-only actions and a `NamedBattleActionIntent` fallback for unsupported ids. Those are transitional lanes and should not become the main built-in action model.

## Execution Result Shape

Each resolved action returns a `BattleActionExecutionResult`.

The result should answer:

- which intent was executed
- whether it succeeded
- if it failed, why
- optional explanatory message

The first failure reasons should stay small and practical:

- `UnsupportedAction`
- `ActionRejected`
- `UnexpectedError`

## Public Surface

The first public executor surface should be:

- `Enqueue(BattleActionIntent intent)`
- `Tick()`
- `DrainQueue()`
- `PendingCount`
- `ActiveIntent`
- `LastResult`

And events:

- `ActionStarted`
- `ActionResolved`

These are executor-level orchestration events, not presentation state events. Presentation should still listen to `BattleSession.EventRaised`.

## Action Resolution Rules

### Move Step

- represented by `MoveStepBattleActionIntent`
- carries `UnitId`, `TargetCell`, and explicit step AP cost
- calls `BattleSession.TryMoveUnitStep(...)`

### Pass Unit

- represented by `PassUnitBattleActionIntent`
- calls `BattleSession.TryPassUnit(...)`
- marks the unit as done for the current faction turn
- selects the next available allied unit when one exists

### End Faction Turn

- represented by `EndFactionTurnBattleActionIntent`
- carries the issuing `Faction`
- calls `BattleSession.TryEndFactionTurn(...)`
- executor should reject the intent if battle state already advanced to a different active side before dequeue
- advances to the next faction when legal

### Throw Item

- represented by `ThrowItemBattleActionIntent`
- carries `UnitId`, `TargetCell`, and a `ThrowableItem` reference
- calls `BattleSession.TryThrowItem(...)`

### Custom

- if a `CustomBattleActionIntent` is present, the executor may delegate to its resolver
- this is a temporary extensibility lane for prototyping
- built-in action types should not use this path

## Relationship To Future Interrupts

This first implementation does not yet suspend or resume actions. The next executor growth path is:

1. convert long movement from one intent into a multi-step execution context
2. after each committed step, query interrupt candidates
3. suspend the current action if a valid interrupt exists
4. resolve the interrupt action
5. either resume or abort the original action

That future design fits naturally on top of the queued `Tick()` API.

## Relationship To Visuals

Visual systems should not listen to executor internals to understand battle state.

Recommended rule:

- listen to `BattleEvent` for visual updates
- optionally listen to executor `ActionResolved` for orchestration or controller logic

Example:

- `UnitMoved` battle event tells the visuals a unit has moved
- controller may use executor result to decide whether to queue the next action

## Initial Test Coverage

The first executor tests should cover:

- queued actions execute in FIFO order
- `move_step` succeeds when legal
- `move_step` fails when illegal
- `pass_unit` marks the unit done and moves selection forward
- `end_faction_turn` advances the active side
- stale `end_faction_turn` intents are rejected once the active side has changed
- `throw_item` consumes the throwable when legal
- unsupported action ids fail cleanly
- custom resolver fallback still works

## Deliberate Limitations In This Slice

- no pathfinding integration yet
- no presentation wait state yet
- no overwatch or interrupt stack yet
- no multithreaded query work
- no durable item/unit ids beyond current runtime references

These are acceptable limitations for the first executor as long as it establishes the right architectural boundary now.
