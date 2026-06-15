using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Tests;
using FunProject.Weapons;
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
    return new BattleTestUnit(result.AffectedUnit.RequireSome());
  }

  public static BattleTestUnit SpawnUnit(BattleSession session, Combatant combatant, Vector3I position, Weapon weapon)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.SpawnUnit(combatant, position, weapon)).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit.IsSome);
    return new BattleTestUnit(result.AffectedUnit.RequireSome());
  }

  public static BattleTestUnit SpawnUnit(BattleRuntime runtime, Combatant combatant, Vector3I position)
  {
    var result = runtime.ExecuteAction(BattleAction.SpawnUnit(combatant, position)).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit.IsSome);
    return new BattleTestUnit(result.AffectedUnit.RequireSome());
  }

  public static void EnsureEveryFactionHasObjective(BattleSession session)
  {
    foreach (var faction in session.GlobalFactionTurnOrder)
      session.GetOperation(faction).IfSome(op =>
      {
        if (op.PendingObjectives.Count == 0)
          op.AddObjective(new EliminateAllOpposingForcesObjective(new ObjectiveData()));
      });
  }

  public static void StartBattle(BattleSession session)
  {
    EnsureEveryFactionHasObjective(session);
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
