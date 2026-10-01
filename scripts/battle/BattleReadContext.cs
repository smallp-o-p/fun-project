using System;

namespace FunProject.Battle;

/// <summary>
/// The one read side of a battle: shared tactical state plus lifecycle facts. Preparation
/// contexts carry only their state; runtime contexts derive the turn and completion answers
/// from the runtime's single internal running-or-completed representation. Inspection code
/// composes reads through <see cref="Query{TDirect}"/>; trusted engine code uses the internal
/// accessors. A completed context answers turn questions with None, availability with false,
/// and keeps state-backed inspection (positions, visibility, hit previews) usable.
/// </summary>
public sealed class BattleReadContext
{
  internal BattleState State { get; }
  internal Option<BattleTurn> CurrentTurn { get; }
  internal Option<CompletedBattle> Completed { get; }

  // Some only while the runtime is running; completed and preparation contexts expose no
  // running receiver. Trusted default systems obtain it for their per-event scope.
  internal Option<BattleSession> RunningSession { get; }

  internal BattleReadContext(
    BattleState state,
    Option<BattleTurn> currentTurn,
    Option<CompletedBattle> completed,
    Option<BattleSession> runningSession)
  {
    ArgumentNullException.ThrowIfNull(state);
    State = state;
    CurrentTurn = currentTurn;
    Completed = completed;
    RunningSession = runningSession;
  }

  public TResult Query<TResult>(IBattleSessionQuery<TResult> query)
  {
    ArgumentNullException.ThrowIfNull(query);
    return query.Execute(this);
  }
}
