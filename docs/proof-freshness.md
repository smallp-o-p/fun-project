# Battle snapshot freshness

The game project activates the analyzer alongside proof and effect annotations.
`FPF001` reports stale use of `[SnapshotProof]` values. The domain
contract covers `AliveUnit`, `LiveObject` and `AttackTarget`: repeated synchronous
reads are valid until a battle mutation. `DeadUnit`, validated tiles and item
capability proofs are stable and are outside this analysis.

The analyzer uses Roslyn control flow. Copies, aliases, generic wrappers such as
`Option<AliveUnit>`, arrays and source query objects retain freshness provenance.
A join is stale if any incoming path is stale; acquiring a replacement on one
branch does not repair another branch. Repeated acquisitions repair only the
value actually assigned. Reading an old option's `Case` or executing an old
proof-carrying query does not mint a replacement.

`[InvalidatesProofs]` identifies global battle snapshot boundaries, including
runtime submissions, event dispatch, occupancy writes and raw damage. All live
snapshots are invalidated, even ones never passed to the boundary. Source helper
calls are checked transitively, including constructors and property setters.
Direct health/board/object writes are also effect boundaries. Spending AP or
ammunition alone preserves the facts represented by these three proofs.

`[PreservesProofs]` is an enforced contract on polymorphic query, stat and
availability helpers. `FPF002` checks implementations and overrides; decorating
a mutating helper does not suppress its effects. This contract preserves snapshot
facts, and permits unrelated writes such as ammunition expenditure or cache
materialization. Unknown dispatch, callbacks, asynchronous suspension and
unrecognized external calls are conservative boundaries.

Actions validate mutable facts during execution. These checks remain necessary because interrupts, hooks and callbacks
can change state before execution. Snapshot `State`, `Entity` and `Id` expose
stable identities for reacquisition; they do not promise current liveness or
position. Private retained proofs used exclusively through these identity properties
do not make their enclosing object a snapshot carrier; exposing a whole proof or
reading its position does. Deferred closures and bound delegates cannot retain snapshot proofs.
Immediate Option/Either callbacks may use fresh proofs repeatedly. Mutating callbacks
on repeated operators must acquire inside each invocation. Direct synchronous local
functions use the caller's current captured state. Converting one to a delegate
uses the deferred rule. Captured proof assignments conservatively invalidate the
caller; recursive proof closures require a different boundary or reacquisition.

## Deliberate limits

This is a domain analyzer, not an ownership or whole-program concurrency system.
The mint doors are the inspected BattleSession/BattleRuntime methods; callers
remain responsible for the validity of raw data and trusted-core construction.
Freshness does not prove session ownership; internal callers use proofs with their
owning session.
Raw positions or numeric query results extracted while fresh are ordinary values,
not proofs. Arbitrary heap storage, reflection, dynamic invocation and concurrent
mutation are outside the supported proof discipline; use raw identities and
reacquire at their boundaries.

Supported dependency operations are enumerated in `ProofEffects.ExternalRead`.
They are trusted contracts for the project's BCL collections with default
comparers, scalar/math operations, LanguageExt options/eithers and ZLinq queries.
Callbacks supplied to these operations are still inspected. Custom comparers,
custom collection/enumerator implementations, and subclassing dependency sum
wrappers are not certified by these contracts. Unknown operations, including
`List.Sort` and explicit `IDisposable.Dispose`, invalidate outstanding proofs.
Compiler-generated disposal of ordinary foreach iterators uses the same trusted
collection contract. Keep this small table auditable when adding dependencies.
Default scalar equality and scalar interpolation are preserving, including through
generic source helpers. Equality or formatting of arbitrary objects remains unknown.

Exception handlers receive conservative stale inputs; acquisition inside a
handler or finally block can restore freshness. Finally transfers are analyzed.
Lazy sequence capture and unsupported return shapes can require reacquisition
or a raw snapshot rather than being optimistically accepted. The analyzer does
not certify arbitrary cross-method field lifetimes, make a default proof valid,
or guarantee that future raw mutations have all been correctly annotated.

## Verification

The compiler-contract tests run without Godot:

```sh
dotnet test analyzers/ProofFreshness.Tests/ProofFreshness.Tests.csproj
```

The game project's reference uses
`OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`, alongside proof
annotations and caller migrations. There are no diagnostic exemptions. Exports
compile no analyzer tests and ship no Roslyn runtime dependency. Gameplay and interrupt behavior remain owned by the existing Godot
fixtures and full suite described in [testing.md](testing.md).
