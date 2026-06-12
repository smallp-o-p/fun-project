using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class ArmorBattleTest
{
  private static BattleUnitState SpawnArmoredUnit(
    BattleActionExecutor executor,
    Combatant combatant,
    Vector3I position,
    Weapon weapon,
    ItemWith<ArmorCapability> armor) =>
    executor.Submit(BattleAction.SpawnUnit(combatant, position, weapon, armor))
      .RequireSingleResult().AffectedUnit.RequireSome();

  [TestCase(TestName = "Spawned unit exposes its equipped armor at full pool")]
  public void SpawnedUnitExposesEquippedArmor()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [playerFaction]);
    var armor = BattleTestFactory.MakeArmor("Plate", armor: 10, element: Element.Thermal);

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.SpawnUnit(
      BattleTestFactory.MakeCombatant("Alpha", playerFaction),
      new Vector3I(1, 0, 1),
      BattleTestFactory.MakeWeapon("Rifle"),
      armor)).RequireSingleResult();

    Assert.True(result.Succeeded);
    var unit = result.AffectedUnit.RequireSome();
    Assert.True(unit.EquippedArmor.IsSome);
    var equipped = unit.EquippedArmor.RequireSome();
    Assert.Equal(10, equipped.Capability.Current);
    Assert.Equal(Element.Thermal, equipped.Capability.Element);
  }

  [TestCase(TestName = "Units spawn without armor by default")]
  public void UnitsSpawnWithoutArmorByDefault()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [playerFaction]);

    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(1, 0, 1));

    Assert.True(unit.State.EquippedArmor.IsNone);
  }

  // NOTE: BattleAction.ApplyDamage emits a Kinetic packet. Tests that are not about
  // element matching author Thermal armor so depletion stays at 1x and the numbers
  // read plainly; the attack test below covers the matched 1.5x path.
  [TestCase(TestName = "Damage depletes armor before health and the event carries the split")]
  public void DamageDepletesArmorBeforeHealth()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [playerFaction]);
    var armor = BattleTestFactory.MakeArmor("Plate", armor: 10, element: Element.Thermal);
    var executor = new BattleActionExecutor(session);
    var unit = SpawnArmoredUnit(executor, BattleTestFactory.MakeCombatant("Alpha", playerFaction, health: 20),
      new Vector3I(1, 0, 1), BattleTestFactory.MakeWeapon("Rifle"), armor);
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;
    var result = executor.Submit(BattleAction.ApplyDamage(unit, 4)).RequireSingleResult();

    Assert.True(result.Succeeded);
    Assert.Equal(6, armor.Capability.Current);
    Assert.Equal(20, unit.CurrentHealth);
    var damagedEvent = raisedEvents.OfType<UnitDamagedBattleEvent>().Single();
    Assert.Equal(4, damagedEvent.ArmorDamage);
    Assert.Equal(0, damagedEvent.HealthDamage);
    Assert.Equal(4, damagedEvent.TotalAmount);
  }

  [TestCase(TestName = "Spillover damages health and can kill through armor")]
  public void SpilloverCanKillThroughArmor()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [playerFaction]);
    var armor = BattleTestFactory.MakeArmor("Vest", armor: 3, element: Element.Thermal);
    var executor = new BattleActionExecutor(session);
    var unit = SpawnArmoredUnit(executor, BattleTestFactory.MakeCombatant("Alpha", playerFaction, health: 5),
      new Vector3I(1, 0, 1), BattleTestFactory.MakeWeapon("Rifle"), armor);
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;
    executor.Submit(BattleAction.ApplyDamage(unit, 10)).RequireSingleResult();

    Assert.Equal(0, armor.Capability.Current);
    Assert.Equal(0, unit.CurrentHealth);
    Assert.True(unit.IsDead);
    var damagedEvent = raisedEvents.OfType<UnitDamagedBattleEvent>().Single();
    Assert.Equal(3, damagedEvent.ArmorDamage);
    Assert.Equal(7, damagedEvent.HealthDamage);
    Assert.True(raisedEvents.OfType<UnitKilledBattleEvent>().Any());
  }

  [TestCase(TestName = "Taking damage re-arms the regen delay")]
  public void DamageReArmsRegenDelay()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [playerFaction]);
    var armor = BattleTestFactory.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 2, regenPerTurn: 3);
    var executor = new BattleActionExecutor(session);
    var unit = SpawnArmoredUnit(executor, BattleTestFactory.MakeCombatant("Alpha", playerFaction),
      new Vector3I(1, 0, 1), BattleTestFactory.MakeWeapon("Rifle"), armor);
    StartBattle(session);

    Assert.Equal(0, armor.Capability.RegenDelayRemaining);
    executor.Submit(BattleAction.ApplyDamage(unit, 4)).RequireSingleResult();

    Assert.Equal(2, armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Attack strips element-matched armor at 1.5x")]
  public void AttackStripsMatchedArmorAtOnePointFive()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction], new AlwaysHitCalculator());
    var armor = BattleTestFactory.MakeArmor("Plate", armor: 6, element: Element.Kinetic);
    var executor = new BattleActionExecutor(session);
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(4, 0, 1), BattleTestFactory.MakeWeapon("Rifle", damage: 5));
    var target = SpawnArmoredUnit(executor, BattleTestFactory.MakeCombatant("Hostile", enemyFaction, health: 20),
      new Vector3I(4, 0, 4), BattleTestFactory.MakeWeapon("Pistol"), armor);
    StartBattle(session);

    var raisedEvents = new List<BattleEvent>();
    session.BattleEventCommitted += raisedEvents.Add;
    var result = executor.Submit(BattleAction.AttackUnit(attacker.State, target)).RequireSingleResult();

    // Default weapon frame emits one Kinetic packet of 5: matched -> min(6, 7) = 6 armor, no spill.
    Assert.True(result.Succeeded);
    Assert.Equal(0, armor.Capability.Current);
    Assert.Equal(20, target.CurrentHealth);
    var damagedEvent = raisedEvents.OfType<UnitDamagedBattleEvent>().Single();
    Assert.Equal(6, damagedEvent.ArmorDamage);
    Assert.Equal(0, damagedEvent.HealthDamage);
    Assert.Equal(5, damagedEvent.TotalAmount);
  }
}
