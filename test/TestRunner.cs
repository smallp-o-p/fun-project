using System;
using Godot;

public abstract partial class TestRunner : Node
{
  private int _passed;
  private int _failed;

  protected void T(string name, Action body)
  {
    bool ok = true;
    try { body(); } catch (Exception e) { GD.PrintErr($"  FAIL: {name}: {e.Message}"); ok = false; }
    if (ok) { _passed++; GD.Print($"  PASS: {name}"); } else { _failed++; }
  }

  protected void Report()
  {
    if (_failed > 0) GD.PrintErr($"There were {_failed} failures out of {_passed + _failed} tests.");

    GD.PrintRich($"[color=green] Tests passed: {_passed} [/color]");

    if (_failed > 0)
      GD.PrintRich($"[color=red] Tests failed: {_failed} [/color]");
    else
      GD.PrintRich($"[color=green] All tests passed! [/color]");
    GetTree().Quit(_failed > 0 ? 1 : 0);
  }
}
