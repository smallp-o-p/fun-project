namespace FunProject.Battle;

public interface IBattleSessionQuery<out TResult>
{
  [PreservesProofs]
  TResult Execute(BattleSession session);
}
