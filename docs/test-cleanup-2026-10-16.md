# Test cleanup evidence — 2026-10-16

Base: `cd705e719a7641a89850103e0a59badd3bd98fd4` (fresh `origin/master`). Branch: `chore/test-purge-consolidation`.

Only test sources and this audit evidence change. Production sources, project dependencies, instrumentation filters, and checked-in settings are unchanged. PR35's checkout/processes were not modified. No push or PR publication.

## Results

| Measurement | Before | After |
| --- | ---: | ---: |
| Completed/passing Godot test cases | 1,260 | 1,258 |
| Failed / skipped | 0 / 0 | 0 / 0 |
| Production lines covered / total | 8,758 / 9,291 | 8,758 / 9,291 |
| Production branches covered / total | 3,484 / 4,043 | 3,484 / 4,043 |
| Private static test helper declarations | 179 | 171 |
| Shared helper C# files | 11 | 11 |
| Audited duplicate local hook classes | 4 | 2 shared replacements |
| Test C# files / source lines | 170 / 29,641 | 170 / 29,546 |

Test-code diff: **13 files, +93 / -188 lines (95 net removed)**. Helper counts use private static declarations excluding readonly/const fields; case counts agree with completed TRX counters.

All 9,291 production class/source-line locations match. No covered locations were lost or added; all 533 uncovered locations are identical. Every reported per-location branch-condition coverage value also matches. Exact comparison and artifact SHA-256 values are in [the JSON evidence](test-cleanup-2026-10-16.json).

## Audited case consolidations

- `BattleObjectSessionTest.StartPlacesObjects` is folded into `PlacementBlocksOccupancy`. Both fresh objects remain at `(1,0,1)` and `(2,0,2)`, with count two, initial status, position/live proof, and rejected reinforcement onto each occupied tile. The original count-only invocation duplicated the same concrete factory startup. Production callers include `BattleLauncher` and `GeoscapeSession`; object enumeration feeds targeting. History `687cac2` retained the two-object success scenario, which this change preserves. Risk: dropping a placement or allowing occupied-cell spawning; the retained test checks both. Focus: `FullyQualifiedName~BattleObjectSessionTest`.
- `ItemCapabilityTest.FindCapabilityReturnsSomeForAttachedAndNoneForAbsent` and `WithReturnsProofBindingItemAndCapability` become `LookupAndProofPreserveCapabilityAndRejectAbsentKinds`. The retained scenario asserts payload range 6, absent charges lookup, item identity, capability identity against a fresh lookup, and absent charges proof. `EquippableItem.With` maps `FindCapability`; neither branches on range. Non-test callers include capability effect/consumption and unit loadout/view code. Both original tests came from capability composition (`c01d2af`). The old proof scenario's incidental default range was never asserted. Risk: binding the wrong item/capability or returning a proof for an absent kind; all remain asserted. Focus: `FullyQualifiedName~ItemCapabilityTest`.

## Helper consolidation

- `PackRoot` → existing `GeoscapeTestScenes.Pack`: identical packing, prototype freeing, error handling; the invalid-map-root case stays.
- Both progression `SeededBattle` helpers → existing `BattleFixture.Started`: preserve 8×1×8 map, fresh seed combatant at origin, supplied faction, inert objective, seed 7, and disposal. Both copies originated in `4357720`.
- `MarkerHook` / `StatusObserverHook` → shared `CallbackHook`: preserve callback timing, priorities, and no interrupts (`582c88a`).
- `BuildInterruptsHook` / `InterruptActionsHook` → shared `MoveInterruptsHook`: preserve `UnitMovedBattleEvent` and build fresh proof-carrying interrupts at firing time (`7fa05be`).
- Repeated interactive-object builders → existing `TestData.MakeObject`, preserving names, capability order/default AP 1 and explicit AP 2. Remove the `UnitAt` forwarder in favor of the existing fixture method.

Retained apparent duplicates include vertical/custom-cost movement, multi-step movement, death/index cleanup and replacement spawning, authored objective identity, playerless/capability-bearing negative factory inputs, expiry/ordering, and resource/rendering/input tests. These protect distinct contracts. The audit favored retaining them over speculative deletion.

## Collection and reproduction

Environment: Windows x64; .NET SDK 10.0.401 / runtime 10.0.12; Godot 4.7.2 mono; pinned `dotnet-coverage` 18.12.0. Imported assets and optional Godot AI addon were copied into this worktree from the existing checkout; no shared cache was changed.

Restore tools with `dotnet tool restore --configfile .config/coverage-nuget.config`. Use the unchanged `.config/coverage.xml`: only `fun-project.dll`, excluding `test/`, generated adapter, `.godot/`, and `obj/`. The report contains no test/generated/diagnostic sources. Filter SHA-256: `bb7af10bfd43bad03bd644064b89586cd01162efae385e21817bd03791a6ec1c`.

Private settings are retained under `TestResults/purge/`: `local.runsettings` adds `--audio-driver Dummy --log-file <this-worktree>/TestResults/purge/godot.log`; `override.cfg` selects Dummy audio, user directory `fun-project-test-purge-20261016`, and disables optional editor plugins. Copy the latter to the project root while reproducing, then move it back. Both full measurements used these same settings, a graphical runtime, and isolated output paths.

GdUnit 5 hardcodes the machine-wide pipe `gdunit4-message-pipe`. To avoid PR35 collisions, **only this worktree's copied** `.godot/mono/temp/bin/Debug/GdUnit4Api.dll` substitutes equal-length UTF-16 literal `gdunit4-purge02-pipe` at its two occurrences. The shared NuGet package is untouched and its SHA-256 matches the saved original copy. After each Debug build, use the retained `TestResults/purge/isolate-pipe.py`, or equivalently:

```python
from pathlib import Path
p = Path('.godot/mono/temp/bin/Debug/GdUnit4Api.dll')
b = p.read_bytes()
old = 'gdunit4-message-pipe'.encode('utf-16le')
new = 'gdunit4-purge02-pipe'.encode('utf-16le')
assert len(old) == len(new) and b.count(old) == 2
p.with_suffix('.dll.purge-original').write_bytes(b)
p.write_bytes(b.replace(old, new))
```

This is private test transport isolation, not a committed dependency or instrumentation modification. No test TCP server/port is shared.

After building the chosen revision and isolating the copied API, save its original game DLL/PDB, then run (substitute `after` for `baseline` only in result paths):

```powershell
dotnet coverage collect 'dotnet test fun-project.csproj --no-build --no-restore --settings TestResults/purge/local.runsettings --results-directory TestResults/purge/baseline' --settings .config/coverage.xml --include-files .godot/mono/temp/bin/Debug/fun-project.dll --output TestResults/purge/baseline.cobertura.xml --output-format cobertura --log-file TestResults/purge/baseline-collector.log
```

Static `--include-files` targets the actual assembly loaded by Godot. Do not build/edit sources while collecting. `dotnet-coverage` restored the original game DLL/PDB after collection; equality with saved originals was verified. The local comparison script `TestResults/purge/compare.py` pins the baseline revision and compares exact root counts, all class/source-line identities and hit states, and branch-condition values.

Instrumentation control: all eight `StatFoldTest` cases passed; production `IHasStats.Fold` had 22/22 lines covered, while unrelated `GeoscapeSession` locations had zero hits. The initial pre-isolation control timed out connecting and executed no tests; its report is explicitly discarded.

## Verification and retained evidence

- Baseline full suite: 1,260 passed, 0 failed/skipped, 1m31s test duration.
- Focused helper/owner suites: 114 passed, 0 failed/skipped (before the capability fold).
- Final full suite, including the capability fold: 1,258 passed, 0 failed/skipped, 1m28s test duration.
- `dotnet format fun-project.csproj --include test --verify-no-changes`: exit 0.
- `dotnet build` and `dotnet build -c ExportRelease`: exit 0, zero warnings/errors.
- Independent read-only simplification/correctness review of all 13 changed test files: no findings; verified fixture equivalence, ownership, proof timing, ordering, and both case folds.
- `git diff --check`: clean. No production/config diff remains.

The bare `dotnet format` command initially rejected ambiguous solution/project selection; the explicit project command succeeded. A temporary decompiler source dump was briefly included by the project's source glob and caused a build failure; it was renamed to `.cs.txt`, then formatting/build/tests were rerun successfully. The formatter's unrelated encoding-only production changes were restored. These failed attempts are not counted as verification passes.

Raw Cobertura reports, completed TRX, logs, private settings, source/coverage comparison script and JSON, original DLL/PDB copies, and `test-code.diff` remain in ignored `TestResults/purge/`. Original copied GdUnit API is preserved beside the private DLL. No recovery artifacts were deleted.

Managed coverage does not measure native Godot, GDScript, or assertion strength. Cobertura reports branch-condition percentages without stable identities for individual branch arms; exact totals and per-location values match, but cannot prove which arm a partially covered condition took. Behavioral assertion preservation was reviewed independently. Native rendering warnings appear in both runs; completed TRX records confirm no failed/skipped tests.
