using System;
using FunProject.Combatants;

namespace FunProject.GameState;

public sealed class Captivity
{
  private readonly SysColGeneric.HashSet<Combatant> _combatants =
    new(SysColGeneric.ReferenceEqualityComparer.Instance);

  /// <summary>Fresh snapshot of unique combatant references, with no ordering guarantee.</summary>
  public SysColGeneric.IReadOnlyList<Combatant> Combatants
    => _combatants.AsValueEnumerable().ToArray();

  internal void Add(Combatant combatant)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    _combatants.Add(combatant);
  }
}
