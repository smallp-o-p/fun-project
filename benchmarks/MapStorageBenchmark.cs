#if MAP_STORAGE_BENCHMARK
using FunProject.Battle;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

// Compile only for an explicit benchmark run; no test framework or shipping entry point.
public partial class MapStorageBenchmark : Node
{
  private static long _sink;
  public override void _Ready()
  {
    try
    {
#if COMPACT_MAP
      if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--migrate-maps") >= 0)
      {
        MigrateMaps();
        GetTree().Quit();
        return;
      }
#endif
      var los = typeof(VisibilityService).GetMethod("IsLineOfSightClear", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<Func<BattleBoardState, BattleBoardState.ValidatedPoint, BattleBoardState.ValidatedPoint, bool>>(new VisibilityService());
      GD.Print($"Configuration={typeof(MapStorageBenchmark).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration}; runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; tiered={System.Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")}");
      foreach (var name in new[] { "debug", "dense", "sparse-tall" }) Run(name, los);
      GetTree().Quit();
    }
    catch (Exception exception) { GD.PrintErr(exception); GetTree().Quit(1); }
  }

  private static BattleMapData Make(string name)
  {
    if (name == "debug")
    {
      using var packed = ResourceLoader.Load<PackedScene>("res://resources/maps/debug_map.tscn", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
      var node = packed.Instantiate<BattleMap>();
      var data = node.MapData;
      node.Free();
      return data;
    }
    var dim = name == "dense" ? new Godot.Vector3I(64, 8, 64) : new Godot.Vector3I(64, 32, 64);
    var map = new BattleMapData { Dimensions = dim };
#if COMPACT_MAP
    map.InitializeTiles();
#endif
    for (int y = 0; y < dim.Y; y++)
      for (int z = 0; z < dim.Z; z++)
        for (int x = 0; x < dim.X; x++)
        {
          if (name == "sparse-tall" && x >= 4 && (x * 17 + z * 31 + y * 13) % 997 != 0) continue;
          if (y > 0 && (x * 17 + z * 31 + y * 13) % 11 != 0) continue;
          using var tile = new BattleMapTileData
          {
            Walkable = y == 0 && (z % 7 != 3 || x % 8 == 0),
            BlocksLineOfSight = y == 0 && z % 7 == 3 && x % 8 != 0,
            BlocksVerticalLineOfSight = y > 0,
            CoverDirections = CoverDirections.North | CoverDirections.East,
            CoverAmount = 40,
            SpawnFactionSlot = y == 0 && z == 0 && x < 2 ? x : -1
          };
#if COMPACT_MAP
          map.SetTile(new(x, y, z), tile);
#else
          // Dictionary owns the native reference. Do not dispose the managed wrapper here.
          map.Tiles[new(x, y, z)] = (BattleMapTileData)tile.Duplicate();
#endif
        }
    return map;
  }

  private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

  private static void Run(string name, Func<BattleBoardState, BattleBoardState.ValidatedPoint, BattleBoardState.ValidatedPoint, bool> los)
  {
    using var source = Make(name);
    string path = $"user://benchmark-{name}.tres";
    if (ResourceSaver.Save(source, path) != Error.Ok) throw new Exception("Save failed");
    string compressed = $"user://benchmark-{name}.res";
    if (ResourceSaver.Save(source, compressed, ResourceSaver.SaverFlags.Compress) != Error.Ok) throw new Exception("Save failed");
    var board = new BattleBoardState(source);
    long expected = Checksum(board);
    foreach (string saved in new[] { path, compressed })
      using (var reopened = ResourceLoader.Load<BattleMapData>(saved, cacheMode: ResourceLoader.CacheMode.IgnoreDeep))
        if (Checksum(new BattleBoardState(reopened)) != expected || Spawns(reopened) != Spawns(source)) throw new Exception("Roundtrip mismatch");
    var points = new List<BattleBoardState.ValidatedPoint>();
    foreach (var point in board.EnumerateBoardPoints()) points.Add(point);
    var walkable = new List<BattleBoardState.ValidatedPoint>();
    foreach (var point in points) if (point.Y == 0 && board.GetTile(point).IsWalkable) walkable.Add(point);
    int successfulPaths = 0;
    long Paths()
    {
      long hash = 17;
      successfulPaths = 0;
      for (int i = 0; i < 16; i++)
      {
        var from = walkable[(i * 13) % walkable.Count];
        var to = walkable[walkable.Count - 1 - (i * 71) % walkable.Count];
        var pathPoints = board.FindPath(from, to);
        if (pathPoints.Length > 0) successfulPaths++;
        foreach (var p in pathPoints) hash = unchecked(hash * 31 + p.X + p.Z * 67 + p.Y * 4099);
      }
      return hash;
    }
    int clearRays = 0, verticalRays = 0;
    long Sight()
    {
      long hash = 17;
      clearRays = 0; verticalRays = 0;
      for (int i = 0; i < 4096; i++)
      {
        int count = i % 2 == 0 ? board.Dimensions.X * board.Dimensions.Z : points.Count;
        var from = points[(i * 17) % count];
        var to = points[(i * 101 + 47) % count];
        bool clear = los(board, from, to);
        if (clear) clearRays++;
        if (from.Y != to.Y) verticalRays++;
        hash = unchecked(hash * 31 + (clear ? 1 : 0));
      }
      return hash;
    }
    Collect();
    long mapBefore = GC.GetTotalMemory(true);
    ulong nativeBefore = OS.GetStaticMemoryUsage();
    var retainedMaps = new BattleMapData[8];
    for (int i = 0; i < retainedMaps.Length; i++) retainedMaps[i] = ResourceLoader.Load<BattleMapData>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
    long mapRetained = (GC.GetTotalMemory(true) - mapBefore) / retainedMaps.Length;
    long nativeRetained = ((long)OS.GetStaticMemoryUsage() - (long)nativeBefore) / retainedMaps.Length;
    GC.KeepAlive(retainedMaps);
    foreach (var retainedMap in retainedMaps) retainedMap.Dispose();
    var load = Measure(() =>
    {
      using var map = ResourceLoader.Load<BattleMapData>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
      return Checksum(new BattleBoardState(map));
    });
    // Retain 8 boards to reduce fixed GC accounting noise; source map is already live.
    Collect();
    long before = GC.GetTotalMemory(true);
    var boards = new BattleBoardState[8];
    for (int i = 0; i < boards.Length; i++) boards[i] = new(source);
    long retained = (GC.GetTotalMemory(true) - before) / boards.Length;
    GC.KeepAlive(boards);
    var construct = Measure(() => { var b = new BattleBoardState(source); GC.KeepAlive(b); return b.Dimensions.X; });
    GD.Print(JsonSerializer.Serialize(new
    {
      name, dimensions = source.Dimensions.ToString(), cells = points.Count,
      textBytes = new System.IO.FileInfo(ProjectSettings.GlobalizePath(path)).Length,
      compressedBytes = new System.IO.FileInfo(ProjectSettings.GlobalizePath(compressed)).Length,
      checksum = expected, spawnChecksum = Spawns(source), pathChecksum = Paths(), successfulPaths, losChecksum = Sight(), clearRays, verticalRays,
      mapRetainedManagedBytes = mapRetained, mapRetainedGodotStaticBytes = nativeRetained,
      boardRetainedManagedBytes = retained, construct, loadBoardAndChecksum = load,
      paths16 = Measure(Paths), los4096 = Measure(Sight), layouts = CompareLayouts(board)
    }));
  }

  private struct Cell { public byte Flags, Directions, Amount; }

  // Representation-only comparison, not a substitute for the production BFS/LOS timings.
  private static object CompareLayouts(BattleBoardState board)
  {
    int count = board.Dimensions.X * board.Dimensions.Y * board.Dimensions.Z;
    var cells = new Cell[count];
    var flags = new byte[count]; var directions = new byte[count]; var amounts = new byte[count];
    int index = 0;
    foreach (var point in board.EnumerateBoardPoints())
    {
      var tile = board.GetTile(point);
      flags[index] = (byte)((tile.IsWalkable ? 1 : 0) | (tile.BlocksLineOfSight ? 2 : 0) | (tile.BlocksVerticalLineOfSight ? 4 : 0));
      directions[index] = (byte)tile.Cover.Directions; amounts[index] = (byte)tile.Cover.Amount;
      cells[index] = new() { Flags = flags[index], Directions = directions[index], Amount = amounts[index] };
      index++;
    }
    long Aos(bool mixed)
    {
      long sum = 0;
      for (int repeat = 0; repeat < 32; repeat++)
        for (int i = 0; i < count; i++)
        {
          int at = mixed ? i * 97 % count : i;
          sum += cells[at].Flags;
          if (mixed) sum += cells[at].Directions + cells[at].Amount;
        }
      return sum;
    }
    long Soa(bool mixed)
    {
      long sum = 0;
      for (int repeat = 0; repeat < 32; repeat++)
        for (int i = 0; i < count; i++)
        {
          int at = mixed ? i * 97 % count : i;
          sum += flags[at];
          if (mixed) sum += directions[at] + amounts[at];
        }
      return sum;
    }
    if (Aos(false) != Soa(false) || Aos(true) != Soa(true)) throw new Exception("Layout checksum mismatch");
    return new { aosCellBytes = System.Runtime.CompilerServices.Unsafe.SizeOf<Cell>(),
      aosFlags = Measure(() => Aos(false)), soaFlags = Measure(() => Soa(false)),
      aosMixed = Measure(() => Aos(true)), soaMixed = Measure(() => Soa(true)) };
  }

#if COMPACT_MAP
  private static void MigrateMaps()
  {
    foreach (string path in new[] { "res://resources/maps/debug_map.tscn", "res://resources/maps/defusal_map.tscn", "res://scenes/battle/authoring/BattleMapAuthoring.tscn" })
    {
      using var packed = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
      var source = packed.Instantiate<BattleMap>();
      long cells = Checksum(new BattleBoardState(source.MapData)), spawns = Spawns(source.MapData);
      string paint = Paint(source);
      using var converted = new PackedScene();
      if (converted.Pack(source) != Error.Ok || ResourceSaver.Save(converted, path) != Error.Ok) throw new Exception("Map migration save failed");
      source.Free();
      using var reopened = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
      var target = reopened.Instantiate<BattleMap>();
      if (Checksum(new BattleBoardState(target.MapData)) != cells || Spawns(target.MapData) != spawns || Paint(target) != paint)
        throw new Exception("Map migration changed content: " + path);
      target.Free();
      GD.Print($"MIGRATED {path} cells={cells} spawns={spawns} paint=identical");
    }
  }

  private static string Paint(BattleMap map)
  {
    var result = new System.Text.StringBuilder();
    foreach (var node in map.GetChildren())
      if (node is GridMap grid)
        foreach (var cell in grid.GetUsedCells()) result.Append($"{grid.Name}:{cell}:{grid.GetCellItem(cell)}:{grid.GetCellItemOrientation(cell)};");
    return result.ToString();
  }
#endif

  private static long Spawns(BattleMapData map)
  {
    long checksum = 0;
#if COMPACT_MAP
    foreach (var (cell, slot) in map.SpawnSlots)
#else
    foreach (var (cell, tile) in map.Tiles)
    {
      int slot = tile.SpawnFactionSlot;
      if (slot < 0) continue;
#endif
      checksum += (slot + 1L) * (1 + cell.X + 67L * cell.Z + 4099L * cell.Y);
#if !COMPACT_MAP
    }
#endif
    return checksum;
  }

  private static long Checksum(BattleBoardState board)
  {
    long hash = 17;
    foreach (var p in board.EnumerateBoardPoints())
    {
      var t = board.GetTile(p);
      hash = unchecked(hash * 31 + (t.IsWalkable ? 1 : 0) + (t.BlocksLineOfSight ? 2 : 0) +
        (t.BlocksVerticalLineOfSight ? 4 : 0) + (int)t.Cover.Directions * 8 + t.Cover.Amount * 128);
    }
    return hash;
  }

  private static object Measure(Func<long> action)
  {
    long expected = action();
    for (int i = 0; i < 3; i++) if (action() != expected) throw new Exception("Unstable warmup checksum");
    double[] ms = new double[9];
    long[] allocations = new long[9];
    for (int i = 0; i < ms.Length; i++)
    {
      Collect();
      long allocation = GC.GetAllocatedBytesForCurrentThread();
      long start = Stopwatch.GetTimestamp();
      long result = action();
      ms[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
      allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocation;
      if (result != expected) throw new Exception("Unstable measurement checksum");
      _sink ^= result;
    }
    System.Array.Sort(ms); System.Array.Sort(allocations);
    return new { medianMs = ms[4], minMs = ms[0], maxMs = ms[8], managedAllocatedBytes = allocations[4] };
  }
}
#endif
