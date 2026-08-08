using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class PlayerActionControllerTest
{
  // 5x1x5 open board. Player "Hero" (armed) at (0,0,0), enemy "Goon" at (3,0,0).
  private static (BattleRuntime Runtime, Faction Player, AliveUnit Hero) MakeArmedBattle() =>
    MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));

  // 5x1x5 open board. Player "Hero" at (0,0,0) with the given weapon (or none), enemy "Goon" at (3,0,0).
  private static (BattleRuntime Runtime, Faction Player, AliveUnit Hero) MakeBattle(Option<Weapon> heroWeapon)
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var runtime = StartRuntime(
      new Vector3I(5, 1, 5),
      new StartPlacement(player, BattleTestFactory.MakeCombatant("Hero", player), new Vector3I(0, 0, 0), heroWeapon),
      new StartPlacement(enemy, BattleTestFactory.MakeCombatant("Goon", enemy), new Vector3I(3, 0, 0)));
    var hero = SingleAliveUnit(runtime, player);
    return (runtime, player, hero);
  }

  private static T OptionOf<T>(PlayerActionController c) where T : UnitActionOption =>
    c.ActionOptions.OfType<T>().Single();

  [TestCase(TestName = "Selecting a unit caches its available action options")]
  public void SelectCachesOptions()
  {
    var (runtime, player, _) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);

    Assert.True(c.TrySelectUnitAt(new Vector3I(0, 0, 0)));
    Assert.True(OptionOf<MoveActionOption>(c).IsAvailable);
    Assert.True(OptionOf<AttackActionOption>(c).IsAvailable);
  }

  [TestCase(TestName = "Unarmed unit: presentation builds no AttackActionOption")]
  public void UnarmedUnitHasNoAttackOption()
  {
    var (runtime, player, _) = MakeBattle(None);
    var c = new PlayerActionController(runtime, player);

    Assert.True(c.TrySelectUnitAt(new Vector3I(0, 0, 0)));
    Assert.False(c.ActionOptions.OfType<AttackActionOption>().Any());
    Assert.True(OptionOf<MoveActionOption>(c).IsAvailable);
  }

  [TestCase(TestName = "BeginAction(Move) -> targeting -> Confirm relocates the unit")]
  public void MoveFlowRelocates()
  {
    var (runtime, player, hero) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));

    c.BeginAction(OptionOf<MoveActionOption>(c));
    Assert.Equal(PlayerActionController.TargetingMode.ActionTargeting, c.Mode);
    Assert.True(c.SetPending(new Vector3I(2, 0, 1)));
    Assert.Equal(PlayerActionController.TargetingMode.ActionPending, c.Mode);
    c.Confirm();

    // The pre-move proof's snapshot is stale after the commit; re-mint to read the new position.
    Assert.Equal(new Vector3I(2, 0, 1), runtime.TryGetAlive(hero.State).RequireSome().Position.Raw);
    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
    Assert.True(c.SelectedUnit.IsSome);
  }

  [TestCase(TestName = "BeginAction(Attack) -> targeting -> Confirm resolves an attack")]
  public void AttackFlowResolves()
  {
    var (runtime, player, _) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));

    c.BeginAction(OptionOf<AttackActionOption>(c));
    Assert.Equal(PlayerActionController.TargetingMode.ActionTargeting, c.Mode);
    ActionPreview preview = GetValue(c.PreviewAt(new Vector3I(3, 0, 0)));
    Assert.True(preview is AttackPreview);
    Assert.True(c.SetPending(new Vector3I(3, 0, 0)));
    c.Confirm();

    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
  }

  [TestCase(TestName = "BeginAction(Pass) submits immediately and stays selected")]
  public void PassSubmitsImmediately()
  {
    var (runtime, player, hero) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));

    c.BeginAction(OptionOf<PassActionOption>(c));

    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
    // After passing, the unit is no longer available this turn.
    Assert.False(runtime.Query(new CanUnitActNow(hero.State)));
  }

  [TestCase(TestName = "Cancel steps back one level")]
  public void CancelStepsBack()
  {
    var (runtime, player, _) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));
    c.BeginAction(OptionOf<MoveActionOption>(c));
    c.SetPending(new Vector3I(2, 0, 1));

    c.Cancel(); // ActionPending -> ActionTargeting
    Assert.Equal(PlayerActionController.TargetingMode.ActionTargeting, c.Mode);
    c.Cancel(); // ActionTargeting -> None (still selected)
    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
    Assert.True(c.SelectedUnit.IsSome);
  }
}
