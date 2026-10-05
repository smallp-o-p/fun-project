# Map storage benchmark

PR-ready summary: baked maps now store three byte arrays and sparse spawn slots; each board owns three copied arrays. `GetTile` is a live value view, preserving retained mutable access without allocating a tile object. BFS and 3D LOS algorithms are unchanged.

Baseline: `d069e431904c74bdf8c9ed8d3ee4c1105f8c9ca3`. Measured final implementation: `refactor/compact-battle-map`, identified by [final-tree.sha256](final-tree.sha256). Raw results: [baseline.jsonl](baseline.jsonl), [final.jsonl](final.jsonl). Measurements were captured before publication; the PR records the final commit and Git tree identities.

The exact runner was also executed end to end: [repeat results](final-repeated.jsonl). Memory, allocations, sizes and semantic checksums repeat exactly. Timing varies: ground-map BFS is 18.4613 ms on the repeat versus 18.6683 ms baseline, so a reliable general BFS improvement is **not established**. Repeat load medians are 2.4105/30.4150/94.0364 ms and LOS medians 0.3121/1.8774/3.3236 ms (debug/ground/sparse). The tables below retain the first final run rather than selecting fastest samples across runs.

## Results

Medians in milliseconds; four warmups, nine measured samples per operation, collection outside timing, tiered compilation disabled. Windows x64, Godot 4.7.2 mono, .NET 10.0.12, **Release** for both. The Debug-named output directory is Godot's lookup location; the harness independently prints `Configuration=Release`.

| Map (X × Y × Z; authored cells) | Board construction | Load + board + checksum | 16 BFS paths | 4,096 LOS rays |
|---|---:|---:|---:|---:|
| Repository debug (8 × 1 × 8; 64) | 0.1173 → 0.0012 | 6.8311 → 2.3603 | 0.1857 → 0.1936 | 0.2523 → 0.3150 |
| Ground + volumes (64 × 8 × 64; 6,704) | 794.6286 → 0.0073 | 888.2235 → 23.1106 | 18.6683 → 14.1480 | 2.6995 → 1.7881 |
| Sparse/tall (64 × 32 × 64; 982) | 21.9738 → 0.0250 | 118.7846 → 88.4712 | 0.8349 → 0.5596 | 6.2831 → 3.5110 |

The harness calls the second scenario `dense`: full ground plus ~1/11 populated upper cells, **not** fully populated volume. All 16 paths succeed. LOS clear/vertical counts: 4,096/0, 1,570/1,790, and 3,907/1,972. Cell, spawn, path and LOS checksums agree before/after. Sample ranges are in the JSON. These are local observations, not throughput guarantees: tiny-map BFS/LOS medians regress. Large construction gains remove per-cell allocations and Godot dictionary/Resource traversal.

Bytes, before → after; retained deltas averaged over eight live instances:

| Map | Board managed retention | Map managed retention | Map Godot static allocations | Board construction allocations | Load + board + checksum allocations |
|---|---:|---:|---:|---:|---:|
| Debug | 3,011 → 680 | 9,968 → 636 | 72,773 → 960 | 16,512 → 672 | 39,096 → 2,832 |
| Ground + volumes | 1,309,016 → 98,723 | 885,745 → 98,688 | 7,114,476 → 1,364 | 2,705,792 → 98,784 | 4,800,056 → 199,056 |
| Sparse/tall | 5,242,758 → 393,704 | 118,099 → 393,663 | 954,236 → 1,388 | 5,447,776 → 393,696 | 5,756,776 → 788,880 |

Runtime retention falls ~92.5% on both large maps: three bytes/cell plus board/array/occupancy-dictionary overhead. Sparse authored **managed** memory grows because dense arrays include empty volume; native allocations and board retention fall. `GC.GetTotalMemory` deltas have accounting noise; allocation columns use `GC.GetAllocatedBytesForCurrentThread` and exclude other threads. `OS.GetStaticMemoryUsage` covers Godot-tracked native allocations, **not total native memory, RSS or GPU memory**. These counters are separate.

| Saved MapData only | Uncompressed `.tres` bytes | Compressed binary `.res` bytes |
|---|---:|---:|
| Debug | 17,563 → 973 | 1,317 → 326 |
| Ground + volumes | 1,815,075 → 131,513 | 85,444 → 2,879 |
| Sparse/tall | 265,890 → 524,730 | 13,323 → 8,388 |

Sparse/tall uncompressed size grows ~97%. Disk sizes exclude visual GridMaps/palettes and are not memory measurements. Both formats were reopened and checked. Timed loads use `IgnoreDeep`, warm filesystem caches, and include board creation and full-cell checksum traversal. One-time legacy conversion is excluded from final compact-file load timing.

## AoS versus SoA

Both payloads use three bytes/cell. A separate experiment copies identical cells into flat three-byte structs (AoS) and three byte arrays (SoA), then performs 32 flags sweeps or mixed-field accesses in fixed permuted order. This representation-only experiment does not replace production BFS/LOS measurements.

| Map | Flags sweep AoS / SoA ms | Mixed access AoS / SoA ms |
|---|---:|---:|
| Debug | 0.0030 / 0.0029 | 0.0065 / 0.0066 |
| Ground + volumes | 1.1424 / 1.2942 | 3.4912 / 3.4091 |
| Sparse/tall | 4.9989 / 6.7952 | 14.7603 / 13.5074 |

No consistent SoA speed advantage was demonstrated. SoA keeps exported bytes directly copyable into runtime storage without custom struct serialization. Results support compact storage, not a universal SoA speed claim.

## Reproduce

Run `./benchmarks/RunMapStorage.ps1`. For baseline, use a separate checkout at the revision above, copy only `MapStorageBenchmark.cs`, `run-map-storage.gd`, and `RunMapStorage.ps1` into its `benchmarks/`, and run with `-Baseline`. Do not run alongside an editor building that checkout. The script executes:

```powershell
$env:DOTNET_TieredCompilation = '0'
# Baseline: omit %3BCOMPACT_MAP from DefineConstants.
dotnet build fun-project.csproj -c Release --no-incremental '-p:DefineConstants=MAP_STORAGE_BENCHMARK%3BCOMPACT_MAP' -p:OutputPath=.godot/mono/temp/bin/Debug
& 'C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --script benchmarks/run-map-storage.gd --log-file benchmarks/final-godot.log
```

The script fails on incomplete runs or changed semantic checksums. Rebuild Debug with `dotnet build fun-project.csproj --no-incremental` before tests; Release restore excludes test packages. Benchmark save paths under `user://benchmark-*` are shared by this project name: run baseline/final sequentially. Caches, assemblies and logs are worktree-local.

## Verification and limits

`dotnet format fun-project.csproj`, build, and **1,166/1,166 tests passed** (baseline 1,163). The new allocation-budget test fails on baseline; retained-view mutation passes both. Independent review found no blocking findings. Native `PackedScene.Pack`/`ResourceSaver` conversion and reopen preserved all three repository maps' cell/spawn checksums and painted GridMap positions, IDs and orientations. Existing tests also cover native authoring edit/save/reload/export.

The legacy `Tiles` loader preserves external assets. Resaving removes old cell Resources; a loaded legacy PackedScene can retain its serialized resource graph until released. Baking still creates temporary cell Resources, disposed after successful packing. Occupancy dictionaries and BFS collections remain; BFS allocations are unchanged and LOS allocates zero managed bytes. Runtime cover follows the authored 0–100 domain; unrepresentable byte values throw rather than wrap.

The worktree lacks the ignored `godot_ai` addon; both baseline/final headless logs report its missing autoload. Neither benchmark uses it. No editor UI was occupied. GdUnit had intermittent named-pipe startup failures; the reported full suite is the successful fresh-host retry, not a zero-test launch.
