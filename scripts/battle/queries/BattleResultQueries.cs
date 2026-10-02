using System;

namespace FunProject.Battle;

// None while the battle runs; Some once the terminal boundary installed the frozen report.
public sealed class GetCompletedBattleQuery : IBattleSessionQuery<Option<CompletedBattle>>
{
  public Option<CompletedBattle> Execute(BattleReadContext context) => context.Completed;
}
