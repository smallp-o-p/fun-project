using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class BattleFactoryTest
{
  // --- helpers -------------------------------------------------------------

  private static BattleRuntime UnwrapStart(Either<BattleSetupFailure, BattleRuntime> result) =>
    result.Match(
      Right: runtime => runtime,
      Left: failure => throw new Exception($"Expected Start to succeed but got {failure.Reason}: {failure.Message}"));

  private static BattleSetupFailure ExpectFailure(Either<BattleSetupFailure, BattleRuntime> result) =>
    result.Match(
      Right: _ => throw new Exception("Expected a setup failure but Start succeeded."),
      Left: failure => failure);

  // Two factions in order [player, enemy], one unit each on distinct cells, an
  // objective for each. A 4x1x4 board has all-walkable tiles by default.
  private static (BattleSetup Setup, Faction Player, Faction Enemy) MinimalSetup()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(4, 1, 4));

    var placements = new List<UnitPlacement>
    {
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)), new Vector3I(0, 0, 0)),
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3)),
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var setup = new BattleSetup(board, new[] { player, enemy }, placements, objectives);
    return (setup, player, enemy);
  }

  // --- tests ---------------------------------------------------------------

  [TestCase(TestName = "Start places units, assigns objectives, and begins turn 1")]
  public void StartHappyPath()
  {
    var (setup, player, enemy) = MinimalSetup();

    BattleRuntime runtime = UnwrapStart(BattleFactory.Start(setup));

    var playerUnit = GetValue(runtime.Query(new GetFactionAliveUnits(player))).Single();
    var enemyUnit = GetValue(runtime.Query(new GetFactionAliveUnits(enemy))).Single();

    Assert.Equal(new Vector3I(0, 0, 0), GetValue(runtime.Query(new GetUnitPosition(playerUnit))).Raw);
    Assert.Equal(new Vector3I(3, 0, 3), GetValue(runtime.Query(new GetUnitPosition(enemyUnit))).Raw);

    // InProgress + player (FactionOrder[0]) is the active side: it can act, enemy cannot yet.
    Assert.True(GetValue(runtime.Query(new CanUnitActNow(playerUnit))));
    Assert.False(GetValue(runtime.Query(new CanUnitActNow(enemyUnit))));
  }

  [TestCase(TestName = "Start fails when a faction has no objective")]
  public void StartMissingObjective()
  {
    var (setup, player, _) = MinimalSetup();
    var noEnemyObjective = setup with
    {
      Objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
      {
        [player] = new Objective[] { new FakeObjective() },
      },
    };

    Assert.Equal(BattleSetupFailureReason.MissingObjective, ExpectFailure(BattleFactory.Start(noEnemyObjective)).Reason);
  }

  [TestCase(TestName = "Start fails when a placement's faction is not in FactionOrder")]
  public void StartUnknownPlacementFaction()
  {
    var (setup, player, enemy) = MinimalSetup();
    var stranger = BattleTestFactory.MakeFaction("Stranger");
    var withStranger = setup with
    {
      Placements = new List<UnitPlacement>
      {
        new(new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)), new Vector3I(0, 0, 0)),
        new(new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3)),
        new(new UnitLoadout(BattleTestFactory.MakeCombatant("Ghost", stranger)), new Vector3I(2, 0, 2)),
      },
    };

    Assert.Equal(BattleSetupFailureReason.UnknownFaction, ExpectFailure(BattleFactory.Start(withStranger)).Reason);
  }

  [TestCase(TestName = "Start fails when PlayerFaction is not in FactionOrder")]
  public void StartUnknownPlayerFaction()
  {
    var (setup, _, _) = MinimalSetup();
    var stranger = BattleTestFactory.MakeFaction("Stranger");
    var withStrangerPlayer = setup with { PlayerFaction = Some(stranger) };

    Assert.Equal(BattleSetupFailureReason.UnknownFaction, ExpectFailure(BattleFactory.Start(withStrangerPlayer)).Reason);
  }

  [TestCase(TestName = "Start fails when an objective references an unknown faction")]
  public void StartUnknownObjectiveFaction()
  {
    var (setup, player, enemy) = MinimalSetup();
    var stranger = BattleTestFactory.MakeFaction("Stranger");
    var withStrangerObjective = setup with
    {
      Objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
      {
        [player] = new Objective[] { new FakeObjective() },
        [enemy] = new Objective[] { new FakeObjective() },
        [stranger] = new Objective[] { new FakeObjective() },
      },
    };

    Assert.Equal(BattleSetupFailureReason.UnknownFaction, ExpectFailure(BattleFactory.Start(withStrangerObjective)).Reason);
  }

  [TestCase(TestName = "Start fails when a spawn cell is out of bounds")]
  public void StartSpawnCellOutOfBounds()
  {
    var (setup, player, enemy) = MinimalSetup();
    var offBoard = setup with
    {
      Placements = new List<UnitPlacement>
      {
        new(new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)), new Vector3I(99, 0, 0)),
        new(new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3)),
      },
    };

    Assert.Equal(BattleSetupFailureReason.SpawnCellUnavailable, ExpectFailure(BattleFactory.Start(offBoard)).Reason);
  }

  [TestCase(TestName = "Start fails when two units share a spawn cell")]
  public void StartDuplicateSpawnCell()
  {
    var (setup, player, enemy) = MinimalSetup();
    var collision = setup with
    {
      Placements = new List<UnitPlacement>
      {
        new(new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)), new Vector3I(1, 0, 1)),
        new(new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)), new Vector3I(1, 0, 1)),
      },
    };

    Assert.Equal(BattleSetupFailureReason.DuplicateSpawnCell, ExpectFailure(BattleFactory.Start(collision)).Reason);
  }

  [TestCase(TestName = "Start throws on null placements")]
  public void StartThrowsOnNullPlacements()
  {
    var (setup, _, _) = MinimalSetup();
    var broken = setup with { Placements = null! };

    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on null objectives")]
  public void StartThrowsOnNullObjectives()
  {
    var (setup, _, _) = MinimalSetup();
    var broken = setup with { Objectives = null! };

    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on empty faction order")]
  public void StartThrowsOnEmptyFactionOrder()
  {
    var (setup, _, _) = MinimalSetup();
    var broken = setup with { FactionOrder = System.Array.Empty<Faction>() };

    Assert.Throws<ArgumentException>(() => BattleFactory.Start(broken));
  }

  // 4x1x4 map with exactly one spawn cell per slot. CreateBoardState() seeds every board cell
  // non-walkable, then makes walkable only the cells present in Tiles (BattleMapTileData.Walkable
  // defaults to true) — so exactly the two cells below are walkable spawn cells, one per slot.
  private static BattleMapData TwoSlotMap() =>
    MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), SpawnTile(0)),
      (new Vector3I(3, 0, 3), SpawnTile(1)));

  [TestCase(TestName = "StartFromMap builds the board, assigns spawns, and starts")]
  public void StartFromMapHappyPath()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      [0] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)) },
      [1] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    BattleRuntime runtime = UnwrapStart(BattleFactory.StartFromMap(setup));

    var alpha = GetValue(runtime.Query(new GetFactionAliveUnits(player))).Single();
    var bandit = GetValue(runtime.Query(new GetFactionAliveUnits(enemy))).Single();
    Assert.Equal(new Vector3I(0, 0, 0), GetValue(runtime.Query(new GetUnitPosition(alpha))).Raw);
    Assert.Equal(new Vector3I(3, 0, 3), GetValue(runtime.Query(new GetUnitPosition(bandit))).Raw);
  }

  [TestCase(TestName = "StartFromMap fails when a slot has fewer cells than its roster")]
  public void StartFromMapShortfall()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      // Slot 0 has two units but the map only tags one slot-0 cell.
      [0] = new[]
      {
        new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)),
        new UnitLoadout(BattleTestFactory.MakeCombatant("Beta", player)),
      },
      [1] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall, ExpectFailure(BattleFactory.StartFromMap(setup)).Reason);
  }

  [TestCase(TestName = "StartFromMap fails when a slot index is out of range")]
  public void StartFromMapInvalidSlot()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      [0] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)) },
      [5] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)) }, // out of range
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.InvalidSpawnSlot, ExpectFailure(BattleFactory.StartFromMap(setup)).Reason);
  }

  [TestCase(TestName = "StartFromMap fails when a slot's unit belongs to the wrong faction")]
  public void StartFromMapSlotFactionMismatch()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      // Slot 0 maps to FactionOrder[0] = player, but this unit belongs to enemy.
      [0] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Wrong", enemy)) },
      [1] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.SlotFactionMismatch, ExpectFailure(BattleFactory.StartFromMap(setup)).Reason);
  }

  [TestCase(TestName = "StartFromMap fails when a spawn cell is not walkable")]
  public void StartFromMapNonWalkableSpawnCell()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    // Slot-0 spawn cell is explicitly non-walkable: AssignSpawns still picks it, but the baked
    // board rejects occupancy, which must surface as a recoverable Left (not a thrown exception).
    var map = MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), new BattleMapTileData { SpawnFactionSlot = 0, Walkable = false }),
      (new Vector3I(3, 0, 3), SpawnTile(1)));
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      [0] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Alpha", player)) },
      [1] = new[] { new UnitLoadout(BattleTestFactory.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var setup = new MapBattleSetup(map, new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.SpawnCellUnavailable, ExpectFailure(BattleFactory.StartFromMap(setup)).Reason);
  }
}
