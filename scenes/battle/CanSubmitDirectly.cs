using FunProject.Battle;

namespace FunProject.Scenes;

interface CanActDirectly
{
  public BattleAction MakeAction(BattleRuntime runtime);
};
