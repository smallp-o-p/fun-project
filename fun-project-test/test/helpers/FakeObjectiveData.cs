#nullable enable

using FunProject.Battle;
using System;

namespace FunProject.Tests;

// Test-local authored objective data: instantiates the mutable FakeObjective double.
public sealed partial class FakeObjectiveData : ObjectiveData
{
  public bool Complete { get; set; }
  public bool Failed { get; set; }
  public Type? Observe { get; set; }

  public override Objective Instantiate() => new FakeObjective(this);
}
