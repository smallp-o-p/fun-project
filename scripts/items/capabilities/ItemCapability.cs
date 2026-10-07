using FunProject.Battle;
namespace FunProject.Items.Capabilities;

public abstract class ItemCapability
{
  private protected readonly BattleAccessBinding _battleAccess = new(shared: true);
  internal virtual void BindBattleAccess(BattleAccessGuard access) => _battleAccess.Bind(access);
  internal virtual void UnbindBattleAccess(BattleAccessGuard access) => _battleAccess.Unbind(access);
}
