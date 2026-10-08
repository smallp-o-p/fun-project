# Coverage-assisted test audit, 2026-10-08

Base: master `131046880c0653d79baa7280676883adb60af872` (PRs 15, 16 and 17). Local branch: `audit/coverage-test-purge`. PR19 concurrency remains on hold. Production files and behavior are unchanged.

## Result

| Measure | Baseline | Final | Change |
| --- | ---: | ---: | ---: |
| Instrumented discovered/passing cases | 1,238 | 1,169 | -69 (5.6%) |
| Test C# files, including helpers | 163 | 162 | -1 |
| Test C# lines, including blanks/comments | 28,544 | 27,511 | -1,033 (3.6%) |
| Collector covered/valid lines | 8,222/8,774 | 8,217/8,774 | -5 |
| Collector covered/valid branch outcomes | 3,171/3,728 | 3,169/3,728 | -2 |
| Distinct source covered/valid lines | 8,121/8,665 | 8,116/8,665 | -5 |
| ReportGenerator line coverage | 93.6% | 93.5% | -5 lines |
| ReportGenerator branch coverage | 85.1% | 85.0% | -2 outcomes |
| Instrumented test duration (rounded console) | 1m12s | 1m7s | -5s |
| Collector wall time, excluding HTML | 107.959s | 102.637s | -5.322s |

Timings are single runs with host variability; no general speedup is established. ReportGenerator aggregates synthetic/class entries differently from collector totals. Compare each denominator consistently; the comparator unions file/line identities. No coverage threshold decided deletions.

59 methods (61 cases) removed plus eight overlapping parameter rows. [Deletion evidence](test-audit-deletions.md) records baseline locations, callers/history, survivors, obsolete claims and validation. Initial empty/hidden state, regeneration delay/cap events, blast identity, same-side visibility, faction continuity and queued-objective assertions were transferred into stronger surviving scenarios. Shared test-support and production LOC are unchanged; the removed copied HUD fixture lived in its test class.

## Approved tools and validated pilot

Repo-local pins: Microsoft `dotnet-coverage` **18.12.0**, `dotnet-reportgenerator-globaltool` **5.5.11**. Installed using the checked-in NuGet configuration with only official NuGet.org enabled. No other coverage tool/addon installed.

Dynamic-only collection returned **Profiler was not initialized** and no coverage; rejected. Static instrumentation of the actual Debug `fun-project.dll` with subprocess collection reached the managed Godot host. Gameplay/tests/generated runner share this assembly: include the assembly, exclude test/generated **source paths**.

Pilot `CurrencyTest.MultiCurrencySpendIsAtomic`: **1/1 passed**, `GameState.TrySpendCurrencyCosts` **7/7 source lines, 6/6 condition outcomes**. Differential `StatFoldTest` control: **12/12 passed**, payment method **zero hits**. An earlier method-name control selected zero tests because custom TestName changes filtering; rejected. Source HTML includes production source and excludes test/runner paths. DLL/PDB restored; no new Godot child remained. This proves real managed condition outcomes for the sampled method, not semantic coverage of every construct.

References: [Microsoft collector CLI](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage), [filter configuration](https://github.com/microsoft/codecoverage/blob/main/docs/configuration.md), [ReportGenerator](https://github.com/danielpalme/ReportGenerator).

## Reproduce

Use the Godot mono path in `.runsettings`; no concurrent editor/build/tests:

```powershell
dotnet tool restore --configfile .config/coverage-nuget.config
dotnet build fun-project.csproj --no-restore
.\tools\Measure-TestCoverage.ps1 -Name full
.\tools\Measure-TestCoverage.ps1 -Name payment -Filter 'FullyQualifiedName~CurrencyTest.MultiCurrencySpendIsAtomic'
.\tools\Measure-TestCoverage.ps1 -Name control -Filter 'FullyQualifiedName~StatFoldTest'
python tools/Compare-Coverage.py <baseline-cobertura.xml> TestResults/coverage/full/coverage.cobertura.xml --output comparison.json
```

Run names must be new. The script validates nonzero tests/production hits, exclusions, DLL/PDB restoration, exit/counts and residual Godot processes before HTML generation. Artifacts retain invocation, revision/worktree state, TRX, Cobertura, logs/source HTML. Recover baseline in a separate worktree at the base above, build it and apply identical pinned tools/settings. Never instrument concurrent runs.

## Intentional coverage differences

Only five distinct source lines lost hits; none gained:

| Source | Reason |
| --- | --- |
| `scripts/stats/MultiStatMod.cs:14-16` | Add/Remove/Clear forwarding wrappers have no non-test callers. Exported contribution paths survive; wrappers retained for separate production cleanup. |
| `scripts/weapons/Weapon.cs:98` | Unused GetCritChanceStat alias was called only by contrived melee-from-firearm data. Typed generic stats and real construction survive. |
| `scripts/battle/TurnScheduler.cs:99` | Directly fabricated disabled spawn candidates are not supplied by production callers. Original inactive-state assertion could pass if wrongly queued. Valid spawn, turn, death and unconscious boundaries survive. |

Only changed branch: `TurnScheduler.AddSpawnedUnit`, line98, **4/4 to 2/4 outcomes**, from fabricated disabled candidates. Same-side visibility coverage was restored by the transferred assertion. Identical line coverage never established assertion equivalence.

## Validation and retained failures

Baseline **1,238/1,238 passed**. Before edits, omitted-candidate suite: **1,176 passed / 1 failed**, retained battle handoff could not find End Turn after second-battle selection (original test line244, helper41). Unchanged focused class **10/10 passed**; identical omission retry **1,177/1,177 passed**. The test/helper remained unchanged. Mouse/layout readiness is plausible but unproven; no product bug or setup dependency established.

Final instrumented **1,169/1,169**, no failed/skipped, DLL/PDB restored, no Godot child. First final plain full run **1,168 passed / 1 failed**: retained `BattleOwnsOpaqueViewport`, minimum800x600, missing selection at line139 (wall118.927s). Unchanged focused class then **10/10 passed**, and unchanged full retry **1,169/1,169 passed**, duration1m3s, wall92.686s. Logs/TRX retained. No assertions weakened to pass; timing/focus explanation remains unproven.

`dotnet format fun-project.csproj --no-restore`, build and `git diff --check` succeeded, 11 pre-existing nullable warnings. Production formatter-only changes restored. LSP diagnostics unavailable; compiler diagnostics checked. Three independent subsystem reviewers and one fresh reviewer inspected removed bodies, surviving assertions and production callers; no deletion-safety or material tooling findings. The fresh reviewer corrected one survivor class reference in the evidence. Coverage script syntax and exclusions were checked directly. The comparator reports branch counts, not individual branch identities.

Coverage is aggregate, not per-test attribution. It covers this Debug gameplay assembly's managed execution. Native Godot internals and GDScript are outside it. Native UI/input/material/animation, authored resources, payment atomicity/identity/overflow, timing boundaries, visibility and recent regressions remain based on distinct behavior.

## Recovery and reports

Large outputs stay ignored under `TestResults/coverage/`. Source reports: `TestResults/coverage/baseline/html/index.html`, `TestResults/coverage/final/html/index.html`. Rejected pilots/omission attempts remain beside them. Plain/focused TRX files are under `TestResults/final-plain`, `final-handoff`, `final-plain-retry`; console logs are one directory above the worktree. `../audit-artifacts/` retains baseline tests archive, full deletion inventory, omission filter, source metrics, comparison, independent review and recovery patch. Baseline/first-omission JSON test counts were reconstructed from original TRX after a serialization issue; original coverage/timing/exit data remained unchanged. The script now serializes counts explicitly. Local/unpublished; no merge or PR19 work.
