using FunProject.Core;
using Godot;

namespace FunProject.Battle;

// Authored definition of an objective: display text (inherited) plus the directives applied
// when it completes/fails. Behavior and parameters live in the runtime Objective subclass
// created by Instantiate; authored parameters live in concrete data subclasses. Not
// constructible on its own — subclass per objective kind.
[GlobalClass]
public abstract partial class ObjectiveData : NamedEntityData
{
  [Export] public ObjectiveDirectiveData? OnComplete { get; set; }

  [Export] public ObjectiveDirectiveData? OnFail { get; set; }

  public abstract Objective Instantiate();
}
