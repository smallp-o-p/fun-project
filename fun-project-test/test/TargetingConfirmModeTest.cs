using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using LanguageExt;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class TargetingConfirmModeTest
{
  [TestCase(TestName = "Move uses click-then-confirm; attack fires immediately")]
  public void ConfirmModesAreAssignedPerVerb()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [faction]);
    var runtime = new BattleRuntime(session);
    BattleTestUnit unit = BattleActionTestHelper.SpawnUnit(
      session,
      BattleTestFactory.MakeCombatant("Hero", faction),
      new Vector3I(1, 0, 1),
      BattleTestFactory.MakeWeapon("Rifle", range: 10));

    var move = new MoveTargeting(runtime, unit.State);
    Weapon weapon = unit.State.EquippedWeapon.RequireSome();
    var attack = new AttackTargeting(runtime, unit.State, weapon);

    Assert.Equal(ConfirmMode.Confirm, move.Confirm);
    Assert.Equal(ConfirmMode.Immediate, attack.Confirm);
  }
}
