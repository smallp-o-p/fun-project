using FunProject.Battle;
using FunProject.Combatants;
using System.Linq;

namespace FunProject.Tests;

internal static class BattleQueryTestHelper
{
  public static Either<BattleQueryFailure, TResult> Query<TResult>(
    BattleSession session,
    BattleSessionQuery<TResult> query)
  {
    using var runtime = new BattleRuntime(session);
    return runtime.Query(query);
  }

  public static AliveUnit SingleAliveUnit(BattleRuntime runtime, Faction faction) =>
    GetValue(runtime.Query(new GetFactionAliveUnits(faction))).Single();

  public static AliveUnit SingleAliveUnit(BattleSession session, Faction faction) =>
    GetValue(Query(session, new GetFactionAliveUnits(faction))).Single();

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
