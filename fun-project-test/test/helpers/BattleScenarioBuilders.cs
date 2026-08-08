using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Weapons;
using Godot;

namespace FunProject.Tests;

// A started single-faction, single-unit battle plus a fresh reusable executor.
// Built by BattleActionTestHelper.StartSoloBattle.
internal sealed record SoloBattle(
  BattleSession Session,
  BattleActionExecutor Executor,
  Faction Faction,
  BattleTestUnit Unit);

// One side of a duel. Position defaults to the dominant (4,0,1)/(4,0,4) convention chosen by
// BattleDuelBuilder when null; a null Weapon spawns the unit unarmed.
internal sealed record DuelSide(
  string Name,
  Vector3I? Position = null,
  int Health = 20,
  int ActionPoints = 4,
  int Aim = 65,
  Weapon Weapon = null,
  Option<ItemWith<ArmorCapability>> Armor = default,
  BuffData[] Buffs = null);

// A started Player-vs-Enemy battle with one unit per side and a shared reusable executor.
internal sealed record TwoFactionBattle(
  BattleSession Session,
  BattleActionExecutor Executor,
  Faction PlayerFaction,
  Faction EnemyFaction,
  BattleTestUnit PlayerUnit,
  BattleTestUnit EnemyUnit);

// Arranges a two-faction ("Player"/"Enemy") duel: one unit per side, StartBattle, and one
// reusable executor. Board wins over Dimensions when both are set.
internal sealed class BattleDuelBuilder
{
  public Vector3I Dimensions { get; init; } = new(8, 1, 8);
  public BattleBoardState Board { get; init; }
  public IHitChanceCalculator HitChanceCalculator { get; init; }
  public int? RandomSeed { get; init; }
  public bool PlayerControlled { get; init; }
  public DuelSide Player { get; init; } = new("Alpha");
  public DuelSide Enemy { get; init; } = new("Hostile");

  public TwoFactionBattle Start()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    BattleBoardState board = Board ?? new BattleBoardState(Dimensions);
    var session = BattleTestFactory.MakeSession(
      board,
      [playerFaction, enemyFaction],
      HitChanceCalculator,
      RandomSeed,
      PlayerControlled ? Some(playerFaction) : None);

    BattleTestUnit playerUnit = SpawnSide(session, playerFaction, Player, new Vector3I(4, 0, 1));
    BattleTestUnit enemyUnit = SpawnSide(session, enemyFaction, Enemy, new Vector3I(4, 0, 4));
    BattleActionTestHelper.StartBattle(session);

    return new TwoFactionBattle(
      session, ExecutorFor(session), playerFaction, enemyFaction, playerUnit, enemyUnit);
  }

  private static BattleTestUnit SpawnSide(BattleSession session, Faction faction, DuelSide side, Vector3I defaultPosition)
  {
    Combatant combatant = BattleTestFactory.MakeCombatant(
      side.Name, faction, health: side.Health, actionPoints: side.ActionPoints, aim: side.Aim, buffs: side.Buffs);
    return BattleActionTestHelper.SpawnUnit(
      session, combatant, side.Position ?? defaultPosition, Optional(side.Weapon), side.Armor);
  }
}
