namespace FunProject.Battle;

public interface IBattleSessionQuery<out TResult>
{
  TResult Execute(BattleSession session);
}
