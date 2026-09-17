using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleFactoryTest
{
  // --- helpers -------------------------------------------------------------

  // Two sides in order [player, enemy], one unit each on distinct cells of an all-walkable
  // 4x1x4 board, one objective per side, fixed seed, player designation.
  private static (BattleSetup Setup, Faction Player, Faction Enemy, Combatant Alpha, Combatant Bandit) MinimalSetup()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var alpha = TestData.MakeCombatant("Alpha", player);
    var bandit = TestData.MakeCombatant("Bandit", enemy);
    var setup = new BattleSetup(TestData.MakeOpenBattleMap(),
    [
      new BattleSideSetup(player, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(alpha), new Vector3I(0, 0, 0))]),
      new BattleSideSetup(enemy, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(bandit), new Vector3I(3, 0, 3))]),
    ], Seed: 7, PlayerFaction: Some(player));
    return (setup, player, enemy, alpha, bandit);
  }

  [TestCase]
  public void BoardSetupIncludesDefaultStunRecovery()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    using var runtime = BattleFactory.Start(setup).RequireRight();
    var faction = runtime.Query(new GetActiveSideQuery());
    var unit = runtime.Query(new GetFactionAliveUnits(faction)).AsValueEnumerable().First();
    runtime.ExecuteAction(BattleAction.ApplyDamage(runtime.TryGetAlive(unit.State).RequireSome(), 8, DamageKind.Stun));
    List<BattleEvent> committed = [];
    runtime.BattleEventCommitted += committed.Add;
    runtime.ExecuteAction(BattleAction.EndFactionTurn(faction));
    Assert.Equal(3, unit.State.CurrentStun);
    Assert.Equal(5u, committed.SingleEvent<UnitStunRecoveredBattleEvent>().AmountRecovered);
  }

  // --- tests ---------------------------------------------------------------

  [TestCase(TestName = "Start places units, assigns objectives, and begins turn 1")]
  public void StartHappyPath()
  {
    var (setup, player, enemy, _, _) = MinimalSetup();

    using var runtime = BattleFactory.Start(setup).RequireRight();

    var playerUnit = runtime.Query(new GetFactionAliveUnits(player)).AsValueEnumerable().Single();
    var enemyUnit = runtime.Query(new GetFactionAliveUnits(enemy)).AsValueEnumerable().Single();

    Assert.Equal(new Vector3I(0, 0, 0), playerUnit.Position.Raw);
    Assert.Equal(new Vector3I(3, 0, 3), enemyUnit.Position.Raw);

    // InProgress + player (Sides[0]) is the active side: it can act, enemy cannot yet.
    Assert.True(runtime.Query(new CanUnitActNow(playerUnit.State)));
    Assert.False(runtime.Query(new CanUnitActNow(enemyUnit.State)));
  }

  // Runs one failure-matrix row: a fresh inert system rides along so the test proves typed
  // failures return before any declared system registers.
  private static BattleSetupFailure StartExpectingFailure(BattleSetup broken, BattleSetupFailureReason expected)
  {
    var system = new SetupSystemData();
    BattleSetupFailure failure = BattleFactory.Start(broken with { Systems = [system] }).RequireLeft();
    Assert.Equal(expected, failure.Reason);
    Assert.True(system.RegisteredRuntime is null);
    return failure;
  }

  [TestCase(TestName = "Start fails when a side has no objective")]
  public void StartMissingObjective()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var noEnemyObjective = setup with
    {
      Sides = [setup.Sides[0], setup.Sides[1] with { Objectives = [] }],
    };

    StartExpectingFailure(noEnemyObjective, BattleSetupFailureReason.MissingObjective);
  }

  [TestCase(TestName = "Start fails when the designated player faction is not among the sides")]
  public void StartUnknownPlayerFaction()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var stranger = TestData.MakeFaction("Stranger");
    var withStrangerPlayer = setup with { PlayerFaction = Some(stranger) };

    StartExpectingFailure(withStrangerPlayer, BattleSetupFailureReason.UnknownFaction);
  }

  [TestCase(TestName = "Start fails when a unit belongs to another side")]
  public void StartUnitFactionMismatch()
  {
    var (setup, player, _, alpha, _) = MinimalSetup();
    var crossed = setup with
    {
      Sides =
      [
        setup.Sides[0] with
        {
          Units =
          [
            new UnitPlacement(new UnitLoadout(alpha), new Vector3I(0, 0, 0)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Bandit", setup.Sides[1].Faction)), new Vector3I(1, 0, 1)),
          ],
        },
        setup.Sides[1],
      ],
    };

    StartExpectingFailure(crossed, BattleSetupFailureReason.FactionMismatch);
    Assert.True(ReferenceEquals(player, alpha.OwningFaction));
  }

  [TestCase(TestName = "Start fails when a unit's faction is entirely foreign")]
  public void StartUnitForeignFactionMismatch()
  {
    var (setup, player, _, alpha, _) = MinimalSetup();
    var foreign = TestData.MakeFaction("Foreign");
    var crossed = setup with
    {
      Sides =
      [
        setup.Sides[0] with
        {
          Units =
          [
            new UnitPlacement(new UnitLoadout(alpha), new Vector3I(0, 0, 0)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Ghost", foreign)), new Vector3I(1, 0, 1)),
          ],
        },
        setup.Sides[1],
      ],
    };

    StartExpectingFailure(crossed, BattleSetupFailureReason.FactionMismatch);
    Assert.True(ReferenceEquals(player, alpha.OwningFaction));
  }

  [TestCase(TestName = "Start fails when a spawn cell is out of bounds")]
  public void StartSpawnCellOutOfBounds()
  {
    var (setup, player, _, _, _) = MinimalSetup();
    var alpha = TestData.MakeCombatant("Alpha", player);
    var broken = setup with
    {
      Sides =
      [
        setup.Sides[0] with { Units = [new UnitPlacement(new UnitLoadout(alpha), new Vector3I(99, 0, 0))] },
        setup.Sides[1],
      ],
    };

    StartExpectingFailure(broken, BattleSetupFailureReason.SpawnCellUnavailable);
    Assert.True(ReferenceEquals(player, alpha.OwningFaction));
  }

  [TestCase(TestName = "Start fails when a spawn cell's tile is a wall")]
  public void StartSpawnCellBlocked()
  {
    var (setup, player, _, _, _) = MinimalSetup();
    setup.Map.Tiles[new Godot.Vector3I(1, 0, 0)] = TestData.WallTile();
    var alpha = TestData.MakeCombatant("Alpha", player);
    var broken = setup with
    {
      Sides =
      [
        setup.Sides[0] with { Units = [new UnitPlacement(new UnitLoadout(alpha), new Vector3I(1, 0, 0))] },
        setup.Sides[1],
      ],
    };

    StartExpectingFailure(broken, BattleSetupFailureReason.SpawnCellUnavailable);
    Assert.True(ReferenceEquals(player, alpha.OwningFaction));
  }

  [TestCase(TestName = "Start fails when two units share a spawn cell")]
  public void StartDuplicateSpawnCell()
  {
    var (setup, player, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Sides =
      [
        setup.Sides[0] with
        {
          Units =
          [
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Alpha", player)), new Vector3I(0, 0, 0)),
            new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Beta", player)), new Vector3I(0, 0, 0)),
          ],
        },
        setup.Sides[1],
      ],
    };

    StartExpectingFailure(broken, BattleSetupFailureReason.DuplicateSpawnCell);
  }

  [TestCase(TestName = "Start fails when an object cell is out of bounds")]
  public void StartObjectCellOutOfBounds()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Objects = [new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" }, new Vector3I(99, 0, 0))],
    };

    StartExpectingFailure(broken, BattleSetupFailureReason.ObjectCellUnavailable);
  }

  [TestCase(TestName = "Start fails when an object's tile is a wall")]
  public void StartObjectCellBlocked()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    setup.Map.Tiles[new Godot.Vector3I(1, 0, 0)] = TestData.WallTile();
    var broken = setup with
    {
      Objects = [new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" }, new Vector3I(1, 0, 0))],
    };

    StartExpectingFailure(broken, BattleSetupFailureReason.ObjectCellUnavailable);
  }

  [TestCase(TestName = "Start fails when two objects share a cell")]
  public void StartDuplicateObjectCell()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Objects =
      [
        new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" }, new Vector3I(1, 0, 0)),
        new ObjectPlacement(new BattleSpecialObjectData { Name = "Twin" }, new Vector3I(1, 0, 0)),
      ],
    };

    StartExpectingFailure(broken, BattleSetupFailureReason.DuplicateObjectCell);
  }

  [TestCase(TestName = "Start fails when an object sits on a unit's spawn cell")]
  public void StartObjectOnUnitCell()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Objects = [new ObjectPlacement(new BattleSpecialObjectData { Name = "Marker" }, new Vector3I(0, 0, 0))],
    };

    StartExpectingFailure(broken, BattleSetupFailureReason.ObjectCellUnavailable);
  }

  // --- argument contracts --------------------------------------------------

  [TestCase(TestName = "Start throws on a null setup")]
  public void StartThrowsOnNullSetup()
  {
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start((BattleSetup)null!));
  }

  [TestCase(TestName = "Start throws on a null map")]
  public void StartThrowsOnNullMap()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(setup with { Map = null! }));
  }

  [TestCase(TestName = "Start throws on null sides")]
  public void StartThrowsOnNullSides()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(setup with { Sides = null! }));
  }

  [TestCase(TestName = "Start throws on a null side faction")]
  public void StartThrowsOnNullSideFaction()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with { Sides = [setup.Sides[0] with { Faction = null! }, setup.Sides[1]] };
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on null side objectives")]
  public void StartThrowsOnNullSideObjectives()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with { Sides = [setup.Sides[0] with { Objectives = null! }, setup.Sides[1]] };
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on null side units")]
  public void StartThrowsOnNullSideUnits()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with { Sides = [setup.Sides[0] with { Units = null! }, setup.Sides[1]] };
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on a null loadout")]
  public void StartThrowsOnNullLoadout()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Sides = [setup.Sides[0] with { Units = [new UnitPlacement(null!, new Vector3I(0, 0, 0))] }, setup.Sides[1]],
    };
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on a null combatant")]
  public void StartThrowsOnNullCombatant()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Sides = [setup.Sides[0] with { Units = [new UnitPlacement(new UnitLoadout(null!), new Vector3I(0, 0, 0))] }, setup.Sides[1]],
    };
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on null object data")]
  public void StartThrowsOnNullObjectData()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Objects = [new ObjectPlacement(null!, new Vector3I(1, 0, 0))],
    };
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(broken));
  }

  [TestCase(TestName = "Start throws on explicit null objects")]
  public void StartThrowsOnNullObjects()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(setup with { Objects = null! }));
  }

  [TestCase(TestName = "Start throws on explicit null systems")]
  public void StartThrowsOnNullSystems()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start(setup with { Systems = null! }));
  }

  [TestCase(TestName = "Start throws on an empty side list")]
  public void StartThrowsOnEmptySides()
  {
    var (setup, _, _, _, _) = MinimalSetup();
    Assert.Throws<ArgumentException>(() => BattleFactory.Start(setup with { Sides = [] }));
  }

  [TestCase(TestName = "Start throws when two sides share the same faction reference")]
  public void StartThrowsOnDuplicateSideFaction()
  {
    var (setup, player, _, _, _) = MinimalSetup();
    var broken = setup with
    {
      Sides =
      [
        setup.Sides[0],
        new BattleSideSetup(player, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Twin", player)), new Vector3I(1, 0, 1))]),
      ],
    };
    Assert.Throws<ArgumentException>(() => BattleFactory.Start(broken));
  }

  // --- authored battle type entry -------------------------------------------

  [TestCase(TestName = "Type start spawns rosters, places objects, registers declared systems")]
  public void RequestHappyPath()
  {
    BattleTypeData type = MakeBombDefusalType();
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

  private static BattleTypeData MakeBombDefusalType()
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

  [TestCase(TestName = "Type start with a single map starts units on the tagged spawn cells")]
  public void StartTypeHappyPath()
  {
    BattleTypeData type = TestData.MakeDuelBattleType();

    using var runtime = BattleFactory.Start(type, seed: 7).RequireRight();

    var player = runtime.Query(new GetPlayerFactionQuery()).RequireSome();
    var playerUnit = runtime.Query(new GetFactionAliveUnits(player)).AsValueEnumerable().Single();
    Faction enemy = runtime.Query(new GetGlobalFactionTurnOrderQuery())
      .AsValueEnumerable().Single(f => !ReferenceEquals(f, player));
    var enemyUnit = runtime.Query(new GetFactionAliveUnits(enemy)).AsValueEnumerable().Single();
    Assert.Equal(new Vector3I(0, 0, 0), playerUnit.Position.Raw);
    Assert.Equal(new Vector3I(1, 0, 0), enemyUnit.Position.Raw);
  }

  [TestCase(TestName = "Type start fails when a slot has fewer cells than its roster")]
  public void StartTypeShortfall()
  {
    BattleTypeData type = TestData.MakeDuelBattleType();
    type.Factions[0].Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Combatant = TestData.MakeCombatantData("Extra", health: 20, aim: 65),
      Weapon = TestData.MakeWeaponData(damage: 1, critChance: 0, range: 10),
    });

    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall,
      BattleFactory.Start(type, seed: 7).RequireLeft().Reason);
  }

  [TestCase(TestName = "Type start fails when a tagged spawn cell is not walkable")]
  public void StartTypeBlockedSpawnCell()
  {
    var type = new BattleTypeData { Name = "Blocked" };
    type.MapPool.Add(TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), new BattleMapTileData { SpawnFactionSlot = 0, Walkable = false }),
      (new Vector3I(3, 0, 3), TestData.SpawnTile(1))));
    var playerDeployment = new FactionDeploymentData { Faction = new FactionData { Name = "Player" } };
    playerDeployment.Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Combatant = TestData.MakeCombatantData("Alpha", health: 20, aim: 65),
    });
    playerDeployment.Objectives.Add(new FakeObjectiveData());
    var enemyDeployment = new FactionDeploymentData { Faction = new FactionData { Name = "Enemy" } };
    enemyDeployment.Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Combatant = TestData.MakeCombatantData("Bandit", health: 20, aim: 65),
    });
    enemyDeployment.Objectives.Add(new FakeObjectiveData());
    type.Factions.Add(playerDeployment);
    type.Factions.Add(enemyDeployment);

    Assert.Equal(BattleSetupFailureReason.SpawnCellUnavailable,
      BattleFactory.Start(type, seed: 7).RequireLeft().Reason);
  }

  [TestCase(TestName = "A supplied player deployment is adopted by its original faction reference")]
  public void StartTypePlayerDeployment()
  {
    BattleTypeData type = TestData.MakeDuelBattleType();
    var campaign = TestData.MakeFaction("Campaign");
    var veteran = TestData.MakeCombatant("Vet", campaign);
    var deployment = new PlayerDeployment(campaign, [new UnitLoadout(veteran)]);

    using var runtime = BattleFactory.Start(type, seed: 7,
      playerDeployment: Some(deployment)).RequireRight();

    Assert.True(ReferenceEquals(campaign,
      runtime.Query(new GetPlayerFactionQuery()).RequireSome()));
    var unit = runtime.Query(new GetFactionAliveUnits(campaign)).AsValueEnumerable().Single();
    Assert.True(ReferenceEquals(campaign, unit.State.Side));
    Assert.True(ReferenceEquals(veteran, unit.State.Combatant));
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
    BattleTypeData type = MakeBombDefusalType();
    type.PlayerFactionIndex = 5;
    Assert.Equal(BattleSetupFailureReason.UnknownFaction,
      BattleFactory.Start(type, seed: 42).RequireLeft().Reason);
  }

  [TestCase(TestName = "Type start throws on a null battle type")]
  public void StartTypeThrowsOnNullType()
  {
    Assert.Throws<ArgumentNullException>(() => BattleFactory.Start((BattleTypeData)null!));
  }
}
