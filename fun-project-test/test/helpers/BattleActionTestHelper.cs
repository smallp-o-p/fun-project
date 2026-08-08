using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Runtime.CompilerServices;

namespace FunProject.Tests;

internal static class BattleActionTestHelper
{
  // ONE executor per session, ever: the executor owns the hook registry and registers the
  // default systems, so a second live executor would double-fire them (statuses ticking
  // twice). ConditionalWeakTable keys the executor's lifetime to the session, so sessions
  // dropped by a test don't leak executors.
  private static readonly ConditionalWeakTable<BattleSession, BattleActionExecutor> ExecutorsBySession = [];

  internal static BattleActionExecutor ExecutorFor(BattleSession session)
    => ExecutorsBySession.GetValue(session, static s => new BattleActionExecutor(s));

  // The Vector3I overloads below are the test-side mint doors: raw literal coordinates are
  // validated here so tests keep their compact call shape.
  public static BattleTestUnit SpawnUnit(BattleSession session, Combatant combatant, Vector3I position)
  {
    var executor = ExecutorFor(session);
    executor.Submit(BattleAction.SpawnUnit(combatant, session.Board.At(position)));
    return SpawnedUnitAt(session, position);
  }

  public static BattleTestUnit SpawnUnit(BattleSession session, Combatant combatant, Vector3I position, Weapon weapon)
  {
    var executor = ExecutorFor(session);
    executor.Submit(BattleAction.SpawnUnit(combatant, session.Board.At(position), weapon));
    return SpawnedUnitAt(session, position);
  }

  public static BattleTestUnit SpawnUnit(
    BattleSession session,
    Combatant combatant,
    Vector3I position,
    Option<Weapon> weapon,
    Option<ItemWith<ArmorCapability>> armor)
  {
    var executor = ExecutorFor(session);
    executor.Submit(new SpawnUnit(combatant, session.Board.At(position), weapon, armor));
    return SpawnedUnitAt(session, position);
  }

  public static BattleTestUnit SpawnUnit(BattleRuntime runtime, Combatant combatant, Vector3I position)
  {
    var point = runtime.TryGetTile(position).RequireSome();
    runtime.ExecuteAction(BattleAction.SpawnUnit(combatant, point));
    return new BattleTestUnit(runtime.Query(new GetUnitAtTile(point)).RequireSome());
  }

  // The simple execution model reports outcomes through state and events, so the spawn
  // helpers read the spawned unit straight back from its tile.
  private static BattleTestUnit SpawnedUnitAt(BattleSession session, Vector3I position)
    => new(session.GetUnitAt(session.Board.At(position)).RequireSome());

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
    var executor = ExecutorFor(session);
    executor.Submit(BattleAction.StartBattle());
  }

  public static void StartBattle(BattleRuntime runtime)
  {
    runtime.ExecuteAction(BattleAction.StartBattle());
  }

  // Session-taking helpers all submit through ExecutorFor(session): the single executor
  // that owns this session's hooks and default systems, so registered hooks observe these
  // submissions exactly as they do submissions through any other handle to that executor.
  public static void AdvanceTurn(BattleSession session)
  {
    EndFactionTurn(session, session.ActiveSide);
  }

  public static void EndFactionTurn(BattleSession session, Faction faction)
  {
    EndFactionTurn(ExecutorFor(session), faction);
  }

  public static void EndFactionTurn(BattleActionExecutor executor, Faction faction)
  {
    executor.Submit(BattleAction.EndFactionTurn(faction));
  }

  public static void PassUnit(BattleSession session, BattleUnitState unit)
  {
    ExecutorFor(session).Submit(
      BattleAction.PassUnit(session.TryGetAlive(unit).RequireSome()));
  }

  public static void ApplyDamage(BattleSession session, BattleUnitState unit, int amount)
  {
    ExecutorFor(session).Submit(
      BattleAction.ApplyDamage(session.TryGetAlive(unit).RequireSome(), amount));
  }

  public static BattleActionExecResult Attack(BattleSession session, BattleActionExecutor executor, BattleUnitState attacker, BattleUnitState target)
  {
    return executor.Submit(BattleAction.AttackUnit(
      session.TryGetAlive(attacker).RequireSome(),
      session.TryGetAlive(target).RequireSome()));
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
    return new SoloBattle(session, ExecutorFor(session), faction, unit);
  }
}
