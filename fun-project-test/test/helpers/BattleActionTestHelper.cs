using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

internal static class BattleActionTestHelper
{
  // The Vector3I overloads below are the test-side mint doors: raw literal coordinates are
  // validated here so tests keep their compact call shape.
  public static BattleTestUnit SpawnUnit(BattleSession session, Combatant combatant, Vector3I position)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.SpawnUnit(combatant, session.Board.At(position))).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit.IsSome);
    return new BattleTestUnit(result.AffectedUnit.RequireSome());
  }

  public static BattleTestUnit SpawnUnit(BattleSession session, Combatant combatant, Vector3I position, Weapon weapon)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.SpawnUnit(combatant, session.Board.At(position), weapon)).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit.IsSome);
    return new BattleTestUnit(result.AffectedUnit.RequireSome());
  }

  public static BattleTestUnit SpawnUnit(
    BattleSession session,
    Combatant combatant,
    Vector3I position,
    Option<Weapon> weapon,
    Option<ItemWith<ArmorCapability>> armor)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(new SpawnUnit(combatant, session.Board.At(position), weapon, armor)).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit.IsSome);
    return new BattleTestUnit(result.AffectedUnit.RequireSome());
  }

  public static BattleTestUnit SpawnUnit(BattleRuntime runtime, Combatant combatant, Vector3I position)
  {
    var point = runtime.TryGetTile(position).RequireSome();
    var result = runtime.ExecuteAction(BattleAction.SpawnUnit(combatant, point)).RequireSingleResult();
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

  // The session-taking submit helpers below run on a throwaway executor. Hooks are
  // registered on the SESSION, so hooks a test has registered WILL also be evaluated for these
  // submissions — the reaction window is per-session, not per-executor. Tests that assert
  // reaction counts or interrupt results should submit through one executor they control.
  public static void AdvanceTurn(BattleSession session)
  {
    EndFactionTurn(session, session.ActiveSide);
  }

  public static void EndFactionTurn(BattleSession session, Faction faction)
  {
    EndFactionTurn(new BattleActionExecutor(session), faction);
  }

  public static void EndFactionTurn(BattleActionExecutor executor, Faction faction)
  {
    var result = executor.Submit(BattleAction.EndFactionTurn(faction)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  public static void PassUnit(BattleSession session, BattleUnitState unit)
  {
    var result = new BattleActionExecutor(session).Submit(BattleAction.PassUnit(unit)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  public static void ApplyDamage(BattleSession session, BattleUnitState unit, int amount)
  {
    var result = new BattleActionExecutor(session).Submit(BattleAction.ApplyDamage(unit, amount)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  public static BattleActionResult Attack(BattleActionExecutor executor, BattleUnitState attacker, BattleUnitState target)
  {
    var result = executor.Submit(BattleAction.AttackUnit(attacker, target)).RequireSingleResult();
    Assert.True(result.Succeeded);
    return result;
  }

  // Single faction, one started unit, and a fresh reusable executor.
  public static SoloBattle StartSoloBattle(
    Vector3I dimensions,
    Vector3I unitPosition,
    int health = 20,
    int actionPoints = 4,
    string unitName = "Alpha")
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(dimensions, [faction]);
    var unit = SpawnUnit(
      session,
      BattleTestFactory.MakeCombatant(unitName, faction, health: health, actionPoints: actionPoints),
      unitPosition);
    StartBattle(session);
    return new SoloBattle(session, new BattleActionExecutor(session), faction, unit);
  }
}
