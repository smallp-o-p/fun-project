namespace FunProject.Battle;

public interface IBattleSessionQuery<out TResult>
{
  TResult Execute(BattleReadContext context);
}
