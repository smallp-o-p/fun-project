---
name: test-audit
description: "Use when writing, changing, reviewing, or sweeping tests in this repository; before adding a test, when coverage looks suspicious, duplicative, or implementation-coupled, or when evaluating a production seam that only tests exercise."
---

# Test Audit

One value bar for three activities. The authoring gate judges every new or
changed test at write time; a scoped audit hunts a few high-confidence
candidates; a subsystem sweep covers one subsystem's whole test surface under
`test/` in coherent batches. Optimize for confidence over deletion count.
GdUnit4 tests live in root `test/` — read
[AGENTS.md](../../../AGENTS.md) and the relevant
[docs/architecture-reference.md](../../../docs/architecture-reference.md)
sections first.

## Authoring gate

Before adding any test, answer four questions; a missing answer means do not
add it yet:

1. What observable behavior, invariant, or independent contract does it protect?
2. What credible regression makes it fail?
3. Why does existing coverage not already catch that failure? Each contract
   has one primary owner at the strongest boundary; another layer needs its
   own distinct risk, such as a lifecycle failure the owner cannot reach.
   Prefer extending an existing test, table-driven case, or fixture scenario
   over a near-duplicate; consolidate duplicated setup in the same change.
4. Does it need a production seam (export, flag, wrapper, injection hook)
   that no production caller needs? If yes, move the test to the real
   boundary.

Then check the test against every [junk pattern](#junk-patterns); a match
fails the gate unless the [retention bar](#retention-bar) names the contract
it independently guards. A test that would break under behavior-preserving
refactoring is asserting implementation, not behavior; rewrite it at the
owning boundary before landing it.

Regression tests must fail on the pre-fix code for the intended reason and
pass after the owner-boundary repair; one that never demonstrably failed
proves the double, not the fix. One regression at the owner boundary covers
the bug; do not replay it at every layer. While iterating a feature, do not
keep or add regression tests for superseded behavior — update or delete with
the contract.

## Repo test rules

- Build on `test/helpers/` (`TestData` for data, `BattleFixture`/
  `GeoscapeFixture` for simulations, `Assert` for assertions) — never ad-hoc
  setup. `using var` owns every fixture; direct
  constructor/factory/lifecycle tests own and dispose their actual subject.
- `BattleFixture.Runtime` owns the session's sole executor; never create a
  second live executor for a fixture session.
- Mint fresh proofs at use time via fixture `Alive`/`Live`/`At`/`Target`; use
  raw fixture `Submit` with a raw `BattleAction` for deliberate invalid or
  stale actions.
- Event capture begins before setup; `ClearEvents()` opens the assertion
  window and nothing resets it implicitly — assert including setup events
  unless cleared.
- Interruption and hook tests exercise interrupting hooks inside an actual
  executor submission, never a simulated path.
- Respect parse/proof contracts: rejected trusted parameters are caller bugs
  (`InvalidOperationException` from `Submit`); re-checked mutable liveness is
  interruption. Route reads through queries — never add public
  `BattleSession` reads for testing.
- Test doubles stand in for collaborators; they must not manufacture the
  result or ordering under assertion.

## Junk patterns

The authoring gate rejects a new test matching one; audits hunt existing
tests that do.

- assertion-free coverage probes; self-comparisons; identity copiers;
- copied fixtures, inventories, manifests, or export lists; exact source,
  import, or string greps; private predicate or call-shape tests duplicated
  at real boundaries;
- duplicate invocations of one contract; local replays of what
  `test/helpers/` already proves;
- tests preserving test-only exports, globals, or wrappers; dead production
  code whose only callers are tests; expected values produced by the helper
  or renderer under test;
- mocks or doubles implementing the asserted behavior or ordering; one
  identical mock standing in for different APIs;
- fixtures supplying event ordering or state the executor should produce;
  persistence asserted against a store the path never writes;
- capability tests restating declared flags instead of exercising the
  promised delivery or acknowledgement;
- negative controls passing for an unrelated reason (a different guard's
  denial, a rejection the production path never reaches); names or fixtures
  promising more than the input exercises.

## Value bar

Tests justify their maintenance cost by protecting behavior, a credible
regression, or an independently meaningful contract. Meaningful independent
lower-level coverage stays: observable call ordering, state/event/cleanup
invariants, and typed failure contracts are real contracts, not duplication.
An existing test that must change for behavior-preserving reorganization is
suspect, not automatically deletable; the authoring gate still rejects new
ones. Before judging a candidate, read the test and its production owner,
entry point, callers, callees, siblings, overlapping tests, and history —
[AGENTS.md](../../../AGENTS.md) first. When the test claims dependency-backed
behavior, inspect the dependency source or types directly.

## Discovery

Keep discovery read-only and report evidence before editing. Tests live in
root `test/`, mapping to owners in `scripts/`, `scenes/`, and `resources/`,
plus shared support in `test/helpers/`; include `addons/` or tooling only when
requested or affected. Exclude generated caches (`.godot/`,
`gdunit4_testadapter_v5/`), `.openxcom-research/`, and retired
`fun-project-test/` leftovers from default scope. Outside a subsystem sweep,
prefer a few high-confidence candidates over a speculative inventory.

## Retention bar

Keep a test that independently enforces a public API, scene, resource,
authored data, config, storage, security, platform, default, or architecture
contract. Also keep:

- call ordering when order is observable behavior;
- regressions with a credible failure mode;
- source inspection when it is the cheapest independent guard: it fails when
  the contract changes and survives an identifier-only refactor;
- a retained test failing on the baseline: treat it as a possible product
  bug, reproduce it, repair the owner rather than deleting it.

Static or slow is not a deletion reason. A source-shape-suspicious test may
be a legitimate authored/export/static architecture contract; prove otherwise
before removing it.

## Candidate evidence

Record every field before editing; a missing field means the candidate is
not ready for deletion:

- exact test name and location;
- what failure it can actually detect;
- non-test callers of the covered production or support seam;
- stronger remaining owner-boundary proof, or why none is needed;
- relevant history and why the test or seam exists; production or
  test-support deletion unlocked;
- risk and the focused validation command.

## Edit shape

Choose one coherent owner-boundary batch. Delete obsolete test-only exports,
wrappers, and dead production paths instead of preserving aliases. Move
retained regressions to their canonical owners; consolidate repeated
assertions into one generic contract. Prefer net-negative production LOC; do
not add replacement tests restating the same implementation or convert
uncertain candidates into cleanup to raise deletion counts.

## Validation

Never edit sources while `dotnet test` runs. For C# changes: run
`lsp_diagnostics` on changed files, `dotnet format`, `dotnet build`, then a
focused run — `dotnet test fun-project.csproj --no-restore --filter
"FullyQualifiedName~<TestClass>"` (e.g. `WeaponSystemTest`), choosing the
classes actually affected. Shared runtime, executor, hook-ordering, or
cross-subsystem changes need the full `dotnet test fun-project.csproj
--no-restore`. Finish with `git diff --check` and a diff self-review.

Tests are GdUnit4 inside a Godot mono runtime, compiled Debug-only by the
csproj and launched via `.runsettings` `GODOT_BIN` (update it if Godot
moves). Never test while the editor rebuilds — both write
`.godot/mono/temp/bin`; a host crash (`-1073741819` / 0xC0000005) means rerun
afterwards. Documentation-only changes need diff, link, and configuration
checks only. Scene/editor work goes through `godot-ai` with a confirmed
session (AGENTS.md); never hand-edit scenes while one is connected.

Report the proof actually run, including failed launches and incomplete runs.
A completed sweep with zero deletion candidates is a valid outcome; report
its scope and evidence without implying cleanup occurred.

## Landing

Commit requested changes per repository rules; push, merge, or publish only
with explicit authorization. Preserve unrelated local edits: stage only
audited files. Land one coherent batch at a time; sequence a remaining
subsystem sweep as a named follow-up.

## Handoff

Report:

- scoped evidence for each deletion or simplification;
- retained false positives and why they remain valuable;
- verification actually run, plus limitations (e.g. an editor-rebuild rerun);
- production versus test-support LOC when auditing (optional for routine
  authoring);
- commit state and named follow-ups.
