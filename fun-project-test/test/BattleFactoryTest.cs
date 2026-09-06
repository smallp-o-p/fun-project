using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleFactoryTest
{
  // --- helpers -------------------------------------------------------------

  // Two factions in order [player, enemy], one unit each on distinct cells, an
  // objective for each. A 4x1x4 board has all-walkable tiles by default.
  private static (BattleSetup Setup, Faction Player, Faction Enemy) MinimalSetup()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(4, 1, 4));

    var placements = new List<UnitPlacement>
    {
      new(new UnitLoadout(TestData.MakeCombatant("Alpha", player)), new Vector3I(0, 0, 0)),
      new(new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3)),
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] = new ObjectiveData[] { new FakeObjectiveData() },
      [enemy] = new ObjectiveData[] { new FakeObjectiveData() },
    };
    var setup = new BattleSetup(board, new[] { player, enemy }, placements, objectives);
    return (setup, player, enemy);
  }

  // --- tests ---------------------------------------------------------------

  [TestCase(TestName = "Start places units, assigns objectives, and begins turn 1")]
  public void StartHappyPath()
  {
    var (setup, player, enemy) = MinimalSetup();

    using var runtime = BattleFactory.Start(setup).RequireRight();

    var playerUnit = runtime.Query(new GetFactionAliveUnits(player)).AsValueEnumerable().Single();
    var enemyUnit = runtime.Query(new GetFactionAliveUnits(enemy)).AsValueEnumerable().Single();

    Assert.Equal(new Vector3I(0, 0, 0), playerUnit.Position.Raw);
    Assert.Equal(new Vector3I(3, 0, 3), enemyUnit.Position.Raw);

    // InProgress + player (FactionOrder[0]) is the active side: it can act, enemy cannot yet.
    Assert.True(runtime.Query(new CanUnitActNow(playerUnit.State)));
    Assert.False(runtime.Query(new CanUnitActNow(enemyUnit.State)));
  }

  [TestCase(TestName = "Start fails when a faction has no objective")]
  public void StartMissingObjective()
  {
    var (setup, player, _) = MinimalSetup();
    var noEnemyObjective = setup with
    {
      Objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
      {
        [player] = new ObjectiveData[] { new FakeObjectiveData() },
      },
    };

    Assert.Equal(BattleSetupFailureReason.MissingObjective, BattleFactory.Start(noEnemyObjective).RequireLeft().Reason);
  }

  [TestCase(TestName = "Start fails when a placement's faction is not in FactionOrder")]
  public void StartUnknownPlacementFaction()
  {
    var (setup, player, enemy) = MinimalSetup();
    var stranger = TestData.MakeFaction("Stranger");
    var withStranger = setup with
    {
      Placements = new List<UnitPlacement>
      {
        new(new UnitLoadout(TestData.MakeCombatant("Alpha", player)), new Vector3I(0, 0, 0)),
        new(new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3)),
        new(new UnitLoadout(TestData.MakeCombatant("Ghost", stranger)), new Vector3I(2, 0, 2)),
      },
    };

    Assert.Equal(BattleSetupFailureReason.UnknownFaction, BattleFactory.Start(withStranger).RequireLeft().Reason);
  }

  [TestCase(TestName = "Start fails when PlayerFaction is not in FactionOrder")]
  public void StartUnknownPlayerFaction()
  {
    var (setup, _, _) = MinimalSetup();
    var stranger = TestData.MakeFaction("Stranger");
    var withStrangerPlayer = setup with { PlayerFaction = Some(stranger) };

    Assert.Equal(BattleSetupFailureReason.UnknownFaction, BattleFactory.Start(withStrangerPlayer).RequireLeft().Reason);
  }

  [TestCase(TestName = "Start fails when an objective references an unknown faction")]
  public void StartUnknownObjectiveFaction()
  {
    var (setup, player, enemy) = MinimalSetup();
    var stranger = TestData.MakeFaction("Stranger");
    var withStrangerObjective = setup with
    {
      Objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
      {
        [player] = new ObjectiveData[] { new FakeObjectiveData() },
        [enemy] = new ObjectiveData[] { new FakeObjectiveData() },
        [stranger] = new ObjectiveData[] { new FakeObjectiveData() },
      },
    };

    Assert.Equal(BattleSetupFailureReason.UnknownFaction, BattleFactory.Start(withStrangerObjective).RequireLeft().Reason);
  }

  [TestCase(TestName = "Start fails when a spawn cell is out of bounds")]
  public void StartSpawnCellOutOfBounds()
  {
    var (setup, player, enemy) = MinimalSetup();
    var offBoard = setup with
    {
      Placements = new List<UnitPlacement>
      {
        new(new UnitLoadout(TestData.MakeCombatant("Alpha", player)), new Vector3I(99, 0, 0)),
        new(new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3)),
      },
    };

    Assert.Equal(BattleSetupFailureReason.SpawnCellUnavailable, BattleFactory.Start(offBoard).RequireLeft().Reason);
  }

  [TestCase(TestName = "Start fails when two units share a spawn cell")]
  public void StartDuplicateSpawnCell()
  {
    var (setup, player, enemy) = MinimalSetup();
    var collision = setup with
    {
      Placements = new List<UnitPlacement>
      {
        new(new UnitLoadout(TestData.MakeCombatant("Alpha", player)), new Vector3I(1, 0, 1)),
        new(new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)), new Vector3I(1, 0, 1)),
      },
    };

    Assert.Equal(BattleSetupFailureReason.DuplicateSpawnCell, BattleFactory.Start(collision).RequireLeft().Reason);
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
    TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(3, 0, 3), TestData.SpawnTile(1)));

  [TestCase(TestName = "StartFromMap builds the board, assigns spawns, and starts")]
  public void StartFromMapHappyPath()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      [0] = new[] { new UnitLoadout(TestData.MakeCombatant("Alpha", player)) },
      [1] = new[] { new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] = new ObjectiveData[] { new FakeObjectiveData() },
      [enemy] = new ObjectiveData[] { new FakeObjectiveData() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    using var runtime = BattleFactory.StartFromMap(setup).RequireRight();

    var alpha = runtime.Query(new GetFactionAliveUnits(player)).AsValueEnumerable().Single();
    var bandit = runtime.Query(new GetFactionAliveUnits(enemy)).AsValueEnumerable().Single();
    Assert.Equal(new Vector3I(0, 0, 0), alpha.Position.Raw);
    Assert.Equal(new Vector3I(3, 0, 3), bandit.Position.Raw);
  }

  [TestCase(TestName = "StartFromMap fails when a slot has fewer cells than its roster")]
  public void StartFromMapShortfall()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      // Slot 0 has two units but the map only tags one slot-0 cell.
      [0] = new[]
      {
        new UnitLoadout(TestData.MakeCombatant("Alpha", player)),
        new UnitLoadout(TestData.MakeCombatant("Beta", player)),
      },
      [1] = new[] { new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] = new ObjectiveData[] { new FakeObjectiveData() },
      [enemy] = new ObjectiveData[] { new FakeObjectiveData() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall, BattleFactory.StartFromMap(setup).RequireLeft().Reason);
  }

  [TestCase(TestName = "StartFromMap fails when a slot index is out of range")]
  public void StartFromMapInvalidSlot()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      [0] = new[] { new UnitLoadout(TestData.MakeCombatant("Alpha", player)) },
      [5] = new[] { new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)) }, // out of range
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] = new ObjectiveData[] { new FakeObjectiveData() },
      [enemy] = new ObjectiveData[] { new FakeObjectiveData() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.InvalidSpawnSlot, BattleFactory.StartFromMap(setup).RequireLeft().Reason);
  }

  [TestCase(TestName = "StartFromMap fails when a slot's unit belongs to the wrong faction")]
  public void StartFromMapSlotFactionMismatch()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      // Slot 0 maps to FactionOrder[0] = player, but this unit belongs to enemy.
      [0] = new[] { new UnitLoadout(TestData.MakeCombatant("Wrong", enemy)) },
      [1] = new[] { new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] = new ObjectiveData[] { new FakeObjectiveData() },
      [enemy] = new ObjectiveData[] { new FakeObjectiveData() },
    };
    var setup = new MapBattleSetup(TwoSlotMap(), new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.SlotFactionMismatch, BattleFactory.StartFromMap(setup).RequireLeft().Reason);
  }

  [TestCase(TestName = "StartFromMap fails when a spawn cell is not walkable")]
  public void StartFromMapNonWalkableSpawnCell()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    // Slot-0 spawn cell is explicitly non-walkable: AssignSpawns still picks it, but the baked
    // board rejects occupancy, which must surface as a recoverable Left (not a thrown exception).
    var map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), new BattleMapTileData { SpawnFactionSlot = 0, Walkable = false }),
      (new Vector3I(3, 0, 3), TestData.SpawnTile(1)));
    var rosters = new Dictionary<int, IReadOnlyList<UnitLoadout>>
    {
      [0] = new[] { new UnitLoadout(TestData.MakeCombatant("Alpha", player)) },
      [1] = new[] { new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)) },
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
    {
      [player] = new ObjectiveData[] { new FakeObjectiveData() },
      [enemy] = new ObjectiveData[] { new FakeObjectiveData() },
    };
    var setup = new MapBattleSetup(map, new[] { player, enemy }, rosters, objectives);

    Assert.Equal(BattleSetupFailureReason.SpawnCellUnavailable, BattleFactory.StartFromMap(setup).RequireLeft().Reason);
  }


  // --- battle type entry ---------------------------------------------------

  private static BattleTypeData MakeBattleType()
  {
    var playerFaction = new FactionData { Name = "Player" };
    var enemyFaction = new FactionData { Name = "Aliens" };
    var trooper = new CombatantData
    {
      Name = "Trooper",
      HealthStat = new FunProject.Stats.HealthStat { BaseValue = 20 },
      ActionPointsStat = new FunProject.Stats.ActionPointsStat { BaseValue = 6 },
      WillStat = new FunProject.Stats.WillStat { BaseValue = 50 },
      MovementStat = new FunProject.Stats.MovementStat { BaseValue = 12 },
      VisionStat = new FunProject.Stats.VisionStat { BaseValue = 20 },
      AimStat = new FunProject.Stats.AimStat { BaseValue = 65 },
      RankTable = TestData.MakeRankTable(),
    };

    var type = new BattleTypeData { Name = "Bomb Defusal" };
    type.MapPool.Add(TestData.MakeMapData(
      new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(2, 0, 3), TestData.SpawnTile(1)),
      (new Vector3I(3, 0, 3), TestData.FloorTile())));
    type.MapPool.Add(TestData.MakeMapData(
      new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(0, 0, 1), TestData.SpawnTile(1))));

    var playerDeployment = new FactionDeploymentData { Faction = playerFaction };
    playerDeployment.Roster.Add(new RosterEntryData { Combatant = trooper, Quantity = 2 });
    playerDeployment.Objectives.Add(new DefuseAllBombsObjectiveData
    {
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    var enemyDeployment = new FactionDeploymentData { Faction = enemyFaction };
    enemyDeployment.Roster.Add(new RosterEntryData { Combatant = trooper, Quantity = 1 });
    enemyDeployment.Objectives.Add(new FakeObjectiveData());

    type.Factions.Add(playerDeployment);
    type.Factions.Add(enemyDeployment);
    type.Systems.Add(new ObjectExpirySystemData());
    return type;
  }

  [TestCase(TestName = "Type start spawns rosters, places objects, registers declared systems")]
  public void RequestHappyPath()
  {
    BattleTypeData type = MakeBattleType();
    var bombData = new BattleSpecialObjectData { Name = "Bomb" };
    bombData.Capabilities.Add(new InteractiveCapabilityData());
    bombData.Capabilities.Add(new TimedEffectCapabilityData { FireAfterTurns = 3 });
    var placement = new ObjectPlacementData { SpecialObject = bombData };
    placement.Positions.Add(new Godot.Vector3I(3, 0, 3));
    type.Objects.Add(placement);

    using var runtime = BattleFactory.Start(type, seed: 7).RequireRight();

    int units = 0;
    for (int x = 0; x < 4; x++)
      for (int z = 0; z < 4; z++)
        if (runtime.Query(new GetUnitAtTile(
              runtime.TryGetTile(new Vector3I(x, 0, z)).RequireSome())).IsSome)
          units++;
    Assert.Equal(3, units);

    var objects = runtime.Query(new GetBattleSpecialObjectsQuery());
    Assert.Equal(1, objects.Count);
    Assert.True(objects[0].Status.IsNone);

    for (int turn = 0; turn < 6 && runtime.Query(new GetBattlePhaseQuery()) == BattlePhase.InProgress; turn++)
      runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));
    Assert.Equal(Some(ObjectStatus.Expired), runtime.Query(new GetBattleSpecialObjectsQuery())[0].Status);
  }

  [TestCase(TestName = "Same seed produces the identical battle layout")]
  public void SeedDeterminism()
  {
    BattleTypeData type = MakeBattleType();
    using var firstRuntime = BattleFactory.Start(type, seed: 7).RequireRight();
    using var secondRuntime = BattleFactory.Start(type, seed: 7).RequireRight();
    Assert.Equal(LayoutFingerprint(firstRuntime), LayoutFingerprint(secondRuntime));
  }

  private static string LayoutFingerprint(BattleRuntime runtime)
  {
    var cells = new List<string>();
    for (int x = 0; x < 4; x++)
      for (int z = 0; z < 4; z++)
        if (runtime.Query(new GetUnitAtTile(
              runtime.TryGetTile(new Vector3I(x, 0, z)).RequireSome())).IsSome)
          cells.Add($"{x},{z}");
    return string.Join("|", cells);
  }

  private static BattleTypeData MakeDuelBattleType()
  {
    var playerFaction = new FactionData { Name = "Player" };
    var enemyFaction = new FactionData { Name = "Enemy" };
    var shooter = new CombatantData
    {
      Name = "Shooter",
      HealthStat = new FunProject.Stats.HealthStat { BaseValue = 20 },
      ActionPointsStat = new FunProject.Stats.ActionPointsStat { BaseValue = 6 },
      WillStat = new FunProject.Stats.WillStat { BaseValue = 50 },
      MovementStat = new FunProject.Stats.MovementStat { BaseValue = 12 },
      VisionStat = new FunProject.Stats.VisionStat { BaseValue = 20 },
      AimStat = new FunProject.Stats.AimStat { BaseValue = 65 },
      RankTable = TestData.MakeRankTable(),
    };

    var type = new BattleTypeData { Name = "Duel" };
    type.MapPool.Add(TestData.MakeMapData(
      new Vector3I(2, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(1))));

    var playerDeployment = new FactionDeploymentData { Faction = playerFaction };
    playerDeployment.Roster.Add(new RosterEntryData
    {
      Combatant = shooter,
      Weapon = TestData.MakeWeaponData(damage: 5, range: 10),
    });
    playerDeployment.Objectives.Add(new FakeObjectiveData());

    var enemyDeployment = new FactionDeploymentData { Faction = enemyFaction };
    enemyDeployment.Roster.Add(new RosterEntryData
    {
      Combatant = shooter,
      Weapon = TestData.MakeWeaponData(damage: 5, range: 10),
    });
    enemyDeployment.Objectives.Add(new FakeObjectiveData());

    type.Factions.Add(playerDeployment);
    type.Factions.Add(enemyDeployment);
    return type;
  }

  private static bool FirstAttackHits(BattleRuntime runtime)
  {
    BattleBoardState.ValidatedPoint attackerTile = runtime.TryGetTile(new Vector3I(0, 0, 0)).RequireSome();
    BattleBoardState.ValidatedPoint targetTile = runtime.TryGetTile(new Vector3I(1, 0, 0)).RequireSome();
    BattleUnitState attacker = runtime.Query(new GetUnitAtTile(attackerTile)).RequireSome();
    BattleUnitState target = runtime.Query(new GetUnitAtTile(targetTile)).RequireSome();

    BattleActionExecResult result = runtime.ExecuteAction(BattleAction.AttackUnit(
      runtime.TryGetAlive(attacker).RequireSome(),
      runtime.TryGetAlive(target).RequireSome()));

    foreach (BattleEvent battleEvent in result.EventsThatOccurred)
    {
      if (battleEvent is UnitAttackedBattleEvent attacked)
        return attacked.IsHit;
    }

    throw new Exception("Expected attack event from duel attack.");
  }

  [TestCase(TestName = "Player roster override is adopted into the started player faction")]
  public void PlayerRosterOverride()
  {
    BattleTypeData type = MakeBattleType();
    var foreignFaction = new Faction(new FactionData { Name = "Override" });
    var loadouts = new List<UnitLoadout>
    {
      new(TestData.MakeCombatant("Vet", foreignFaction)),
    };

    using BattleRuntime runtime = BattleFactory.Start(
      type, seed: 42, playerRosterOverride: Some((IReadOnlyList<UnitLoadout>)loadouts)).RequireRight();
    Faction playerFaction = runtime.Query(new GetPlayerFactionQuery()).RequireSome();

    int playerUnits = 0;
    for (int x = 0; x < 4; x++)
    {
      for (int z = 0; z < 4; z++)
      {
        Option<BattleUnitState> unit = runtime.Query(new GetUnitAtTile(
          runtime.TryGetTile(new Vector3I(x, 0, z)).RequireSome()));
        if (unit.Match(Some: placed => placed.Side == playerFaction, None: () => false))
          playerUnits++;
      }
    }

    Assert.Equal(1, playerUnits);
  }

  [TestCase(TestName = "Seed flows into the battle session RNG")]
  public void RequestSeedDrivesBattleRng()
  {
    BattleTypeData type = MakeDuelBattleType();

    using BattleRuntime first = BattleFactory.Start(type, seed: 7).RequireRight();
    using BattleRuntime second = BattleFactory.Start(type, seed: 7).RequireRight();

    Assert.Equal(FirstAttackHits(first), FirstAttackHits(second));
  }

  [TestCase(TestName = "Empty map pool is a typed failure")]
  public void EmptyMapPoolFails()
  {
    var type = new BattleTypeData { Name = "Broken" };
    Assert.Equal(BattleSetupFailureReason.EmptyMapPool,
      BattleFactory.Start(type).RequireLeft().Reason);
  }

  [TestCase(TestName = "Out-of-range player faction index is a typed failure")]
  public void BadPlayerFactionIndex()
  {
    BattleTypeData type = MakeBattleType();
    type.PlayerFactionIndex = 5;
    Assert.Equal(BattleSetupFailureReason.UnknownFaction,
      BattleFactory.Start(type, seed: 42).RequireLeft().Reason);
  }
}
