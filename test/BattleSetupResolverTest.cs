using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
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
    var setup = new BattleSetup(
      TestData.MakeOpenBattleMap(),
      [
        new BattleSideSetup(player, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(alpha) { StatMods = mods }, new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemy, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Bandit", enemy)), new Vector3I(3, 0, 3))]),
      ],
      Seed: 7,
      PlayerFaction: Some(player));

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
    type.MapPool.Add(TestData.MakeMapData(new Vector3I(2, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(1))));

    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();

    Assert.Equal(7, setup.Seed);
    Assert.True(ReferenceEquals(type.MapPool[new Random(7).Next(type.MapPool.Count)], setup.Map));
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

  [TestCase(TestName = "Authored quantity expands with fresh combatant, weapon, and armor identities")]
  public void QuantityAndEquipmentExpansion()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Roster.Clear();
    type.Factions[0].Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Combatant = TestData.MakeCombatantData("Trooper", health: 20, aim: 65),
      Quantity = 3,
      Weapon = TestData.MakeWeaponData(damage: 3, range: 8),
      Armor = TestData.MakeArmorData("Vest", armor: 4),
    });
    type.MapPool.Clear();
    type.MapPool.Add(TestData.MakeMapData(new Vector3I(4, 1, 1),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(2, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(3, 0, 0), TestData.SpawnTile(1))));

    BattleSetup setup = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();
    BattleSideSetup playerSide = setup.Sides[0];
    Assert.Equal(3, playerSide.Units.Count);
    for (int i = 0; i < playerSide.Units.Count; i++)
    {
      UnitLoadout loadout = playerSide.Units[i].Loadout;
      Assert.True(loadout.Weapon.IsSome);
      Assert.True(loadout.Armor.IsSome);
      Assert.True(ReferenceEquals(loadout.Combatant.OwningFaction, playerSide.Faction));
      for (int j = i + 1; j < playerSide.Units.Count; j++)
      {
        UnitLoadout other = playerSide.Units[j].Loadout;
        Assert.False(ReferenceEquals(loadout.Combatant, other.Combatant));
        Assert.False(ReferenceEquals(loadout.Weapon.RequireSome(), other.Weapon.RequireSome()));
        Assert.False(ReferenceEquals(loadout.Armor.RequireSome().Item, other.Armor.RequireSome().Item));
      }
    }

    // Authored quantity floors at one.
    type.Factions[0].Roster[0].Quantity = 0;
    BattleSetup floored = BattleSetupResolver.Resolve(type, seed: 7).RequireRight();
    Assert.Equal(1, floored.Sides[0].Units.Count);
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
