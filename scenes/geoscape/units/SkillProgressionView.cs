using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Combatants;
using FunProject.Progression;
using FunProject.Stats;
using FunProject.Strategic;
using Godot;
using System;
using System.Collections.Generic;
using FunProject.Scenes.Ext;

public sealed partial class SkillProgressionView : GeoscapeView
{
  public Combatant? Unit;

  public override void _Ready()
  {
    base._Ready();
    GetNode<Button>("%AwardButton").Pressed += () =>
    {
      Unit?.Progression.AwardPoints(5); // DEBUG: stands in for unwired mission/kill rewards
      Rebuild();
    };
  }

  public override void Present(CampaignGameState state, GeoscapeSession session)
  {
    ArgumentNullException.ThrowIfNull(Unit);
    Rebuild();
  }

  private void Rebuild()
  {
    if (Unit is null)
      return;

    GetNode<RichTextLabel>("%Title").Text = $"{Unit.Name} — Skill Paths";
    GetNode<RichTextLabel>("%CurrencyLabel").Text = $"Points: {Unit.Progression.CurrencyPoints}";
    RebuildCommitted();
    RebuildAvailable();
  }

  private void RebuildCommitted()
  {
    var list = GetNode<VBoxContainer>("%CommittedList");
    list.QueueFreeAllChildren();

    foreach (SkillPathData path in Unit!.Progression.CommittedPaths)
    {
      UnitProgression progression = Unit.Progression;
      int unlocked = progression.StepsUnlockedFor(path);

      AddLabel(list, $"{path.Name} — {unlocked}/{path.Steps.Count} unlocked", 22);

      for (int i = 0; i < path.Steps.Count; i++)
      {
        SkillUpgradeStepData step = path.Steps[i];
        string marker = i < unlocked ? "[OK]" : (i == unlocked ? "[>] " : "[..]");
        AddLabel(list, $"    {marker} Step {i + 1}  (cost {step.Cost})  {EffectSummary(step)}", 18);
      }

      progression.NextStep(path).IfSome(next =>
      {
        var button = new Button { Text = $"Unlock next  ({next.Cost} pts)" };
        button.Disabled = Unit.Progression.CurrencyPoints < next.Cost;
        button.Pressed += () =>
        {
          progression.TryUnlockNext(path);
          Rebuild();
        };
        list.AddChild(button);
      });
      if (progression.NextStep(path).IsNone)
        AddLabel(list, "    (chain complete)", 18);
    }

    if (list.GetChildCount() == 0)
      AddLabel(list, "— none —", 18);
  }

  private void RebuildAvailable()
  {
    var list = GetNode<VBoxContainer>("%AvailableList");
    list.QueueFreeAllChildren();

    UnitProgression progression = Unit!.Progression;
    bool slotsFree = progression.CommittedPaths.Count < UnitProgression.MaxCommittedPaths;
    foreach (SkillPathData path in SkillPathCatalog.Paths)
    {
      bool committedAlready = false;
      foreach (SkillPathData held in progression.CommittedPaths)
        if (held == path)
        {
          committedAlready = true;
          break;
        }

      if (committedAlready)
        continue;

      if (slotsFree)
      {
        var button = new Button { Text = $"Commit: {path.Name}  (first step: {EffectSummary(path.Steps[0])}, cost {path.Steps[0].Cost})" };
        button.Pressed += () =>
        {
          progression.TryCommit(path);
          Rebuild();
        };
        list.AddChild(button);
      }
      else
      {
        AddLabel(list, $"{path.Name} — no path slots free", 18);
      }
    }
  }

  private static string EffectSummary(SkillUpgradeStepData step)
  {
    var parts = new List<string>();
    foreach (UpgradeEffectData effect in step.Effects)
      switch (effect)
      {
        case StatModUpgradeEffectData statModEffect:
          foreach (StatMod mod in statModEffect.StatMods)
            parts.Add($"{PrettyStatName(mod.TargetType)}{ModifierText(mod)}");
          break;
        case BuffGrantUpgradeEffectData buffEffect:
          foreach (FunProject.Buffs.Buff buff in buffEffect.Buffs)
            parts.Add($"Buffs: {buff.Name}");
          break;
        case AbilityGrantUpgradeEffectData abilityEffect:
          parts.Add($"Ability: {abilityEffect.Ability?.Name ?? "(no ability)"}");
          break;
      }

    return parts.Count > 0 ? string.Join(", ", parts) : "no effects";
  }

  private static string PrettyStatName(System.Type statType)
    => statType.Name.EndsWith("Stat") ? statType.Name[..^"Stat".Length] : statType.Name;

  private static string ModifierText(StatMod mod)
  {
    var texts = new List<string>();
    foreach (StatModifier modifier in mod.Modifiers)
      texts.Add(modifier.Operation switch
      {
        ModifierOperation.Add => $"{modifier.Value:+0.#;-0.#}",
        ModifierOperation.Multiply => $" x{modifier.Value:0.#}",
        ModifierOperation.PercentAdd => $" +{modifier.Value * 100f:0.#}%",
        ModifierOperation.CapMin => $" (min {modifier.Value:0.#})",
        ModifierOperation.CapMax => $" (max {modifier.Value:0.#})",
        ModifierOperation.Override => $" = {modifier.Value:0.#}",
        _ => $" {modifier.Operation} {modifier.Value}",
      });
    return string.Join(" ", texts);
  }

  private static void AddLabel(Container parent, string text, int fontSize)
  {
    var label = new RichTextLabel
    {
      Text = text,
      FitContent = true,
      ScrollActive = false,
    };
    label.AddThemeFontSizeOverride("normal_font_size", fontSize);
    parent.AddChild(label);
  }
}
