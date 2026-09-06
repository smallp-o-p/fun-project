using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class TargetingConfirmModeTest
{
  [TestCase(TestName = "Move uses click-then-confirm; attack fires immediately")]
  public void ConfirmModesAreAssignedPerVerb()
  {
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [TestData.MakeFaction("Player")]);
    // The spawn and both targeters share the fixture's one runtime.
    BattleUnitState unit = battle.Spawn(TestData.MakeCombatant("Hero", battle.PlayerFaction),
      new Vector3I(1, 0, 1), TestData.MakeWeapon("Rifle", range: 10));

    var move = new MoveTargeting(battle.Runtime, unit);
    Weapon weapon = unit.EquippedWeapon.RequireSome();
    var attack = new AttackTargeting(battle.Runtime, unit, weapon);

    Assert.Equal(ConfirmMode.Confirm, move.Confirm);
    Assert.Equal(ConfirmMode.Immediate, attack.Confirm);
  }
}
