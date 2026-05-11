using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Tests;
using GdUnit4;
using Godot;

internal static class BattleActionTestHelper
{
  public static BattleTestUnit SpawnUnit(BattleSession session, Combatant combatant, Vector3I position)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.SpawnUnit(combatant, position)).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit.IsSome);
    Assert.True(result.AffectedUnitHandle.IsSome);
    return new BattleTestUnit(result.AffectedUnit.RequireSome(), result.AffectedUnitHandle.RequireSome());
  }

  public static BattleTestUnit SpawnUnit(BattleRuntime runtime, Combatant combatant, Vector3I position)
  {
    var result = runtime.ExecuteAction(BattleAction.SpawnUnit(combatant, position)).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit.IsSome);
    Assert.True(result.AffectedUnitHandle.IsSome);
    return new BattleTestUnit(result.AffectedUnit.RequireSome(), result.AffectedUnitHandle.RequireSome());
  }

  public static void StartBattle(BattleSession session)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.StartBattle()).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  public static void StartBattle(BattleRuntime runtime)
  {
    var result = runtime.ExecuteAction(BattleAction.StartBattle()).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

}
