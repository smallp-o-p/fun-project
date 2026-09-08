using System;
using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.GameState;

public sealed class Captivity
{
  private readonly SysColGeneric.HashSet<Combatant> _combatants =
    new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

  public IReadOnlyList<Combatant> Combatants
    => _combatants.AsValueEnumerable().ToArray();

  internal void Add(Combatant combatant)
  {
    ArgumentNullException.ThrowIfNull(combatant);
    _combatants.Add(combatant);
  }
}
