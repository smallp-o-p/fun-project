using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Strategic;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleSetupResolverTest
{
  [TestCase(TestName = "Deployment preserves faction/combatant/equipment identity and captures modifiers at spawn")]
  public void DeploymentPreservesIdentityAndCapturesModifiersAtSpawn()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Faction.FactionBonuses.Add(
      new HealthStatMod { Modifiers = [StatModifier.Add(100)] });
    var campaignData = new FactionData { Name = "Campaign" };
    campaignData.FactionBonuses.Add(
      new HealthStatMod { Modifiers = [StatModifier.Add(10)] });
    var campaign = new Faction(campaignData);
    var veteran = TestData.MakeCombatant("Vet", campaign, health: 20);
    var weapon = TestData.MakeWeapon("Rifle");
    var armor = TestData.MakeArmor("Vest");
    var mods = new List<StatMod>
    {
      new HealthStatMod { Modifiers = [StatModifier.Add(12)] },
    };
    var loadout = new UnitLoadout(veteran, Some(weapon), Some(armor)) { StatMods = mods };
    var deployment = new PlayerDeployment(campaign, [loadout]);

    using var runtime = BattleFactory.Start(type, seed: 7,
      playerDeployment: Some(deployment)).RequireRight();
    var unit = runtime.Query(new GetFactionAliveUnits(campaign))
      .AsValueEnumerable().Single().State;

    Assert.True(ReferenceEquals(campaign, runtime.Query(new GetPlayerFactionQuery()).RequireSome()));
    Assert.True(ReferenceEquals(campaign, veteran.OwningFaction));
    Assert.True(ReferenceEquals(veteran, unit.Combatant));
    Assert.True(ReferenceEquals(weapon, unit.EquippedWeapon.RequireSome()));
    Assert.True(ReferenceEquals(armor.Item, unit.EquippedArmor.RequireSome().Item));
    Assert.Equal(42, unit.MaxHealth); // 20 + campaign 10 + deployment 12
    mods.Clear();
    Assert.Equal(42, unit.MaxHealth);
  }

  [TestCase(TestName = "Concrete-setup StatMods capture at spawn and stay stable afterwards")]
  public void ConcreteSetupCapturesModifiersAtSpawn()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var alpha = TestData.MakeCombatant("Alpha", player, health: 20);
    var mods = new List<StatMod>
    {
      new HealthStatMod { Modifiers = [StatModifier.Add(12)] },
    };
    var setup = TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(alpha) { StatMods = mods },
      new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)),
      [new FakeObjectiveData()], [new FakeObjectiveData()], Some(player));

    using var runtime = BattleFactory.Start(setup).RequireRight();
    var unit = runtime.Query(new GetFactionAliveUnits(player))
      .AsValueEnumerable().Single().State;

    Assert.Equal(32, unit.MaxHealth); // 20 + deployment 12
    Assert.Equal(32, unit.CurrentHealth);
    mods.Clear();
    Assert.Equal(32, unit.MaxHealth);
  }

  [TestCase(TestName = "Resolve preserves the seed and selects the map with a separate stream")]
  public void SeedAndMapChoice()
  {
    var type = TestData.MakeDuelBattleType();
    type.MapPool.Add(TestData.MakeMapScene(TestData.MakeMapData(new Vector3I(2, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(1)))));

    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();

    Assert.Equal(7, setup.Seed);
    PackedScene chosen = type.MapPool[new Random(7).Next(type.MapPool.Count)];
    Assert.True(ReferenceEquals(chosen, setup.MapScene.RequireSome()));
  }

  [TestCase(TestName = "A pooled BattleMap scene contributes its MapData and records the chosen scene")]
  public void SingleScenePoolResolvesMapDataAndScene()
  {
    var type = TestData.MakeDuelBattleType();
    PackedScene scene = TestData.MakeMapScene(TestData.MakeMapData(new Vector3I(4, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(3, 0, 0), TestData.SpawnTile(1))));
    type.MapPool.Clear();
    type.MapPool.Add(scene);

    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();

    Assert.True(ReferenceEquals(scene, setup.MapScene.RequireSome()));
    Assert.Equal(new Godot.Vector3I(4, 1, 1), setup.Map.Dimensions);
    Assert.Equal(2, setup.Map.Tiles.Count);
  }

  [TestCase(TestName = "A pooled scene whose root is not a BattleMap fails with MapSceneInvalid")]
  public void NonBattleMapSceneRootFails()
  {
    var type = TestData.MakeDuelBattleType();
    type.MapPool.Clear();
    type.MapPool.Add(PackRoot(new Node3D()));

    BattleSetupFailure failure = BattleSetupResolver.Resolve(type, seed: 7).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.MapSceneInvalid, failure.Reason);
  }

  [TestCase(TestName = "A null pool entry fails with MapSceneInvalid")]
  public void NullPoolEntryFails()
  {
    var type = TestData.MakeDuelBattleType();
    type.MapPool.Clear();
    type.MapPool.Add(null!);

    BattleSetupFailure failure = BattleSetupResolver.Resolve(type, seed: 7).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.MapSceneInvalid, failure.Reason);
  }

  [TestCase(TestName = "An empty pool still fails with EmptyMapPool")]
  public void EmptyPoolFailsWithEmptyMapPool()
  {
    var type = TestData.MakeDuelBattleType();
    type.MapPool.Clear();

    BattleSetupFailure failure = BattleSetupResolver.Resolve(type, seed: 7).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.EmptyMapPool, failure.Reason);
  }

  private static PackedScene PackRoot(Node prototype)
  {
    var scene = new PackedScene();
    Error error = scene.Pack(prototype);
    prototype.Free();
    if (error != Error.Ok)
      throw new InvalidOperationException($"Test scene packing failed: {error}");
    return scene;
  }

  [TestCase(TestName = "Resolving without a seed returns the integer that reproduces the layout")]
  public void MissingSeed()
  {
    var type = TestData.MakeDuelBattleType();
    BattleSetup first = BattleSetupResolver.Resolve(type).RequireRight();
    BattleSetup second = BattleSetupResolver.Resolve(type, first.Seed).RequireRight();

    Assert.True(ReferenceEquals(first.Map, second.Map));
    Assert.Equal(first.Sides.Count, second.Sides.Count);
    for (int sideIndex = 0; sideIndex < first.Sides.Count; sideIndex++)
    {
      Assert.Equal(first.Sides[sideIndex].Units.Count, second.Sides[sideIndex].Units.Count);
      for (int unitIndex = 0; unitIndex < first.Sides[sideIndex].Units.Count; unitIndex++)
        Assert.Equal(first.Sides[sideIndex].Units[unitIndex].Position,
          second.Sides[sideIndex].Units[unitIndex].Position);
    }
  }

  [TestCase(TestName = "A mixed deployment roster fails without reassigning any owning faction")]
  public void MixedRosterFailsWithoutReassigningFactions()
  {
    var type = TestData.MakeDuelBattleType();
    var campaign = TestData.MakeFaction("Campaign");
    var foreign = TestData.MakeFaction("Foreign");
    var insider = TestData.MakeCombatant("Insider", campaign);
    var outsider = TestData.MakeCombatant("Outsider", foreign);
    var deployment = new PlayerDeployment(campaign,
      [new UnitLoadout(insider), new UnitLoadout(outsider)]);

    BattleSetupFailure failure = BattleSetupResolver.Resolve(type, seed: 7,
      playerDeployment: Some(deployment)).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.FactionMismatch, failure.Reason);
    Assert.True(ReferenceEquals(campaign, insider.OwningFaction));
    Assert.True(ReferenceEquals(foreign, outsider.OwningFaction));
  }

  [TestCase(TestName = "Resolve guards the supplied deployment's required inputs")]
  public void SuppliedDeploymentGuards()
  {
    var type = TestData.MakeDuelBattleType();
    var campaign = TestData.MakeFaction("Campaign");
    var loadout = new UnitLoadout(TestData.MakeCombatant("Vet", campaign));

    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      playerDeployment: Some(new PlayerDeployment(null!, [loadout]))));
    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      playerDeployment: Some(new PlayerDeployment(campaign, null!))));
    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      playerDeployment: Some(new PlayerDeployment(campaign, [null!]))));
    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      playerDeployment: Some(new PlayerDeployment(campaign, [new UnitLoadout(null!)]))));
  }

  [TestCase(TestName = "An empty deployment keeps its faction and does not restore the authored roster")]
  public void EmptyDeployment()
  {
    var type = TestData.MakeDuelBattleType();
    var campaign = TestData.MakeFaction("Campaign");
    var deployment = new PlayerDeployment(campaign, []);

    using var runtime = BattleFactory.Start(type, seed: 7,
      playerDeployment: Some(deployment)).RequireRight();

    Assert.True(ReferenceEquals(campaign, runtime.Query(new GetPlayerFactionQuery()).RequireSome()));
    Assert.Equal(0, runtime.Query(new GetFactionAliveUnits(campaign)).Count);
    Faction enemy = runtime.Query(new GetGlobalFactionTurnOrderQuery())
      .AsValueEnumerable().Single(f => !ReferenceEquals(f, campaign));
    Assert.Equal(1, runtime.Query(new GetFactionAliveUnits(enemy)).Count);
  }

  [TestCase(TestName = "Without a deployment the authored player faction bonuses apply")]
  public void NoDeploymentUsesAuthoredFactionBonuses()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Faction.FactionBonuses.Add(
      new HealthStatMod { Modifiers = [StatModifier.Add(10)] });

    using var runtime = BattleFactory.Start(type, seed: 7).RequireRight();
    Faction player = runtime.Query(new GetPlayerFactionQuery()).RequireSome();
    var unit = runtime.Query(new GetFactionAliveUnits(player))
      .AsValueEnumerable().Single().State;

    Assert.Equal(30, unit.MaxHealth); // 20 + authored player bonus 10
  }

  [TestCase(TestName = "Other sides stay authored with fresh factions and their own bonuses")]
  public void OtherSidesRemainAuthored()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[1].Faction.FactionBonuses.Add(
      new AimStatMod { Modifiers = [StatModifier.Add(15)] });
    var campaign = TestData.MakeFaction("Campaign");
    var deployment = new PlayerDeployment(campaign,
      [new UnitLoadout(TestData.MakeCombatant("Vet", campaign))]);

    using BattleRuntime first = BattleFactory.Start(type, seed: 7,
      playerDeployment: Some(deployment)).RequireRight();
    using BattleRuntime second = BattleFactory.Start(type, seed: 7,
      playerDeployment: Some(deployment)).RequireRight();

    Faction firstEnemy = first.Query(new GetGlobalFactionTurnOrderQuery())
      .AsValueEnumerable().Single(f => !ReferenceEquals(f, campaign));
    Faction secondEnemy = second.Query(new GetGlobalFactionTurnOrderQuery())
      .AsValueEnumerable().Single(f => !ReferenceEquals(f, campaign));
    var firstEnemyUnit = first.Query(new GetFactionAliveUnits(firstEnemy))
      .AsValueEnumerable().Single().State;
    var secondEnemyUnit = second.Query(new GetFactionAliveUnits(secondEnemy))
      .AsValueEnumerable().Single().State;

    Assert.Equal(80, Mathf.RoundToInt(firstEnemyUnit.EffectiveStat<AimStat>())); // 65 + 15
    Assert.False(ReferenceEquals(firstEnemy, secondEnemy));
  }

  [TestCase(TestName = "Authored loadouts expand into fresh combatant, weapon, and armor instances per unit")]
  public void AuthoredLoadoutsCreateIndependentInstances()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Roster.Clear();
    type.Factions[0].Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Loadout = new UnitLoadoutData
      {
        Combatant = TestData.MakeCombatantData("Trooper", health: 20, aim: 65),
        Weapon = TestData.MakeAmmoWeaponData("Rifle", magazine: 4, damage: 3, range: 8),
        Armor = TestData.MakeArmorData("Vest", armor: 4),
      },
      Quantity = 2,
    });
    type.MapPool.Clear();
    type.MapPool.Add(TestData.MakeMapScene(TestData.MakeMapData(new Vector3I(4, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(2, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(3, 0, 0), TestData.SpawnTile(1)))));

    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();
    BattleSideSetup playerSide = setup.Sides[0];
    Assert.Equal(2, playerSide.Units.Count);
    UnitLoadout first = playerSide.Units[0].Loadout;
    UnitLoadout second = playerSide.Units[1].Loadout;
    Assert.True(ReferenceEquals(first.Combatant.OwningFaction, playerSide.Faction));
    Assert.True(ReferenceEquals(second.Combatant.OwningFaction, playerSide.Faction));
    Assert.False(ReferenceEquals(first.Combatant, second.Combatant));
    Assert.False(ReferenceEquals(first.Weapon.RequireSome(), second.Weapon.RequireSome()));
    Assert.False(ReferenceEquals(first.Armor.RequireSome().Item, second.Armor.RequireSome().Item));

    // Spent ammunition and damaged armor on one instance leave the sibling untouched.
    var spent = (AmmunitionedWeapon)first.Weapon.RequireSome();
    spent.TrySpendShot();
    Assert.Equal(3, spent.CurrentAmmo);
    Assert.Equal(4, ((AmmunitionedWeapon)second.Weapon.RequireSome()).CurrentAmmo);
    first.Armor.RequireSome().Capability.Reduce(2);
    Assert.Equal(2, first.Armor.RequireSome().Capability.Current);
    Assert.Equal(4, second.Armor.RequireSome().Capability.Current);

    // Authored quantity floors at one.
    type.Factions[0].Roster[0].Quantity = 0;
    BattleSetup floored = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();
    Assert.Equal(1, floored.Sides[0].Units.Count);
  }

  [TestCase(TestName = "Equipment authored as armor without an armor capability throws at resolution")]
  public void MalformedArmorThrows()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Roster.Clear();
    type.Factions[0].Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Loadout = new UnitLoadoutData
      {
        Combatant = TestData.MakeCombatantData("Trooper", health: 20, aim: 65),
        Armor = TestData.MakeWeaponData(damage: 3, range: 8),
      },
    });

    Assert.Throws<InvalidOperationException>(() => BattleSetupResolver.Resolve(type, seed: 7));
  }

  [TestCase(TestName = "A loadout without authored gear stays unarmed and unarmored")]
  public void MissingGearRemainsUnequipped()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Roster.Clear();
    type.Factions[0].Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Loadout = new UnitLoadoutData
      {
        Combatant = TestData.MakeCombatantData("Trooper", health: 20, aim: 65),
      },
    });

    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();
    UnitLoadout loadout = setup.Sides[0].Units[0].Loadout;
    Assert.True(loadout.Weapon.IsNone);
    Assert.True(loadout.Armor.IsNone);
  }

  [TestCase(TestName = "Resolved output keeps collection membership independent of the authored lists")]
  public void CollectionMembershipSurvivesOriginalMutation()
  {
    var type = TestData.MakeDuelBattleType();
    var campaign = TestData.MakeFaction("Campaign");
    var loadouts = new List<UnitLoadout> { new(TestData.MakeCombatant("Vet", campaign)) };
    var deployment = new PlayerDeployment(campaign, loadouts);
    type.Systems.Add(new ObjectExpirySystemData());
    var objectData = new BattleSpecialObjectData { Name = "Marker" };
    var placement = new ObjectPlacementData { SpecialObject = objectData };
    placement.Positions.Add(new Godot.Vector3I(1, 0, 0));
    type.Objects.Add(placement);

    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7,
      playerDeployment: Some(deployment)).RequireRight();
    UnitLoadout retainedLoadout = loadouts[0];
    BattleSideSetup retainedPlayerSide = setup.Sides[0];
    BattleTypeSystemData retainedSystem = setup.Systems[0];
    ObjectPlacement retainedObject = setup.Objects[0];

    loadouts.Clear();
    foreach (FactionDeploymentData side in type.Factions)
      side.Objectives.Clear();
    type.Systems.Clear();
    type.Objects.Clear();

    Assert.Equal(1, retainedPlayerSide.Units.Count);
    Assert.True(ReferenceEquals(retainedLoadout, retainedPlayerSide.Units[0].Loadout));
    Assert.True(ReferenceEquals(campaign, retainedPlayerSide.Faction));
    Assert.Equal(1, retainedPlayerSide.Objectives.Count);
    Assert.True(retainedPlayerSide.Objectives[0] is FakeObjectiveData);
    Assert.Equal(1, setup.Systems.Count);
    Assert.True(ReferenceEquals(retainedSystem, setup.Systems[0]));
    Assert.Equal(1, setup.Objects.Count);
    Assert.True(ReferenceEquals(retainedObject.Data, objectData));
    Assert.Equal(new Vector3I(1, 0, 0), retainedObject.Position);
  }

  [TestCase(TestName = "A side deployment replaces only its designated non-player slot")]
  public void SideDeploymentReplacesOnlyItsDesignatedSlot()
  {
    var type = MissionStageType();
    var mission = TestData.MakeTacticalMission(type, minEnemyUnits: 2, maxEnemyUnits: 2);
    mission.SpecialEnemies.Add(new UnitLoadoutData
    {
      Combatant = TestData.MakeCombatantData("Special", health: 30),
    });

    SideDeployment force = MissionEnemyResolver.Resolve(mission, seed: 7);
    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(force)).RequireRight();

    Assert.Equal(2, setup.Sides.Count);
    // The designated slot deploys the generated force by reference, in order.
    Assert.True(ReferenceEquals(force.Faction, setup.Sides[1].Faction));
    Assert.Equal(force.Loadouts.Count, setup.Sides[1].Units.Count);
    for (int index = 0; index < force.Loadouts.Count; index++)
    {
      Assert.True(ReferenceEquals(force.Loadouts[index], setup.Sides[1].Units[index].Loadout));
      Assert.True(ReferenceEquals(force.Faction,
        setup.Sides[1].Units[index].Loadout.Combatant.OwningFaction));
    }
    // The other slot keeps its authored roster and its own fresh faction.
    Assert.Equal(1, setup.Sides[0].Units.Count);
    Assert.False(ReferenceEquals(force.Faction, setup.Sides[0].Faction));
  }

  [TestCase(7, TestName = "Seed 7: a side deployment never changes the seeded map choice")]
  [TestCase(-7, TestName = "Seed -7: a side deployment never changes the seeded map choice")]
  public void SideDeploymentPreservesSeededMapChoice(int seed)
  {
    var type = MissionStageType();
    type.MapPool.Add(TestData.MakeMapScene(TestData.MakeMapData(new Vector3I(5, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(1)),
      (new Vector3I(2, 0, 0), TestData.SpawnTile(1)),
      (new Vector3I(3, 0, 0), TestData.SpawnTile(1)),
      (new Vector3I(4, 0, 0), TestData.SpawnTile(1)))));
    var mission = TestData.MakeTacticalMission(type, minEnemyUnits: 2, maxEnemyUnits: 2);

    SideDeployment force = MissionEnemyResolver.Resolve(mission, seed: seed);
    PackedScene expected = type.MapPool[new Random(seed).Next(type.MapPool.Count)];

    BattleSetup withForce = BattleSetupResolver.Resolve(type, seed: seed,
      sideDeployment: Some(force)).RequireRight();
    BattleSetup authored = BattleSetupResolver.Resolve(type, seed: seed).RequireRight();

    Assert.True(ReferenceEquals(expected, withForce.MapScene.RequireSome()));
    Assert.True(ReferenceEquals(expected, authored.MapScene.RequireSome()));
  }

  [TestCase(TestName = "Same-seed resolutions of a generated force repeat the layout with fresh identities")]
  public void GeneratedForcesRepeatLayoutWithFreshIdentities()
  {
    var type = MissionStageType();
    var mission = TestData.MakeTacticalMission(type, minEnemyUnits: 2, maxEnemyUnits: 2);
    mission.SpecialEnemies.Add(new UnitLoadoutData
    {
      Combatant = TestData.MakeCombatantData("Special", health: 30),
    });

    BattleSetup first = BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(MissionEnemyResolver.Resolve(mission, seed: 7))).RequireRight();
    BattleSetup second = BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(MissionEnemyResolver.Resolve(mission, seed: 7))).RequireRight();

    Assert.True(ReferenceEquals(first.MapScene.RequireSome(), second.MapScene.RequireSome()));
    Assert.False(ReferenceEquals(first.Sides[1].Faction, second.Sides[1].Faction));
    Assert.Equal(first.Sides.Count, second.Sides.Count);
    for (int side = 0; side < first.Sides.Count; side++)
    {
      Assert.Equal(first.Sides[side].Units.Count, second.Sides[side].Units.Count);
      for (int unit = 0; unit < first.Sides[side].Units.Count; unit++)
      {
        Assert.Equal(first.Sides[side].Units[unit].Position, second.Sides[side].Units[unit].Position);
        Assert.Equal(first.Sides[side].Units[unit].Loadout.Combatant.Name,
          second.Sides[side].Units[unit].Loadout.Combatant.Name);
        Assert.False(ReferenceEquals(first.Sides[side].Units[unit].Loadout.Combatant,
          second.Sides[side].Units[unit].Loadout.Combatant));
      }
    }
  }

  [TestCase(TestName = "Side deployments guard index, player slot, membership, and required inputs")]
  public void SideDeploymentGuards()
  {
    var type = TestData.MakeDuelBattleType();
    var campaign = TestData.MakeFaction("Campaign");
    var foreign = TestData.MakeFaction("Foreign");

    BattleSetupFailure outOfRange = BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(new SideDeployment(2, campaign,
        [new UnitLoadout(TestData.MakeCombatant("Vet", campaign))]))).RequireLeft();
    Assert.Equal(BattleSetupFailureReason.UnknownFaction, outOfRange.Reason);

    BattleSetupFailure playerSlot = BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(new SideDeployment(0, campaign,
        [new UnitLoadout(TestData.MakeCombatant("Vet", campaign))]))).RequireLeft();
    Assert.Equal(BattleSetupFailureReason.FactionMismatch, playerSlot.Reason);

    BattleSetupFailure mismatch = BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(new SideDeployment(1, campaign,
        [new UnitLoadout(TestData.MakeCombatant("Outsider", foreign))]))).RequireLeft();
    Assert.Equal(BattleSetupFailureReason.FactionMismatch, mismatch.Reason);

    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(new SideDeployment(1, null!, []))));
    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(new SideDeployment(1, campaign, null!))));
    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(new SideDeployment(1, campaign, [null!]))));
    Assert.Throws<ArgumentNullException>(() => BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(new SideDeployment(1, campaign, [new UnitLoadout(null!)]))));
  }

  [TestCase(TestName = "A generated force larger than its slot's spawn capacity fails with SpawnSlotShortfall")]
  public void OversizedGeneratedForceFailsWithShortfall()
  {
    var type = TestData.MakeDuelBattleType(); // 2x1x1 map: one spawn cell per side
    var mission = TestData.MakeTacticalMission(type, minEnemyUnits: 2, maxEnemyUnits: 2);

    SideDeployment force = MissionEnemyResolver.Resolve(mission, seed: 7);
    BattleSetupFailure failure = BattleSetupResolver.Resolve(type, seed: 7,
      sideDeployment: Some(force)).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall, failure.Reason);
  }

  // Two-faction duel on a one-map pool with one player cell and four enemy cells, enough for
  // the largest force the mission tests generate (two ordinary draws plus one special).
  private static BattleTypeData MissionStageType()
  {
    var type = TestData.MakeDuelBattleType();
    type.MapPool.Clear();
    type.MapPool.Add(TestData.MakeMapScene(TestData.MakeMapData(new Vector3I(5, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(1)),
      (new Vector3I(2, 0, 0), TestData.SpawnTile(1)),
      (new Vector3I(3, 0, 0), TestData.SpawnTile(1)),
      (new Vector3I(4, 0, 0), TestData.SpawnTile(1)))));
    return type;
  }

  [TestCase(TestName = "Campaign equipment slots are never read; only loadout equipment equips")]
  public void ExplicitEquipmentOnly()
  {
    var type = TestData.MakeDuelBattleType();
    var campaign = TestData.MakeFaction("Campaign");
    var veteran = TestData.MakeCombatant("Vet", campaign, modSlotCount: 2);
    veteran.EquipWeapon(TestData.MakeWeapon("Campaign Rifle"));
    veteran.EquipArmor(TestData.MakeArmor("Campaign Vest"));
    var deployment = new PlayerDeployment(campaign, [new UnitLoadout(veteran)]);

    using var runtime = BattleFactory.Start(type, seed: 7,
      playerDeployment: Some(deployment)).RequireRight();
    var unit = runtime.Query(new GetFactionAliveUnits(campaign))
      .AsValueEnumerable().Single().State;

    Assert.True(unit.EquippedWeapon.IsNone);
    Assert.True(unit.EquippedArmor.IsNone);
  }
}
