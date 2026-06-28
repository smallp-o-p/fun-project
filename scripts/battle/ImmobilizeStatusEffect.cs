using FunProject.Items.Effects;

namespace FunProject.Battle;

/// <summary>
/// Runtime status effect that prevents the owning unit from acting while active, driven
/// by an authored <see cref="ImmobilizeStatusSpecData"/>.
/// </summary>
public sealed class ImmobilizeStatusEffect : ActiveStatusEffect
{
  internal ImmobilizeStatusEffect(ImmobilizeStatusSpecData spec)
    : base(spec)
  {
  }

  internal override bool BlocksAction => true;
}
