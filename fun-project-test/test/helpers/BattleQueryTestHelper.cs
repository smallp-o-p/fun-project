using FunProject.Battle;
using FunProject.Combatants;

namespace FunProject.Tests;

internal static class BattleQueryTestHelper
{
  public static TResult Query<TResult>(
    BattleSession session,
    IBattleSessionQuery<TResult> query)
  {
    return query.Execute(session);
  }

  public static AliveUnit SingleAliveUnit(BattleRuntime runtime, Faction faction) =>
    runtime.Query(new GetFactionAliveUnits(faction)).Single();

  public static TResult GetValue<TResult>(Either<BattleQueryFailure, TResult> result)
  {
    return result.Match(
      failure => throw new InvalidOperationException(failure.Message),
      value => value);
  }

  public static BattleQueryFailure GetFailure<TResult>(Either<BattleQueryFailure, TResult> result)
  {
    return result.Match(
      failure => failure,
      _ => throw new InvalidOperationException("Expected query failure, but query succeeded."));
  }
}
