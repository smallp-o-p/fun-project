using FunProject.Battle;
using FunProject.Core;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class ArmorBattleTest
{
  [TestCase(TestName = "Spawned unit exposes its equipped armor at full pool")]
  public void SpawnedUnitExposesEquippedArmor()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [playerFaction]);
    var armor = TestData.MakeArmor("Plate", armor: 10, element: Element.Thermal);

    var unit = battle.Spawn(
      TestData.MakeCombatant("Alpha", playerFaction),
      new Vector3I(1, 0, 1),
      TestData.MakeWeapon("Rifle"),
      armor);

    Assert.True(unit.EquippedArmor.IsSome);
    var equipped = unit.EquippedArmor.RequireSome();
    Assert.Equal(10, equipped.Capability.Current);
    Assert.Equal(Element.Thermal, equipped.Capability.Element);
  }

  [TestCase(TestName = "Units spawn without armor by default")]
  public void UnitsSpawnWithoutArmorByDefault()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [playerFaction]);

    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction), new Vector3I(1, 0, 1));

    Assert.True(unit.EquippedArmor.IsNone);
  }

  // NOTE: BattleAction.ApplyDamage emits a Kinetic packet. Tests that are not about
  // element matching author Thermal armor so depletion stays at 1x and the numbers
  // read plainly; the attack test below covers the matched 1.5x path.
  [TestCase(TestName = "Damage depletes armor before health and the event carries the split")]
  public void DamageDepletesArmorBeforeHealth()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [playerFaction]);
    var armor = TestData.MakeArmor("Plate", armor: 10, element: Element.Thermal);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction, health: 20),
      new Vector3I(1, 0, 1), TestData.MakeWeapon("Rifle"), armor);
    battle.Start();

    battle.ClearEvents();
    battle.ApplyDamage(unit, 4);

    Assert.Equal(6, armor.Capability.Current);
    Assert.Equal(20, unit.CurrentHealth);
    var damagedEvent = battle.Events.SingleEvent<UnitDamagedBattleEvent>();
    Assert.Equal(4, damagedEvent.ArmorDamage);
    Assert.Equal(0, damagedEvent.HealthDamage);
    Assert.Equal(4, damagedEvent.TotalAmount);
  }

  [TestCase(TestName = "Spillover damages health and can kill through armor")]
  public void SpilloverCanKillThroughArmor()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [playerFaction]);
    var armor = TestData.MakeArmor("Vest", armor: 3, element: Element.Thermal);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction, health: 5),
      new Vector3I(1, 0, 1), TestData.MakeWeapon("Rifle"), armor);
    battle.Start();

    battle.ClearEvents();
    battle.ApplyDamage(unit, 10);

    Assert.Equal(0, armor.Capability.Current);
    Assert.Equal(0, unit.CurrentHealth);
    Assert.True(unit.IsDead);
    // A killing blow commits death only: no damage event accompanies the kill.
    Assert.False(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any());
    Assert.True(battle.Events.EventsOf<UnitKilledBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Attack strips element-matched armor at 1.5x")]
  public void AttackStripsMatchedArmorAtOnePointFive()
  {
    var armor = TestData.MakeArmor("Plate", armor: 6, element: Element.Kinetic);
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(8, 1, 8),
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle", damage: 5)),
      enemy: new("Hostile", Health: 20, Weapon: TestData.MakeWeapon("Pistol"), Armor: armor));

    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    // Default weapon frame emits one Kinetic packet of 5: matched -> min(6, 7) = 6 armor, no spill.
    Assert.Equal(0, armor.Capability.Current);
    Assert.Equal(20, battle.EnemyUnit.CurrentHealth);
    var damagedEvent = battle.Events.SingleEvent<UnitDamagedBattleEvent>();
    Assert.Equal(6, damagedEvent.ArmorDamage);
    Assert.Equal(0, damagedEvent.HealthDamage);
    Assert.Equal(5, damagedEvent.TotalAmount);
  }
}
