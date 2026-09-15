using Godot;
using System;

namespace FunProject.Dialogue;

// A linear click-through conversation. Lines are required non-empty at play time —
// EnsurePlayable() throws on an empty sequence.
[GlobalClass]
public partial class DialogueSequenceData : Resource
{
  [Export] public DialogueLineData[] Lines { get; set; } = [];

  // The play-time authoring gate: DialogueView calls this from _Ready so bad authoring
  // (empty sequence/line, missing speaker, blank display name/portrait/text) throws
  // before the first line renders.
  public void EnsurePlayable()
  {
    if (Lines is not { Length: > 0 })
      throw new InvalidOperationException("DialogueSequenceData requires at least one line.");
    for (int i = 0; i < Lines.Length; i++)
    {
      DialogueLineData? line = Lines[i];
      int number = i + 1;
      if (line is null)
        throw new InvalidOperationException($"Dialogue line {number} requires a DialogueLineData.");
      if (line.Speaker is null)
        throw new InvalidOperationException($"Dialogue line {number} requires a Speaker.");
      if (string.IsNullOrWhiteSpace(line.Speaker.DisplayName))
        throw new InvalidOperationException($"Dialogue line {number} requires a speaker display name.");
      if (line.Speaker.Portrait is null)
        throw new InvalidOperationException($"Dialogue line {number} requires a speaker portrait.");
      if (string.IsNullOrWhiteSpace(line.Text))
        throw new InvalidOperationException($"Dialogue line {number} requires text.");
    }
  }
}
