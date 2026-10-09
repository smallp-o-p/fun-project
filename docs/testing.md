# Testing and coverage

## Environment

The project uses GdUnit4 inside a Godot mono runtime. `fun-project.csproj` includes root `test/` sources and test packages only in Debug; export configurations exclude them and the generated adapter runner. The retired `fun-project-test/` directory is not the test project. Shared fixtures and test-design rules are in the [test-audit skill](../.agents/skills/test-audit/SKILL.md).

`.runsettings` is selected by the project. It currently contains a Windows-specific `GODOT_BIN` path. On another machine, supply a local settings copy with the correct mono executable and preserve the other settings; do not commit a machine-path substitution as a product change. Pass the copy with `--settings <local.runsettings>`. A shell variable alone does not remove the checked-in settings value.

Restore LFS assets and complete Godot imports before scene/model tests. Blender sources require a compatible Blender installation configured in Godot. A missing import or failed test-host launch is not a passing test run; distinguish setup failures from actual assertion failures.

Do not overlap editor rebuilds, CLI builds, or tests: they share `.godot/mono/temp/bin`. A host crash during a competing build requires rerunning after the build finishes. Do not edit sources while tests run.

## Commands

See [AGENTS.md](../AGENTS.md#verification) for the normal verification sequence. A focused run is:

```sh
dotnet test fun-project.csproj --no-restore --filter "FullyQualifiedName~WeaponSystemTest"
```

The settings retain console/TRX results, disable the problematic HTML logger, and bound a wedged host with a five-minute session timeout. Diagnose the logs when a run does not complete; do not count it as success. Shared runtime, hook-ordering, and cross-subsystem edits need the full suite. Report the actual configuration, test counts, failures/skips, and any environment limitation.

## Coverage

Restore the pinned local tools with:

```sh
dotnet tool restore --configfile .config/coverage-nuget.config
```

Use the repository's `.config/coverage.xml` filters with `dotnet-coverage`; ReportGenerator is available for HTML. There are no custom coverage wrapper scripts. See the [collector CLI](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage) for `instrument`, `collect`, and `uninstrument` and the [tool manifest](../.config/dotnet-tools.json) for versions.

Collect from the actual Debug game assembly loaded by the Godot subprocess; use static instrumentation if dynamic collection does not reach it. Confirm collection on the current platform with a known exercised method and an unrelated control before trusting percentages. Preserve and restore the original DLL/PDB; never rebuild or run concurrent tests over instrumented output. Record the revision, invocation, completed TRX, filters, and report denominators.

Managed coverage excludes native Godot and GDScript. Equal line coverage does not prove assertion equivalence. Keep meaningful authored-resource, rendering/input, identity, failure, and ordering tests even when their managed lines overlap.
