namespace FunProject.Battle;

/// <summary>Trusted synchronous, read-only code. Off-thread implementations must be worker compatible;
/// scene/native work is not marshalled while owning the gate. Borrowed results require an outer scope.</summary>
public interface IBattleSessionQuery<out TResult>
{
  TResult Execute(BattleSession session);
}
